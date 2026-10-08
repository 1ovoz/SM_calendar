using System.ComponentModel;
using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>앱 수명 관리: 트레이 아이콘, 메뉴, 위젯.</summary>
internal sealed class TrayContext : ApplicationContext
{
    static readonly double[] OpacityLevels = [0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.0];
    static readonly int[] SyncLevels = [1, 5, 15, 30, 60];

    readonly AppSettings _s;
    readonly CalendarService _svc;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu = new();
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 30_000 };
    CalendarWidget _widget = null!;
    DateTime _today = DateTime.Today;
    bool _exiting;

    public TrayContext(bool demo)
    {
        if (demo) AppPaths.UseDemo();
        _s = AppSettings.Load();
        _svc = new CalendarService(_s, demo);
        _svc.Init();

        _menu.Opening += BuildMenu;
        _menu.Closing += KeepOpenOnClick;
        _tray = new NotifyIcon
        {
            Text = "SM Calendar",
            Icon = IconFactory.CreateTrayIcon(_today.Day, SystemInformation.SmallIconSize.Width * 2),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _widget.Peek(); };

        if (!_s.FirstRunDone && !demo)
        {
            _s.FirstRunDone = true;
            StartupRegistry.Set(true);
            _s.Save();
        }
        else if (!demo)
        {
            StartupRegistry.RefreshPath();
        }

        CreateWidget();

        _clock.Tick += (_, _) => CheckDate();
        _clock.Start();

        if (_svc.IsSignedIn) _svc.ScheduleSync(TimeSpan.FromMilliseconds(300));
    }

    void CreateWidget()
    {
        _widget = new CalendarWidget(_s, _svc, _menu);
        _widget.HandleDestroyed += OnWidgetHandleDestroyed;
        _widget.Show();
    }

    /// <summary>
    /// 탐색기(explorer.exe)가 재시작되면 소유자인 바탕화면 창과 함께 위젯 창도 파괴된다.
    /// 그럴 때 잠시 뒤 위젯을 다시 만든다.
    /// </summary>
    void OnWidgetHandleDestroyed(object? sender, EventArgs e)
    {
        if (_exiting || sender is not CalendarWidget w || w.RecreatingHandle) return;
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            if (_exiting) return;
            w.HandleDestroyed -= OnWidgetHandleDestroyed;
            w.Dispose();
            CreateWidget();
        };
        t.Start();
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
        _widget.DayChanged(old);
    }

    // ===================== 메뉴 =====================

    void BuildMenu(object? sender, CancelEventArgs e)
    {
        DisposeItems(_menu.Items);

        if (_svc.IsSignedIn)
        {
            Add(_menu.Items, "지금 동기화", () => _ = _svc.SyncAsync(), enabled: !_svc.Demo && !_svc.IsSyncing);
        }
        else
        {
            Add(_menu.Items, "구글 계정 연결…", () => _ = _svc.SignInAsync(null), enabled: !_svc.IsSigningIn);
        }
        Add(_menu.Items, "새 일정…", () => _widget.OpenEditor(null, DateTime.Today), enabled: _svc.IsSignedIn);
        _menu.Items.Add(new ToolStripSeparator());

        // 캘린더 표시
        var cals = new ToolStripMenuItem("표시할 캘린더");
        foreach (var c in _svc.Store.Calendars)
        {
            var cal = c;
            Add(cals.DropDownItems, c.Name, () =>
            {
                _s.ToggleCalendar(cal);
                _s.Save();
                _widget.RebuildDays();
                _widget.Render();
                if (_s.IsCalendarVisible(cal)) _svc.ScheduleSync(TimeSpan.FromMilliseconds(100));
            }, check: _s.IsCalendarVisible(c), keepOpen: true, color: Theme.Hex(c.Color, Color.SteelBlue));
        }
        if (cals.DropDownItems.Count == 0) Add(cals.DropDownItems, "(동기화 후 표시됩니다)", () => { }, enabled: false);
        cals.DropDown.Closing += KeepOpenOnClick;
        _menu.Items.Add(cals);

        // 모양
        var look = new ToolStripMenuItem("모양");
        var opacity = new ToolStripMenuItem("배경 불투명도");
        foreach (var o in OpacityLevels)
        {
            var v = o;
            Add(opacity.DropDownItems, $"{v * 100:0}%", () => { _s.Opacity = v; Apply(); }, check: Math.Abs(_s.Opacity - v) < 0.01);
        }
        look.DropDownItems.Add(opacity);
        Add(look.DropDownItems, "어두운 테마", () => { _s.DarkTheme = true; Apply(); }, check: _s.DarkTheme);
        Add(look.DropDownItems, "밝은 테마", () => { _s.DarkTheme = false; Apply(); }, check: !_s.DarkTheme);
        look.DropDownItems.Add(new ToolStripSeparator());
        Add(look.DropDownItems, "일요일부터 시작", () => { _s.WeekStartsMonday = false; Apply(grid: true); }, check: !_s.WeekStartsMonday);
        Add(look.DropDownItems, "월요일부터 시작", () => { _s.WeekStartsMonday = true; Apply(grid: true); }, check: _s.WeekStartsMonday);
        _menu.Items.Add(look);

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
        _menu.Items.Add(sync);
        _menu.Items.Add(new ToolStripSeparator());

        Add(_menu.Items, "바탕화면에 고정", () => { _s.PinToDesktop = !_s.PinToDesktop; _s.Save(); _widget.ApplyPin(); }, check: _s.PinToDesktop);
        Add(_menu.Items, "위치/크기 잠금", () => { _s.Locked = !_s.Locked; Apply(); }, check: _s.Locked);
        Add(_menu.Items, "Windows 시작 시 실행", () => StartupRegistry.Set(!StartupRegistry.IsEnabled()),
            check: StartupRegistry.IsEnabled(), enabled: !_svc.Demo);
        _menu.Items.Add(new ToolStripSeparator());

        if (_svc.IsSignedIn && !_svc.Demo)
        {
            Add(_menu.Items, $"로그아웃 ({_svc.Account ?? "Google"})", () =>
            {
                if (MessageBox.Show("Google 계정 연결을 해제할까요?", "SM Calendar", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    _svc.SignOut();
            });
        }
        Add(_menu.Items, "종료", ExitApp);
        e.Cancel = false;
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

    void Apply(bool grid = false)
    {
        _s.Save();
        if (grid) _widget.SettingsChanged();
        else _widget.Render();
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
        _clock.Stop();
        _tray.Visible = false;
        _widget.Dispose();
        _tray.Dispose();
        ExitThread();
    }
}
