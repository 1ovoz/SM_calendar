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

    public bool OccursOn(DateTime day)
    {
        var next = day.AddDays(1);
        return Start < next && (End > day || Start >= day);
    }

    [JsonIgnore] public bool IsMultiDay => AllDay ? (End - Start).TotalDays > 1 : End > Start.Date.AddDays(1);
}

/// <summary>편집기에서 저장할 내용.</summary>
public sealed class EventDraft
{
    public string CalendarId { get; set; } = "";
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
[JsonSerializable(typeof(EventCache))]
[JsonSerializable(typeof(GCalendarList))]
[JsonSerializable(typeof(GEventList))]
[JsonSerializable(typeof(GEvent))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(ClientSecretFile))]
internal partial class JsonCtx : JsonSerializerContext;
