namespace SMCalendar.Core;

/// <summary>
/// 인증 · API · 캐시를 묶고 동기화 일정을 관리한다. UI 스레드에서만 사용.
/// 타이머는 하나만 쓰고, 성공 시 설정 주기 / 실패 시 백오프로 다음 동기화를 예약한다.
/// </summary>
internal sealed class CalendarService
{
    public GoogleAuth Auth { get; } = new();
    public CalendarApi Api { get; }
    public EventStore Store { get; } = new();
    public bool Demo { get; }

    public string Status { get; private set; } = "";
    public bool IsSyncing { get; private set; }
    public bool IsSigningIn { get; private set; }
    public bool IsSignedIn => Demo || Auth.IsSignedIn;
    public string? Account => Store.Calendars.FirstOrDefault(c => c.Primary)?.Id;

    /// <summary>일정/상태가 바뀌면 발생 (UI 다시 그리기).</summary>
    public event Action? Changed;

    readonly AppSettings _settings;
    readonly System.Windows.Forms.Timer _timer = new();
    /// <summary>위젯마다 보고 있는 날짜 범위. 동기화는 이 범위들을 모두 덮는다.</summary>
    readonly Dictionary<object, (DateTime Min, DateTime Max)> _ranges = new();
    int _failures;
    bool _pending;

    public CalendarService(AppSettings settings, bool demo)
    {
        _settings = settings;
        Demo = demo;
        Api = new CalendarApi(Auth);
        _timer.Tick += (_, _) => { _timer.Stop(); _ = SyncAsync(); };
    }

    public void Init()
    {
        if (Demo)
        {
            DemoData.Fill(Store);
            Status = "데모 모드";
            return;
        }
        Auth.Load();
        Status = !Auth.IsSignedIn ? "구글 계정 연결하기" : "";
    }

    /// <summary>위젯을 처음 띄울 때 캐시를 읽는다 (위젯이 모두 꺼진 채 부팅하면 읽지 않음).</summary>
    public void EnsureLoaded()
    {
        if (Demo || Store.Loaded) return;
        Store.Load();
        if (Auth.IsSignedIn && Store.LastSync is { } t) Status = $"캐시 · {t:MM/dd HH:mm}";
    }

    /// <summary>
    /// 위젯이 모두 꺼지면 주기 동기화를 멈춘다 (백그라운드 리소스 최소화).
    /// 다시 켜면 마지막 동기화가 오래됐을 때만 바로 동기화.
    /// </summary>
    public bool Paused { get; private set; }

    public void Pause()
    {
        Paused = true;
        _timer.Stop();
    }

    public void Resume()
    {
        if (!Paused && _timer.Enabled) return;
        Paused = false;
        EnsureLoaded();
        if (!IsSignedIn || Demo) return;
        var since = DateTime.Now - (Store.LastSync ?? DateTime.MinValue);
        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.SyncMinutes));
        ScheduleSync(since >= interval ? TimeSpan.FromMilliseconds(300) : interval - since);
    }

    /// <summary>위젯이 보고 있는 범위 (앞뒤로 한 달씩 여유를 두고 가져온다).</summary>
    public void SetViewRange(object owner, DateTime start, DateTime end) =>
        _ranges[owner] = (start.AddDays(-35), end.AddDays(35));

    public void RemoveViewRange(object owner) => _ranges.Remove(owner);

    (DateTime Min, DateTime Max) SyncRange()
    {
        if (_ranges.Count == 0) return (DateTime.Today.AddDays(-35), DateTime.Today.AddDays(70));
        return (_ranges.Values.Min(r => r.Min), _ranges.Values.Max(r => r.Max));
    }

    public void ScheduleSync(TimeSpan delay)
    {
        if (Demo || Paused) return;
        _timer.Stop();
        _timer.Interval = (int)Math.Clamp(delay.TotalMilliseconds, 1, int.MaxValue);
        _timer.Start();
    }

    public async Task SyncAsync()
    {
        if (Demo || !Auth.IsSignedIn) return;
        if (IsSyncing) { _pending = true; return; }
        EnsureLoaded();

        IsSyncing = true;
        _timer.Stop();
        Changed?.Invoke();
        try
        {
            var (min, max) = SyncRange();
            var calendars = await Api.ListCalendarsAsync(default);
            Store.SetCalendars(calendars);

            var visible = calendars.Where(_settings.IsCalendarVisible).ToList();
            var results = await Task.WhenAll(visible.Select(async c =>
            {
                try { return (c.Id, Events: await Api.ListEventsAsync(c.Id, min, max, default)); }
                catch (ApiException ex) when (ex.Status is 403 or 404) { return (c.Id, Events: (List<CalEvent>?)null); }
            }));
            foreach (var (id, events) in results)
                if (events != null) Store.ReplaceRange(id, min, max, events);

            Store.LastSync = DateTime.Now;
            Store.Save();
            _failures = 0;
            Status = $"동기화 {DateTime.Now:HH:mm}";
            ScheduleSync(TimeSpan.FromMinutes(Math.Max(1, _settings.SyncMinutes)));
        }
        catch (AuthException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex)
        {
            // 부팅 직후 네트워크가 아직 없을 때 등: 15초부터 최대 5분까지 늘려가며 재시도
            _failures++;
            Status = ex is ApiException api ? $"오류 {api.Message}" : "오프라인 · 캐시 표시 중";
            ScheduleSync(TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, _failures - 1))));
        }
        finally
        {
            IsSyncing = false;
            Changed?.Invoke();
            if (_pending)
            {
                _pending = false;
                ScheduleSync(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    public async Task SignInAsync(IWin32Window? owner)
    {
        if (Demo || IsSigningIn) return;
        if (!Auth.HasClient && !PromptClientFile(owner)) return;

        IsSigningIn = true;
        Status = "브라우저에서 로그인하세요…";
        Changed?.Invoke();
        try
        {
            await Auth.SignInAsync(default);
            Status = "로그인 완료";
            Store.Clear();
            await SyncAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "로그인 시간 초과";
        }
        catch (Exception ex)
        {
            Status = "로그인 실패";
            UI.Dialogs.Alert(owner, "로그인하지 못했습니다", ex.Message, warning: true);
        }
        finally
        {
            IsSigningIn = false;
            Changed?.Invoke();
        }
    }

    public void SignOut()
    {
        if (Demo) return;
        _timer.Stop();
        Auth.SignOut();
        Store.Clear();
        Status = "구글 계정 연결하기";
        Changed?.Invoke();
    }

    bool PromptClientFile(IWin32Window? owner)
    {
        if (!UI.Dialogs.Confirm(owner, "OAuth 클라이언트 파일이 필요합니다",
                "Google 캘린더에 연결하려면 Google Cloud 에서 만든 OAuth 클라이언트 파일(client_secret_….json)이 필요합니다. " +
                "만드는 방법은 README.md 의 \"Google 연동 설정\" 을 참고하세요.\n\n지금 파일을 선택할까요?",
                ok: "파일 선택")) return false;

        using var dlg = new OpenFileDialog
        {
            Title = "OAuth 클라이언트 JSON 선택",
            Filter = "JSON 파일 (*.json)|*.json",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        if (Auth.ImportClientFile(dlg.FileName)) return true;

        UI.Dialogs.Alert(owner, "올바른 파일이 아닙니다", "'데스크톱 앱' 유형으로 만든 OAuth 클라이언트 JSON 을 선택하세요.", warning: true);
        return false;
    }

    // ---- 쓰기 ----

    /// <summary>반복 일정 회차의 원본(master)을 불러온다. recurrence 규칙을 편집할 때 필요.</summary>
    public async Task<CalEvent?> LoadMasterAsync(CalEvent instance)
    {
        if (Demo || instance.RecurringEventId == null) return null;
        return await Api.GetEventAsync(instance.CalendarId, instance.RecurringEventId, default);
    }

    /// <summary>반복 일정은 시간대가 필수라서 캘린더(없으면 기본 캘린더)의 시간대를 쓴다.</summary>
    public string? TimeZoneFor(string calendarId) =>
        Store.FindCalendar(calendarId)?.TimeZone ?? Store.Calendars.FirstOrDefault(c => c.Primary)?.TimeZone;

    public async Task SaveEventAsync(CalEvent? original, EventDraft d, EditScope scope)
    {
        d.TimeZone ??= TimeZoneFor(d.CalendarId);
        if (Demo)
        {
            SaveDemo(original, d);
            return;
        }

        bool resync = false;
        if (original == null)
        {
            var saved = await Api.InsertEventAsync(d, default);
            // 반복 일정은 회차들이 동기화로 들어오므로 원본은 캐시에 넣지 않는다
            if (d.Recurrence != null) resync = true;
            else Store.Upsert(saved);
        }
        else if (scope == EditScope.Series && original.RecurringEventId != null)
        {
            var masterId = original.RecurringEventId;
            var calendarId = original.CalendarId;
            if (d.CalendarId != calendarId)
            {
                await Api.MoveEventAsync(calendarId, masterId, d.CalendarId, default);
                calendarId = d.CalendarId;
            }
            await Api.PatchEventAsync(calendarId, masterId, d, default);
            resync = true;
        }
        else
        {
            var calendarId = original.CalendarId;
            var eventId = original.Id;
            if (scope == EditScope.Single && d.CalendarId != calendarId)
            {
                var moved = await Api.MoveEventAsync(calendarId, eventId, d.CalendarId, default);
                calendarId = moved.CalendarId;
                eventId = moved.Id;
            }
            if (scope == EditScope.Instance) d.RecurrenceSet = false; // 회차 하나에는 반복 규칙을 넣을 수 없다
            var saved = await Api.PatchEventAsync(calendarId, eventId, d, default);
            Store.Remove(original.CalendarId, original.Id);
            if (saved.Recurrence is { Count: > 0 }) resync = true; // 단일 일정 → 반복 일정
            else Store.Upsert(saved);
        }
        Store.Save();
        Changed?.Invoke();
        if (resync) ScheduleSync(TimeSpan.FromMilliseconds(100));
    }

    public async Task DeleteEventAsync(CalEvent e, bool wholeSeries)
    {
        if (Demo)
        {
            if (wholeSeries && e.RecurringEventId != null) Store.Events.RemoveAll(x => x.RecurringEventId == e.RecurringEventId);
            else Store.Remove(e.CalendarId, e.Id);
            Changed?.Invoke();
            return;
        }
        if (wholeSeries && e.RecurringEventId != null)
        {
            await Api.DeleteEventAsync(e.CalendarId, e.RecurringEventId, default);
            Store.Events.RemoveAll(x => x.CalendarId == e.CalendarId && x.RecurringEventId == e.RecurringEventId);
        }
        else
        {
            await Api.DeleteEventAsync(e.CalendarId, e.Id, default);
            Store.Remove(e.CalendarId, e.Id);
        }
        Store.Save();
        Changed?.Invoke();
    }

    void SaveDemo(CalEvent? original, EventDraft d)
    {
        var e = new CalEvent
        {
            CalendarId = d.CalendarId, Id = original?.Id ?? Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(d.Title) ? "(제목 없음)" : d.Title,
            Location = d.Location, Description = d.Description, AllDay = d.AllDay, Start = d.Start, End = d.End,
            ColorId = d.ColorSet ? d.ColorId : original?.ColorId,
            ReminderDefault = d.Reminders?.UseDefault ?? original?.ReminderDefault ?? true,
            ReminderMinutes = d.Reminders?.Minutes ?? original?.ReminderMinutes,
            RecurringEventId = original?.RecurringEventId,
        };
        if (original != null) Store.Remove(original.CalendarId, original.Id);
        Store.Upsert(e);
        Changed?.Invoke();
    }
}
