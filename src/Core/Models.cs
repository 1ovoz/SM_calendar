using System.Drawing;
using System.Text.Json.Serialization;

namespace SMCalendar.Core;

public sealed class CalendarInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4F8CFF";
    public string AccessRole { get; set; } = "reader";
    public bool Primary { get; set; }
    public bool SelectedInGoogle { get; set; } = true;
    /// <summary>IANA 시간대 (예: Asia/Seoul). 반복 일정 생성 시 필요.</summary>
    public string? TimeZone { get; set; }

    [JsonIgnore] public bool CanWrite => AccessRole is "owner" or "writer";
    [JsonIgnore] public bool IsHoliday => Id.Contains("#holiday@", StringComparison.Ordinal);
}

public sealed class CalEvent
{
    public string CalendarId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string? Description { get; set; }
    public bool AllDay { get; set; }
    /// <summary>로컬 시각. 종일 일정은 날짜 00:00.</summary>
    public DateTime Start { get; set; }
    /// <summary>로컬 시각. 종일 일정은 다음 날 00:00 (Google 과 동일하게 배타적 끝).</summary>
    public DateTime End { get; set; }
    public string? ColorId { get; set; }
    public string? RecurringEventId { get; set; }
    public string? HtmlLink { get; set; }
    /// <summary>true 면 캘린더 기본 알림 사용.</summary>
    public bool ReminderDefault { get; set; } = true;
    /// <summary>개별 알림 (시작 몇 분 전).</summary>
    public List<int>? ReminderMinutes { get; set; }
    /// <summary>반복 규칙 (RRULE 등). 반복 일정의 원본(master)을 따로 조회했을 때만 채워진다.</summary>
    public List<string>? Recurrence { get; set; }

    public bool OccursOn(DateTime day)
    {
        var next = day.AddDays(1);
        return Start < next && (End > day || Start >= day);
    }

    [JsonIgnore] public bool IsMultiDay => AllDay ? (End - Start).TotalDays > 1 : End > Start.Date.AddDays(1);
}

public sealed class ReminderSpec
{
    public bool UseDefault { get; set; }
    public List<int> Minutes { get; set; } = new();
}

/// <summary>반복 일정 편집 범위.</summary>
public enum EditScope { Single, Instance, Series }

/// <summary>편집기에서 저장할 내용. null/false 인 선택 항목은 서버 값을 건드리지 않는다.</summary>
public sealed class EventDraft
{
    public string CalendarId { get; set; } = "";
    public string? TimeZone { get; set; }
    /// <summary>ColorSet 이 true 일 때만 반영. null = 캘린더 기본색.</summary>
    public string? ColorId { get; set; }
    public bool ColorSet { get; set; }
    /// <summary>null 이면 알림을 건드리지 않음.</summary>
    public ReminderSpec? Reminders { get; set; }
    /// <summary>RecurrenceSet 이 true 일 때만 반영. null = 반복 안 함.</summary>
    public List<string>? Recurrence { get; set; }
    public bool RecurrenceSet { get; set; }
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string? Description { get; set; }
    public bool AllDay { get; set; }
    public DateTime Start { get; set; }
    /// <summary>종일 일정이면 배타적 끝 날짜(마지막 날 + 1).</summary>
    public DateTime End { get; set; }
}

public sealed class EventCache
{
    public List<CalendarInfo> Calendars { get; set; } = new();
    public List<CalEvent> Events { get; set; } = new();
    public DateTime? LastSync { get; set; }
}

// ---- Google API DTO ----

public sealed class GCalendarList
{
    public List<GCalendarListEntry>? Items { get; set; }
    public string? NextPageToken { get; set; }
}

public sealed class GCalendarListEntry
{
    public string? Id { get; set; }
    public string? Summary { get; set; }
    public string? SummaryOverride { get; set; }
    public string? BackgroundColor { get; set; }
    public string? AccessRole { get; set; }
    public bool? Primary { get; set; }
    public bool? Selected { get; set; }
    public string? TimeZone { get; set; }
}

public sealed class GEventList
{
    public List<GEvent>? Items { get; set; }
    public string? NextPageToken { get; set; }
}

public sealed class GEvent
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? ColorId { get; set; }
    public string? RecurringEventId { get; set; }
    public string? HtmlLink { get; set; }
    public GEventTime? Start { get; set; }
    public GEventTime? End { get; set; }
    public GReminders? Reminders { get; set; }
    public List<string>? Recurrence { get; set; }
}

public sealed class GReminders
{
    public bool? UseDefault { get; set; }
    public List<GReminder>? Overrides { get; set; }
}

public sealed class GReminder
{
    public string? Method { get; set; }
    public int Minutes { get; set; }
}

public sealed class GEventTime
{
    public string? Date { get; set; }
    [JsonPropertyName("dateTime")] public string? DateTimeValue { get; set; }
}

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

public sealed class ClientSecretFile
{
    public ClientInfo? Installed { get; set; }
    public ClientInfo? Web { get; set; }
}

public sealed class ClientInfo
{
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [JsonPropertyName("client_secret")] public string? ClientSecret { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WidgetSettings))]
[JsonSerializable(typeof(EventCache))]
[JsonSerializable(typeof(GCalendarList))]
[JsonSerializable(typeof(GEventList))]
[JsonSerializable(typeof(GEvent))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(ClientSecretFile))]
internal partial class JsonCtx : JsonSerializerContext;
