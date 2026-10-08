using SMCalendar.UI;

namespace SMCalendar;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 이미 실행 중이면 조용히 종료 (시작 프로그램 + 수동 실행 중복 방지)
        using var mutex = new Mutex(true, @"Local\SMCalendar.SingleInstance", out bool created);
        if (!created) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int snap = Array.IndexOf(args, "--snapshot");
        if (snap >= 0 && snap + 1 < args.Length)
        {
            Snapshot(args[snap + 1], args);
            return;
        }
        Application.Run(new TrayContext(demo: args.Contains("--demo")));
    }

    /// <summary>개발용: 데모 데이터로 위젯을 그려 PNG 로 저장하고 종료.</summary>
    static void Snapshot(string path, string[] args)
    {
        Core.AppPaths.UseDemo();
        var settings = new Core.AppSettings
        {
            DarkTheme = !args.Contains("--light"),
            Opacity = args.Contains("--clear") ? 0.0 : 0.45,
            WeekStartsMonday = args.Contains("--monday"),
        };
        var svc = new Core.CalendarService(settings, demo: true);
        svc.Init();
        using var menu = new ContextMenuStrip();
        using var w = new CalendarWidget(settings, svc, menu);
        w.CreateControl();
        _ = w.Handle;
        if (args.Contains("--editor"))
        {
            var ev = svc.Store.Events.First(e => !e.AllDay);
            using var f = new EventEditorForm(svc, settings, ev, ev.Start.Date) { StartPosition = FormStartPosition.Manual, Location = new Point(100, 100) };
            f.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
            bmp.Save(path);
            return;
        }
        w.SaveSnapshot(path);
    }
}
