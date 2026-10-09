using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SMCalendar.UI;

/// <summary>편집기/팝업용 불투명 색상표 (위젯 테마와 짝을 이룬다).</summary>
internal sealed class Palette
{
    public required Color Bg, Surface, SurfaceHover, Border, Fg, Muted, Accent, AccentFg, Danger;

    public static readonly Palette Dark = new()
    {
        Bg = Color.FromArgb(30, 31, 36), Surface = Color.FromArgb(42, 43, 50), SurfaceHover = Color.FromArgb(53, 54, 62),
        Border = Color.FromArgb(62, 63, 72), Fg = Color.FromArgb(236, 236, 242), Muted = Color.FromArgb(150, 151, 164),
        Accent = Color.FromArgb(79, 140, 255), AccentFg = Color.White, Danger = Color.FromArgb(255, 107, 107),
    };

    public static readonly Palette Light = new()
    {
        Bg = Color.White, Surface = Color.FromArgb(243, 244, 247), SurfaceHover = Color.FromArgb(232, 233, 238),
        Border = Color.FromArgb(218, 219, 226), Fg = Color.FromArgb(28, 28, 34), Muted = Color.FromArgb(107, 108, 120),
        Accent = Color.FromArgb(40, 104, 230), AccentFg = Color.White, Danger = Color.FromArgb(217, 48, 37),
    };

    public static Palette For(bool dark) => dark ? Dark : Light;
}

internal static class Ui
{
    /// <summary>
    /// 글꼴: 기본은 앱에 포함된 Pretendard (무료, SIL OFL). 처음 쓸 때만 불러온다.
    /// GDI+(직접 그리기)는 PrivateFontCollection, GDI(입력칸 등 기본 컨트롤)는 AddFontResourceEx(FR_PRIVATE)로
    /// 이 프로세스 안에서만 등록한다 (시스템에 설치하지 않음).
    /// </summary>
    public const string Pretendard = "Pretendard";
    public const string Malgun = "Malgun Gothic";
    static string _preferred = Pretendard;
    static System.Drawing.Text.PrivateFontCollection? _private;
    static FontFamily? _family;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);
    const uint FR_PRIVATE = 0x10;

    public static string FontName => Family.Name;

    public static void UseFont(string name)
    {
        if (name == _preferred) return;
        _preferred = name;
        _family = null;
    }

    public static FontFamily Family => _family ??= ResolveFamily();

    static FontFamily ResolveFamily()
    {
        if (_preferred == Pretendard)
        {
            try
            {
                if (_private == null)
                {
                    var dir = Path.Combine(AppContext.BaseDirectory, "fonts");
                    var files = new[] { "Pretendard-Regular.ttf", "Pretendard-Bold.ttf" }
                        .Select(f => Path.Combine(dir, f)).Where(File.Exists).ToArray();
                    if (files.Length > 0)
                    {
                        var pfc = new System.Drawing.Text.PrivateFontCollection();
                        foreach (var f in files)
                        {
                            pfc.AddFontFile(f);
                            AddFontResourceEx(f, FR_PRIVATE, IntPtr.Zero);
                        }
                        _private = pfc;
                    }
                }
                if (_private?.Families.FirstOrDefault(ff => ff.Name == Pretendard) is { } fam) return fam;
            }
            catch { /* 글꼴 파일이 없거나 손상 → 맑은 고딕 */ }
        }
        return new FontFamily(Malgun);
    }

    public static Font Font(float px, FontStyle style = FontStyle.Regular) => new(Family, px, style, GraphicsUnit.Pixel);
    public static readonly string IconFamily = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    static bool FontExists(string name)
    {
        try { using var ff = new FontFamily(name); return true; }
        catch { return false; }
    }

    /// <summary>TextRenderer 는 알파를 무시하므로 반투명 글자색은 배경과 미리 섞는다.</summary>
    public static Color Blend(Color fg, Color bg, double amount) => Color.FromArgb(
        (int)(bg.R + (fg.R - bg.R) * amount), (int)(bg.G + (fg.G - bg.G) * amount), (int)(bg.B + (fg.B - bg.B) * amount));

    public static GraphicsPath Round(RectangleF r, float radius) => CalendarWidget.RoundRect(r, radius);

    public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
    {
        using var path = Round(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static void DrawRound(Graphics g, RectangleF r, float radius, Color c, float width = 1)
    {
        using var path = Round(r, radius);
        using var p = new Pen(c, width);
        g.DrawPath(p, path);
    }

    public static void Glyph(Graphics g, string glyph, Font font, Color c, Rectangle r) =>
        TextRenderer.DrawText(g, glyph, font, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

    // ---- Windows 11 둥근 모서리 / 어두운 제목줄 ----
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    /// <summary>스크롤바 등 기본 컨트롤 부분을 어두운 테마로.</summary>
    public static void DarkScrollbars(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
    }

    public static void DarkTitleBar(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, 20, ref v, 4); } catch { }
    }

    public static void RoundCorners(IntPtr hwnd, bool small = false)
    {
        int pref = small ? 3 : 2; // DWMWCP_ROUNDSMALL / DWMWCP_ROUND
        try { DwmSetWindowAttribute(hwnd, 33, ref pref, 4); } catch { }
    }
}

/// <summary>더블 버퍼 + 마우스 오버 추적을 하는 직접 그리기 컨트롤의 기반.</summary>
internal abstract class UiControl : Control
{
    protected Palette P;
    protected bool Hover, Pressed;

    protected UiControl(Palette p)
    {
        P = p;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = p.Bg;
        ForeColor = p.Fg;
    }

    protected float S => DeviceDpi / 96f;

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover = false; Pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
}

internal enum PillStyle { Chip, Primary, Ghost, Danger, DangerFill }

/// <summary>둥근 버튼 / 선택 칩. 색 점, 아이콘, 펼침 화살표를 선택적으로 표시.</summary>
internal sealed class PillButton : UiControl
{
    public PillStyle Style { get; set; }
    public Color? Dot { get; set; }
    public string? Glyph { get; set; }
    public bool Chevron { get; set; }
    public bool Selected { get; set; }

    public PillButton(Palette p, string text, PillStyle style = PillStyle.Chip) : base(p)
    {
        Text = text;
        Style = style;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public void SetPalette(Palette p) { P = p; BackColor = p.Bg; Invalidate(); }

    /// <summary>글자 길이에 맞춰 너비 조정.</summary>
    public int PreferredWidth()
    {
        int w = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(24 * S);
        if (Dot != null) w += (int)(16 * S);
        if (Glyph != null) w += (int)(20 * S);
        if (Chevron) w += (int)(16 * S);
        return w;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float radius = Style == PillStyle.Chip ? Height / 2f : 8 * S;

        Color bg, fg;
        switch (Style)
        {
            case PillStyle.Primary:
                bg = Enabled ? (Pressed ? ControlPaint.Dark(P.Accent, 0.05f) : Hover ? ControlPaint.Light(P.Accent, 0.15f) : P.Accent) : P.Surface;
                fg = Enabled ? P.AccentFg : P.Muted;
                break;
            case PillStyle.Danger:
                bg = Hover && Enabled ? Color.FromArgb(40, P.Danger) : P.Bg;
                fg = Enabled ? P.Danger : P.Muted;
                break;
            case PillStyle.DangerFill:
                bg = Enabled ? (Pressed ? ControlPaint.Dark(P.Danger, 0.05f) : Hover ? ControlPaint.Light(P.Danger, 0.15f) : P.Danger) : P.Surface;
                fg = Enabled ? Color.White : P.Muted;
                break;
            case PillStyle.Ghost:
                bg = Hover && Enabled ? P.SurfaceHover : P.Bg;
                fg = Enabled ? P.Fg : P.Muted;
                break;
            default:
                bg = Selected ? Color.FromArgb(50, P.Accent) : Hover && Enabled ? P.SurfaceHover : P.Surface;
                fg = Enabled ? (Selected ? P.Accent : P.Fg) : P.Muted;
                break;
        }
        Ui.FillRound(g, r, radius, bg);
        if (Focused && ShowFocusCues) Ui.DrawRound(g, RectangleF.Inflate(r, -1, -1), radius, Color.FromArgb(160, P.Accent), 1.5f * S);

        float x = 12 * S;
        if (Dot is { } dot)
        {
            float d = 10 * S;
            using var b = new SolidBrush(Enabled ? dot : Color.FromArgb(110, dot));
            g.FillEllipse(b, x, (Height - d) / 2, d, d);
            x += d + 6 * S;
        }
        if (Glyph != null)
        {
            using var f = new Font(Ui.IconFamily, 12 * S, GraphicsUnit.Pixel);
            Ui.Glyph(g, Glyph, f, fg, new Rectangle((int)x - 2, 0, (int)(18 * S), Height));
            x += 20 * S;
        }
        int right = Chevron ? (int)(26 * S) : (int)(12 * S);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                    (Dot == null && Glyph == null && !Chevron ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
        var tr = flags.HasFlag(TextFormatFlags.HorizontalCenter)
            ? new Rectangle(0, 0, Width, Height)
            : new Rectangle((int)x, 0, Width - (int)x - right, Height);
        TextRenderer.DrawText(g, Text, Font, tr, fg, flags);
        if (Chevron)
        {
            using var f = new Font(Ui.IconFamily, 9 * S, GraphicsUnit.Pixel);
            Ui.Glyph(g, "\uE70D", f, P.Muted, new Rectangle(Width - (int)(24 * S), 0, (int)(16 * S), Height));
        }
    }
}

/// <summary>켜기/끄기 스위치.</summary>
internal sealed class ToggleSwitch : UiControl
{
    bool _checked;
    public event EventHandler? CheckedChanged;

    public ToggleSwitch(Palette p) : base(p)
    {
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = 20 * S, w = 36 * S;
        var track = new RectangleF(0.5f, (Height - h) / 2, w, h);
        var on = Enabled ? P.Accent : P.Border;
        Ui.FillRound(g, track, h / 2, _checked ? on : Hover ? P.SurfaceHover : P.Surface);
        if (!_checked) Ui.DrawRound(g, track, h / 2, P.Border);
        float k = h - 6 * S;
        float kx = _checked ? track.Right - k - 3 * S : track.X + 3 * S;
        using var b = new SolidBrush(_checked ? Color.White : P.Muted);
        g.FillEllipse(b, kx, track.Y + 3 * S, k, k);
    }
}

/// <summary>일정 색상 선택 (0 = 캘린더 기본색, 1~11 = Google 일정 색).</summary>
internal sealed class ColorSwatches : UiControl
{
    public static readonly string[] Names =
        ["캘린더 색상", "라벤더", "세이지", "포도", "플라밍고", "바나나", "귤", "공작", "흑연", "블루베리", "바질", "토마토"];

    readonly ToolTip _tip = new();
    int _selected, _hover = -1;
    Color _calendarColor;
    public event EventHandler? SelectedChanged;

    public ColorSwatches(Palette p, Color calendarColor) : base(p)
    {
        _calendarColor = calendarColor;
        Cursor = Cursors.Hand;
    }

    public int Selected
    {
        get => _selected;
        set { _selected = Math.Clamp(value, 0, 11); Invalidate(); }
    }

    public Color CalendarColor
    {
        get => _calendarColor;
        set { _calendarColor = value; Invalidate(); }
    }

    public static Color ColorOf(int index, Color calendarColor) =>
        index == 0 ? calendarColor : Theme.EventColor(index.ToString(), "#4F8CFF");

    float Step => 26 * S;
    float D => 18 * S;

    int IndexAt(Point p)
    {
        int i = (int)(p.X / Step);
        return i is >= 0 and < 12 && p.X - i * Step <= D + 4 * S ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i == _hover) return;
        _hover = i;
        _tip.SetToolTip(this, i >= 0 ? Names[i] : null);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexAt(e.Location);
        if (i < 0 || !Enabled) return;
        Selected = i;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float y = (Height - D) / 2;
        for (int i = 0; i < 12; i++)
        {
            var c = ColorOf(i, _calendarColor);
            if (!Enabled) c = Color.FromArgb(90, c);
            float x = i * Step + 2 * S;
            var r = new RectangleF(x, y, D, D);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            if (i == _selected)
            {
                using var pen = new Pen(P.Fg, 2 * S);
                g.DrawEllipse(pen, RectangleF.Inflate(r, 3 * S, 3 * S));
            }
            else if (i == _hover)
            {
                using var pen = new Pen(Color.FromArgb(120, P.Fg), 1.5f * S);
                g.DrawEllipse(pen, RectangleF.Inflate(r, 2.5f * S, 2.5f * S));
            }
            if (i == 0)
            {
                // 기본색 표시: 가운데 작은 점
                using var b = new SolidBrush(Theme.TextOn(c));
                float d = 5 * S;
                g.FillEllipse(b, r.X + (D - d) / 2, r.Y + (D - d) / 2, d, d);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>둥근 배경을 가진 입력칸. 안쪽에 테두리 없는 TextBox 를 넣는다.</summary>
internal sealed class FieldBox : UiControl
{
    public TextBox Box { get; }
    public bool Underline { get; init; }

    public FieldBox(Palette p, bool multiline = false, bool underline = false) : base(p)
    {
        Underline = underline;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Multiline = multiline,
            AcceptsReturn = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            BackColor = underline ? p.Bg : p.Surface,
            ForeColor = p.Fg,
        };
        Controls.Add(Box);
        if (multiline && p == Palette.Dark) Box.HandleCreated += (_, _) => Ui.DarkScrollbars(Box.Handle);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.MouseEnter += (_, _) => { Hover = true; Invalidate(); };
        Box.MouseLeave += (_, _) => { Hover = false; Invalidate(); };
        Cursor = Cursors.IBeam;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }
    public string Placeholder { get => Box.PlaceholderText; set => Box.PlaceholderText = value; }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Box.Font = Font; LayoutBox(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); LayoutBox(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Box.ForeColor = Enabled ? P.Fg : P.Muted;
    }

    void LayoutBox()
    {
        int padX = Underline ? (int)(2 * S) : (int)(10 * S);
        if (Box.Multiline)
            Box.Bounds = new Rectangle(padX, (int)(8 * S), Width - padX * 2 + (int)(4 * S), Height - (int)(14 * S));
        else
        {
            int h = Box.PreferredHeight;
            Box.Bounds = new Rectangle(padX, (Height - h) / 2 + 1, Width - padX * 2, h);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool focus = Box.Focused;
        if (Underline)
        {
            using var pen = new Pen(focus ? P.Accent : Hover ? P.Muted : P.Border, (focus ? 2 : 1) * S);
            g.DrawLine(pen, 0, Height - pen.Width / 2, Width, Height - pen.Width / 2);
            return;
        }
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Ui.FillRound(g, r, 8 * S, P.Surface);
        if (focus) Ui.DrawRound(g, RectangleF.Inflate(r, -0.5f, -0.5f), 8 * S, P.Accent, 1.5f * S);
        else if (Hover && Enabled) Ui.DrawRound(g, r, 8 * S, P.Border);
    }
}

/// <summary>0~1 값을 고르는 슬라이더.</summary>
internal sealed class Slider : UiControl
{
    double _value;
    bool _drag;
    public event EventHandler? ValueChanged;

    public Slider(Palette p) : base(p) { Cursor = Cursors.Hand; }

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - _value) < 0.0001) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    float Pad => 10 * S;

    void SetFrom(int x) => Value = Math.Round((x - Pad) / (Width - Pad * 2) * 20) / 20; // 5% 단위

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _drag = true; SetFrom(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) SetFrom(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += e.Delta > 0 ? 0.05 : -0.05; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f, th = 4 * S;
        var track = new RectangleF(Pad, cy - th / 2, Width - Pad * 2, th);
        Ui.FillRound(g, track, th / 2, P.Border);
        var fill = track with { Width = (float)(track.Width * _value) };
        if (fill.Width > 0) Ui.FillRound(g, fill, th / 2, P.Accent);
        float k = 16 * S;
        float kx = track.X + (float)(track.Width * _value) - k / 2;
        using var b = new SolidBrush(Color.White);
        g.FillEllipse(b, kx, cy - k / 2, k, k);
        using var pen = new Pen(Hover || _drag ? P.Accent : P.Border, 1.5f * S);
        g.DrawEllipse(pen, kx, cy - k / 2, k, k);
    }
}
