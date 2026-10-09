using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SMCalendar.Core;

/// <summary>시작 메뉴에 "SM Calendar" 바로가기를 만든다 (종료한 뒤 다시 켤 때 검색해서 실행).</summary>
internal static class StartMenuShortcut
{
    static string StartMenuPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SM Calendar.lnk");

    /// <summary>없을 때만 만든다 (부팅할 때마다 쓰지 않도록).</summary>
    public static void EnsureStartMenu()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || File.Exists(StartMenuPath)) return;
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetDescription("SM Calendar - Google 캘린더 바탕화면 위젯");
            link.SetIconLocation(exe, 0);
            ((IPersistFile)link).Save(StartMenuPath, true);
            Marshal.ReleaseComObject(link);
        }
        catch { /* 바로가기는 없어도 앱은 동작한다 */ }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int cch, IntPtr fd, uint flags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
