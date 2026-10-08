namespace SMCalendar.Core;

internal static class AppPaths
{
    public static string Dir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMCalendar");

    public static void UseDemo() =>
        Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMCalendar-demo");

    public static string File(string name) => Path.Combine(Dir, name);

    /// <summary>임시 파일에 쓴 뒤 교체해서 중간에 꺼져도 파일이 깨지지 않게 한다.</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        System.IO.File.WriteAllBytes(tmp, data);
        System.IO.File.Move(tmp, path, overwrite: true);
    }
}
