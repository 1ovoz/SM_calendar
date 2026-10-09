using SMCalendar.UI;

namespace SMCalendar;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int tls = Array.IndexOf(args, "--tls-check");
        if (tls >= 0 && tls + 1 < args.Length)
        {
            TlsCheck(args[tls + 1]);
            return;
        }
        int snap = Array.IndexOf(args, "--snapshot");
        if (snap >= 0 && snap + 1 < args.Length)
        {
            Snapshot(args[snap + 1], args);
            return;
        }

        // 이미 실행 중이면: 직접 실행한 경우엔 켜져 있는 앱에 '위젯 보이기'를 알리고 종료
        // (Windows 시작 시 자동 실행이 겹친 경우엔 조용히 종료)
        using var mutex = new Mutex(true, @"Local\SMCalendar.SingleInstance", out bool created);
        if (!created)
        {
            if (!args.Contains("--startup") && EventWaitHandle.TryOpenExisting(TrayContext.ShowEventName, out var show))
            {
                show.Set();
                show.Dispose();
            }
            return;
        }

        Application.Run(new TrayContext(demo: args.Contains("--demo")));
    }

    /// <summary>개발용: 인증서 고정(pinning)이 Google 은 통과시키고 다른 곳은 막는지 확인.</summary>
    static void TlsCheck(string path)
    {
        var lines = new List<string>();
        foreach (var url in new[]
                 {
                     "https://www.googleapis.com/calendar/v3/users/me/calendarList",
                     "https://oauth2.googleapis.com/token",
                     "https://letsencrypt.org/",
                     "https://www.microsoft.com/",
                 })
        {
            try
            {
                using var resp = Core.Net.Http.GetAsync(url).GetAwaiter().GetResult();
                lines.Add($"CONNECTED  {(int)resp.StatusCode}  {url}");
            }
            catch (Exception ex)
            {
                lines.Add($"BLOCKED    {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}  {url}");
            }
        }
        File.WriteAllLines(path, lines);
    }

    /// <summary>개발용: 데모 데이터로 위젯을 그려 PNG 로 저장하고 종료.</summary>
    static void Snapshot(string path, string[] args)
    {
        Core.AppPaths.UseDemo();
        var settings = new Core.AppSettings
        {
            DarkTheme = !args.Contains("--light"),
            WeekStartsMonday = args.Contains("--monday"),
        };
        int k = Array.IndexOf(args, "--kind");
        var kind = k >= 0 && k + 1 < args.Length && Enum.TryParse<Core.WidgetKind>(args[k + 1], out var parsed) ? parsed : Core.WidgetKind.Month;
        var ws = Core.WidgetSettings.Create(kind);
        ws.Opacity = args.Contains("--clear") ? 0.0 : args.Contains("--solid") ? 0.92 : 0.45;
        ws.Collapsed = args.Contains("--collapsed");
        ws.AlwaysOnTop = args.Contains("--top");
        if (args.Contains("--big")) ws.FontScale = 1.55;
        if (args.Contains("--small")) ws.FontScale = 0.85;
        ws.X = 100;
        ws.Y = 100;
        var svc = new Core.CalendarService(settings, demo: true);
        svc.Init();
        using var w = new CalendarWidget(settings, ws, svc, new SnapshotHost());
        w.CreateControl();
        _ = w.Handle;
        if (args.Contains("--popups"))
        {
            // 팝업 컨트롤들을 한 장에 그려서 확인
            var pal = UI.Palette.For(settings.DarkTheme);
            using var font = UI.Ui.Font(13.5f * w.DeviceDpi / 96f);
            var controls = new Control[]
            {
                new UI.MiniCalendar(pal, DateTime.Today, false) { Font = font },
                new UI.OptionList(pal, Core.Reminders.Options(false).Select(m => new UI.OptionItem(Core.Reminders.Label(m, false), m, Selected: m == 30)).ToList(), 180) { Font = font },
                new UI.OptionList(pal, svc.Store.Calendars.Select(c => new UI.OptionItem(c.Name, c, UI.Theme.Hex(c.Color, Color.Blue), c.Primary)).ToList(), 240) { Font = font },
                new UI.ViewPanel(pal, 0.45, 1.0, font),
                new UI.MonthPicker(pal, DateTime.Today.Year, DateTime.Today.Month, font),
            };
            using var host = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(100, 100), BackColor = Color.Gray };
            int x = 10;
            foreach (var c in controls) { c.Location = new Point(x, 10); host.Controls.Add(c); x += c.Width + 10; }
            host.ClientSize = new Size(x, controls.Max(c => c.Height) + 20);
            host.Show();
            Application.DoEvents();
            using var img = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(img, new Rectangle(Point.Empty, host.Size));
            img.Save(path);
            return;
        }
        if (args.Contains("--dialog"))
        {
            using var d = new UI.ThemedDialog(UI.Palette.For(settings.DarkTheme), "일정을 삭제할까요?", "'치과 예약'", "삭제", "취소", UI.DialogIcon.Danger)
                { StartPosition = FormStartPosition.Manual, Location = new Point(100, 100) };
            d.Show();
            Application.DoEvents();
            using var img = new Bitmap(d.Width, d.Height);
            d.DrawToBitmap(img, new Rectangle(Point.Empty, d.Size));
            img.Save(path);
            return;
        }
        if (args.Contains("--menu"))
        {
            // 메뉴 모양 확인용: 항목을 그대로 그려 본다
            var pal = UI.Palette.For(settings.DarkTheme);
            using var menu = new ContextMenuStrip();
            foreach (var t in new[] { "지금 동기화", "새 일정…", "-", "위젯 변경", "배경 불투명도", "글자 크기", "접기", "항상 위에 표시", "바탕화면에 고정", "-", "종료" })
            {
                if (t == "-") { menu.Items.Add(new ToolStripSeparator()); continue; }
                var mi = new ToolStripMenuItem(t) { Checked = t == "바탕화면에 고정" };
                if (t is "위젯 변경" or "글자 크기") mi.DropDownItems.Add("하위");
                menu.Items.Add(mi);
            }
            using var mf = UI.Ui.Font(13.5f * menu.DeviceDpi / 96f);
            UI.DarkMenuRenderer.Style(menu, pal, mf);
            menu.Show(new Point(100, 100));
            Application.DoEvents();
            using var img = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(img, new Rectangle(Point.Empty, menu.Size));
            img.Save(path);
            menu.Close();
            return;
        }
        if (args.Contains("--editor"))
        {
            var ev = args.Contains("--new") ? null
                   : args.Contains("--recurring") ? svc.Store.Events.First(e => e.RecurringEventId != null)
                   : svc.Store.Events.First(e => !e.AllDay);
            using var f = new EventEditorForm(svc, settings, ev, DateTime.Today) { StartPosition = FormStartPosition.Manual, Location = new Point(100, 100) };
            f.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
            bmp.Save(path);
            return;
        }
        w.SaveSnapshot(path, hover: args.Contains("--hover"));
    }

    sealed class SnapshotHost : IWidgetHost
    {
        public void ShowWidgetMenu(CalendarWidget widget, Point screen) { }
        public void HideWidget(CalendarWidget widget) { }
        public void SaveSettings() { }
    }
}
