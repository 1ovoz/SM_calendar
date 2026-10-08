using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SMCalendar.Core;

/// <summary>Google Calendar REST v3 를 필요한 만큼만 직접 호출 (무거운 클라이언트 라이브러리 미사용).</summary>
internal sealed class CalendarApi(GoogleAuth auth)
{
    const string Base = "https://www.googleapis.com/calendar/v3/";
    const string EventFields = "id,status,summary,description,location,colorId,recurringEventId,htmlLink,start,end";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<List<CalendarInfo>> ListCalendarsAsync(CancellationToken ct)
    {
        var result = new List<CalendarInfo>();
        string? page = null;
        do
        {
            var path = "users/me/calendarList?maxResults=250&fields=" +
                       Uri.EscapeDataString("nextPageToken,items(id,summary,summaryOverride,backgroundColor,accessRole,primary,selected)") +
                       (page != null ? "&pageToken=" + Uri.EscapeDataString(page) : "");
            var list = await GetJsonAsync(path, JsonCtx.Default.GCalendarList, ct).ConfigureAwait(false);
            foreach (var c in list.Items ?? [])
            {
                if (c.Id == null) continue;
                result.Add(new CalendarInfo
                {
                    Id = c.Id,
                    Name = c.SummaryOverride ?? c.Summary ?? c.Id,
                    Color = c.BackgroundColor ?? "#4F8CFF",
                    AccessRole = c.AccessRole ?? "reader",
                    Primary = c.Primary == true,
                    SelectedInGoogle = c.Selected == true || c.Primary == true,
                });
            }
            page = list.NextPageToken;
        } while (page != null);

        // 기본 캘린더를 맨 앞에
        return result.OrderByDescending(c => c.Primary).ToList();
    }

    public async Task<List<CalEvent>> ListEventsAsync(string calendarId, DateTime min, DateTime max, CancellationToken ct)
    {
        var result = new List<CalEvent>();
        string? page = null;
        do
        {
            var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events" +
                       "?singleEvents=true&maxResults=2500" +
                       "&timeMin=" + Uri.EscapeDataString(Rfc3339(min)) +
                       "&timeMax=" + Uri.EscapeDataString(Rfc3339(max)) +
                       "&fields=" + Uri.EscapeDataString($"nextPageToken,items({EventFields})") +
                       (page != null ? "&pageToken=" + Uri.EscapeDataString(page) : "");
            var list = await GetJsonAsync(path, JsonCtx.Default.GEventList, ct).ConfigureAwait(false);
            foreach (var g in list.Items ?? [])
                if (Convert(g, calendarId) is { } e) result.Add(e);
            page = list.NextPageToken;
        } while (page != null);
        return result;
    }

    public async Task<CalEvent> InsertEventAsync(EventDraft d, CancellationToken ct)
    {
        var path = $"calendars/{Uri.EscapeDataString(d.CalendarId)}/events?fields={Uri.EscapeDataString(EventFields)}";
        using var resp = await SendAsync(HttpMethod.Post, path, BuildJson(d, patch: false), ct).ConfigureAwait(false);
        return await ReadEventAsync(resp, d.CalendarId, ct).ConfigureAwait(false);
    }

    public async Task<CalEvent> PatchEventAsync(string calendarId, string eventId, EventDraft d, CancellationToken ct)
    {
        var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}?fields={Uri.EscapeDataString(EventFields)}";
        using var resp = await SendAsync(HttpMethod.Patch, path, BuildJson(d, patch: true), ct).ConfigureAwait(false);
        return await ReadEventAsync(resp, calendarId, ct).ConfigureAwait(false);
    }

    public async Task<CalEvent> MoveEventAsync(string calendarId, string eventId, string destinationId, CancellationToken ct)
    {
        var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}/move" +
                   $"?destination={Uri.EscapeDataString(destinationId)}&fields={Uri.EscapeDataString(EventFields)}";
        using var resp = await SendAsync(HttpMethod.Post, path, null, ct).ConfigureAwait(false);
        return await ReadEventAsync(resp, destinationId, ct).ConfigureAwait(false);
    }

    public async Task DeleteEventAsync(string calendarId, string eventId, CancellationToken ct)
    {
        try
        {
            var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
            using var _ = await SendAsync(HttpMethod.Delete, path, null, ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.Status is 404 or 410) { /* 이미 삭제됨 */ }
    }

    // ---- 내부 ----

    async Task<T> GetJsonAsync<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false)
               ?? throw new ApiException(0, "빈 응답");
    }

    async Task<CalEvent> ReadEventAsync(HttpResponseMessage resp, string calendarId, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var g = await JsonSerializer.DeserializeAsync(stream, JsonCtx.Default.GEvent, ct).ConfigureAwait(false);
        return (g != null ? Convert(g, calendarId) : null) ?? throw new ApiException(0, "일정 응답을 읽을 수 없음");
    }

    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[]? json, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            var token = await auth.GetAccessTokenAsync(ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(method, Base + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json != null)
            {
                req.Content = new ByteArrayContent(json);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            }
            var resp = await Net.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                resp.Dispose();
                auth.InvalidateAccessToken();
                continue;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var status = (int)resp.StatusCode;
                resp.Dispose();
                throw new ApiException(status, ExtractError(body, status));
            }
            return resp;
        }
    }

    static string ExtractError(string body, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.ValueKind == JsonValueKind.Object &&
                err.TryGetProperty("message", out var msg))
                return $"{status} {msg.GetString()}";
        }
        catch { }
        return $"HTTP {status}";
    }

    static byte[] BuildJson(EventDraft d, bool patch)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("summary", d.Title);
            w.WriteString("location", d.Location ?? "");
            w.WriteString("description", d.Description ?? "");
            WriteTime(w, "start", d.AllDay, d.Start, patch);
            WriteTime(w, "end", d.AllDay, d.End, patch);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    static void WriteTime(Utf8JsonWriter w, string name, bool allDay, DateTime t, bool patch)
    {
        w.WriteStartObject(name);
        if (allDay)
        {
            w.WriteString("date", t.ToString("yyyy-MM-dd", Inv));
            if (patch) w.WriteNull("dateTime"); // 시간 일정 → 종일 일정 전환 시 기존 값 제거
        }
        else
        {
            w.WriteString("dateTime", Rfc3339(t));
            if (patch) w.WriteNull("date");
        }
        w.WriteEndObject();
    }

    static string Rfc3339(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local))
            .ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv);

    static CalEvent? Convert(GEvent g, string calendarId)
    {
        if (g.Id == null || g.Status == "cancelled" || g.Start == null || g.End == null) return null;
        var e = new CalEvent
        {
            CalendarId = calendarId,
            Id = g.Id,
            Title = string.IsNullOrWhiteSpace(g.Summary) ? "(제목 없음)" : g.Summary,
            Location = g.Location,
            Description = g.Description,
            ColorId = g.ColorId,
            RecurringEventId = g.RecurringEventId,
            HtmlLink = g.HtmlLink,
        };
        if (g.Start.Date != null)
        {
            e.AllDay = true;
            e.Start = DateTime.ParseExact(g.Start.Date, "yyyy-MM-dd", Inv);
            e.End = g.End.Date != null ? DateTime.ParseExact(g.End.Date, "yyyy-MM-dd", Inv) : e.Start.AddDays(1);
            if (e.End <= e.Start) e.End = e.Start.AddDays(1);
        }
        else if (g.Start.DateTimeValue != null)
        {
            e.Start = DateTimeOffset.Parse(g.Start.DateTimeValue, Inv).LocalDateTime;
            e.End = g.End.DateTimeValue != null ? DateTimeOffset.Parse(g.End.DateTimeValue, Inv).LocalDateTime : e.Start;
            if (e.End < e.Start) e.End = e.Start;
        }
        else return null;
        return e;
    }
}
