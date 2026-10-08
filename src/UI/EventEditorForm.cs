using System.Diagnostics;
using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>일정 추가 / 수정 / 삭제.</summary>
internal sealed class EventEditorForm : Form
{
    readonly CalendarService _svc;
    readonly AppSettings _s;
    readonly CalEvent? _ev;
    readonly bool _readOnly;

    readonly TextBox _title = new() { Width = 330 };
    readonly ComboBox _calendar = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed };
    readonly CheckBox _allDay = new() { Text = "종일", AutoSize = true };
    readonly DateTimePicker _startDate = DatePicker(), _endDate = DatePicker();
    readonly DateTimePicker _startTime = TimePicker(), _endTime = TimePicker();
    readonly TextBox _location = new() { Width = 330 };
    readonly TextBox _description = new() { Width = 330, Height = 90, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    readonly Label _note = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(380, 0) };
    readonly Button _save = new() { Text = "저장", AutoSize = true, MinimumSize = new Size(80, 0) };
    readonly Button _delete = new() { Text = "삭제", AutoSize = true, MinimumSize = new Size(70, 0) };
    readonly Button _cancel = new() { Text = "닫기", AutoSize = true, MinimumSize = new Size(70, 0), DialogResult = DialogResult.Cancel };
    readonly LinkLabel _open = new() { Text = "Google 캘린더에서 열기", AutoSize = true };

    readonly List<CalendarInfo> _calendars;
    DateTime _lastStart;

    public EventEditorForm(CalendarService svc, AppSettings settings, CalEvent? ev, DateTime day)
    {
        _svc = svc;
        _s = settings;
        _ev = ev;

        Text = ev == null ? "새 일정" : "일정 편집";
        Font = new Font("Malgun Gothic", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ShowInTaskbar = true;
        KeyPreview = true;
        Icon = IconFactory.CreateTrayIcon(DateTime.Today.Day, 32);

        // --- 캘린더 목록 (쓰기 가능한 것만)
        var evCal = ev != null ? svc.Store.FindCalendar(ev.CalendarId) : null;
        _readOnly = ev != null && evCal is { CanWrite: false };
        _calendars = _readOnly && evCal != null ? [evCal] : svc.Store.Calendars.Where(c => c.CanWrite).ToList();
        foreach (var c in _calendars) _calendar.Items.Add(c.Name);
        var selectId = ev?.CalendarId ?? settings.DefaultCalendarId;
        int idx = _calendars.FindIndex(c => c.Id == selectId);
        if (idx < 0) idx = Math.Max(0, _calendars.FindIndex(c => c.Primary));
        if (_calendars.Count > 0) _calendar.SelectedIndex = idx;
        _calendar.DrawItem += DrawCalendarItem;

        // --- 값 채우기
        if (ev != null)
        {
            _title.Text = ev.Title == "(제목 없음)" ? "" : ev.Title;
            _location.Text = ev.Location ?? "";
            _description.Text = (ev.Description ?? "").ReplaceLineEndings("\r\n");
            _allDay.Checked = ev.AllDay;
            _startDate.Value = ev.Start.Date;
            _startTime.Value = DateTime.Today + ev.Start.TimeOfDay;
            var end = ev.AllDay ? ev.End.AddDays(-1) : ev.End;
            _endDate.Value = end.Date;
            _endTime.Value = DateTime.Today + end.TimeOfDay;
        }
        else
        {
            _allDay.Checked = true;
            _startDate.Value = _endDate.Value = day.Date;
            var now = DateTime.Now;
            var startTime = day.Date == now.Date ? now.Date.AddHours(Math.Min(23, now.Hour + 1)) : day.Date.AddHours(9);
            _startTime.Value = DateTime.Today + startTime.TimeOfDay;
            _endTime.Value = DateTime.Today + startTime.AddHours(1).TimeOfDay;
        }
        _lastStart = _startDate.Value.Date + _startTime.Value.TimeOfDay;

        // --- 배치
        var startRow = Row(_startDate, _startTime);
        var endRow = Row(_endDate, _endTime);
        var table = new TableLayoutPanel
        {
            ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 14, 14, 6),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(table, "제목", _title);
        AddRow(table, "캘린더", _calendar);
        AddRow(table, "", _allDay);
        AddRow(table, "시작", startRow);
        AddRow(table, "종료", endRow);
        AddRow(table, "장소", _location);
        AddRow(table, "메모", _description);

        if (_readOnly) _note.Text = "읽기 전용 캘린더의 일정입니다.";
        else if (ev?.RecurringEventId != null) _note.Text = "반복 일정입니다. 이 날짜의 일정만 수정/삭제됩니다.";
        if (_note.Text.Length > 0) AddRow(table, "", _note);
        if (ev?.HtmlLink != null) AddRow(table, "", _open);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), Margin = new Padding(0),
        };
        buttons.Controls.Add(_cancel);
        if (!_readOnly) buttons.Controls.Add(_save);
        if (ev != null && !_readOnly) buttons.Controls.Add(_delete);
        table.Controls.Add(buttons, 0, table.RowCount);
        table.SetColumnSpan(buttons, 2);
        table.RowCount++;
        Controls.Add(table);

        if (!_readOnly) AcceptButton = null; // 메모 칸에서 Enter 를 쓸 수 있도록 Ctrl+Enter 로 저장
        CancelButton = _cancel;

        // --- 동작
        if (_readOnly)
        {
            foreach (Control c in new Control[] { _title, _calendar, _allDay, _startDate, _startTime, _endDate, _endTime, _location })
                c.Enabled = false;
            _description.ReadOnly = true;
        }
        if (ev?.RecurringEventId != null) _calendar.Enabled = false; // 반복 일정의 한 회차는 다른 캘린더로 옮길 수 없음

        _allDay.CheckedChanged += (_, _) => UpdateTimeVisibility();
        _startDate.ValueChanged += (_, _) => KeepDuration();
        _startTime.ValueChanged += (_, _) => KeepDuration();
        _save.Click += async (_, _) => await SaveAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _cancel.Click += (_, _) => Close();
        _open.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(_ev!.HtmlLink!) { UseShellExecute = true }); } catch { }
        };
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Control && !_readOnly) { e.SuppressKeyPress = true; await SaveAsync(); }
        };
        UpdateTimeVisibility();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _title.Focus();
        _title.SelectAll();
    }

    static DateTimePicker DatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd (ddd)", Width = 192,
    };

    static DateTimePicker TimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 90,
    };

    static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        p.Controls.AddRange(controls);
        return p;
    }

    static void AddRow(TableLayoutPanel t, string label, Control c)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        c.Margin = new Padding(3, 3, 3, 3);
        t.Controls.Add(l, 0, t.RowCount);
        t.Controls.Add(c, 1, t.RowCount);
        t.RowCount++;
    }

    void DrawCalendarItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _calendars.Count) return;
        var c = _calendars[e.Index];
        int d = e.Bounds.Height - 8;
        using (var b = new SolidBrush(Theme.Hex(c.Color, Color.SteelBlue)))
            e.Graphics.FillEllipse(b, e.Bounds.X + 4, e.Bounds.Y + 4, d, d);
        TextRenderer.DrawText(e.Graphics, c.Name, e.Font, new Rectangle(e.Bounds.X + d + 10, e.Bounds.Y, e.Bounds.Width - d - 10, e.Bounds.Height),
            e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void UpdateTimeVisibility()
    {
        _startTime.Visible = _endTime.Visible = !_allDay.Checked;
    }

    DateTime StartValue() => _startDate.Value.Date + (_allDay.Checked ? TimeSpan.Zero : _startTime.Value.TimeOfDay);
    DateTime EndValue() => _endDate.Value.Date + (_allDay.Checked ? TimeSpan.Zero : _endTime.Value.TimeOfDay);

    /// <summary>시작을 옮기면 종료도 같은 길이만큼 따라 움직인다.</summary>
    void KeepDuration()
    {
        var newStart = _startDate.Value.Date + _startTime.Value.TimeOfDay;
        var oldStart = _lastStart;
        _lastStart = newStart;
        var oldEnd = _endDate.Value.Date + _endTime.Value.TimeOfDay;
        var duration = oldEnd - oldStart;
        if (duration < TimeSpan.Zero) duration = TimeSpan.FromHours(1);
        var newEnd = newStart + duration;
        _endDate.Value = newEnd.Date;
        _endTime.Value = DateTime.Today + newEnd.TimeOfDay;
    }

    async Task SaveAsync()
    {
        if (_readOnly || !_save.Enabled) return;
        if (_calendars.Count == 0 || _calendar.SelectedIndex < 0)
        {
            MessageBox.Show(this, "저장할 수 있는 캘린더가 없습니다. 먼저 동기화해 주세요.", Text);
            return;
        }
        var start = StartValue();
        var end = EndValue();
        if (_allDay.Checked)
        {
            if (end < start) { MessageBox.Show(this, "종료 날짜가 시작 날짜보다 빠릅니다.", Text); return; }
            end = end.AddDays(1); // Google 은 종일 일정의 끝을 '다음 날'로 표현
        }
        else if (end < start)
        {
            MessageBox.Show(this, "종료 시간이 시작 시간보다 빠릅니다.", Text);
            return;
        }

        var cal = _calendars[_calendar.SelectedIndex];
        var draft = new EventDraft
        {
            CalendarId = cal.Id,
            Title = _title.Text.Trim(),
            Location = _location.Text.Trim(),
            Description = _description.Text,
            AllDay = _allDay.Checked,
            Start = start,
            End = end,
        };
        if (_ev == null && _s.DefaultCalendarId != cal.Id)
        {
            _s.DefaultCalendarId = cal.Id;
            _s.Save();
        }

        SetBusy(true);
        try
        {
            await _svc.SaveEventAsync(_ev, draft);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "저장하지 못했습니다.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false);
        }
    }

    async Task DeleteAsync()
    {
        if (_ev == null || _readOnly) return;
        var msg = _ev.RecurringEventId != null ? $"'{_ev.Title}' 반복 일정 중 이 날짜만 삭제할까요?" : $"'{_ev.Title}' 일정을 삭제할까요?";
        if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            await _svc.DeleteEventAsync(_ev);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "삭제하지 못했습니다.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _save.Enabled = _delete.Enabled = !busy;
        _save.Text = busy ? "저장 중…" : "저장";
        UseWaitCursor = busy;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}
