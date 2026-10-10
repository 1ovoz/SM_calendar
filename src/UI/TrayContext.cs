using System.ComponentModel;
using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>
/// 앱 수명 관리: 트레이 아이콘, 메뉴, 위젯들.
/// 위젯을 모두 끄면 창을 전부 해제하고 동기화를 멈춘 뒤 메모리를 정리해서, 트레이 아이콘만 남는다.
/// </summary>
internal sealed class TrayContext : ApplicationContext, IWidgetHost
{
    static readonly double[] OpacityLevels = [0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.0];
    static readonly int[] SyncLevels = [1, 5, 15, 30, 60];
    static readonly WidgetKind[] Kinds = [WidgetKind.Month, WidgetKind.MiniMonth, WidgetKind.MiniAgenda, WidgetKind.MiniApp];

    readonly AppSettings _s;
    readonly CalendarService _svc;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu = new();
    readonly System.Windows.Forms.Timer _clock = new();
    readonly System.Windows.Forms.Timer _trim = new() { Interval = 1500 };
    readonly List<CalendarWidget> _widgets = new();
    readonly HashSet<CalendarWidget> _closing = new();
    CalendarWidget? _menuTarget;
    Font? _menuFont;

    /// <summary>앱을 한 번 더 실행하면 이 신호로 위젯을 보이게 한다.</summary>
    public const string ShowEventName = @"Local\SMCalendar.Show";
    readonly EventWaitHandle _showEvent = new(false, EventResetMode.AutoReset, ShowEventName);
    readonly RegisteredWaitHandle _showWait;
    readonly SynchronizationContext _ui;
    DateTime _today = DateTime.Today;
    bool _exiting;

    public TrayContext(bool demo)
    {
        if (demo) AppPaths.UseDemo();
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = SynchronizationContext.Current!;
        // 대기 스레드를 따로 만들지 않고 스레드 풀의 대기 기능을 쓴다 (평소 CPU 0)
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _ui.Post(_ => { if (!_exiting) ShowWidget(); }, null), null, Timeout.Infinite, executeOnlyOnce: false);
        _s = AppSettings.Load();
        Ui.UseFont(_s.FontName);
        Dialogs.Dark = _s.DarkTheme;
        _svc = new CalendarService(_s, demo);
        _svc.Init();

        _menu.Opening += BuildMenu;
        _menu.Closing += KeepOpenOnClick;
        _menu.Closed += (_, _) => _menuTarget = null;
        _menu.HandleCreated += (_, _) => Ui.RoundCorners(_menu.Handle, small: true);
        _tray = new NotifyIcon
        {
            Text = "SM Calendar (클릭: 위젯 켜기/끄기)",
            Icon = IconFactory.CreateTrayIcon(_today.Day, SystemInformation.SmallIconSize.Width * 2),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleWidget(); };

        if (!_s.FirstRunDone && !demo)
        {
            _s.FirstRunDone = true;
            StartupRegistry.Set(true);
        }
        else if (!demo)
        {
            StartupRegistry.RefreshPath();
        }
        if (!demo) StartMenuShortcut.EnsureStartMenu();
        _s.Save();

        // 위젯이 켜져 있을 때만 만든다. 꺼져 있으면 캐시도 읽지 않고 트레이만 띄운다.
        if (!Widget.Hidden) CreateWidget(Widget);
        if (_widgets.Count > 0) _svc.Resume();
        else _svc.Pause();

        _clock.Tick += (_, _) => { CheckDate(); ScheduleClock(); };
        ScheduleClock();
        _trim.Tick += (_, _) => { _trim.Stop(); Native.TrimMemory(); };
        // 시작 직후 한 번 정리 (시작에만 쓰인 메모리를 돌려준다). 위젯이 없으면 바로, 있으면 첫 동기화가 끝날 즈음
        _trim.Interval = _widgets.Count == 0 ? 1500 : 20000;
        _trim.Start();
    }

    /// <summary>자정에 맞춰 날짜 표시를 갱신 (절전 복귀 대비 최대 15분 간격으로 확인).</summary>
    void ScheduleClock()
    {
        _clock.Stop();
        var untilMidnight = DateTime.Today.AddDays(1).AddSeconds(2) - DateTime.Now;
        _clock.Interval = (int)Math.Clamp(untilMidnight.TotalMilliseconds, 1000, 15 * 60 * 1000);
        _clock.Start();
    }

    void CheckDate()
    {
        var now = DateTime.Today;
        if (now == _today) return;
        var old = _today;
        _today = now;
        var oldIcon = _tray.Icon;
        _tray.Icon = IconFactory.CreateTrayIcon(now.Day, SystemInformation.SmallIconSize.Width * 2);
        oldIcon?.Dispose();
        foreach (var w in _widgets) w.DayChanged(old);
    }

    // ===================== 위젯 관리 =====================

    void CreateWidget(WidgetSettings ws)
    {
        _svc.EnsureLoaded();
        var w = new CalendarWidget(_s, ws, _svc, this);
        w.HandleDestroyed += OnWidgetHandleDestroyed;
        _widgets.Add(w);
        w.Show();
    }

    void DestroyWidget(CalendarWidget w)
    {
        _closing.Add(w);
        _widgets.Remove(w);
        w.HandleDestroyed -= OnWidgetHandleDestroyed;
        w.Dispose();
        _closing.Remove(w);
    }

    /// <summary>
    /// 탐색기(explorer.exe)가 재시작되면 소유자인 바탕화면 창과 함께 위젯 창도 파괴된다.
    /// 그럴 때 잠시 뒤 위젯을 다시 만든다.
    /// </summary>
    void OnWidgetHandleDestroyed(object? sender, EventArgs e)
    {
        if (_exiting || sender is not CalendarWidget w || w.RecreatingHandle || _closing.Contains(w)) return;
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            if (_exiting || !_widgets.Contains(w)) return;
            DestroyWidget(w);
            CreateWidget(w.Settings);
        };
        t.Start();
    }

    public void HideWidget(CalendarWidget w)
    {
        w.Settings.Hidden = true;
        DestroyWidget(w);
        _s.Save();
        AfterVisibilityChange();
    }

    /// <summary>위젯 설정 (항상 하나).</summary>
    WidgetSettings Widget
    {
        get
        {
            if (_s.Widgets.Count == 0) _s.Widgets.Add(WidgetSettings.Create(WidgetKind.Month));
            return _s.Widgets[0];
        }
    }

    void ShowWidget()
    {
        var ws = Widget;
        ws.Hidden = false;
        if (_widgets.Count == 0) CreateWidget(ws);
        _widgets[0].Peek(); // 이미 켜져 있으면 다른 창 위로 잠깐 보여 준다
        _s.Save();
        AfterVisibilityChange();
    }

    /// <summary>트레이 아이콘 클릭: 위젯이 켜져 있으면 끄고(트레이로), 꺼져 있으면 켠다.</summary>
    void ToggleWidget()
    {
        if (_widgets.Count > 0) HideWidget(_widgets[0]);
        else ShowWidget();
    }

    /// <summary>위젯 종류 변경 (새로 추가하지 않고 지금 위젯을 바꾼다). 왼쪽 위 위치는 유지.</summary>
    void ChangeKind(WidgetKind kind)
    {
        var ws = Widget;
        if (ws.Kind == kind && _widgets.Count > 0) return;
        var open = _widgets.FirstOrDefault();
        if (open != null) DestroyWidget(open);
        ws.ChangeKind(kind);
        ws.Collapsed = false;
        ws.Hidden = false;
        CreateWidget(ws);
        _s.Save();
        AfterVisibilityChange();
    }

    void AfterVisibilityChange()
    {
        if (_widgets.Count > 0)
        {
            _svc.Resume();
            _trim.Stop();
        }
        else
        {
            // 백그라운드 대기: 동기화 중지 + 메모리 정리
            _svc.Pause();
            _trim.Stop();
            _trim.Interval = 1500;
            _trim.Start();
        }
    }

    public void SaveSettings() => _s.Save();

    public void ShowWidgetMenu(CalendarWidget widget, Point screen)
    {
        _menuTarget = widget;
        // 활성화되지 않은 창에서 띄운 메뉴는 바깥 클릭으로 닫히지 않으므로 먼저 전경으로
        Native.SetForegroundWindow(widget.Handle);
        _menu.Show(screen, ToolStripDropDownDirection.Default);
    }

    void ForAll(Action<CalendarWidget> action)
    {
        foreach (var w in _widgets) action(w);
    }

    // ===================== 메뉴 =====================

    void BuildMenu(object? sender, CancelEventArgs e)
    {
        DisposeItems(_menu.Items);
        var pal = Palette.For(_s.DarkTheme);
        var target = _menuTarget ?? _widgets.FirstOrDefault();

        if (_svc.IsSignedIn)
            Add(_menu.Items, "지금 동기화", () => _ = _svc.SyncAsync(), enabled: !_svc.Demo && !_svc.IsSyncing);
        else
            Add(_menu.Items, "구글 계정 연결…", () => _ = _svc.SignInAsync(null), enabled: !_svc.IsSigningIn);
        Add(_menu.Items, "새 일정…", () => target?.OpenEditor(null, DateTime.Today), enabled: _svc.IsSignedIn && target != null);
        _menu.Items.Add(new ToolStripSeparator());

        // ---- 위젯 종류 변경 (하나의 위젯을 다른 형태로 바꾼다)
        var change = new ToolStripMenuItem("위젯 변경");
        foreach (var kind in Kinds)
        {
            var k = kind;
            Add(change.DropDownItems, WidgetSettings.KindName(k), () => ChangeKind(k), check: Widget.Kind == k);
        }
        _menu.Items.Add(change);

        // ---- 이 위젯 (켜져 있을 때)
        if (target != null)
        {
            var ws = target.Settings;
            var opacity = new ToolStripMenuItem("배경 불투명도");
            foreach (var o in OpacityLevels)
            {
                var v = o;
                Add(opacity.DropDownItems, $"{v * 100:0}%", () => { target.SetOpacity(v); _s.Save(); }, check: Math.Abs(ws.Opacity - v) < 0.01);
            }
            _menu.Items.Add(opacity);
            var font = new ToolStripMenuItem("글자 크기");
            foreach (var (name, scale) in ViewPanel.FontSizes)
            {
                var v = scale;
                Add(font.DropDownItems, name, () => target.SetFontScale(v), check: Math.Abs(ws.FontScale - v) < 0.01);
            }
            _menu.Items.Add(font);
            Add(_menu.Items, ws.Collapsed ? "펼치기" : "접기", target.ToggleCollapsed);
            // 표시 방식은 둘 중 하나 (일반 창 모드는 없음)
            Add(_menu.Items, "바탕화면에 고정", () => { if (ws.AlwaysOnTop) target.ToggleAlwaysOnTop(); }, check: !ws.AlwaysOnTop);
            Add(_menu.Items, "항상 위에 표시", () => { if (!ws.AlwaysOnTop) target.ToggleAlwaysOnTop(); }, check: ws.AlwaysOnTop);
            Add(_menu.Items, "위치/크기 잠금", () => { ws.Locked = !ws.Locked; _s.Save(); target.Render(); }, check: ws.Locked);
            Add(_menu.Items, "위젯 끄기 (트레이로)", () => HideWidget(target));
        }
        else
        {
            Add(_menu.Items, "위젯 켜기", ShowWidget);
        }
        _menu.Items.Add(new ToolStripSeparator());

        // ---- 전체 설정
        var cals = new ToolStripMenuItem("표시할 캘린더");
        _svc.EnsureLoaded();
        foreach (var c in _svc.Store.Calendars)
        {
            var cal = c;
            Add(cals.DropDownItems, c.Name, () =>
            {
                _s.ToggleCalendar(cal);
                _s.Save();
                ForAll(w => { w.RebuildDays(); w.Render(); });
                if (_s.IsCalendarVisible(cal)) _svc.ScheduleSync(TimeSpan.FromMilliseconds(100));
            }, check: _s.IsCalendarVisible(c), keepOpen: true, color: Theme.Hex(c.Color, Color.SteelBlue));
        }
        if (cals.DropDownItems.Count == 0) Add(cals.DropDownItems, "(동기화 후 표시됩니다)", () => { }, enabled: false);
        cals.DropDown.Closing += KeepOpenOnClick;
        _menu.Items.Add(cals);

        var prefs = new ToolStripMenuItem("설정");
        Add(prefs.DropDownItems, "어두운 테마", () => { _s.DarkTheme = true; ApplyGlobal(); }, check: _s.DarkTheme);
        Add(prefs.DropDownItems, "밝은 테마", () => { _s.DarkTheme = false; ApplyGlobal(); }, check: !_s.DarkTheme);
        prefs.DropDownItems.Add(new ToolStripSeparator());
        Add(prefs.DropDownItems, "글꼴: Pretendard", () => SetFont(Ui.Pretendard), check: _s.FontName == Ui.Pretendard);
        Add(prefs.DropDownItems, "글꼴: 맑은 고딕", () => SetFont(Ui.Malgun), check: _s.FontName == Ui.Malgun);
        prefs.DropDownItems.Add(new ToolStripSeparator());
        Add(prefs.DropDownItems, "일요일부터 시작", () => { _s.WeekStartsMonday = false; ApplyGlobal(); }, check: !_s.WeekStartsMonday);
        Add(prefs.DropDownItems, "월요일부터 시작", () => { _s.WeekStartsMonday = true; ApplyGlobal(); }, check: _s.WeekStartsMonday);
        prefs.DropDownItems.Add(new ToolStripSeparator());
        var sync = new ToolStripMenuItem("동기화 주기");
        foreach (var m in SyncLevels)
        {
            var v = m;
            Add(sync.DropDownItems, v < 60 ? $"{v}분" : $"{v / 60}시간", () =>
            {
                _s.SyncMinutes = v;
                _s.Save();
                if (_svc.IsSignedIn && !_svc.IsSyncing) _svc.ScheduleSync(TimeSpan.FromMinutes(v));
            }, check: _s.SyncMinutes == v);
        }
        prefs.DropDownItems.Add(sync);
        Add(prefs.DropDownItems, "Windows 시작 시 실행", () => StartupRegistry.Set(!StartupRegistry.IsEnabled()),
            check: StartupRegistry.IsEnabled(), enabled: !_svc.Demo);
        _menu.Items.Add(prefs);
        _menu.Items.Add(new ToolStripSeparator());

        if (_svc.IsSignedIn && !_svc.Demo)
        {
            Add(_menu.Items, $"로그아웃 ({_svc.Account ?? "Google"})", () =>
            {
                if (Dialogs.Confirm(null, "Google 계정 연결을 해제할까요?",
                        "이 PC 에 저장된 로그인 정보와 일정 캐시가 삭제됩니다. 다시 연결하면 처음부터 동기화합니다.",
                        ok: "로그아웃", danger: true))
                    _svc.SignOut();
            });
        }
        Add(_menu.Items, "종료", ExitApp);

        // 편집 창과 같은 글꼴·색·둥근 모양으로
        _menuFont?.Dispose();
        _menuFont = Ui.Font(13.5f * _menu.DeviceDpi / 96f);
        DarkMenuRenderer.Style(_menu, pal, _menuFont);
        e.Cancel = false;
    }

    void SetFont(string name)
    {
        _s.FontName = name;
        Ui.UseFont(name);
        ApplyGlobal();
    }

    void ApplyGlobal()
    {
        Dialogs.Dark = _s.DarkTheme;
        _s.Save();
        ForAll(w => w.SettingsChanged());
    }

    static void DisposeItems(ToolStripItemCollection items)
    {
        var old = items.Cast<ToolStripItem>().ToArray();
        items.Clear();
        foreach (var item in old)
        {
            if (item is ToolStripMenuItem mi) DisposeItems(mi.DropDownItems);
            item.Image?.Dispose();
            item.Dispose();
        }
    }

    static void Add(ToolStripItemCollection items, string text, Action onClick, bool check = false, bool enabled = true,
        bool keepOpen = false, Color? color = null)
    {
        var item = new ToolStripMenuItem(text) { Checked = check, Enabled = enabled };
        if (color is { } c)
        {
            var bmp = new Bitmap(12, 12);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var b = new SolidBrush(c);
                g.FillEllipse(b, 1, 1, 10, 10);
            }
            item.Image = bmp;
        }
        item.Click += (_, _) =>
        {
            onClick();
            if (keepOpen)
            {
                item.Checked = !item.Checked;
                _keepOpenUntil = Environment.TickCount64 + 300;
            }
        };
        items.Add(item);
    }

    /// <summary>캘린더 여러 개를 연달아 켜고 끌 수 있게 클릭 직후에는 메뉴를 닫지 않는다.</summary>
    static long _keepOpenUntil;

    static void KeepOpenOnClick(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && Environment.TickCount64 < _keepOpenUntil) e.Cancel = true;
    }

    void ExitApp()
    {
        _exiting = true;
        _showWait.Unregister(null);
        _showEvent.Dispose();
        _clock.Stop();
        _trim.Stop();
        _tray.Visible = false;
        foreach (var w in _widgets.ToList()) DestroyWidget(w);
        _s.Save();
        _tray.Dispose();
        ExitThread();
    }
}
