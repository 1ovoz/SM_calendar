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
    DateTime _min, _max;
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
        Store.Load();
        Status = !Auth.IsSignedIn ? "구글 계정 연결하기"
               : Store.LastSync is { } t ? $"캐시 · {t:MM/dd HH:mm}" : "";
    }

    /// <summary>현재 보이는 달력 범위 (앞뒤로 한 달씩 여유를 두고 가져온다).</summary>
    public void SetViewRange(DateTime gridStart, DateTime gridEnd)
    {
        _min = gridStart.AddDays(-35);
        _max = gridEnd.AddDays(35);
    }

    public void ScheduleSync(TimeSpan delay)
    {
        if (Demo) return;
        _timer.Stop();
        _timer.Interval = (int)Math.Clamp(delay.TotalMilliseconds, 1, int.MaxValue);
        _timer.Start();
    }

    public async Task SyncAsync()
    {
        if (Demo || !Auth.IsSignedIn) return;
        if (IsSyncing) { _pending = true; return; }

        IsSyncing = true;
        _timer.Stop();
        Changed?.Invoke();
        try
        {
            var (min, max) = (_min, _max);
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
            MessageBox.Show(owner, ex.Message, "SM Calendar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        var answer = MessageBox.Show(owner,
            "Google 캘린더에 연결하려면 Google Cloud 에서 만든 OAuth 클라이언트 파일(client_secret_….json)이 필요합니다.\n\n" +
            "만드는 방법은 README.md 의 \"Google 연동 설정\" 을 참고하세요.\n\n지금 파일을 선택할까요?",
            "SM Calendar", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes) return false;

        using var dlg = new OpenFileDialog
        {
            Title = "OAuth 클라이언트 JSON 선택",
            Filter = "JSON 파일 (*.json)|*.json",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        if (Auth.ImportClientFile(dlg.FileName)) return true;

        MessageBox.Show(owner, "올바른 OAuth 클라이언트 파일이 아닙니다.\n'데스크톱 앱' 유형으로 만든 JSON 을 선택하세요.",
            "SM Calendar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    // ---- 쓰기 ----

    public async Task SaveEventAsync(CalEvent? original, EventDraft d)
    {
        if (Demo)
        {
            var e = new CalEvent
            {
                CalendarId = d.CalendarId, Id = original?.Id ?? Guid.NewGuid().ToString("N"),
                Title = string.IsNullOrWhiteSpace(d.Title) ? "(제목 없음)" : d.Title,
                Location = d.Location, Description = d.Description, AllDay = d.AllDay, Start = d.Start, End = d.End,
                ColorId = original?.ColorId,
            };
            if (original != null) Store.Remove(original.CalendarId, original.Id);
            Store.Upsert(e);
            Changed?.Invoke();
            return;
        }

        CalEvent saved;
        if (original == null)
        {
            saved = await Api.InsertEventAsync(d, default);
        }
        else
        {
            var calendarId = original.CalendarId;
            var eventId = original.Id;
            if (d.CalendarId != calendarId)
            {
                var moved = await Api.MoveEventAsync(calendarId, eventId, d.CalendarId, default);
                calendarId = moved.CalendarId;
                eventId = moved.Id;
            }
            saved = await Api.PatchEventAsync(calendarId, eventId, d, default);
            Store.Remove(original.CalendarId, original.Id);
        }
        Store.Upsert(saved);
        Store.Save();
        Changed?.Invoke();
    }

    public async Task DeleteEventAsync(CalEvent e)
    {
        if (!Demo)
        {
            await Api.DeleteEventAsync(e.CalendarId, e.Id, default);
            Store.Remove(e.CalendarId, e.Id);
            Store.Save();
        }
        else Store.Remove(e.CalendarId, e.Id);
        Changed?.Invoke();
    }
}
