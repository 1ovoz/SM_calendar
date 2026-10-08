using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using SMCalendar.Core;

namespace SMCalendar.UI;

internal enum HitKind { None, Drag, Button, Cell, DayNumber, Event, More, Status, Grip }
internal enum Btn { Prev, Next, Today, Sync, Add, Menu }

internal readonly record struct Hit(HitKind Kind, RectangleF Rect, int Index = -1, Btn Button = default, CalEvent? Event = null);

/// <summary>
/// 바탕화면 달력 위젯.
/// 컨트롤을 하나도 쓰지 않고 per-pixel alpha 레이어드 창(UpdateLayeredWindow)에 직접 그린다.
/// 상태가 바뀔 때만 다시 그리므로 평소 CPU 사용량은 0 에 가깝다.
/// </summary>
internal sealed class CalendarWidget : Form
{
    const int Weeks = 6;
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];
    static readonly string IconFamily = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    const string UiFamily = "Malgun Gothic";

    readonly AppSettings _s;
    readonly CalendarService _svc;
    readonly ContextMenuStrip _menu;
    readonly List<Hit> _hits = new();
    Hit _hover, _pressed;

    int _year, _month;
    DateTime _selected = DateTime.Today;
    DateTime _gridStart;
    readonly List<CalEvent>[] _days = new List<CalEvent>[Weeks * 7];
    readonly bool[] _holiday = new bool[Weeks * 7];

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

    public CalendarWidget(AppSettings settings, CalendarService service, ContextMenuStrip menu)
    {
        _s = settings;
        _svc = service;
        _menu = menu;

        Text = "SM Calendar";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = MinimizeBox = ControlBox = false;
        AutoScaleMode = AutoScaleMode.None;

        for (int i = 0; i < _days.Length; i++) _days[i] = new List<CalEvent>();
        _year = DateTime.Today.Year;
        _month = DateTime.Today.Month;
        ApplyBounds(DeviceDpi);
        ComputeGrid();

        _svc.Changed += OnServiceChanged;
        _navTimer.Tick += (_, _) => { _navTimer.Stop(); _ = _svc.SyncAsync(); };
        _peekTimer.Tick += (_, _) => { if (!Bounds.Contains(Cursor.Position)) EndPeek(); };
    }

    new float Scale => DeviceDpi / 96f;

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
        var b = new Rectangle(_s.X, _s.Y, (int)(_s.Width * sc), (int)(_s.Height * sc));
        if (_s.X == int.MinValue || !Screen.AllScreens.Any(sc2 => sc2.WorkingArea.IntersectsWith(b)))
        {
            var wa = Screen.PrimaryScreen!.WorkingArea;
            b.X = wa.Right - b.Width - (int)(24 * sc);
            b.Y = wa.Top + (int)(24 * sc);
        }
        MinimumSize = new Size((int)(340 * sc), (int)(280 * sc));
        Bounds = b;
    }

    // ===================== 바탕화면 고정 =====================

    /// <summary>
    /// 바탕화면 창(Progman)을 소유자로 지정하면 Win+D(바탕화면 보기)에도 숨겨지지 않고,
    /// z-order 를 항상 맨 아래로 고정해서 다른 창 위로 올라오지 않는다.
    /// </summary>
    public void ApplyPin()
    {
        if (!IsHandleCreated) return;
        const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        if (_s.PinToDesktop)
        {
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, Native.FindWindow("Progman", null));
            Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, flags);
        }
        else
        {
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, IntPtr.Zero);
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0, flags);
        }
    }

    /// <summary>트레이 아이콘 클릭: 다른 창에 가려졌을 때 잠깐 맨 앞으로.</summary>
    public void Peek()
    {
        if (!IsHandleCreated) return;
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
        Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        ApplyPin();
    }

    protected override void WndProc(ref Message m)
    {
        if (_s.PinToDesktop && !_peek)
        {
            if (m.Msg == Native.WM_WINDOWPOSCHANGING)
            {
                var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
            else if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                // 클릭해도 활성화되지 않음 → 작업 중인 창의 포커스를 뺏지 않는다
                m.Result = Native.MA_NOACTIVATE;
                return;
            }
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
        _svc.SetViewRange(_gridStart, _gridStart.AddDays(Weeks * 7));
        RebuildDays();
    }

    public void RebuildDays()
    {
        foreach (var d in _days) d.Clear();
        Array.Clear(_holiday);
        var gridEnd = _gridStart.AddDays(Weeks * 7);
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

    public void SettingsChanged()
    {
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

    // ===================== 그리기 =====================

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
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
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
    public void SaveSnapshot(string path)
    {
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

    UiFonts Fonts
    {
        get
        {
            if (_fonts == null || _fonts.Scale != Scale)
            {
                _fonts?.Dispose();
                _fonts = new UiFonts(Scale);
            }
            return _fonts;
        }
    }

    void PaintWidget(Graphics g)
    {
        _hits.Clear();
        float S = Scale;
        var th = _s.DarkTheme ? Theme.Dark : Theme.Light;
        var f = Fonts;
        int W = Width, H = Height;
        // 배경이 거의 투명하면 글자에 그림자를 넣어서 배경화면 위에서도 읽히게
        bool shadow = _s.DarkTheme && _s.Opacity < 0.6;

        // --- 배경 (알파 0 이면 클릭이 통과하므로 최소값 유지)
        int bgA = Math.Max(4, (int)Math.Round(Math.Clamp(_s.Opacity, 0, 1) * 255));
        using (var path = RoundRect(new RectangleF(0.5f, 0.5f, W - 1, H - 1), 12 * S))
        {
            using (var b = new SolidBrush(Color.FromArgb(bgA, th.Bg))) g.FillPath(b, path);
            if (_s.Opacity >= 0.08)
                using (var p = new Pen(Color.FromArgb(Math.Min(40, bgA / 4), th.Border), 1)) g.DrawPath(p, path);
        }

        float pad = 14 * S;
        float headerH = 46 * S;
        float weekH = 24 * S;
        float footerH = 22 * S;

        // --- 머리글
        _hits.Add(new Hit(HitKind.Drag, new RectangleF(0, 0, W, headerH + weekH)));
        var titleRect = new RectangleF(pad, 4 * S, W / 2f, headerH - 4 * S);
        DrawText(g, $"{_year}년 {_month}월", f.Title, th.Fg, titleRect, Sf.LeftCenter, shadow);

        float bs = 30 * S;
        float by = 4 * S + (headerH - 4 * S - bs) / 2;
        float bx = W - pad + 4 * S;
        Button(g, ref bx, by, bs, bs, Btn.Menu, "", th, f, shadow);
        Button(g, ref bx, by, bs, bs, Btn.Add, "", th, f, shadow);
        Button(g, ref bx, by, bs, bs, Btn.Sync, "", th, f, shadow, dim: _svc.IsSyncing);
        bx -= 6 * S;
        Button(g, ref bx, by, bs, bs, Btn.Next, "", th, f, shadow);
        Button(g, ref bx, by, 44 * S, bs, Btn.Today, "오늘", th, f, shadow, text: true);
        Button(g, ref bx, by, bs, bs, Btn.Prev, "", th, f, shadow);

        // --- 요일
        float gridL = 8 * S, gridR = W - 8 * S;
        float cellW = (gridR - gridL) / 7f;
        int startDow = _s.WeekStartsMonday ? 1 : 0;
        for (int c = 0; c < 7; c++)
        {
            int dow = (startDow + c) % 7;
            var color = dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : Theme.WithAlpha(th.Fg, 170);
            DrawText(g, DayNames[dow], f.Weekday, color, new RectangleF(gridL + c * cellW, headerH, cellW, weekH), Sf.Center, shadow);
        }

        // --- 날짜 칸
        float gridT = headerH + weekH;
        float gridB = H - footerH;
        float cellH = (gridB - gridT) / Weeks;
        var today = DateTime.Today;
        using var gridPen = new Pen(th.Grid, Math.Max(1, S * 0.8f));

        for (int r = 0; r < Weeks; r++)
            g.DrawLine(gridPen, gridL + 4 * S, gridT + r * cellH, gridR - 4 * S, gridT + r * cellH);

        float lineH = 17 * S;
        for (int i = 0; i < Weeks * 7; i++)
        {
            var day = _gridStart.AddDays(i);
            var cell = new RectangleF(gridL + (i % 7) * cellW, gridT + (i / 7) * cellH, cellW, cellH);
            _hits.Add(new Hit(HitKind.Cell, cell, i));
            bool inMonth = day.Month == _month;

            var inner = RectangleF.Inflate(cell, -2 * S, -2 * S);
            if (day == _selected && !(day == today))
                FillRound(g, inner, 6 * S, Theme.WithAlpha(th.Accent, 40));
            else if (_hover.Kind is HitKind.Cell or HitKind.DayNumber && _hover.Index == i)
                FillRound(g, inner, 6 * S, th.Hover);

            // 날짜 숫자
            int dow = (int)day.DayOfWeek;
            var numColor = _holiday[i] || dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : th.Fg;
            if (!inMonth) numColor = Theme.WithAlpha(numColor, th.DimAlpha);
            var numRect = new RectangleF(cell.X + 4 * S, cell.Y + 3 * S, 22 * S, 19 * S);
            if (day == today)
            {
                FillRound(g, numRect, 9.5f * S, th.Accent);
                DrawText(g, day.Day.ToString(), f.DayBold, Color.White, numRect, Sf.Center, false);
            }
            else
            {
                DrawText(g, day.Day.ToString(), day == _selected ? f.DayBold : f.Day, numColor, numRect, Sf.Center, shadow);
            }
            _hits.Add(new Hit(HitKind.DayNumber, numRect, i));

            // 일정
            var events = _days[i];
            if (events.Count == 0) continue;
            float y = cell.Y + 24 * S;
            int fit = Math.Max(0, (int)((cell.Bottom - y - 2 * S) / lineH));
            if (fit == 0) continue;
            int show = events.Count > fit ? fit - 1 : events.Count;
            for (int k = 0; k < show; k++)
            {
                var ev = events[k];
                var er = new RectangleF(cell.X + 3 * S, y, cell.Width - 6 * S, lineH - 2 * S);
                DrawEvent(g, ev, er, th, f, shadow, inMonth, _hover.Kind == HitKind.Event && _hover.Event == ev && _hover.Index == i);
                _hits.Add(new Hit(HitKind.Event, er, i, Event: ev));
                y += lineH;
            }
            if (show < events.Count)
            {
                var mr = new RectangleF(cell.X + 3 * S, y, cell.Width - 6 * S, lineH - 2 * S);
                bool hov = _hover.Kind == HitKind.More && _hover.Index == i;
                if (hov) FillRound(g, mr, 4 * S, th.Hover);
                DrawText(g, $"+{events.Count - show}개", f.Small, Theme.WithAlpha(th.Fg, 170), RectangleF.Inflate(mr, -4 * S, 0), Sf.LeftCenter, shadow);
                _hits.Add(new Hit(HitKind.More, mr, i));
            }
        }

        // --- 바닥글: 상태 / 크기 조절
        var statusRect = new RectangleF(pad, gridB, W * 0.6f, footerH - 2 * S);
        bool actionable = !_svc.IsSignedIn && !_svc.IsSigningIn;
        string status = _svc.IsSyncing ? "동기화 중…" : _svc.Status;
        var statusColor = actionable ? th.Accent : Theme.WithAlpha(th.Fg, 120);
        if (actionable && _hover.Kind == HitKind.Status) statusColor = Theme.WithAlpha(th.Accent, 200);
        DrawText(g, status, actionable ? f.SmallBold : f.Small, statusColor, statusRect, Sf.LeftCenter, shadow);
        if (actionable)
        {
            var sz = g.MeasureString(status, f.SmallBold, PointF.Empty, Sf.LeftCenter);
            _hits.Add(new Hit(HitKind.Status, new RectangleF(statusRect.X, statusRect.Y, sz.Width + 4 * S, statusRect.Height)));
        }
        _hits.Add(new Hit(HitKind.Drag, new RectangleF(statusRect.Right, gridB, W - statusRect.Right - 20 * S, footerH)));

        if (!_s.Locked)
        {
            var grip = new RectangleF(W - 20 * S, H - 20 * S, 20 * S, 20 * S);
            using var gb = new SolidBrush(Theme.WithAlpha(th.Fg, _hover.Kind == HitKind.Grip ? 200 : 90));
            float d = 2.2f * S;
            for (int a = 0; a < 3; a++)
                for (int b = 0; b <= a; b++)
                    g.FillEllipse(gb, W - (7 + b * 4.5f) * S - d / 2, H - (7 + (a - b) * 4.5f) * S - d / 2, d, d);
            _hits.Add(new Hit(HitKind.Grip, grip));
        }
    }

    void DrawEvent(Graphics g, CalEvent ev, RectangleF r, Theme th, UiFonts f, bool shadow, bool inMonth, bool hover)
    {
        var cal = _svc.Store.FindCalendar(ev.CalendarId);
        var color = Theme.EventColor(ev.ColorId, cal?.Color ?? "#4F8CFF");
        float S = Scale;
        int dimMul = inMonth ? 255 : 150;

        if (ev.AllDay || ev.IsMultiDay)
        {
            FillRound(g, r, 4 * S, Color.FromArgb((hover ? 255 : 225) * dimMul / 255, color));
            var tr = RectangleF.Inflate(r, -4 * S, 0);
            DrawText(g, ev.Title, f.Event, Theme.WithAlpha(Theme.TextOn(color), dimMul), tr, Sf.LeftCenter, false);
        }
        else
        {
            if (hover) FillRound(g, r, 4 * S, th.Hover);
            var bar = new RectangleF(r.X + 1 * S, r.Y + 2 * S, 3 * S, r.Height - 4 * S);
            FillRound(g, bar, 1.5f * S, Theme.WithAlpha(color, dimMul));
            var text = $"{ev.Start:HH:mm} {ev.Title}";
            var tr = new RectangleF(r.X + 7 * S, r.Y, r.Width - 8 * S, r.Height);
            DrawText(g, text, f.Event, Theme.WithAlpha(th.Fg, inMonth ? 235 : 130), tr, Sf.LeftCenter, shadow);
        }
    }

    void Button(Graphics g, ref float right, float y, float w, float h, Btn id, string glyph, Theme th, UiFonts f,
        bool shadow, bool text = false, bool dim = false)
    {
        var r = new RectangleF(right - w, y, w, h);
        right -= w + 2 * Scale;
        bool hover = _hover.Kind == HitKind.Button && _hover.Button == id;
        if (hover) FillRound(g, r, 6 * Scale, th.Hover);
        var color = Theme.WithAlpha(th.Fg, dim ? 90 : hover ? 255 : 210);
        DrawText(g, glyph, text ? f.ButtonText : f.Icon, color, r, Sf.Center, shadow);
        _hits.Add(new Hit(HitKind.Button, r, Button: id));
    }

    void DrawText(Graphics g, string s, Font font, Color color, RectangleF r, StringFormat sf, bool shadow)
    {
        if (shadow)
        {
            var sr = r;
            sr.Offset(0, Math.Max(1, Scale));
            using var sb = new SolidBrush(Color.FromArgb(color.A * 140 / 255, 0, 0, 0));
            g.DrawString(s, font, sb, sr, sf);
        }
        using var b = new SolidBrush(color);
        g.DrawString(s, font, b, r, sf);
    }

    static void FillRound(Graphics g, RectangleF r, float radius, Color c)
    {
        using var path = RoundRect(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static bool FontExists(string name)
    {
        try { using var ff = new FontFamily(name); return true; }
        catch { return false; }
    }

    // ===================== 입력 =====================

    Hit HitTest(Point p)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Rect.Contains(p)) return _hits[i];
        return default;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitTest(e.Location);
        if (h == _hover) return;
        _hover = h;
        Cursor = h.Kind switch
        {
            HitKind.Button or HitKind.Event or HitKind.More or HitKind.Status or HitKind.DayNumber => Cursors.Hand,
            HitKind.Grip => Cursors.SizeNWSE,
            _ => Cursors.Default,
        };
        Render();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover.Kind != HitKind.None)
        {
            _hover = default;
            Render();
        }
        if (_peek && !Bounds.Contains(Cursor.Position)) EndPeek();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var h = HitTest(e.Location);
        _pressed = default;
        if (e.Button == MouseButtons.Right)
        {
            ShowMenu(PointToScreen(e.Location));
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (e.Clicks == 2 && h.Kind is HitKind.Cell)
        {
            OpenEditor(null, _gridStart.AddDays(h.Index));
            return;
        }
        if (!_s.Locked && h.Kind is HitKind.Drag or HitKind.Grip)
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN,
                h.Kind == HitKind.Grip ? Native.HTBOTTOMRIGHT : Native.HTCAPTION, IntPtr.Zero);
            return;
        }
        _pressed = h;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var h = HitTest(e.Location);
        var p = _pressed;
        _pressed = default;
        if (p.Kind == HitKind.None || p.Kind != h.Kind || p.Index != h.Index || p.Button != h.Button) return;

        switch (h.Kind)
        {
            case HitKind.Button: OnButton(h.Button, h.Rect); break;
            case HitKind.Event: OpenEditor(h.Event, _gridStart.AddDays(h.Index)); break;
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
            case HitKind.Status: _ = _svc.SignInAsync(this); break;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Navigate(e.Delta > 0 ? -1 : 1);
    }

    void OnButton(Btn b, RectangleF rect)
    {
        switch (b)
        {
            case Btn.Prev: Navigate(-1); break;
            case Btn.Next: Navigate(1); break;
            case Btn.Today:
                _selected = DateTime.Today;
                if (_year == _selected.Year && _month == _selected.Month) Render();
                else GoTo(_selected.Year, _selected.Month);
                break;
            case Btn.Sync:
                if (_svc.IsSignedIn) _ = _svc.SyncAsync();
                else _ = _svc.SignInAsync(this);
                break;
            case Btn.Add: OpenEditor(null, _selected); break;
            case Btn.Menu: ShowMenu(PointToScreen(new Point((int)rect.Right, (int)rect.Bottom))); break;
        }
    }

    void ShowMenu(Point screen)
    {
        // 활성화되지 않은 창에서 띄운 메뉴는 바깥 클릭으로 닫히지 않으므로 먼저 전경으로
        Native.SetForegroundWindow(Handle);
        _menu.Show(screen, ToolStripDropDownDirection.Default);
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
        _s.X = Left;
        _s.Y = Top;
        _s.Width = (int)Math.Round(Width / Scale);
        _s.Height = (int)Math.Round(Height / Scale);
        _s.Save();
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
            _navTimer.Dispose();
            _peekTimer.Dispose();
            _fonts?.Dispose();
            _editor?.Close();
            _dayList?.Close();
        }
        FreeSurface();
        base.Dispose(disposing);
    }

    // ===================== 글꼴 / 정렬 =====================

    sealed class UiFonts : IDisposable
    {
        public readonly float Scale;
        public readonly Font Title, Weekday, Day, DayBold, Event, Small, SmallBold, Icon, ButtonText;

        public UiFonts(float s)
        {
            Scale = s;
            Title = new Font(UiFamily, 19 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            Weekday = new Font(UiFamily, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            Day = new Font(UiFamily, 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            DayBold = new Font(UiFamily, 12.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            Event = new Font(UiFamily, 11 * s, FontStyle.Regular, GraphicsUnit.Pixel);
            Small = new Font(UiFamily, 11 * s, FontStyle.Regular, GraphicsUnit.Pixel);
            SmallBold = new Font(UiFamily, 11 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            Icon = new Font(IconFamily, 13 * s, FontStyle.Regular, GraphicsUnit.Pixel);
            ButtonText = new Font(UiFamily, 12 * s, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            foreach (var f in new[] { Title, Weekday, Day, DayBold, Event, Small, SmallBold, Icon, ButtonText }) f.Dispose();
        }
    }

    static class Sf
    {
        // GenericTypographic 기반: GDI+ 기본 서식은 한글 자간이 넓게 벌어진다
        public static readonly StringFormat Center = Make(StringAlignment.Center, StringTrimming.None);
        public static readonly StringFormat LeftCenter = Make(StringAlignment.Near, StringTrimming.EllipsisCharacter);

        static StringFormat Make(StringAlignment align, StringTrimming trimming)
        {
            var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            sf.Alignment = align;
            sf.LineAlignment = StringAlignment.Center;
            sf.Trimming = trimming;
            return sf;
        }
    }
}
