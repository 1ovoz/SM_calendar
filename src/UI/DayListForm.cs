using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>하루 일정 전체 목록 (칸에 다 안 들어갈 때).</summary>
internal sealed class DayListForm : Form
{
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];
    readonly CalendarWidget _widget;
    readonly ListBox _list = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly Button _add = new() { Text = "+ 새 일정", Dock = DockStyle.Bottom, Height = 32 };
    DateTime _day;

    public DayListForm(CalendarWidget widget)
    {
        _widget = widget;
        Font = new Font("Malgun Gothic", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        Size = new Size(300, 320);
        _list.ItemHeight = 26;
        Controls.Add(_list);
        Controls.Add(_add);

        _list.DrawItem += DrawItem;
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OpenSelected(); };
        _add.Click += (_, _) => _widget.OpenEditor(null, _day);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Deactivate += (_, _) => Close();
    }

    public void ShowFor(DateTime day, Point near)
    {
        _day = day.Date;
        Text = $"{day.Month}월 {day.Day}일 ({DayNames[(int)day.DayOfWeek]})";
        RefreshList();
        var wa = Screen.FromPoint(near).WorkingArea;
        Location = new Point(Math.Clamp(near.X - Width / 2, wa.Left, wa.Right - Width), Math.Clamp(near.Y + 12, wa.Top, wa.Bottom - Height));
        if (!Visible) Show();
        Activate();
    }

    public void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _widget.EventsOn(_day)) _list.Items.Add(e);
        _list.EndUpdate();
    }

    void OpenSelected()
    {
        if (_list.SelectedItem is CalEvent e)
        {
            Close();
            _widget.OpenEditor(e, _day);
        }
    }

    void DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || _list.Items[e.Index] is not CalEvent ev) return;
        var g = e.Graphics;
        var b = e.Bounds;
        var cal = _widget.FindCalendar(ev.CalendarId);
        using (var br = new SolidBrush(Theme.EventColor(ev.ColorId, cal?.Color ?? "#4F8CFF")))
            g.FillRectangle(br, b.X + 6, b.Y + 5, 4, b.Height - 10);
        string time = ev.AllDay ? "종일" : ev.Start.Date == _day ? ev.Start.ToString("HH:mm") : "~" + ev.End.ToString("HH:mm");
        var timeRect = new Rectangle(b.X + 16, b.Y, 46, b.Height);
        TextRenderer.DrawText(g, time, e.Font, timeRect, (e.State & DrawItemState.Selected) != 0 ? e.ForeColor : SystemColors.GrayText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(g, ev.Title, e.Font, new Rectangle(b.X + 64, b.Y, b.Width - 68, b.Height), e.ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
