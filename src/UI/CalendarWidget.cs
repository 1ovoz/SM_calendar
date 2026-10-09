using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using SMCalendar.Core;

namespace SMCalendar.UI;

internal enum HitKind { None, Drag, Title, Button, Cell, DayNumber, Event, More, Status, Grip, MiniDay, AddDay, AgendaArea }
internal enum Btn { Prev, Next, Today, View, Add, Pin, Collapse, Hide, Menu }

internal readonly record struct Hit(HitKind Kind, RectangleF Rect, int Index = -1, Btn Button = default, CalEvent? Event = null,
    DateTime Date = default);

/// <summary>위젯이 앱(트레이)에 요청하는 동작.</summary>
internal interface IWidgetHost
{
    void ShowWidgetMenu(CalendarWidget widget, Point screen);
    void HideWidget(CalendarWidget widget);
    void SaveSettings();
}

/// <summary>
/// 바탕화면 달력 위젯 (월간 / 미니 월간 / 미니 어젠다 / 미니 앱).
/// 컨트롤을 하나도 쓰지 않고 per-pixel alpha 레이어드 창(UpdateLayeredWindow)에 직접 그린다.
/// 상태가 바뀔 때만 다시 그리므로 평소 CPU 사용량은 0 에 가깝다.
/// </summary>
internal sealed partial class CalendarWidget : Form
{
    const int GridDays = 42;
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];
    static readonly string IconFamily = Ui.IconFamily;

    readonly AppSettings _s;
    readonly WidgetSettings _w;
    readonly CalendarService _svc;
    readonly IWidgetHost _host;
    readonly List<Hit> _hits = new();
    Hit _hover, _pressed;
    Point _pressPoint;
    bool _inside;

    int _year, _month;
    DateTime _selected = DateTime.Today;
    DateTime _gridStart;
    readonly List<CalEvent>[] _days = new List<CalEvent>[GridDays];
    readonly bool[] _holiday = new bool[GridDays];
    readonly List<(DateTime Day, List<CalEvent> Events)> _agenda = new();
    float _agendaScroll, _agendaMaxScroll;

    readonly System.Windows.Forms.Timer _navTimer = new() { Interval = 450 };
    readonly System.Windows.Forms.Timer _peekTimer = new() { Interval = 8000 };
    bool _peek;

    EventEditorForm? _editor;
    DayListForm? _dayList;

    // 그리기 표면 (DIB section 을 재사용해서 매번 할당하지 않는다)
    IntPtr _memDc, _hBitmap, _oldBitmap, _bits;
    Bitmap? _surface;
    int _surfaceW, _surfaceH;
    UiFonts? _fonts;
    Font? _popupFont;

    public CalendarWidget(AppSettings settings, WidgetSettings widget, CalendarService service, IWidgetHost host)
    {
        _s = settings;
        _w = widget;
        _svc = service;
        _host = host;

        Text = "SM Calendar";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = MinimizeBox = ControlBox = false;
        AutoScaleMode = AutoScaleMode.None;

        for (int i = 0; i < _days.Length; i++) _days[i] = new List<CalEvent>();
        _year = DateTime.Today.Year;
        _month = DateTime.Today.Month;
        ComputeGrid();
        ApplyBounds(DeviceDpi);

        _svc.Changed += OnServiceChanged;
        _navTimer.Tick += (_, _) => { _navTimer.Stop(); _ = _svc.SyncAsync(); };
        _peekTimer.Tick += (_, _) => { if (!Bounds.Contains(Cursor.Position)) EndPeek(); };
    }

    public WidgetSettings Settings => _w;
    public bool DarkTheme => _s.DarkTheme;
    new float Scale => DeviceDpi / 96f;
    /// <summary>글자 크기 배율.</summary>
    float F => (float)_w.FontScale;
    /// <summary>글자에 맞춰 커지는 길이 단위.</summary>
    float U => Scale * F;
    bool HasAgenda => _w.Kind is WidgetKind.MiniAgenda or WidgetKind.MiniApp;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // WinForms 가 표시 과정에서 소유자를 자체 숨김 창으로 되돌리므로 표시 후 다시 적용
        ApplyPin();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (DeviceDpi != 96) ApplyBounds(DeviceDpi);
        ApplyPin();
        Render();
    }

    void ApplyBounds(int dpi)
    {
        float sc = dpi / 96f;
        var min = WidgetSettings.MinimumSize(_w.Kind);
        var b = new Rectangle(_w.X, _w.Y, (int)(_w.Width * sc), _w.Collapsed ? CollapsedHeight(sc) : (int)(_w.Height * sc));
        if (_w.X == int.MinValue || !Screen.AllScreens.Any(scr => scr.WorkingArea.IntersectsWith(b)))
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            b.X = wa.Right - b.Width - (int)(24 * sc);
            b.Y = wa.Top + (int)(24 * sc);
        }
        MinimumSize = new Size((int)(min.Width * sc), _w.Collapsed ? 1 : (int)(min.Height * sc));
        Bounds = b;
    }

    int CollapsedHeight(float sc) => (int)Math.Ceiling(HeaderHeight(sc) + 4 * sc);

    float HeaderHeight(float sc)
    {
        float fl = 1 + (F - 1) * 0.7f;
        return (_w.Kind == WidgetKind.Month ? 46 : 38) * sc * fl;
    }

    // ===================== 창 고정 방식 =====================

    /// <summary>
    /// 항상 위: 다른 창보다 위에 표시.
    /// 바탕화면 고정: 바탕화면 창(Progman)을 소유자로 지정해서 Win+D 에도 숨겨지지 않고, 맨 아래에 머문다.
    /// </summary>
    public void ApplyPin()
    {
        if (!IsHandleCreated) return;
        const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        if (_w.AlwaysOnTop)
        {
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, IntPtr.Zero);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
        }
        else if (_w.PinToDesktop)
        {
            Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, Native.FindWindow("Progman", null));
            Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, flags);
        }
        else
        {
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, IntPtr.Zero);
            Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0, flags);
        }
    }

    /// <summary>트레이 아이콘 클릭: 다른 창에 가려졌을 때 잠깐 맨 앞으로.</summary>
    public void Peek()
    {
        if (!IsHandleCreated || _w.AlwaysOnTop) return;
        _peek = true;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    void EndPeek()
    {
        if (!_peek) return;
        _peek = false;
        _peekTimer.Stop();
        ApplyPin();
    }

    protected override void WndProc(ref Message m)
    {
        bool atBottom = _w.PinToDesktop && !_w.AlwaysOnTop && !_peek;
        if (atBottom && m.Msg == Native.WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
            wp.hwndInsertAfter = Native.HWND_BOTTOM;
            Marshal.StructureToPtr(wp, m.LParam, false);
        }
        else if ((atBottom || _w.AlwaysOnTop) && m.Msg == Native.WM_MOUSEACTIVATE)
        {
            // 클릭해도 활성화되지 않음 → 작업 중인 창의 포커스를 뺏지 않는다
            m.Result = Native.MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    // ===================== 상태 / 탐색 =====================

    void OnServiceChanged()
    {
        RebuildDays();
        Render();
        _dayList?.RefreshList();
    }

    void ComputeGrid()
    {
        var first = new DateTime(_year, _month, 1);
        int startDow = _s.WeekStartsMonday ? 1 : 0;
        int offset = ((int)first.DayOfWeek - startDow + 7) % 7;
        _gridStart = first.AddDays(-offset);
        var end = _gridStart.AddDays(GridDays);
        if (HasAgenda && _selected.AddDays(60) > end) end = _selected.AddDays(60);
        _svc.SetViewRange(this, _gridStart < _selected || !HasAgenda ? _gridStart : _selected, end);
        RebuildDays();
    }

    /// <summary>달력에 보이는 날짜 수 (월간은 항상 6주, 미니는 그 달에 필요한 만큼).</summary>
    int VisibleWeeks
    {
        get
        {
            if (_w.Kind == WidgetKind.Month) return 6;
            var first = new DateTime(_year, _month, 1);
            int offset = (int)(first - _gridStart).TotalDays;
            return (offset + DateTime.DaysInMonth(_year, _month) + 6) / 7;
        }
    }

    public void RebuildDays()
    {
        foreach (var d in _days) d.Clear();
        Array.Clear(_holiday);
        var gridEnd = _gridStart.AddDays(GridDays);
        var visible = _svc.Store.Calendars.Where(_s.IsCalendarVisible).ToDictionary(c => c.Id);

        foreach (var e in _svc.Store.Events)
        {
            if (e.Start >= gridEnd || e.End < _gridStart) continue;
            if (!visible.TryGetValue(e.CalendarId, out var cal)) continue;
            int from = Math.Max(0, (int)(e.Start.Date - _gridStart).TotalDays);
            int to = Math.Min(_days.Length - 1, (int)(e.End.Date - _gridStart).TotalDays);
            for (int i = from; i <= to; i++)
            {
                if (!e.OccursOn(_gridStart.AddDays(i))) continue;
                _days[i].Add(e);
                if (cal.IsHoliday) _holiday[i] = true;
            }
        }
        foreach (var d in _days) d.Sort(CompareEvents);
        RebuildAgenda();
    }

    /// <summary>선택한 날부터 일정이 있는 날만 모은 목록 (선택한 날은 비어 있어도 표시).</summary>
    void RebuildAgenda()
    {
        _agenda.Clear();
        if (!HasAgenda) return;
        var from = _selected.Date;
        var to = from.AddDays(60);
        var visible = _svc.Store.Calendars.Where(_s.IsCalendarVisible).Select(c => c.Id).ToHashSet();
        var candidates = _svc.Store.Events.Where(e => visible.Contains(e.CalendarId) && e.Start < to && e.End > from).ToList();
        int total = 0;
        for (var day = from; day < to && total < 40; day = day.AddDays(1))
        {
            var list = candidates.Where(e => e.OccursOn(day)).ToList();
            if (list.Count == 0 && day != from) continue;
            list.Sort(CompareEvents);
            _agenda.Add((day, list));
            total += list.Count;
        }
    }

    public static int CompareEvents(CalEvent a, CalEvent b)
    {
        if (a.AllDay != b.AllDay) return a.AllDay ? -1 : 1;
        if (a.IsMultiDay != b.IsMultiDay) return a.IsMultiDay ? -1 : 1;
        int c = a.Start.CompareTo(b.Start);
        return c != 0 ? c : string.CompareOrdinal(a.Title, b.Title);
    }

    public CalendarInfo? FindCalendar(string id) => _svc.Store.FindCalendar(id);

    public IReadOnlyList<CalEvent> EventsOn(DateTime day)
    {
        int i = (int)(day.Date - _gridStart).TotalDays;
        if (i >= 0 && i < _days.Length) return _days[i];
        var visible = _svc.Store.Calendars.Where(_s.IsCalendarVisible).Select(c => c.Id).ToHashSet();
        var list = _svc.Store.Events.Where(e => visible.Contains(e.CalendarId) && e.OccursOn(day.Date)).ToList();
        list.Sort(CompareEvents);
        return list;
    }

    void Navigate(int months)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(months);
        GoTo(d.Year, d.Month);
    }

    void GoTo(int year, int month)
    {
        if (year == _year && month == _month) return;
        _year = year;
        _month = month;
        ComputeGrid();
        Render();
        _navTimer.Stop();
        _navTimer.Start();
    }

    void GoToday()
    {
        _selected = DateTime.Today;
        _agendaScroll = 0;
        if (_year == _selected.Year && _month == _selected.Month)
        {
            ComputeGrid();
            Render();
        }
        else GoTo(_selected.Year, _selected.Month);
    }

    void Select(DateTime day)
    {
        _selected = day.Date;
        if (HasAgenda)
        {
            _agendaScroll = 0;
            ComputeGrid();
            if (_selected.AddDays(60) > _gridStart.AddDays(GridDays + 35))
            {
                _navTimer.Stop();
                _navTimer.Start();
            }
        }
        Render();
    }

    /// <summary>테마·주 시작 요일 등 전체 설정이 바뀌었을 때.</summary>
    public void SettingsChanged()
    {
        // 글꼴이 바뀌었을 수 있으니 다시 만든다
        _fonts?.Dispose();
        _fonts = null;
        _popupFont?.Dispose();
        _popupFont = null;
        if (_w.Collapsed) ApplyCollapse();
        ComputeGrid();
        Render();
    }

    /// <summary>날짜가 바뀌면(자정) 오늘 표시를 갱신.</summary>
    public void DayChanged(DateTime oldToday)
    {
        if (_year == oldToday.Year && _month == oldToday.Month)
        {
            _year = DateTime.Today.Year;
            _month = DateTime.Today.Month;
        }
        if (_selected == oldToday) _selected = DateTime.Today;
        ComputeGrid();
        Render();
    }

    // ===================== 위젯 설정 =====================

    public void SetOpacity(double value)
    {
        _w.Opacity = Math.Round(Math.Clamp(value, 0, 1) * 20) / 20;
        Render();
    }

    /// <summary>글자 크기 변경. 내용이 잘리지 않도록 위젯 크기도 같은 비율로 조절한다 (화면 크기 안에서).</summary>
    public void SetFontScale(double scale)
    {
        scale = Math.Clamp(scale, 0.8, 1.6);
        double ratio = scale / _w.FontScale;
        _w.FontScale = scale;
        if (Math.Abs(ratio - 1) > 0.001)
        {
            var wa = Screen.FromControl(this).WorkingArea;
            var min = WidgetSettings.MinimumSize(_w.Kind);
            _w.Width = (int)Math.Clamp(Math.Round(_w.Width * ratio), min.Width, wa.Width / Scale * 0.95);
            _w.Height = (int)Math.Clamp(Math.Round(_w.Height * ratio), min.Height, wa.Height / Scale * 0.95);
            var b = new Rectangle(Left, Top, (int)(_w.Width * Scale), _w.Collapsed ? CollapsedHeight(Scale) : (int)(_w.Height * Scale));
            // 화면 밖으로 나가지 않게
            b.X = Math.Clamp(b.X, wa.Left, Math.Max(wa.Left, wa.Right - b.Width));
            b.Y = Math.Clamp(b.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - b.Height));
            Bounds = b;
            _w.X = b.X;
            _w.Y = b.Y;
        }
        if (_w.Collapsed) ApplyCollapse();
        Render();
        _host.SaveSettings();
    }

    public void ToggleCollapsed()
    {
        _w.Collapsed = !_w.Collapsed;
        ApplyCollapse();
        _host.SaveSettings();
    }

    void ApplyCollapse()
    {
        var min = WidgetSettings.MinimumSize(_w.Kind);
        if (_w.Collapsed)
        {
            MinimumSize = new Size((int)(min.Width * Scale), 1);
            Height = CollapsedHeight(Scale);
        }
        else
        {
            MinimumSize = new Size((int)(min.Width * Scale), (int)(min.Height * Scale));
            Height = (int)(_w.Height * Scale);
        }
        Render();
    }

    public void ToggleAlwaysOnTop()
    {
        _w.AlwaysOnTop = !_w.AlwaysOnTop;
        ApplyPin();
        Render();
        _host.SaveSettings();
    }

    // ===================== 그리기 표면 =====================

    void EnsureSurface(int w, int h)
    {
        if (_surface != null && w == _surfaceW && h == _surfaceH) return;
        FreeSurface();
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        var screen = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(screen);
        _hBitmap = Native.CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        Native.ReleaseDC(IntPtr.Zero, screen);
        _oldBitmap = Native.SelectObject(_memDc, _hBitmap);
        _surface = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, _bits);
        _surfaceW = w;
        _surfaceH = h;
    }

    void FreeSurface()
    {
        _surface?.Dispose();
        _surface = null;
        if (_memDc != IntPtr.Zero)
        {
            Native.SelectObject(_memDc, _oldBitmap);
            Native.DeleteObject(_hBitmap);
            Native.DeleteDC(_memDc);
            _memDc = _hBitmap = IntPtr.Zero;
        }
    }

    public void Render()
    {
        if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
        EnsureSurface(Width, Height);
        using (var g = Graphics.FromImage(_surface!))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            PaintWidget(g);
        }
        var dst = new Native.POINT(Left, Top);
        var size = new Native.SIZE(Width, Height);
        var src = new Native.POINT(0, 0);
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA,
        };
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
    }

    /// <summary>개발용: 위젯을 샘플 배경 위에 합성해서 PNG 로 저장.</summary>
    public void SaveSnapshot(string path, bool hover = false)
    {
        _inside = hover;
        Render();
        if (_surface == null) return;
        using var bmp = new Bitmap(Width + 40, Height + 40);
        using (var g = Graphics.FromImage(bmp))
        {
            using var bg = new LinearGradientBrush(new Rectangle(0, 0, bmp.Width, bmp.Height),
                Color.FromArgb(40, 90, 140), Color.FromArgb(200, 140, 90), 35f);
            g.FillRectangle(bg, 0, 0, bmp.Width, bmp.Height);
            g.DrawImage(_surface, 20, 20);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // ===================== 입력 =====================

    Hit HitTest(Point p)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Rect.Contains(p) && _hits[i].Kind != HitKind.AgendaArea) return _hits[i];
        return default;
    }

    bool OverAgenda(Point p) => _hits.Any(h => h.Kind == HitKind.AgendaArea && h.Rect.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // 제목을 누른 채 움직이면 창 이동, 그냥 떼면 '오늘'로
        if (_pressed.Kind == HitKind.Title && e.Button == MouseButtons.Left && !_w.Locked &&
            (Math.Abs(e.X - _pressPoint.X) > 4 || Math.Abs(e.Y - _pressPoint.Y) > 4))
        {
            _pressed = default;
            BeginNativeDrag(Native.HTCAPTION);
            return;
        }
        var h = HitTest(e.Location);
        bool enter = !_inside;
        _inside = true;
        if (h == _hover && !enter) return;
        _hover = h;
        Cursor = h.Kind switch
        {
            HitKind.Button or HitKind.Event or HitKind.More or HitKind.Status or HitKind.DayNumber
                or HitKind.MiniDay or HitKind.AddDay or HitKind.Title => Cursors.Hand,
            HitKind.Grip => Cursors.SizeNWSE,
            _ => Cursors.Default,
        };
        Render();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (Bounds.Contains(Cursor.Position)) return; // 팝업 위로 잠시 벗어난 경우
        _inside = false;
        _hover = default;
        Render();
        if (_peek) EndPeek();
    }

    void BeginNativeDrag(int hitTest)
    {
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)hitTest, IntPtr.Zero);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var h = HitTest(e.Location);
        _pressed = default;
        if (e.Button == MouseButtons.Right)
        {
            _host.ShowWidgetMenu(this, PointToScreen(e.Location));
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (e.Clicks == 2 && h.Kind is HitKind.Cell or HitKind.MiniDay)
        {
            OpenEditor(null, _gridStart.AddDays(h.Index));
            return;
        }
        if (!_w.Locked && h.Kind is HitKind.Drag or HitKind.Grip)
        {
            BeginNativeDrag(h.Kind == HitKind.Grip ? Native.HTBOTTOMRIGHT : Native.HTCAPTION);
            return;
        }
        _pressed = h;
        _pressPoint = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var h = HitTest(e.Location);
        var p = _pressed;
        _pressed = default;
        if (p.Kind == HitKind.None || p.Kind != h.Kind || p.Index != h.Index || p.Button != h.Button || p.Date != h.Date) return;

        switch (h.Kind)
        {
            case HitKind.Button: OnButton(h.Button, h.Rect); break;
            case HitKind.Title: ShowMonthPicker(h.Rect); break;
            case HitKind.Event: OpenEditor(h.Event, h.Date == default ? _gridStart.AddDays(h.Index) : h.Date); break;
            case HitKind.AddDay: OpenEditor(null, h.Date); break;
            case HitKind.More:
            case HitKind.DayNumber:
                _selected = _gridStart.AddDays(h.Index);
                Render();
                OpenDayList(_selected);
                break;
            case HitKind.Cell:
                _selected = _gridStart.AddDays(h.Index);
                Render();
                break;
            case HitKind.MiniDay:
                var day = _gridStart.AddDays(h.Index);
                if (HasAgenda) Select(day);
                else
                {
                    _selected = day;
                    Render();
                    OpenDayList(day);
                }
                break;
            case HitKind.Status:
                if (!_svc.IsSignedIn) _ = _svc.SignInAsync(this);
                else if (!_svc.IsSyncing) _ = _svc.SyncAsync();
                break;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0)
        {
            // Ctrl + 휠: 배경 불투명도 5% 단위
            SetOpacity(_w.Opacity + (e.Delta > 0 ? 0.05 : -0.05));
            _host.SaveSettings();
            return;
        }
        if (OverAgenda(e.Location))
        {
            _agendaScroll = Math.Clamp(_agendaScroll - Math.Sign(e.Delta) * 60 * U, 0, _agendaMaxScroll);
            Render();
            return;
        }
        Navigate(e.Delta > 0 ? -1 : 1);
    }

    void OnButton(Btn b, RectangleF rect)
    {
        switch (b)
        {
            case Btn.Prev: Navigate(-1); break;
            case Btn.Next: Navigate(1); break;
            case Btn.Today: GoToday(); break;
            case Btn.Add: OpenEditor(null, _selected); break;
            case Btn.View: ShowViewPopup(rect); break;
            case Btn.Pin: ToggleAlwaysOnTop(); break;
            case Btn.Collapse: ToggleCollapsed(); break;
            case Btn.Hide: _host.HideWidget(this); break;
            case Btn.Menu: _host.ShowWidgetMenu(this, PointToScreen(new Point((int)rect.Right, (int)rect.Bottom))); break;
        }
    }

    /// <summary>제목을 누르면 연·월을 골라 바로 이동.</summary>
    void ShowMonthPicker(RectangleF title)
    {
        var p = Palette.For(_s.DarkTheme);
        _popupFont ??= Ui.Font(13 * Scale);
        var picker = new MonthPicker(p, _year, _month, _popupFont);
        picker.Chosen += (y, m) => GoTo(y, m);
        Native.SetForegroundWindow(Handle);
        var pt = PointToScreen(new Point((int)title.X, (int)title.Bottom + 4));
        Popup.ShowAt(this, pt, picker, p);
    }

    void ShowViewPopup(RectangleF button)
    {
        var p = Palette.For(_s.DarkTheme);
        _popupFont ??= Ui.Font(13 * Scale);
        var panel = new ViewPanel(p, _w.Opacity, _w.FontScale, _popupFont);
        panel.OpacityChanged += SetOpacity;
        panel.FontScaleChanged += SetFontScale;
        Native.SetForegroundWindow(Handle);
        var pt = PointToScreen(new Point((int)(button.Right - panel.Width), (int)button.Bottom + 4));
        Popup.ShowAt(this, pt, panel, p, closed: _host.SaveSettings);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SaveBounds();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Render();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Render();
    }

    void SaveBounds()
    {
        _w.X = Left;
        _w.Y = Top;
        _w.Width = (int)Math.Round(Width / Scale);
        if (!_w.Collapsed) _w.Height = (int)Math.Round(Height / Scale);
        _host.SaveSettings();
    }

    // ===================== 대화상자 =====================

    public void OpenEditor(CalEvent? ev, DateTime day)
    {
        if (!_svc.IsSignedIn)
        {
            _ = _svc.SignInAsync(this);
            return;
        }
        _editor?.Close();
        _editor = new EventEditorForm(_svc, _s, ev, day);
        _editor.FormClosed += (s, _) => { if (_editor == s) _editor = null; };
        _editor.Show();
        _editor.Activate();
    }

    void OpenDayList(DateTime day)
    {
        if (_dayList == null || _dayList.IsDisposed)
        {
            _dayList = new DayListForm(this);
            _dayList.FormClosed += (_, _) => _dayList = null;
        }
        _dayList.ShowFor(day, Cursor.Position);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _svc.Changed -= OnServiceChanged;
            _svc.RemoveViewRange(this);
            _navTimer.Dispose();
            _peekTimer.Dispose();
            _fonts?.Dispose();
            _popupFont?.Dispose();
            _editor?.Close();
            _dayList?.Close();
        }
        FreeSurface();
        base.Dispose(disposing);
    }
}
