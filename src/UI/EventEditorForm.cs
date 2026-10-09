using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>
/// 일정 추가 / 수정 창. 제목 · 색상 · 날짜 · 시간 · 반복 · 알림 · 장소 · 메모 · 캘린더.
/// 위젯 테마에 맞춘 테두리 없는 창으로, 열 때 만들고 닫으면 바로 해제된다.
/// </summary>
internal sealed partial class EventEditorForm : Form
{
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];
    const int W = 470;

    readonly CalendarService _svc;
    readonly AppSettings _s;
    readonly CalEvent? _ev;
    readonly bool _readOnly;
    readonly bool _recurringInstance;
    readonly Palette P;
    readonly List<CalendarInfo> _calendars;

    // ---- 편집 상태
    CalendarInfo? _cal;
    DateTime _startDate, _endDate;
    TimeSpan _startTime, _endTime;
    RepeatRule _rule = new();
    bool _remDefault;
    List<int> _remMinutes = new();
    bool _colorDirty, _ruleDirty, _remDirty;
    EditScope _scope;
    CalEvent? _master;
    bool _masterLoading, _busy;
    string? _message;
    bool _messageIsError;

    // ---- 컨트롤
    readonly FieldBox _title, _location, _notes, _startTimeBox, _endTimeBox;
    readonly PillButton _startDateBtn, _endDateBtn, _repeatBtn, _repeatEndBtn, _calendarBtn;
    readonly PillButton _save, _cancel, _delete, _close, _scopeOne, _scopeAll, _addReminder, _openLink;
    readonly ToggleSwitch _allDay;
    readonly ColorSwatches _colors;
    readonly List<PillButton> _reminderChips = new();

    Font _font = null!, _titleFont = null!, _iconFont = null!, _smallFont = null!;
    readonly List<(string Glyph, Rectangle Rect)> _icons = new();
    readonly List<(string Text, Rectangle Rect)> _labels = new();
    Rectangle _allDayLabel, _messageRect;

    float S => DeviceDpi / 96f;
    int Px(float v) => (int)Math.Round(v * S);

    public EventEditorForm(CalendarService svc, AppSettings settings, CalEvent? ev, DateTime day)
    {
        _svc = svc;
        _s = settings;
        _ev = ev;
        P = Palette.For(settings.DarkTheme);

        Text = ev == null ? "새 일정" : "일정 편집";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = P.Bg;
        ForeColor = P.Fg;
        DoubleBuffered = true;
        Icon = IconFactory.CreateTrayIcon(DateTime.Today.Day, 32);

        // ---- 캘린더
        var evCal = ev != null ? svc.Store.FindCalendar(ev.CalendarId) : null;
        _readOnly = ev != null && evCal is { CanWrite: false };
        _recurringInstance = ev?.RecurringEventId != null && !_readOnly;
        _scope = _recurringInstance ? EditScope.Instance : EditScope.Single;
        _calendars = _readOnly && evCal != null ? [evCal] : svc.Store.Calendars.Where(c => c.CanWrite).ToList();
        var calId = ev?.CalendarId ?? settings.DefaultCalendarId;
        _cal = _calendars.FirstOrDefault(c => c.Id == calId) ?? _calendars.FirstOrDefault(c => c.Primary) ?? _calendars.FirstOrDefault();

        // ---- 초기값
        if (ev != null)
        {
            _startDate = ev.Start.Date;
            _startTime = ev.Start.TimeOfDay;
            var end = ev.AllDay ? ev.End.AddDays(-1) : ev.End;
            _endDate = end.Date;
            _endTime = ev.AllDay ? ev.Start.TimeOfDay : end.TimeOfDay;
            if (ev.AllDay) { _startTime = TimeSpan.FromHours(9); _endTime = TimeSpan.FromHours(10); }
            _remDefault = ev.ReminderDefault;
            _remMinutes = ev.ReminderMinutes?.ToList() ?? new();
        }
        else
        {
            var now = DateTime.Now;
            _startDate = _endDate = day.Date;
            _startTime = day.Date == now.Date ? TimeSpan.FromHours(Math.Min(23, now.Hour + 1)) : TimeSpan.FromHours(9);
            _endTime = _startTime + TimeSpan.FromHours(1);
            if (_endTime >= TimeSpan.FromDays(1)) { _endTime -= TimeSpan.FromDays(1); _endDate = _endDate.AddDays(1); }
            _remDefault = true;
        }

        // ---- 컨트롤 생성
        _title = new FieldBox(P, underline: true) { Placeholder = "제목 추가" };
        _location = new FieldBox(P) { Placeholder = "장소 추가" };
        _notes = new FieldBox(P, multiline: true) { Placeholder = "메모 추가" };
        _startTimeBox = new FieldBox(P);
        _endTimeBox = new FieldBox(P);
        _startTimeBox.Box.TextAlign = _endTimeBox.Box.TextAlign = HorizontalAlignment.Center;

        _startDateBtn = new PillButton(P, "");
        _endDateBtn = new PillButton(P, "");
        _repeatBtn = new PillButton(P, "") { Chevron = true };
        _repeatEndBtn = new PillButton(P, "") { Chevron = true };
        _calendarBtn = new PillButton(P, "") { Chevron = true };
        _addReminder = new PillButton(P, "알림 추가", PillStyle.Ghost) { Glyph = "\uE710" };
        _save = new PillButton(P, "저장", PillStyle.Primary);
        _cancel = new PillButton(P, "취소", PillStyle.Ghost);
        _delete = new PillButton(P, "삭제", PillStyle.Danger) { Glyph = "\uE74D" };
        _close = new PillButton(P, "", PillStyle.Ghost) { Glyph = "\uE711", TabStop = false };
        _scopeOne = new PillButton(P, "이 일정만");
        _scopeAll = new PillButton(P, "모든 반복 일정");
        _openLink = new PillButton(P, "Google 캘린더에서 열기", PillStyle.Ghost) { Glyph = "\uE8A7", TabStop = false };
        _allDay = new ToggleSwitch(P);
        _colors = new ColorSwatches(P, CalendarColor);

        if (ev != null)
        {
            _title.Text = ev.Title == "(제목 없음)" ? "" : ev.Title;
            _location.Text = ev.Location ?? "";
            _notes.Text = (ev.Description ?? "").ReplaceLineEndings("\r\n");
            _allDay.Checked = ev.AllDay;
            _colors.Selected = int.TryParse(ev.ColorId, out var ci) ? ci : 0;
        }
        else
        {
            _allDay.Checked = false; // 새 일정은 시간 일정으로 시작 (종일은 스위치로)
        }

        Controls.AddRange(new Control[]
        {
            _title, _colors, _startDateBtn, _startTimeBox, _endDateBtn, _endTimeBox, _allDay,
            _repeatBtn, _repeatEndBtn, _addReminder, _location, _notes, _calendarBtn,
            _openLink, _delete, _cancel, _save, _close, _scopeOne, _scopeAll,
        });

        WireEvents();
        if (_readOnly)
        {
            foreach (Control c in Controls)
                if (c != _cancel && c != _close && c != _openLink && c != _notes) c.Enabled = false;
            _notes.Box.ReadOnly = true;
            _message = "읽기 전용 캘린더의 일정입니다.";
        }

        ApplyScale();
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max(0, (wa.Height - Height) / 3));
    }

    Color CalendarColor => Theme.Hex(_cal?.Color, P.Accent);
    DateTime StartDT => _startDate + (_allDay.Checked ? TimeSpan.Zero : _startTime);
    DateTime EndDT => _endDate + (_allDay.Checked ? TimeSpan.Zero : _endTime);
    /// <summary>반복 규칙의 기준 날짜 (전체 수정이면 원본의 시작일).</summary>
    DateTime RuleBase => _scope == EditScope.Series && _master != null ? _master.Start : StartDT;

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

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        if (_title.Enabled)
        {
            _title.Box.Focus();
            _title.Box.SelectionStart = _title.Box.TextLength;
        }
        else _cancel.Focus();
        if (_recurringInstance) await LoadMasterAsync();
    }

    async Task LoadMasterAsync()
    {
        if (_ev == null || _master != null || _masterLoading) return;
        _masterLoading = true;
        UpdateAll();
        try
        {
            _master = await _svc.LoadMasterAsync(_ev);
            if (_master != null) _rule = Recurrence.Parse(_master.Recurrence, _master.Start);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowMessage("반복 규칙을 불러오지 못했습니다: " + ex.Message, true);
        }
        finally
        {
            _masterLoading = false;
            if (!IsDisposed) UpdateAll();
        }
    }

    // =========================================================
    // 이벤트 연결
    // =========================================================

    void WireEvents()
    {
        _close.Click += (_, _) => Close();
        _cancel.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _openLink.Click += (_, _) =>
        {
            // 브라우저로 여는 주소는 Google 캘린더 https 링크만 허용 (캐시 변조 등으로 다른 프로그램이 실행되지 않도록)
            if (IsSafeGoogleLink(_ev?.HtmlLink, out var uri))
                try { Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true }); } catch { }
        };
        _allDay.CheckedChanged += (_, _) => UpdateAll();
        _colors.SelectedChanged += (_, _) => _colorDirty = true;

        _scopeOne.Click += (_, _) => SetScope(EditScope.Instance);
        _scopeAll.Click += (_, _) => SetScope(EditScope.Series);

        _startDateBtn.Click += (_, _) => PickDate(_startDateBtn, _startDate, d =>
        {
            var delta = d - _startDate;
            _startDate = d;
            _endDate += delta;
        });
        _endDateBtn.Click += (_, _) => PickDate(_endDateBtn, _endDate, d => _endDate = d < _startDate ? _startDate : d);

        _startTimeBox.Box.MouseClick += (_, _) => PickTime(_startTimeBox, true);
        _endTimeBox.Box.MouseClick += (_, _) => PickTime(_endTimeBox, false);
        _startTimeBox.Box.Leave += (_, _) => CommitTypedTime(true);
        _endTimeBox.Box.Leave += (_, _) => CommitTypedTime(false);
        _startTimeBox.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitTypedTime(true); } };
        _endTimeBox.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitTypedTime(false); } };

        _repeatBtn.Click += (_, _) => PickRepeat();
        _repeatEndBtn.Click += (_, _) => PickRepeatEnd();
        _addReminder.Click += (_, _) => PickReminder(_addReminder, null);
        _calendarBtn.Click += (_, _) => PickCalendar();
    }

    static bool IsSafeGoogleLink(string? link, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        if (u.Host != "google.com" && !u.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)) return false;
        uri = u;
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
        else if (e.KeyCode == Keys.Enter && e.Control) { e.SuppressKeyPress = true; _ = SaveAsync(); }
    }

    // 빈 곳을 끌면 창 이동
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
    }

    void SetScope(EditScope scope)
    {
        if (_scope == scope) return;
        _scope = scope;
        _ruleDirty = false;
        if (scope == EditScope.Series && _master == null) _ = LoadMasterAsync();
        UpdateAll();
    }

    // =========================================================
    // 선택 팝업
    // =========================================================

    void PickDate(Control anchor, DateTime current, Action<DateTime> apply)
    {
        var cal = new MiniCalendar(P, current, _s.WeekStartsMonday) { Font = _font };
        cal.Chosen += d => { apply(d); UpdateAll(); };
        Popup.Show(anchor, cal, P);
    }

    void PickTime(FieldBox box, bool isStart)
    {
        var items = new List<OptionItem>();
        if (isStart)
        {
            for (int m = 0; m < 24 * 60; m += 15)
            {
                var t = TimeSpan.FromMinutes(m);
                items.Add(new OptionItem(Fmt(t), t, Selected: t == _startTime));
            }
        }
        else
        {
            // 종료 시간은 시작 이후 15분 간격으로, 길이를 함께 표시
            var start = _startDate + _startTime;
            for (int m = 15; m <= 24 * 60; m += 15)
            {
                var end = start.AddMinutes(m);
                items.Add(new OptionItem(Fmt(end.TimeOfDay), end, Selected: end == _endDate + _endTime, Hint: Duration(m)));
            }
        }
        var list = new OptionList(P, items, 190) { Font = _font };
        list.Chosen += item =>
        {
            if (item.Tag is TimeSpan t) SetStartTime(t);
            else if (item.Tag is DateTime end) { _endDate = end.Date; _endTime = end.TimeOfDay; }
            UpdateAll();
        };
        Popup.Show(box, list, P);
    }

    static string Duration(int minutes) =>
        minutes < 60 ? $"{minutes}분" : minutes % 60 == 0 ? $"{minutes / 60}시간" : $"{minutes / 60.0:0.##}시간";

    void SetStartTime(TimeSpan t)
    {
        var duration = (_endDate + _endTime) - (_startDate + _startTime);
        if (duration < TimeSpan.Zero) duration = TimeSpan.FromHours(1);
        _startTime = t;
        var end = _startDate + t + duration;
        _endDate = end.Date;
        _endTime = end.TimeOfDay;
    }

    [GeneratedRegex(@"^\s*(\d{1,2})(?:\s*[:시.]?\s*(\d{2}))?\s*분?\s*$")]
    private static partial Regex TimePattern();

    void CommitTypedTime(bool isStart)
    {
        var box = isStart ? _startTimeBox : _endTimeBox;
        var m = TimePattern().Match(box.Text);
        if (m.Success)
        {
            int h = int.Parse(m.Groups[1].Value);
            int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (h < 24 && min < 60)
            {
                var t = new TimeSpan(h, min, 0);
                if (isStart) SetStartTime(t);
                else
                {
                    _endTime = t;
                    // 시작보다 이른 시간이면 다음 날, 아니면 같은 날로 본다
                    _endDate = t < _startTime ? _startDate.AddDays(1) : _startDate;
                }
            }
        }
        UpdateAll();
    }

    void PickRepeat()
    {
        var b = RuleBase;
        var kinds = new[] { RepeatKind.None, RepeatKind.Daily, RepeatKind.Weekly, RepeatKind.Monthly, RepeatKind.Yearly, RepeatKind.Weekdays };
        var items = kinds.Select(k => new OptionItem(Recurrence.Label(k, b), k, Selected: _rule.Kind == k)).ToList();
        bool masterCustom = _master?.Recurrence != null && Recurrence.Parse(_master.Recurrence, _master.Start).Kind == RepeatKind.Custom;
        if (_rule.Kind == RepeatKind.Custom || masterCustom)
            items.Add(new OptionItem("사용자 지정 (기존 규칙 유지)", RepeatKind.Custom, Selected: _rule.Kind == RepeatKind.Custom));
        var list = new OptionList(P, items, 230) { Font = _font };
        list.Chosen += item =>
        {
            var kind = (RepeatKind)item.Tag!;
            if (kind == RepeatKind.Custom && _master != null) _rule = Recurrence.Parse(_master.Recurrence, _master.Start);
            else
            {
                _rule.Kind = kind;
                _rule.Raw = null;
            }
            _ruleDirty = true;
            UpdateAll();
        };
        Popup.Show(_repeatBtn, list, P);
    }

    void PickRepeatEnd()
    {
        var items = new List<OptionItem>
        {
            new("계속 반복", "forever", Selected: _rule.Until == null && _rule.Count == null),
            new("날짜 지정…", "date", Selected: _rule.Until != null, Hint: _rule.Until?.ToString("yyyy-MM-dd")),
        };
        foreach (var n in new[] { 5, 10, 20, 30, 50 })
            items.Add(new OptionItem($"{n}회 반복", n, Selected: _rule.Count == n));
        var list = new OptionList(P, items, 200) { Font = _font };
        list.Chosen += item =>
        {
            _ruleDirty = true;
            switch (item.Tag)
            {
                case "forever": _rule.Until = null; _rule.Count = null; break;
                case int n: _rule.Count = n; _rule.Until = null; break;
                case "date":
                    BeginInvoke(() => PickDate(_repeatEndBtn, _rule.Until ?? StartDT.Date.AddMonths(1), d =>
                    {
                        _rule.Until = d < StartDT.Date ? StartDT.Date : d;
                        _rule.Count = null;
                    }));
                    return;
            }
            UpdateAll();
        };
        Popup.Show(_repeatEndBtn, list, P);
    }

    /// <param name="existing">바꿀 알림(분). null 이면 새로 추가, -1 이면 기본/없음 칩.</param>
    void PickReminder(Control anchor, int? existing)
    {
        var items = new List<OptionItem>();
        if (existing is -1)
        {
            items.Add(new OptionItem("기본 알림", "default", Selected: _remDefault));
            items.Add(new OptionItem("알림 없음", "none", Selected: !_remDefault && _remMinutes.Count == 0));
        }
        bool allDay = _allDay.Checked;
        var options = Reminders.Options(allDay).ToList();
        if (existing is >= 0 && !options.Contains(existing.Value)) options.Insert(0, existing.Value);
        foreach (var m in options)
        {
            if (m != existing && !_remDefault && _remMinutes.Contains(m)) continue;
            items.Add(new OptionItem(Reminders.Label(m, allDay), m, Selected: m == existing));
        }
        if (existing is >= 0) items.Add(new OptionItem("이 알림 삭제", "remove"));

        var list = new OptionList(P, items, 180, maxRows: 9) { Font = _font };
        list.Chosen += item =>
        {
            _remDirty = true;
            switch (item.Tag)
            {
                case "default": _remDefault = true; _remMinutes.Clear(); break;
                case "none": _remDefault = false; _remMinutes.Clear(); break;
                case "remove": _remMinutes.Remove(existing!.Value); break;
                case int m:
                    if (_remDefault) { _remDefault = false; _remMinutes.Clear(); }
                    if (existing is >= 0) _remMinutes.Remove(existing.Value);
                    if (!_remMinutes.Contains(m)) _remMinutes.Add(m);
                    _remMinutes.Sort();
                    break;
            }
            UpdateAll();
        };
        Popup.Show(anchor, list, P);
    }

    void PickCalendar()
    {
        var items = _calendars.Select(c => new OptionItem(c.Name, c, Theme.Hex(c.Color, P.Accent), c == _cal)).ToList();
        if (items.Count == 0) return;
        var list = new OptionList(P, items, 260) { Font = _font };
        list.Chosen += item =>
        {
            _cal = (CalendarInfo)item.Tag!;
            _colors.CalendarColor = CalendarColor;
            UpdateAll();
        };
        Popup.Show(_calendarBtn, list, P);
    }

    // =========================================================
    // 배치
    // =========================================================

    void ApplyScale()
    {
        // 새 글꼴을 먼저 적용한 뒤 이전 글꼴을 해제 (사용 중인 글꼴을 먼저 지우지 않도록)
        Font?[] old = [_font, _titleFont, _iconFont, _smallFont];
        _font = Ui.Font(13.5f * S);
        _titleFont = Ui.Font(20 * S, FontStyle.Bold);
        _iconFont = new Font(Ui.IconFamily, 15 * S, GraphicsUnit.Pixel);
        _smallFont = Ui.Font(12 * S);
        Font = _font;
        _title.Font = _titleFont;
        UpdateAll();
        foreach (var f in old) f?.Dispose();
    }

    static string Fmt(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

    static string DateLabel(DateTime d) =>
        (d.Year != DateTime.Today.Year ? $"{d.Year}년 " : "") + $"{d.Month}월 {d.Day}일 ({DayNames[(int)d.DayOfWeek]})";

    void ShowMessage(string? text, bool error)
    {
        _message = text;
        _messageIsError = error;
        Invalidate();
    }

    /// <summary>상태를 컨트롤에 반영하고 다시 배치.</summary>
    void UpdateAll()
    {
        if (IsDisposed) return;
        SuspendLayout();
        bool allDay = _allDay.Checked;
        bool series = _scope == EditScope.Series;
        bool editable = !_readOnly && !_busy;

        _startDateBtn.Text = DateLabel(_startDate);
        _endDateBtn.Text = DateLabel(_endDate);
        _startTimeBox.Text = Fmt(_startTime);
        _endTimeBox.Text = Fmt(_endTime);
        _startTimeBox.Visible = _endTimeBox.Visible = !allDay;
        // 반복 전체 수정에서는 날짜를 바꾸지 않는다 (원본의 시작일 유지)
        _startDateBtn.Enabled = _endDateBtn.Enabled = editable && !series;

        // 반복
        if (_scope == EditScope.Instance)
        {
            _repeatBtn.Text = "반복 일정 · 이 일정만 수정";
            _repeatBtn.Enabled = false;
        }
        else if (series && _masterLoading)
        {
            _repeatBtn.Text = "불러오는 중…";
            _repeatBtn.Enabled = false;
        }
        else
        {
            _repeatBtn.Text = Recurrence.Label(_rule.Kind, RuleBase);
            _repeatBtn.Enabled = editable;
        }
        _repeatEndBtn.Visible = _scope != EditScope.Instance && _rule.Kind is not (RepeatKind.None or RepeatKind.Custom) && !_masterLoading;
        _repeatEndBtn.Text = Recurrence.EndLabel(_rule);
        _repeatEndBtn.Enabled = editable;

        _scopeOne.Visible = _scopeAll.Visible = _recurringInstance;
        _scopeOne.Selected = _scope == EditScope.Instance;
        _scopeAll.Selected = series;
        _scopeOne.Invalidate();
        _scopeAll.Invalidate();

        // 캘린더
        _calendarBtn.Text = _cal?.Name ?? "캘린더 없음 (먼저 동기화하세요)";
        _calendarBtn.Dot = _cal != null ? CalendarColor : null;
        _calendarBtn.Enabled = editable && _scope != EditScope.Instance && _calendars.Count > 1;

        _delete.Visible = _ev != null && !_readOnly;
        _save.Visible = !_readOnly;
        _save.Enabled = !_busy && !(series && (_masterLoading || _master == null));
        _save.Text = _busy ? "저장 중…" : "저장";
        _openLink.Visible = IsSafeGoogleLink(_ev?.HtmlLink, out _);

        RebuildReminderChips(editable);
        DoLayout();
        ResumeLayout();
        Invalidate();
    }

    void RebuildReminderChips(bool editable)
    {
        foreach (var c in _reminderChips) { Controls.Remove(c); c.Dispose(); }
        _reminderChips.Clear();

        void Chip(string text, int tag)
        {
            var chip = new PillButton(P, text) { Chevron = true, Enabled = editable, Font = _font };
            chip.Click += (_, _) => PickReminder(chip, tag);
            _reminderChips.Add(chip);
            Controls.Add(chip);
        }

        if (_remDefault) Chip("기본 알림", -1);
        else if (_remMinutes.Count == 0) Chip("알림 없음", -1);
        else foreach (var m in _remMinutes) Chip(Reminders.Label(m, _allDay.Checked), m);

        _addReminder.Visible = editable && !_remDefault && _remMinutes.Count is > 0 and < 5;
    }

    void DoLayout()
    {
        _icons.Clear();
        _labels.Clear();
        int pad = Px(22), width = Px(W);
        int cx = pad + Px(34);                 // 내용 시작 x (왼쪽은 아이콘 칸)
        int cw = width - cx - pad;              // 내용 너비
        int rowH = Px(34), gap = Px(10);
        int y = Px(16);

        // 머리글
        _labels.Add((_ev == null ? "새 일정" : _readOnly ? "일정 보기" : "일정 편집", new Rectangle(pad, y, Px(200), Px(28))));
        _close.Bounds = new Rectangle(width - pad - Px(30) + Px(8), y, Px(30), Px(28));
        y += Px(38);

        if (_recurringInstance)
        {
            _scopeOne.Font = _scopeAll.Font = _font;
            _scopeOne.Bounds = new Rectangle(pad, y, _scopeOne.PreferredWidth(), Px(30));
            _scopeAll.Bounds = new Rectangle(_scopeOne.Right + Px(8), y, _scopeAll.PreferredWidth(), Px(30));
            y += Px(30) + Px(14);
        }

        // 제목
        _title.Bounds = new Rectangle(pad, y, width - pad * 2, Px(42));
        y += Px(42) + Px(16);

        // 색상
        _icons.Add(("\uE790", IconRect(pad, y, Px(26))));
        _colors.Bounds = new Rectangle(cx - Px(2), y, Px(26 * 12 + 4), Px(26));
        y += Px(26) + Px(14);

        // 날짜 / 시간
        int labelW = Px(36), dateW = Px(170), timeW = Px(74);
        _icons.Add(("\uE823", IconRect(pad, y, rowH)));
        _labels.Add(("시작", new Rectangle(cx, y, labelW, rowH)));
        _startDateBtn.Bounds = new Rectangle(cx + labelW, y, dateW, rowH);
        _startTimeBox.Bounds = new Rectangle(_startDateBtn.Right + Px(8), y, timeW, rowH);
        _allDay.Bounds = new Rectangle(width - pad - Px(38), y, Px(38), rowH);
        _allDayLabel = new Rectangle(_allDay.Left - Px(40), y, Px(36), rowH);
        y += rowH + gap;
        _labels.Add(("종료", new Rectangle(cx, y, labelW, rowH)));
        _endDateBtn.Bounds = new Rectangle(cx + labelW, y, dateW, rowH);
        _endTimeBox.Bounds = new Rectangle(_endDateBtn.Right + Px(8), y, timeW, rowH);
        y += rowH + gap + Px(4);

        // 반복
        _icons.Add(("\uE8EE", IconRect(pad, y, rowH)));
        _repeatBtn.Font = _repeatEndBtn.Font = _font;
        _repeatBtn.Bounds = new Rectangle(cx, y, Math.Min(cw, Math.Max(Px(150), _repeatBtn.PreferredWidth())), rowH);
        _repeatEndBtn.Bounds = new Rectangle(_repeatBtn.Right + Px(8), y, Math.Max(Px(110), _repeatEndBtn.PreferredWidth()), rowH);
        y += rowH + gap;

        // 알림 (줄바꿈되는 칩)
        _icons.Add(("\uEA8F", IconRect(pad, y, rowH)));
        int x = cx;
        var chips = new List<PillButton>(_reminderChips);
        if (_addReminder.Visible) chips.Add(_addReminder);
        foreach (var chip in chips)
        {
            chip.Font = _font;
            int w = chip.PreferredWidth();
            if (x + w > cx + cw && x > cx) { x = cx; y += rowH + Px(6); }
            chip.Bounds = new Rectangle(x, y, w, rowH);
            x += w + Px(6);
        }
        y += rowH + gap;

        // 장소 / 메모
        _icons.Add(("\uE81D", IconRect(pad, y, rowH)));
        _location.Bounds = new Rectangle(cx, y, cw, rowH);
        y += rowH + gap;
        _icons.Add(("\uE70B", IconRect(pad, y, rowH)));
        _notes.Bounds = new Rectangle(cx, y, cw, Px(92));
        y += Px(92) + gap;

        // 캘린더
        _icons.Add(("\uE787", IconRect(pad, y, rowH)));
        _calendarBtn.Font = _font;
        _calendarBtn.Bounds = new Rectangle(cx, y, Math.Min(cw, Math.Max(Px(160), _calendarBtn.PreferredWidth())), rowH);
        y += rowH + Px(6);

        if (_openLink.Visible)
        {
            _openLink.Font = _smallFont;
            _openLink.Bounds = new Rectangle(cx - Px(6), y, _openLink.PreferredWidth(), Px(28));
            y += Px(28);
        }

        // 안내 / 오류 메시지
        _messageRect = new Rectangle(pad, y + Px(4), width - pad * 2, Px(22));
        y += Px(30);

        // 버튼
        int bh = Px(38);
        _save.Bounds = new Rectangle(width - pad - Px(92), y, Px(92), bh);
        _cancel.Bounds = new Rectangle(_save.Left - Px(8) - Px(80), y, Px(80), bh);
        _delete.Bounds = new Rectangle(pad - Px(6), y, Px(84), bh);
        y += bh + pad;

        ClientSize = new Size(width, y);
    }

    Rectangle IconRect(int pad, int y, int h) => new(pad - Px(4), y, Px(28), h);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(P.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        foreach (var (glyph, r) in _icons) Ui.Glyph(g, glyph, _iconFont, P.Muted, r);
        for (int i = 0; i < _labels.Count; i++)
        {
            var (text, r) = _labels[i];
            TextRenderer.DrawText(g, text, i == 0 ? _smallFont : _font, r, P.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(g, "종일", _font, _allDayLabel, P.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);

        string? msg = _message;
        bool err = _messageIsError;
        if (msg == null && _scope == EditScope.Series && !_masterLoading)
            msg = "모든 반복 일정에 적용됩니다. 날짜는 그대로 두고 시간·내용만 바뀝니다.";
        if (msg != null)
            TextRenderer.DrawText(g, msg, _smallFont, _messageRect, err ? P.Danger : P.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // =========================================================
    // 저장 / 삭제
    // =========================================================

    async Task SaveAsync()
    {
        if (_readOnly || _busy || !_save.Enabled) return;
        if (_startTimeBox.Box.Focused) CommitTypedTime(true);
        if (_endTimeBox.Box.Focused) CommitTypedTime(false);
        if (_cal == null) { ShowMessage("저장할 캘린더가 없습니다. 먼저 동기화해 주세요.", true); return; }

        bool allDay = _allDay.Checked;
        var start = StartDT;
        var end = EndDT;
        if (end < start) { ShowMessage(allDay ? "종료 날짜가 시작 날짜보다 빠릅니다." : "종료 시간이 시작 시간보다 빠릅니다.", true); return; }
        if (allDay) end = end.AddDays(1); // Google 은 종일 일정의 끝을 '다음 날'로 표현

        if (_scope == EditScope.Series && _master != null)
        {
            // 전체 수정: 원본의 시작 날짜는 유지하고 시간/길이만 반영
            var length = end - start;
            start = _master.Start.Date + (allDay ? TimeSpan.Zero : _startTime);
            end = start + length;
        }

        bool isNew = _ev == null;
        var draft = new EventDraft
        {
            CalendarId = _cal.Id,
            Title = _title.Text.Trim(),
            Location = _location.Text.Trim(),
            Description = _notes.Text,
            AllDay = allDay,
            Start = start,
            End = end,
            ColorSet = isNew ? _colors.Selected != 0 : _colorDirty,
            ColorId = _colors.Selected == 0 ? null : _colors.Selected.ToString(),
            Reminders = isNew || _remDirty ? new ReminderSpec { UseDefault = _remDefault, Minutes = _remMinutes.ToList() } : null,
        };
        // 반복 규칙: 새 일정이면 선택한 대로, 기존 일정이면 바꿨을 때만 (전체 수정은 요일 등이 시작일에 맞도록 다시 씀)
        bool writeRule = _scope != EditScope.Instance &&
                         (isNew ? _rule.Kind != RepeatKind.None
                                : _ruleDirty || (_scope == EditScope.Series && _rule.Kind != RepeatKind.Custom));
        if (writeRule)
        {
            draft.RecurrenceSet = true;
            draft.Recurrence = Recurrence.Build(_rule, start, allDay);
        }
        if (isNew && _s.DefaultCalendarId != _cal.Id)
        {
            _s.DefaultCalendarId = _cal.Id;
            _s.Save();
        }

        SetBusy(true);
        try
        {
            await _svc.SaveEventAsync(_ev, draft, _scope);
            Close();
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowMessage("저장하지 못했습니다: " + ex.Message, true);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false);
        }
    }

    async Task DeleteAsync()
    {
        if (_ev == null || _readOnly || _busy) return;
        bool series = _scope == EditScope.Series;
        var (title, message) = series ? ("모든 반복 일정을 삭제할까요?", $"'{_ev.Title}' 의 모든 반복 일정이 삭제됩니다.")
                             : _recurringInstance ? ("이 날짜만 삭제할까요?", $"'{_ev.Title}' 반복 일정 중 이 날짜의 일정만 삭제됩니다.")
                             : ("일정을 삭제할까요?", $"'{_ev.Title}'");
        if (!Dialogs.Confirm(this, title, message, ok: "삭제", danger: true)) return;
        SetBusy(true);
        try
        {
            await _svc.DeleteEventAsync(_ev, series);
            Close();
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowMessage("삭제하지 못했습니다: " + ex.Message, true);
        }
        finally
        {
            if (!IsDisposed) SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        UpdateAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon?.Dispose();
            _font?.Dispose(); _titleFont?.Dispose(); _iconFont?.Dispose(); _smallFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
