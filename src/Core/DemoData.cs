namespace SMCalendar.Core;

/// <summary>--demo 로 실행하면 로그인 없이 샘플 일정으로 화면을 확인할 수 있다.</summary>
internal static class DemoData
{
    public static void Fill(EventStore store)
    {
        store.Persist = false;
        store.SetCalendars(
        [
            new CalendarInfo { Id = "me@example.com", Name = "내 캘린더", Color = "#4F8CFF", AccessRole = "owner", Primary = true },
            new CalendarInfo { Id = "work", Name = "업무", Color = "#33B679", AccessRole = "owner" },
            new CalendarInfo { Id = "ko.south_korea#holiday@group.v.calendar.google.com", Name = "대한민국의 휴일", Color = "#E67C73", AccessRole = "reader" },
        ]);
        var t = DateTime.Today;
        var m = new DateTime(t.Year, t.Month, 1);
        int n = 0;
        void Add(string cal, string title, DateTime start, DateTime end, bool allDay, string? color = null) =>
            store.Events.Add(new CalEvent
            {
                CalendarId = cal, Id = "demo" + n++, Title = title, Start = start, End = end, AllDay = allDay, ColorId = color,
            });

        Add("me@example.com", "치과 예약", t.AddHours(10), t.AddHours(11), false);
        Add("work", "주간 회의", t.AddHours(14), t.AddHours(15), false);
        Add("work", "프로젝트 마감", t.AddDays(3), t.AddDays(4), true, "11");
        Add("me@example.com", "운동", t.AddDays(1).AddHours(19), t.AddDays(1).AddHours(20), false);
        Add("me@example.com", "가족 여행", t.AddDays(8), t.AddDays(11), true, "5");
        Add("work", "1:1 미팅", t.AddDays(2).AddHours(11), t.AddDays(2).AddHours(11.5), false);
        Add("work", "디자인 리뷰", t.AddDays(2).AddHours(15), t.AddDays(2).AddHours(16), false);
        Add("me@example.com", "저녁 약속", t.AddDays(2).AddHours(19), t.AddDays(2).AddHours(21), false);
        Add("me@example.com", "도서관 반납", t.AddDays(2).AddHours(9), t.AddDays(2).AddHours(9.5), false);
        Add("ko.south_korea#holiday@group.v.calendar.google.com", "한글날", new DateTime(t.Year, 10, 9), new DateTime(t.Year, 10, 10), true);
        Add("me@example.com", "월급날", m.AddDays(24), m.AddDays(25), true, "2");
        Add("work", "분기 보고", m.AddDays(-3), m.AddDays(-2), true);
    }
}
