using System.Globalization;

namespace SMCalendar.Core;

public enum RepeatKind { None, Daily, Weekly, Monthly, Yearly, Weekdays, Custom }

/// <summary>편집기에서 다루는 단순화된 반복 규칙. 표현할 수 없는 규칙은 Custom 으로 원문을 그대로 보존한다.</summary>
public sealed class RepeatRule
{
    public RepeatKind Kind { get; set; }
    public DateTime? Until { get; set; }
    public int? Count { get; set; }
    public List<string>? Raw { get; set; }

    public RepeatRule Clone() => new() { Kind = Kind, Until = Until, Count = Count, Raw = Raw };
}

public static class Recurrence
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] ByDay = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];
    const string Weekdays = "MO,TU,WE,TH,FR";

    public static RepeatRule Parse(List<string>? lines, DateTime start)
    {
        if (lines == null || lines.Count == 0) return new RepeatRule();
        var custom = new RepeatRule { Kind = RepeatKind.Custom, Raw = lines };
        // RRULE 한 줄만 있는 경우만 단순 규칙으로 해석 (EXDATE 등이 있으면 원문 보존)
        if (lines.Count != 1 || !lines[0].StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) return custom;

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in lines[0][6..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq > 0) parts[kv[..eq]] = kv[(eq + 1)..];
        }
        parts.Remove("WKST");
        var rule = new RepeatRule();
        if (parts.Remove("INTERVAL", out var interval) && interval != "1") return custom;
        if (parts.Remove("COUNT", out var count))
        {
            if (!int.TryParse(count, out var c)) return custom;
            rule.Count = c;
        }
        if (parts.Remove("UNTIL", out var until))
        {
            if (ParseUntil(until) is not { } u) return custom;
            rule.Until = u;
        }
        parts.TryGetValue("BYDAY", out var byDay);
        parts.TryGetValue("BYMONTHDAY", out var byMonthDay);
        parts.TryGetValue("BYMONTH", out var byMonth);
        var freq = parts.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        int extra = parts.Count - 1; // FREQ 제외

        switch (freq)
        {
            case "DAILY" when extra == 0:
                rule.Kind = RepeatKind.Daily;
                break;
            case "WEEKLY" when extra == 0 || (extra == 1 && byDay == ByDay[(int)start.DayOfWeek]):
                rule.Kind = RepeatKind.Weekly;
                break;
            case "WEEKLY" when extra == 1 && byDay == Weekdays:
                rule.Kind = RepeatKind.Weekdays;
                break;
            case "MONTHLY" when extra == 0 || (extra == 1 && byMonthDay == start.Day.ToString(Inv)):
                rule.Kind = RepeatKind.Monthly;
                break;
            case "YEARLY" when parts.Keys.All(k => k.ToUpperInvariant() is "FREQ" or "BYMONTH" or "BYMONTHDAY") &&
                               (byMonth == null || byMonth == start.Month.ToString(Inv)) &&
                               (byMonthDay == null || byMonthDay == start.Day.ToString(Inv)):
                rule.Kind = RepeatKind.Yearly;
                break;
            default:
                return custom;
        }
        return rule;
    }

    public static List<string>? Build(RepeatRule r, DateTime start, bool allDay)
    {
        if (r.Kind == RepeatKind.Custom) return r.Raw;
        string? body = r.Kind switch
        {
            RepeatKind.Daily => "FREQ=DAILY",
            RepeatKind.Weekly => "FREQ=WEEKLY;BYDAY=" + ByDay[(int)start.DayOfWeek],
            RepeatKind.Monthly => "FREQ=MONTHLY;BYMONTHDAY=" + start.Day.ToString(Inv),
            RepeatKind.Yearly => "FREQ=YEARLY",
            RepeatKind.Weekdays => "FREQ=WEEKLY;BYDAY=" + Weekdays,
            _ => null,
        };
        if (body == null) return null;
        if (r.Count is > 0) body += ";COUNT=" + r.Count.Value.ToString(Inv);
        else if (r.Until is { } u)
        {
            // 시간 일정은 UNTIL 도 UTC 시각으로 써야 한다 (RFC 5545)
            body += allDay
                ? ";UNTIL=" + u.ToString("yyyyMMdd", Inv)
                : ";UNTIL=" + u.Date.AddDays(1).AddSeconds(-1).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", Inv);
        }
        return ["RRULE:" + body];
    }

    static DateTime? ParseUntil(string s)
    {
        if (DateTime.TryParseExact(s, "yyyyMMdd", Inv, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParseExact(s, "yyyyMMdd'T'HHmmss'Z'", Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
            return t.ToLocalTime().Date;
        if (DateTime.TryParseExact(s, "yyyyMMdd'T'HHmmss", Inv, DateTimeStyles.None, out var l)) return l.Date;
        return null;
    }

    public static string Label(RepeatKind kind, DateTime start) => kind switch
    {
        RepeatKind.None => "반복 안 함",
        RepeatKind.Daily => "매일",
        RepeatKind.Weekly => $"매주 {DayNames[(int)start.DayOfWeek]}요일",
        RepeatKind.Monthly => $"매월 {start.Day}일",
        RepeatKind.Yearly => $"매년 {start.Month}월 {start.Day}일",
        RepeatKind.Weekdays => "주중 매일 (월~금)",
        _ => "사용자 지정 반복",
    };

    public static string EndLabel(RepeatRule r) =>
        r.Count is > 0 ? $"{r.Count}회 반복"
        : r.Until is { } u ? $"{u:yyyy-MM-dd}까지"
        : "계속 반복";
}

public static class Reminders
{
    static readonly int[] TimedOptions = [0, 5, 10, 15, 30, 60, 120, 1440, 2880, 10080];
    /// <summary>종일 일정은 '그날 0시 기준 몇 분 전' 이라 Google 웹처럼 "N일 전 HH:mm" 형태로 고른다.</summary>
    static readonly int[] AllDayOptions = [0, 540, 900, 1980, 2340, 9540];

    public static int[] Options(bool allDay) => allDay ? AllDayOptions : TimedOptions;

    public static string Label(int minutes, bool allDay)
    {
        if (allDay)
        {
            int days = (minutes + 1439) / 1440;
            var at = TimeSpan.FromMinutes(days * 1440 - minutes);
            return (days == 0 ? "당일" : days == 7 ? "1주 전" : $"{days}일 전") + $" {at.Hours:00}:{at.Minutes:00}";
        }
        if (minutes == 0) return "정시";
        if (minutes % 10080 == 0) return $"{minutes / 10080}주 전";
        var parts = new List<string>();
        if (minutes / 1440 > 0) parts.Add($"{minutes / 1440}일");
        if (minutes % 1440 / 60 > 0) parts.Add($"{minutes % 1440 / 60}시간");
        if (minutes % 60 > 0) parts.Add($"{minutes % 60}분");
        return string.Join(" ", parts) + " 전";
    }
}
