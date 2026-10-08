using System.Text.Json;

namespace SMCalendar.Core;

/// <summary>메모리 + 디스크 캐시. 부팅 직후 네트워크 없이도 바로 그릴 수 있게 한다.</summary>
internal sealed class EventStore
{
    public List<CalendarInfo> Calendars { get; private set; } = new();
    public List<CalEvent> Events { get; } = new();
    public DateTime? LastSync { get; set; }
    public bool Persist { get; set; } = true;

    static string FilePath => AppPaths.File("cache.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var cache = JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), JsonCtx.Default.EventCache);
            if (cache == null) return;
            Calendars = cache.Calendars;
            Events.AddRange(cache.Events);
            LastSync = cache.LastSync;
        }
        catch { }
    }

    public void Save()
    {
        if (!Persist) return;
        // 오래된 일정은 버려서 캐시가 무한히 커지지 않게
        var today = DateTime.Today;
        Events.RemoveAll(e => e.End < today.AddDays(-120) || e.Start > today.AddDays(400));
        var cache = new EventCache { Calendars = Calendars, Events = Events, LastSync = LastSync };
        try { AppPaths.WriteAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(cache, JsonCtx.Default.EventCache)); }
        catch { }
    }

    public void Clear()
    {
        Calendars = new();
        Events.Clear();
        LastSync = null;
        try { File.Delete(FilePath); } catch { }
    }

    public CalendarInfo? FindCalendar(string id) => Calendars.FirstOrDefault(c => c.Id == id);

    public void SetCalendars(List<CalendarInfo> calendars)
    {
        Calendars = calendars;
        var ids = calendars.Select(c => c.Id).ToHashSet();
        Events.RemoveAll(e => !ids.Contains(e.CalendarId));
    }

    /// <summary>[min, max) 와 겹치는 해당 캘린더 일정을 새로 받은 목록으로 교체.</summary>
    public void ReplaceRange(string calendarId, DateTime min, DateTime max, List<CalEvent> fresh)
    {
        Events.RemoveAll(e => e.CalendarId == calendarId && e.Start < max && e.End > min);
        // 범위 경계에 걸친 일정이 중복되지 않도록
        var ids = fresh.Select(e => e.Id).ToHashSet();
        Events.RemoveAll(e => e.CalendarId == calendarId && ids.Contains(e.Id));
        Events.AddRange(fresh);
    }

    public void Upsert(CalEvent e)
    {
        Remove(e.CalendarId, e.Id);
        Events.Add(e);
    }

    public void Remove(string calendarId, string id) =>
        Events.RemoveAll(e => e.CalendarId == calendarId && e.Id == id);
}
