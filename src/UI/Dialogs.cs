using System.Drawing.Drawing2D;

namespace SMCalendar.UI;

/// <summary>"~하시겠습니까?" 확인 / 알림 창. 기본 메시지 상자 대신 편집 창과 같은 모양으로.</summary>
internal static class Dialogs
{
    /// <summary>현재 테마 (설정이 바뀌면 TrayContext 가 갱신).</summary>
    public static bool Dark { get; set; } = true;

    public static bool Confirm(IWin32Window? owner, string title, string message, string ok = "확인", string cancel = "취소",
        bool danger = false)
    {
        using var d = new ThemedDialog(Palette.For(Dark), title, message, ok, cancel, danger ? DialogIcon.Danger : DialogIcon.Question);
        return d.ShowDialog(SafeOwner(owner)) == DialogResult.OK;
    }

    public static void Alert(IWin32Window? owner, string title, string message, bool warning = false)
    {
        using var d = new ThemedDialog(Palette.For(Dark), title, message, "확인", null, warning ? DialogIcon.Warning : DialogIcon.Info);
        d.ShowDialog(SafeOwner(owner));
    }

    /// <summary>바탕화면에 깔린 위젯을 소유자로 두면 대화상자가 뒤로 숨을 수 있어 일반 창만 소유자로 쓴다.</summary>
    static IWin32Window? SafeOwner(IWin32Window? owner) => owner is CalendarWidget ? null : owner;
}

internal enum DialogIcon { Info, Question, Warning, Danger }

internal sealed class ThemedDialog : Form
{
    readonly Palette P;
    readonly string _title, _message;
    readonly DialogIcon _icon;
    readonly PillButton _ok;
    readonly PillButton? _cancel;
    readonly Font _font, _titleFont, _iconFont;
    Rectangle _iconRect, _titleRect, _messageRect;

    float S => DeviceDpi / 96f;

    public ThemedDialog(Palette p, string title, string message, string ok, string? cancel, DialogIcon icon)
    {
        P = p;
        _title = title;
        _message = message;
        _icon = icon;
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = p.Bg;
        DoubleBuffered = true;

        _font = Ui.Font(13.5f * S);
        _titleFont = Ui.Font(16 * S, FontStyle.Bold);
        _iconFont = new Font(Ui.IconFamily, 18 * S, GraphicsUnit.Pixel);
        Font = _font;

        _ok = new PillButton(p, ok, icon == DialogIcon.Danger ? PillStyle.DangerFill : PillStyle.Primary) { Font = _font };
        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(_ok);
        if (cancel != null)
        {
            _cancel = new PillButton(p, cancel, PillStyle.Chip) { Font = _font };
            _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancel);
        }
        DoLayout();
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 3);
    }

    void DoLayout()
    {
        int W = (int)(400 * S), pad = (int)(24 * S);
        int iconSize = (int)(36 * S);
        int textX = pad + iconSize + (int)(14 * S);
        int textW = W - textX - pad;
        _iconRect = new Rectangle(pad, pad, iconSize, iconSize);
        int titleH = TextRenderer.MeasureText(_title, _titleFont, new Size(textW, 0), TextFormatFlags.WordBreak).Height;
        _titleRect = new Rectangle(textX, pad + Math.Max(0, (iconSize - titleH) / 2), textW, titleH);
        int y = Math.Max(_titleRect.Bottom, _iconRect.Bottom) + (int)(10 * S);
        int msgH = string.IsNullOrEmpty(_message) ? 0
            : TextRenderer.MeasureText(_message, _font, new Size(textW, 0), TextFormatFlags.WordBreak).Height;
        _messageRect = new Rectangle(textX, y, textW, msgH);
        y = _messageRect.Bottom + (int)(22 * S);

        int bh = (int)(38 * S);
        int okW = Math.Max((int)(88 * S), _ok.PreferredWidth());
        _ok.Bounds = new Rectangle(W - pad - okW, y, okW, bh);
        if (_cancel != null)
        {
            int cw = Math.Max((int)(80 * S), _cancel.PreferredWidth());
            _cancel.Bounds = new Rectangle(_ok.Left - (int)(8 * S) - cw, y, cw, bh);
        }
        ClientSize = new Size(W, y + bh + pad);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.RoundCorners(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        (_cancel ?? _ok).Focus(); // 실수로 Enter 를 눌러도 위험한 동작이 실행되지 않도록 취소에 포커스
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { DialogResult = _cancel != null ? DialogResult.Cancel : DialogResult.OK; Close(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Core.Native.ReleaseCapture();
        Core.Native.SendMessage(Handle, Core.Native.WM_NCLBUTTONDOWN, Core.Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(P.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var (glyph, color) = _icon switch
        {
            DialogIcon.Danger => ("", P.Danger),
            DialogIcon.Warning => ("", Color.FromArgb(240, 170, 40)),
            DialogIcon.Info => ("", P.Accent),
            _ => ("", P.Accent),
        };
        using (var b = new SolidBrush(Color.FromArgb(40, color))) g.FillEllipse(b, _iconRect);
        Ui.Glyph(g, glyph, _iconFont, color, _iconRect);
        TextRenderer.DrawText(g, _title, _titleFont, _titleRect, P.Fg, TextFormatFlags.WordBreak | TextFormatFlags.Left);
        if (_message.Length > 0)
            TextRenderer.DrawText(g, _message, _font, _messageRect, P.Muted, TextFormatFlags.WordBreak | TextFormatFlags.Left);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _titleFont.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
