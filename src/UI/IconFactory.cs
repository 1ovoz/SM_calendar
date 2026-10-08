using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>트레이 아이콘: 오늘 날짜 숫자가 들어간 작은 달력.</summary>
internal static class IconFactory
{
    public static Icon CreateTrayIcon(int day, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float s = size / 32f;
            var body = new RectangleF(2 * s, 4 * s, 28 * s, 26 * s);
            using (var path = CalendarWidget.RoundRect(body, 5 * s))
            using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
            using (var path = CalendarWidget.RoundRect(new RectangleF(2 * s, 4 * s, 28 * s, 9 * s), 5 * s))
            using (var b = new SolidBrush(Color.FromArgb(79, 140, 255))) g.FillPath(b, path);
            using (var b = new SolidBrush(Color.FromArgb(79, 140, 255))) g.FillRectangle(b, 2 * s, 9 * s, 28 * s, 4 * s);
            using var font = new Font("Segoe UI", 14 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(Color.FromArgb(30, 30, 36));
            g.DrawString(day.ToString(), font, tb, new RectangleF(0, 12 * s, size, 18 * s), sf);
        }
        var h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        Native.DestroyIcon(h);
        return icon;
    }
}
