using System.Text.Json;

namespace SMCalendar.Core;

public sealed class AppSettings
{
    /// <summary>화면 좌표(물리 픽셀). int.MinValue 면 기본 위치.</summary>
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    /// <summary>96 DPI 기준 논리 크기.</summary>
    public int Width { get; set; } = 560;
    public int Height { get; set; } = 480;

    /// <summary>배경 불투명도 0.0 ~ 1.0 (글자는 항상 선명하게 표시).</summary>
    public double Opacity { get; set; } = 0.45;
    public bool DarkTheme { get; set; } = true;
    public bool Locked { get; set; }
    public bool PinToDesktop { get; set; } = true;
    public bool WeekStartsMonday { get; set; }
    public int SyncMinutes { get; set; } = 5;
    public bool FirstRunDone { get; set; }
    public string? DefaultCalendarId { get; set; }

    /// <summary>사용자가 직접 숨긴/표시한 캘린더. 둘 다 없으면 Google 의 표시 설정을 따른다.</summary>
    public List<string> HiddenCalendars { get; set; } = new();
    public List<string> ShownCalendars { get; set; } = new();

    public bool IsCalendarVisible(CalendarInfo c) =>
        ShownCalendars.Contains(c.Id) || (!HiddenCalendars.Contains(c.Id) && c.SelectedInGoogle);

    public void ToggleCalendar(CalendarInfo c)
    {
        bool visible = IsCalendarVisible(c);
        HiddenCalendars.Remove(c.Id);
        ShownCalendars.Remove(c.Id);
        (visible ? HiddenCalendars : ShownCalendars).Add(c.Id);
    }

    static string FilePath => AppPaths.File("settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), JsonCtx.Default.AppSettings) ?? new();
        }
        catch { /* 손상된 설정은 무시하고 기본값 */ }
        return new();
    }

    public void Save()
    {
        try { AppPaths.WriteAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(this, JsonCtx.Default.AppSettings)); }
        catch { }
    }
}
