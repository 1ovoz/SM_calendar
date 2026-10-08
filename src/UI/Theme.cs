using System.Globalization;

namespace SMCalendar.UI;

internal sealed class Theme
{
    public required Color Bg, Fg, Border, Grid, Hover, Accent, Sunday, Saturday;
    public required int DimAlpha;

    public static readonly Theme Dark = new()
    {
        Bg = Color.FromArgb(18, 18, 22), Fg = Color.FromArgb(242, 242, 246), Border = Color.White,
        Grid = Color.FromArgb(26, 255, 255, 255), Hover = Color.FromArgb(22, 255, 255, 255),
        Accent = Color.FromArgb(79, 140, 255), Sunday = Color.FromArgb(255, 112, 112), Saturday = Color.FromArgb(110, 168, 255),
        DimAlpha = 100,
    };

    public static readonly Theme Light = new()
    {
        Bg = Color.FromArgb(250, 250, 252), Fg = Color.FromArgb(28, 28, 34), Border = Color.Black,
        Grid = Color.FromArgb(30, 0, 0, 0), Hover = Color.FromArgb(16, 0, 0, 0),
        Accent = Color.FromArgb(40, 104, 230), Sunday = Color.FromArgb(214, 48, 49), Saturday = Color.FromArgb(32, 102, 214),
        DimAlpha = 110,
    };

    /// <summary>Google 캘린더 웹의 일정 색상 (colorId 1~11).</summary>
    static readonly string[] EventColors =
        ["", "#7986CB", "#33B679", "#8E24AA", "#E67C73", "#F6BF26", "#F4511E", "#039BE5", "#616161", "#3F51B5", "#0B8043", "#D50000"];

    public static Color EventColor(string? colorId, string calendarColor)
    {
        if (int.TryParse(colorId, out var i) && i > 0 && i < EventColors.Length) return Hex(EventColors[i], Color.SteelBlue);
        return Hex(calendarColor, Color.FromArgb(79, 140, 255));
    }

    public static Color Hex(string? hex, Color fallback)
    {
        if (hex is { Length: 7 } && hex[0] == '#' &&
            int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return fallback;
    }

    public static Color TextOn(Color bg)
    {
        double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255;
        return lum > 0.65 ? Color.FromArgb(32, 32, 36) : Color.White;
    }

    public static Color WithAlpha(Color c, int a) => Color.FromArgb(Math.Clamp(a, 0, 255), c.R, c.G, c.B);
}
