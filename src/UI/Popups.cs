using System.Drawing.Drawing2D;

namespace SMCalendar.UI;

/// <summary>컨트롤을 띄우는 드롭다운. 바깥을 클릭하면 닫히고 닫히면 정리된다.</summary>
internal static class Popup
{
    public static ToolStripDropDown Show(Control anchor, Control content, Palette p, Action? closed = null)
    {
        var dd = Create(content, p);
        dd.Closed += (_, _) =>
        {
            closed?.Invoke();
            anchor.BeginInvoke(dd.Dispose);
        };
        var screen = Screen.FromControl(anchor).WorkingArea;
        var below = anchor.PointToScreen(new Point(0, anchor.Height + 4));
        // 아래 공간이 부족하면 위로
        if (below.Y + content.Height > screen.Bottom) dd.Show(anchor, new Point(0, -content.Height - 6));
        else dd.Show(anchor, new Point(0, anchor.Height + 4));
        return dd;
    }

    /// <summary>화면 좌표에 띄우기 (위젯처럼 기준 컨트롤이 없을 때).</summary>
    public static ToolStripDropDown ShowAt(Control invoker, Point screen, Control content, Palette p, Action? closed = null)
    {
        var dd = Create(content, p);
        dd.Closed += (_, _) =>
        {
            closed?.Invoke();
            invoker.BeginInvoke(dd.Dispose);
        };
        var wa = Screen.FromPoint(screen).WorkingArea;
        var pt = new Point(Math.Clamp(screen.X, wa.Left, wa.Right - content.Width - 4), Math.Clamp(screen.Y, wa.Top, wa.Bottom - content.Height - 4));
        dd.Show(pt);
        return dd;
    }

    static ToolStripDropDown Create(Control content, Palette p)
    {
        var host = new ToolStripControlHost(content)
        {
            Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = content.Size,
        };
        var dd = new ToolStripDropDown
        {
            Padding = new Padding(1), Margin = Padding.Empty, BackColor = p.Bg,
            Renderer = new DarkMenuRenderer(p), DropShadowEnabled = true,
        };
        dd.Items.Add(host);
        dd.HandleCreated += (_, _) => Ui.RoundCorners(dd.Handle, small: true);
        return dd;
    }

    public static void Close(Control content)
    {
        if (content.Parent is ToolStripDropDown dd) dd.Close();
        else (content.Parent?.Parent as ToolStripDropDown)?.Close();
    }
}

internal sealed record OptionItem(string Text, object? Tag = null, Color? Dot = null, bool Selected = false, string? Hint = null);

/// <summary>드롭다운 안의 선택 목록 (휠 스크롤).</summary>
internal sealed class OptionList : UiControl
{
    readonly List<OptionItem> _items;
    readonly int _visible;
    int _hover = -1, _scroll;
    public event Action<OptionItem>? Chosen;

    public OptionList(Palette p, List<OptionItem> items, int width, int maxRows = 8) : base(p)
    {
        _items = items;
        _visible = Math.Min(maxRows, items.Count);
        Cursor = Cursors.Hand;
        Size = new Size((int)(width * S), (int)(RowH * _visible + 8 * S));
        int sel = items.FindIndex(i => i.Selected);
        if (sel >= 0) _scroll = Math.Clamp(sel - _visible / 2, 0, Math.Max(0, items.Count - _visible));
    }

    float RowH => 32 * S;

    int IndexAt(Point p)
    {
        int i = (int)((p.Y - 4 * S) / RowH) + _scroll;
        return i >= 0 && i < _items.Count && p.Y > 4 * S ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll = Math.Clamp(_scroll + (e.Delta > 0 ? -2 : 2), 0, Math.Max(0, _items.Count - _visible));
        _hover = IndexAt(e.Location);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexAt(e.Location);
        if (i < 0) return;
        var item = _items[i];
        Popup.Close(this);
        Chosen?.Invoke(item);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(P.Bg);
        for (int k = 0; k < _visible; k++)
        {
            int i = k + _scroll;
            if (i >= _items.Count) break;
            var item = _items[i];
            var r = new RectangleF(4 * S, 4 * S + k * RowH, Width - 8 * S, RowH - 2 * S);
            if (i == _hover) Ui.FillRound(g, r, 6 * S, P.SurfaceHover);
            else if (item.Selected) Ui.FillRound(g, r, 6 * S, Color.FromArgb(40, P.Accent));
            float x = r.X + 10 * S;
            if (item.Dot is { } dot)
            {
                float d = 10 * S;
                using var b = new SolidBrush(dot);
                g.FillEllipse(b, x, r.Y + (r.Height - d) / 2, d, d);
                x += d + 8 * S;
            }
            var tr = new Rectangle((int)x, (int)r.Y, (int)(r.Right - x - 8 * S), (int)r.Height);
            TextRenderer.DrawText(g, item.Text, Font, tr, item.Selected ? P.Accent : P.Fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (item.Hint != null)
                TextRenderer.DrawText(g, item.Hint, Font, tr, P.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }
        // 스크롤 위치 표시
        if (_items.Count > _visible)
        {
            float trackH = Height - 12 * S;
            float barH = Math.Max(20 * S, trackH * _visible / _items.Count);
            float y = 6 * S + (trackH - barH) * _scroll / (_items.Count - _visible);
            Ui.FillRound(g, new RectangleF(Width - 5 * S, y, 3 * S, barH), 1.5f * S, P.Border);
        }
    }
}

/// <summary>드롭다운용 작은 달력.</summary>
internal sealed class MiniCalendar : UiControl
{
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];
    readonly bool _mondayFirst;
    DateTime _month, _selected;
    int _hover = -1;
    public event Action<DateTime>? Chosen;

    public MiniCalendar(Palette p, DateTime selected, bool mondayFirst) : base(p)
    {
        _selected = selected.Date;
        _month = new DateTime(selected.Year, selected.Month, 1);
        _mondayFirst = mondayFirst;
        Size = new Size((int)(252 * S), (int)(268 * S));
        Cursor = Cursors.Hand;
    }

    float Cell => 34 * S;
    float GridTop => 70 * S;
    DateTime GridStart
    {
        get
        {
            int start = _mondayFirst ? 1 : 0;
            return _month.AddDays(-(((int)_month.DayOfWeek - start + 7) % 7));
        }
    }

    RectangleF PrevRect => new(Width - 72 * S, 8 * S, 30 * S, 30 * S);
    RectangleF NextRect => new(Width - 38 * S, 8 * S, 30 * S, 30 * S);

    int IndexAt(Point p)
    {
        if (p.Y < GridTop) return p.Y < 40 * S && PrevRect.Contains(p) ? -2 : NextRect.Contains(p) ? -3 : -1;
        int c = (int)((p.X - 7 * S) / Cell), r = (int)((p.Y - GridTop) / Cell);
        return c is >= 0 and < 7 && r is >= 0 and < 6 ? r * 7 + c : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _month = _month.AddMonths(e.Delta > 0 ? -1 : 1);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexAt(e.Location);
        if (i == -2) { _month = _month.AddMonths(-1); Invalidate(); return; }
        if (i == -3) { _month = _month.AddMonths(1); Invalidate(); return; }
        if (i < 0) return;
        var day = GridStart.AddDays(i);
        Popup.Close(this);
        Chosen?.Invoke(day);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(P.Bg);
        using var bold = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(g, $"{_month.Year}년 {_month.Month}월", bold, new Rectangle((int)(14 * S), (int)(8 * S), (int)(150 * S), (int)(30 * S)),
            P.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        using (var icon = new Font(Ui.IconFamily, 11 * S, GraphicsUnit.Pixel))
        {
            if (_hover == -2) Ui.FillRound(g, PrevRect, 6 * S, P.SurfaceHover);
            if (_hover == -3) Ui.FillRound(g, NextRect, 6 * S, P.SurfaceHover);
            Ui.Glyph(g, "\uE76B", icon, P.Fg, Rectangle.Round(PrevRect));
            Ui.Glyph(g, "\uE76C", icon, P.Fg, Rectangle.Round(NextRect));
        }
        int startDow = _mondayFirst ? 1 : 0;
        for (int c = 0; c < 7; c++)
        {
            int dow = (startDow + c) % 7;
            TextRenderer.DrawText(g, DayNames[dow], Font, new Rectangle((int)(7 * S + c * Cell), (int)(42 * S), (int)Cell, (int)(24 * S)),
                dow == 0 ? Color.FromArgb(255, 112, 112) : dow == 6 ? Color.FromArgb(110, 168, 255) : P.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var gs = GridStart;
        var today = DateTime.Today;
        for (int i = 0; i < 42; i++)
        {
            var d = gs.AddDays(i);
            var r = new RectangleF(7 * S + (i % 7) * Cell + 2 * S, GridTop + (i / 7) * Cell + 2 * S, Cell - 4 * S, Cell - 4 * S);
            var fg = d.Month == _month.Month ? P.Fg : Ui.Blend(P.Fg, P.Bg, 0.4);
            if (d == _selected)
            {
                using var b = new SolidBrush(P.Accent);
                g.FillEllipse(b, r);
                fg = P.AccentFg;
            }
            else if (i == _hover)
            {
                using var b = new SolidBrush(P.SurfaceHover);
                g.FillEllipse(b, r);
            }
            if (d == today && d != _selected)
            {
                using var pen = new Pen(P.Accent, 1.5f * S);
                g.DrawEllipse(pen, r);
            }
            TextRenderer.DrawText(g, d.Day.ToString(), Font, Rectangle.Round(r), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>
/// 메뉴/드롭다운을 편집 창과 같은 모양으로 그리는 렌더러:
/// 둥근 강조, 체크 표시, 얇은 구분선, 테마 색.
/// </summary>
internal sealed class DarkMenuRenderer(Palette p) : ToolStripProfessionalRenderer(new Colors(p))
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(p.Bg);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ToolStripDropDown) return;
        using var pen = new Pen(p.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { /* 별도 여백 색 없음 */ }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Ui.FillRound(g, new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2), 6, p.SurfaceHover);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? p.Fg : p.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        using var f = new Font(Ui.IconFamily, Math.Max(8, e.ArrowRectangle.Height * 0.45f), GraphicsUnit.Pixel);
        Ui.Glyph(e.Graphics, "\uE76C", f, p.Muted, e.ArrowRectangle);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var f = new Font(Ui.IconFamily, Math.Max(10, e.ImageRectangle.Height * 0.75f), GraphicsUnit.Pixel);
        Ui.Glyph(e.Graphics, "\uE73E", f, p.Accent, e.ImageRectangle);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(p.Border);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    /// <summary>메뉴 전체(하위 메뉴 포함)에 글꼴·여백·둥근 모서리를 적용.</summary>
    public static void Style(ToolStrip strip, Palette p, Font font)
    {
        var renderer = new DarkMenuRenderer(p);
        void Apply(ToolStrip t)
        {
            t.Renderer = renderer;
            t.Font = font;
            t.BackColor = p.Bg;
            t.ForeColor = p.Fg;
            if (t is ToolStripDropDown dd)
            {
                dd.Padding = new Padding(4, 6, 4, 6);
                if (dd.IsHandleCreated) Ui.RoundCorners(dd.Handle, small: true);
                else dd.HandleCreated += (_, _) => Ui.RoundCorners(dd.Handle, small: true);
            }
            foreach (ToolStripItem item in t.Items)
            {
                item.Font = font;
                if (item is ToolStripMenuItem mi)
                {
                    mi.Padding = new Padding(2, 5, 2, 5);
                    if (mi.HasDropDownItems) Apply(mi.DropDown);
                }
            }
        }
        Apply(strip);
    }

    sealed class Colors(Palette p) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => p.Bg;
        public override Color ImageMarginGradientBegin => p.Bg;
        public override Color ImageMarginGradientMiddle => p.Bg;
        public override Color ImageMarginGradientEnd => p.Bg;
        public override Color MenuBorder => p.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => p.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => p.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => p.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => p.SurfaceHover;
        public override Color MenuItemPressedGradientEnd => p.SurfaceHover;
        public override Color CheckBackground => p.Bg;
        public override Color CheckSelectedBackground => p.Bg;
        public override Color CheckPressedBackground => p.Bg;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Bg;
    }
}

/// <summary>연·월 선택 팝업 (위젯 제목을 누르면 열림).</summary>
internal sealed class MonthPicker : UiControl
{
    int _year;
    readonly int _selYear, _selMonth;
    int _hover = -1;
    public event Action<int, int>? Chosen;

    public MonthPicker(Palette p, int year, int month, Font font) : base(p)
    {
        Font = font;
        _year = _selYear = year;
        _selMonth = month;
        Size = new Size((int)(264 * S), (int)(212 * S));
        Cursor = Cursors.Hand;
    }

    RectangleF PrevRect => new(8 * S, 8 * S, 32 * S, 32 * S);
    RectangleF NextRect => new(Width - 40 * S, 8 * S, 32 * S, 32 * S);
    float GridTop => 48 * S;

    RectangleF CellRect(int i)
    {
        float cw = (Width - 16 * S) / 4f, ch = (Height - GridTop - 8 * S) / 3f;
        return new RectangleF(8 * S + (i % 4) * cw + 3 * S, GridTop + (i / 4) * ch + 3 * S, cw - 6 * S, ch - 6 * S);
    }

    int IndexAt(Point pt)
    {
        if (PrevRect.Contains(pt)) return -2;
        if (NextRect.Contains(pt)) return -3;
        for (int i = 0; i < 12; i++) if (CellRect(i).Contains(pt)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _year += e.Delta > 0 ? -1 : 1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexAt(e.Location);
        if (i == -2) { _year--; Invalidate(); return; }
        if (i == -3) { _year++; Invalidate(); return; }
        if (i < 0) return;
        Popup.Close(this);
        Chosen?.Invoke(_year, i + 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(P.Bg);
        using (var bold = new Font(Font.FontFamily, Font.Size * 1.1f, FontStyle.Bold, GraphicsUnit.Pixel))
            TextRenderer.DrawText(g, $"{_year}년", bold, new Rectangle(0, (int)(8 * S), Width, (int)(32 * S)), P.Fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (var icon = new Font(Ui.IconFamily, 11 * S, GraphicsUnit.Pixel))
        {
            if (_hover == -2) Ui.FillRound(g, PrevRect, 6 * S, P.SurfaceHover);
            if (_hover == -3) Ui.FillRound(g, NextRect, 6 * S, P.SurfaceHover);
            Ui.Glyph(g, "\uE76B", icon, P.Fg, Rectangle.Round(PrevRect));
            Ui.Glyph(g, "\uE76C", icon, P.Fg, Rectangle.Round(NextRect));
        }
        var today = DateTime.Today;
        for (int i = 0; i < 12; i++)
        {
            var r = CellRect(i);
            bool selected = _year == _selYear && i + 1 == _selMonth;
            bool isNow = _year == today.Year && i + 1 == today.Month;
            if (selected) Ui.FillRound(g, r, 8 * S, P.Accent);
            else if (i == _hover) Ui.FillRound(g, r, 8 * S, P.SurfaceHover);
            if (isNow && !selected) Ui.DrawRound(g, r, 8 * S, P.Accent, 1.5f * S);
            TextRenderer.DrawText(g, $"{i + 1}월", Font, Rectangle.Round(r), selected ? P.AccentFg : P.Fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>위젯 보기 설정 팝업: 배경 불투명도 + 글자 크기.</summary>
internal sealed class ViewPanel : UiControl
{
    public static readonly (string Name, double Scale)[] FontSizes = [("작게", 0.85), ("보통", 1.0), ("크게", 1.25), ("아주 크게", 1.55)];

    readonly Slider _slider;
    readonly List<PillButton> _sizes = new();
    public event Action<double>? OpacityChanged;
    public event Action<double>? FontScaleChanged;

    public ViewPanel(Palette p, double opacity, double fontScale, Font font) : base(p)
    {
        Font = font;
        int pad = (int)(14 * S);
        Size = new Size((int)(330 * S), (int)(150 * S));
        _slider = new Slider(p) { Bounds = new Rectangle((int)(4 * S), (int)(32 * S), Width - (int)(8 * S), (int)(28 * S)) };
        _slider.Value = opacity;
        _slider.ValueChanged += (_, _) => { Invalidate(); OpacityChanged?.Invoke(_slider.Value); };
        Controls.Add(_slider);

        int x = pad, y = (int)(104 * S), h = (int)(30 * S);
        int w = (Width - pad * 2 - (int)(6 * S) * 3) / 4;
        foreach (var (name, scale) in FontSizes)
        {
            var b = new PillButton(p, name) { Font = font, Bounds = new Rectangle(x, y, w, h), Selected = Math.Abs(scale - fontScale) < 0.01 };
            b.Click += (_, _) =>
            {
                foreach (var o in _sizes) { o.Selected = o == b; o.Invalidate(); }
                FontScaleChanged?.Invoke(scale);
            };
            _sizes.Add(b);
            Controls.Add(b);
            x += w + (int)(6 * S);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(P.Bg);
        int pad = (int)(14 * S);
        var top = new Rectangle(pad, (int)(8 * S), Width - pad * 2, (int)(24 * S));
        TextRenderer.DrawText(g, "배경 불투명도", Font, top, P.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(g, $"{_slider.Value * 100:0}%", Font, top, P.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        using var small = new Font(Font.FontFamily, Font.Size * 0.85f, GraphicsUnit.Pixel);
        var hint = new Rectangle(pad, (int)(58 * S), Width - pad * 2, (int)(18 * S));
        TextRenderer.DrawText(g, "투명", small, hint, P.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(g, "Ctrl + 휠로도 조절", small, hint, P.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "불투명", small, hint, P.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        TextRenderer.DrawText(g, "글자 크기", Font, new Rectangle(pad, (int)(78 * S), Width - pad * 2, (int)(24 * S)), P.Fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
