using Microsoft.Win32;

namespace SMCalendar.Core;

internal static class StartupRegistry
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "SMCalendar";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch { }
    }

    /// <summary>exe 를 옮긴 경우 등록된 경로를 최신으로 맞춘다.</summary>
    public static void RefreshPath()
    {
        if (IsEnabled()) Set(true);
    }
}
