using System.Drawing.Drawing2D;
using SMCalendar.Core;

namespace SMCalendar.UI;

/// <summary>위젯 종류별 그리기.</summary>
internal sealed partial class CalendarWidget
{
    UiFonts Fonts
    {
        get
        {
            if (_fonts == null || _fonts.Scale != Scale || _fonts.FontScale != F)
            {
                _fonts?.Dispose();
                _fonts = new UiFonts(Scale, F);
            }
            return _fonts;
        }
    }

    void PaintWidget(Graphics g)
    {
        _hits.Clear();
        var th = _s.DarkTheme ? Theme.Dark : Theme.Light;
        var f = Fonts;
        // 배경이 거의 투명하면 글자에 그림자를 넣어서 배경화면 위에서도 읽히게
        bool shadow = _s.DarkTheme && _w.Opacity < 0.6;
        float S = Scale;

        // --- 배경 (알파 0 이면 클릭이 통과하므로 최소값 유지)
        int bgA = Math.Max(4, (int)Math.Round(Math.Clamp(_w.Opacity, 0, 1) * 255));
        using (var path = RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12 * S))
        {
            using (var b = new SolidBrush(Color.FromArgb(bgA, th.Bg))) g.FillPath(b, path);
            if (_w.Opacity >= 0.08)
                using (var p = new Pen(Color.FromArgb(Math.Min(40, bgA / 4), th.Border), 1)) g.DrawPath(p, path);
        }
        _hits.Add(new Hit(HitKind.Drag, new RectangleF(0, 0, Width, Height)));

        switch (_w.Kind)
        {
            case WidgetKind.MiniMonth: PaintMiniMonth(g, th, f, shadow); break;
            case WidgetKind.MiniAgenda: PaintMiniAgenda(g, th, f, shadow); break;
            case WidgetKind.MiniApp: PaintMiniApp(g, th, f, shadow); break;
            default: PaintMonth(g, th, f, shadow); break;
        }

        // 크기 조절 손잡이 (잠금이 아니고, 펼친 상태에서 마우스를 올렸을 때)
        if (!_w.Locked && !_w.Collapsed && (_inside || _w.Kind == WidgetKind.Month))
        {
            var grip = new RectangleF(Width - 20 * S, Height - 20 * S, 20 * S, 20 * S);
            using var gb = new SolidBrush(Theme.WithAlpha(th.Fg, _hover.Kind == HitKind.Grip ? 200 : 90));
            float d = 2.2f * S;
            for (int a = 0; a < 3; a++)
                for (int b = 0; b <= a; b++)
                    g.FillEllipse(gb, Width - (7 + b * 4.5f) * S - d / 2, Height - (7 + (a - b) * 4.5f) * S - d / 2, d, d);
            _hits.Add(new Hit(HitKind.Grip, grip));
        }
    }

    // ===================== 머리글 공통 =====================

    /// <summary>오른쪽 동작 버튼 (오른쪽부터): 끄기 · 메뉴 · 접기 · 항상 위 · 보기 · 추가. 끄기(✕)는 항상 맨 끝.</summary>
    static readonly Btn[] ActionOrder = [Btn.Hide, Btn.Menu, Btn.Collapse, Btn.Pin, Btn.View, Btn.Add];

    string ActionGlyph(Btn id) => id switch
    {
        Btn.Hide => "\uE711",
        Btn.Menu => "\uE712",
        Btn.Collapse => _w.Collapsed ? "\uE923" : "\uE921",
        Btn.Pin => "\uE718",
        Btn.View => "\uE790",
        _ => "\uE710",
    };

    /// <summary>
    /// 마우스를 올렸을 때만 보이는 동작 버튼. [leftLimit, right] 안에 들어가는 만큼만 (덜 중요한 것부터 생략).
    /// 항상 위가 켜져 있으면 핀은 늘 보인다.
    /// </summary>
    void PaintActionsFit(Graphics g, float leftLimit, float right, float y, float size, Theme th, UiFonts f, bool shadow)
    {
        float step = size + 2 * Scale;
        int fit = Math.Clamp((int)Math.Floor((right - leftLimit + 2 * Scale) / step), 0, ActionOrder.Length);
        for (int i = 0; i < fit; i++)
        {
            var id = ActionOrder[i];
            bool active = id == Btn.Pin && _w.AlwaysOnTop;
            if (_inside || active) Button(g, ref right, y, size, size, id, ActionGlyph(id), th, f, shadow, active: active);
            else right -= step;
        }
    }

    float Measure(Graphics g, string text, Font font) => g.MeasureString(text, font, PointF.Empty, Sf.LeftCenter).Width;

    /// <summary>
    /// 머리글: [‹][2026년 10월][›][오늘] ……… [+][보기][핀][접기][⋯][✕]
    /// 제목을 누르면 연·월 선택. 자리가 모자라면 제목을 줄이고, 그래도 모자라면 오른쪽 버튼을 덜 중요한 것부터 생략한다.
    /// </summary>
    void PaintHeader(Graphics g, Theme th, UiFonts f, bool shadow, float pad, float headerH, float bs,
        string full, string shortTitle, Font titleFont)
    {
        float S = Scale;
        float by = (headerH - bs) / 2 + 2 * S;
        float right = Width - pad + 4 * S;
        var todayFont = _w.Kind == WidgetKind.Month ? f.ButtonText : f.Small;
        float todayW = Measure(g, "오늘", todayFont) + 18 * S;
        float rest = (bs + 2 * S) * 2 + 12 * S + 6 * S + todayW + 8 * S;
        float minActions = 2 * (bs + 2 * S); // ✕ 와 ⋯ 자리는 확보

        string title = full;
        float titleW = Measure(g, full, titleFont);
        if (pad + titleW + rest + minActions > right)
        {
            title = shortTitle;
            titleW = Measure(g, title, titleFont);
        }

        float x = pad - 4 * S;
        ButtonAt(g, new RectangleF(x, by, bs, bs), Btn.Prev, "\uE76B", th, f, shadow);
        x += bs + 2 * S;
        x = TitleButton(g, title, titleFont, x, titleW, headerH, th, shadow);
        ButtonAt(g, new RectangleF(x + 2 * S, by, bs, bs), Btn.Next, "\uE76C", th, f, shadow);
        x += bs + 2 * S + 6 * S;
        x = TodayButton(g, x, by, todayW, bs, todayFont, th, shadow);

        PaintActionsFit(g, x + 8 * S, right, by, bs, th, f, shadow);
    }

    /// <summary>누를 수 있는 제목 (연·월 선택). 오른쪽 끝 x 를 돌려준다.</summary>
    float TitleButton(Graphics g, string title, Font font, float x, float textW, float headerH, Theme th, bool shadow)
    {
        float S = Scale;
        float h = font.GetHeight() + 8 * S;
        var r = new RectangleF(x, (headerH - h) / 2 + 2 * S, textW + 12 * S, h);
        if (_hover.Kind == HitKind.Title) FillRound(g, r, 6 * S, th.Hover);
        DrawText(g, title, font, th.Fg, RectangleF.Inflate(r, -6 * S, 0), Sf.Center, shadow);
        _hits.Add(new Hit(HitKind.Title, r));
        return r.Right;
    }

    /// <summary>테두리 있는 "오늘" 버튼. 오른쪽 끝 x 를 돌려준다.</summary>
    float TodayButton(Graphics g, float x, float by, float w, float bs, Font font, Theme th, bool shadow)
    {
        float h = Math.Max(bs - 6 * Scale, font.GetHeight() + 4 * Scale);
        var r = new RectangleF(x, by + (bs - h) / 2, w, h);
        bool hover = _hover.Kind == HitKind.Button && _hover.Button == Btn.Today;
        if (hover) FillRound(g, r, r.Height / 2, th.Hover);
        using (var path = RoundRect(r, r.Height / 2))
        using (var pen = new Pen(Theme.WithAlpha(th.Fg, hover ? 140 : 80), Math.Max(1, Scale)))
            g.DrawPath(pen, path);
        DrawText(g, "오늘", font, Theme.WithAlpha(th.Fg, hover ? 255 : 220), r, Sf.Center, shadow);
        _hits.Add(new Hit(HitKind.Button, r, Button: Btn.Today));
        return r.Right + 4 * Scale;
    }

    void ButtonAt(Graphics g, RectangleF r, Btn id, string glyph, Theme th, UiFonts f, bool shadow)
    {
        float right = r.Right;
        Button(g, ref right, r.Y, r.Width, r.Height, id, glyph, th, f, shadow);
    }

    string MonthTitle => _year == DateTime.Today.Year ? $"{_month}월" : $"{_year}년 {_month}월";

    static string DayTitle(DateTime d) => $"{d.Month}월 {d.Day}일 {DayNames[(int)d.DayOfWeek]}";

    // ===================== 월간 (큰 달력) =====================

    void PaintMonth(Graphics g, Theme th, UiFonts f, bool shadow)
    {
        float S = Scale;
        int W = Width, H = Height;
        float pad = 14 * S;
        float headerH = HeaderHeight(S);
        float weekH = 24 * S * (1 + (F - 1) * 0.6f);
        float footerH = 22 * U;

        PaintHeader(g, th, f, shadow, pad, headerH, 30 * S, $"{_year}년 {_month}월", $"{_month}월", f.Title);
        if (_w.Collapsed) return;

        // 요일
        float gridL = 8 * S, gridR = W - 8 * S;
        float cellW = (gridR - gridL) / 7f;
        int startDow = _s.WeekStartsMonday ? 1 : 0;
        for (int c = 0; c < 7; c++)
        {
            int dow = (startDow + c) % 7;
            var color = dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : Theme.WithAlpha(th.Fg, 170);
            DrawText(g, DayNames[dow], f.Weekday, color, new RectangleF(gridL + c * cellW, headerH, cellW, weekH), Sf.Center, shadow);
        }

        // 날짜 칸
        int weeks = VisibleWeeks;
        float gridT = headerH + weekH;
        float gridB = H - footerH;
        float cellH = (gridB - gridT) / weeks;
        var today = DateTime.Today;
        using (var gridPen = new Pen(th.Grid, Math.Max(1, S * 0.8f)))
            for (int r = 0; r < weeks; r++)
                g.DrawLine(gridPen, gridL + 4 * S, gridT + r * cellH, gridR - 4 * S, gridT + r * cellH);

        float lineH = 17 * U;
        for (int i = 0; i < weeks * 7; i++)
        {
            var day = _gridStart.AddDays(i);
            var cell = new RectangleF(gridL + (i % 7) * cellW, gridT + (i / 7) * cellH, cellW, cellH);
            _hits.Add(new Hit(HitKind.Cell, cell, i));
            bool inMonth = day.Month == _month;

            var inner = RectangleF.Inflate(cell, -2 * S, -2 * S);
            if (day == _selected && day != today)
                FillRound(g, inner, 6 * S, Theme.WithAlpha(th.Accent, 40));
            else if (_hover.Kind is HitKind.Cell or HitKind.DayNumber && _hover.Index == i)
                FillRound(g, inner, 6 * S, th.Hover);

            // 날짜 숫자
            int dow = (int)day.DayOfWeek;
            var numColor = _holiday[i] || dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : th.Fg;
            if (!inMonth) numColor = Theme.WithAlpha(numColor, th.DimAlpha);
            var numRect = new RectangleF(cell.X + 4 * S, cell.Y + 3 * S, 22 * U, 19 * U);
            if (day == today)
            {
                FillRound(g, numRect, numRect.Height / 2, th.Accent);
                DrawText(g, day.Day.ToString(), f.DayBold, Color.White, numRect, Sf.Center, false);
            }
            else
            {
                DrawText(g, day.Day.ToString(), day == _selected ? f.DayBold : f.Day, numColor, numRect, Sf.Center, shadow);
            }
            _hits.Add(new Hit(HitKind.DayNumber, numRect, i));

            // 일정
            var events = _days[i];
            if (events.Count == 0) continue;
            float y = cell.Y + 24 * U;
            int fit = Math.Max(0, (int)((cell.Bottom - y - 2 * S) / lineH));
            if (fit == 0)
            {
                // 칸이 너무 작으면 날짜 옆에 색 점으로만 표시 (클릭하면 하루 목록)
                float d = 5 * S;
                float dx = numRect.Right + 3 * S;
                for (int k = 0; k < Math.Min(3, events.Count) && dx + d < cell.Right - 2 * S; k++, dx += d + 3 * S)
                {
                    using var b = new SolidBrush(Theme.WithAlpha(EventColor(events[k]), inMonth ? 255 : 130));
                    g.FillEllipse(b, dx, numRect.Y + (numRect.Height - d) / 2, d, d);
                }
                _hits.Add(new Hit(HitKind.More, new RectangleF(numRect.Right, cell.Y, cell.Right - numRect.Right, numRect.Bottom - cell.Y + 4 * S), i));
                continue;
            }
            int show = events.Count > fit ? fit - 1 : events.Count;
            for (int k = 0; k < show; k++)
            {
                var ev = events[k];
                var er = new RectangleF(cell.X + 3 * S, y, cell.Width - 6 * S, lineH - 2 * S);
                DrawEventBar(g, ev, er, th, f, shadow, inMonth, _hover.Kind == HitKind.Event && _hover.Event == ev && _hover.Index == i);
                _hits.Add(new Hit(HitKind.Event, er, i, Event: ev, Date: day));
                y += lineH;
            }
            if (show < events.Count)
            {
                var mr = new RectangleF(cell.X + 3 * S, y, cell.Width - 6 * S, lineH - 2 * S);
                if (_hover.Kind == HitKind.More && _hover.Index == i) FillRound(g, mr, 4 * S, th.Hover);
                DrawText(g, $"+{events.Count - show}개", f.Small, Theme.WithAlpha(th.Fg, 170), RectangleF.Inflate(mr, -4 * S, 0), Sf.LeftCenter, shadow);
                _hits.Add(new Hit(HitKind.More, mr, i));
            }
        }

        // 바닥글: 상태 (클릭하면 동기화 / 로그인)
        PaintStatus(g, new RectangleF(pad, gridB, W * 0.6f, footerH - 2 * S), th, f, shadow, always: true);
    }

    /// <summary>상태 줄. 로그인 전이면 강조해서 표시하고, 로그인 후에는 클릭하면 바로 동기화.</summary>
    void PaintStatus(Graphics g, RectangleF r, Theme th, UiFonts f, bool shadow, bool always)
    {
        bool signedIn = _svc.IsSignedIn;
        if (!always && signedIn) return;
        bool busy = _svc.IsSyncing || _svc.IsSigningIn;
        string text = _svc.IsSyncing ? "동기화 중…" : _svc.Status;
        if (text.Length == 0) return;
        bool hover = _hover.Kind == HitKind.Status;
        var color = !signedIn ? Theme.WithAlpha(th.Accent, hover ? 200 : 255) : Theme.WithAlpha(th.Fg, hover ? 220 : 120);
        float x = r.X;
        if (signedIn)
        {
            var ir = new RectangleF(x - 2 * Scale, r.Y, 16 * U, r.Height);
            DrawText(g, "\uE72C", f.SmallIcon, color, ir, Sf.Center, shadow);
            x += 16 * U;
        }
        var font = signedIn ? f.Small : f.SmallBold;
        var tr = new RectangleF(x, r.Y, r.Right - x, r.Height);
        DrawText(g, text, font, color, tr, Sf.LeftCenter, shadow);
        if (!busy)
        {
            float w = g.MeasureString(text, font, PointF.Empty, Sf.LeftCenter).Width + (x - r.X) + 4 * Scale;
            _hits.Add(new Hit(HitKind.Status, new RectangleF(r.X, r.Y, Math.Min(w, r.Width), r.Height)));
        }
    }

    void DrawEventBar(Graphics g, CalEvent ev, RectangleF r, Theme th, UiFonts f, bool shadow, bool inMonth, bool hover)
    {
        var color = EventColor(ev);
        float S = Scale;
        int dimMul = inMonth ? 255 : 150;

        if (ev.AllDay || ev.IsMultiDay)
        {
            FillRound(g, r, 4 * S, Color.FromArgb((hover ? 255 : 225) * dimMul / 255, color));
            var tr = RectangleF.Inflate(r, -4 * S, 0);
            DrawText(g, ev.Title, f.Event, Theme.WithAlpha(Theme.TextOn(color), dimMul), tr, Sf.LeftCenter, false);
        }
        else
        {
            if (hover) FillRound(g, r, 4 * S, th.Hover);
            var bar = new RectangleF(r.X + 1 * S, r.Y + 2 * S, 3 * S, r.Height - 4 * S);
            FillRound(g, bar, 1.5f * S, Theme.WithAlpha(color, dimMul));
            var tr = new RectangleF(r.X + 7 * S, r.Y, r.Width - 8 * S, r.Height);
            DrawText(g, $"{ev.Start:HH:mm} {ev.Title}", f.Event, Theme.WithAlpha(th.Fg, inMonth ? 235 : 130), tr, Sf.LeftCenter, shadow);
        }
    }

    Color EventColor(CalEvent ev) =>
        Theme.EventColor(ev.ColorId, _svc.Store.FindCalendar(ev.CalendarId)?.Color ?? "#4F8CFF");

    // ===================== 미니 월간 =====================

    void PaintMiniMonth(Graphics g, Theme th, UiFonts f, bool shadow)
    {
        float S = Scale, pad = 12 * S;
        float headerH = HeaderHeight(S);
        PaintHeader(g, th, f, shadow, pad, headerH, 26 * S, MonthTitle, $"{_month}월", f.TitleMini);
        if (_w.Collapsed) return;

        var area = new RectangleF(pad, headerH, Width - pad * 2, Height - headerH - pad);
        PaintMiniGrid(g, area, th, f.MiniDay, f.MiniDayBold, f.Weekday, shadow, showSelection: false);
        PaintStatus(g, new RectangleF(pad, Height - 22 * U, Width - pad * 2, 20 * U), th, f, shadow, always: false);
    }

    /// <summary>작은 달력: 날짜 숫자 + 일정 있는 날 아래 점.</summary>
    void PaintMiniGrid(Graphics g, RectangleF area, Theme th, Font dayFont, Font dayBold, Font weekFont, bool shadow, bool showSelection)
    {
        float S = Scale;
        int weeks = VisibleWeeks;
        float weekH = weekFont.GetHeight() + 8 * S;
        float cellW = area.Width / 7f;
        float cellH = (area.Height - weekH) / weeks;
        int startDow = _s.WeekStartsMonday ? 1 : 0;
        for (int c = 0; c < 7; c++)
        {
            int dow = (startDow + c) % 7;
            var color = dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : Theme.WithAlpha(th.Fg, 200);
            DrawText(g, DayNames[dow], weekFont, color, new RectangleF(area.X + c * cellW, area.Y, cellW, weekH), Sf.Center, shadow);
        }

        var today = DateTime.Today;
        float diameter = Math.Min(cellW, cellH) * 0.74f;
        for (int i = 0; i < weeks * 7; i++)
        {
            var day = _gridStart.AddDays(i);
            var cell = new RectangleF(area.X + (i % 7) * cellW, area.Y + weekH + (i / 7) * cellH, cellW, cellH);
            var circle = new RectangleF(cell.X + (cellW - diameter) / 2, cell.Y + (cellH - diameter) / 2 - 2 * S, diameter, diameter);
            bool inMonth = day.Month == _month;
            bool isToday = day == today;
            bool selected = showSelection && day == _selected;
            bool hover = _hover.Kind == HitKind.MiniDay && _hover.Index == i;

            if (isToday)
            {
                using var b = new SolidBrush(th.Accent);
                g.FillEllipse(b, circle);
            }
            else if (hover)
            {
                using var b = new SolidBrush(th.Hover);
                g.FillEllipse(b, circle);
            }
            if (selected)
            {
                using var p = new Pen(isToday ? Color.White : th.Accent, 1.5f * S);
                g.DrawEllipse(p, RectangleF.Inflate(circle, isToday ? -2 * S : 0, isToday ? -2 * S : 0));
            }

            int dow = (int)day.DayOfWeek;
            var color = isToday ? Color.White : _holiday[i] || dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : th.Fg;
            if (!inMonth && !isToday) color = Theme.WithAlpha(color, th.DimAlpha);
            DrawText(g, day.Day.ToString(), isToday || selected ? dayBold : dayFont, color, circle, Sf.Center, shadow && !isToday);

            // 일정 표시 점 (첫 일정의 색)
            if (_days[i].Count > 0)
            {
                float d = Math.Max(3, 3.6f * S * F);
                var dotColor = EventColor(_days[i][0]);
                if (!inMonth) dotColor = Theme.WithAlpha(dotColor, 120);
                using var b = new SolidBrush(dotColor);
                g.FillEllipse(b, cell.X + (cellW - d) / 2, circle.Bottom + 1.5f * S, d, d);
            }
            _hits.Add(new Hit(HitKind.MiniDay, cell, i));
        }
    }

    // ===================== 미니 어젠다 (왼쪽 일정 목록 + 오른쪽 작은 달력) =====================

    void PaintMiniAgenda(Graphics g, Theme th, UiFonts f, bool shadow)
    {
        float S = Scale, pad = 12 * S;
        float headerH = HeaderHeight(S);
        float split = Width * 0.52f;

        // 왼쪽: [선택한 날짜][오늘]   오른쪽: [달][︿][﹀] …… [동작][✕]
        float bs = 24 * S, by = (headerH - bs) / 2 + 2 * S;
        string title = DayTitle(_selected);
        float titleW = Measure(g, title, f.TitleMini);
        float todayW = Measure(g, "오늘", f.Small) + 18 * S;
        if (pad + titleW + 10 * S + todayW > split - 4 * S)
        {
            title = $"{_selected.Month}/{_selected.Day}";
            titleW = Measure(g, title, f.TitleMini);
        }
        DrawText(g, title, f.TitleMini, th.Fg, new RectangleF(pad + 2 * S, 2 * S, titleW + 4 * S, headerH), Sf.LeftCenter, shadow);
        TodayButton(g, pad + 2 * S + titleW + 10 * S, by, todayW, bs, f.Small, th, shadow);

        // 오른쪽 달력 머리글: [‹][10월][›]
        float mx = split - 2 * S;
        ButtonAt(g, new RectangleF(mx, by, bs, bs), Btn.Prev, "\uE76B", th, f, shadow);
        mx += bs + 2 * S;
        mx = TitleButton(g, MonthTitle, f.SmallBold, mx, Measure(g, MonthTitle, f.SmallBold), headerH, th, shadow);
        ButtonAt(g, new RectangleF(mx + 2 * S, by, bs, bs), Btn.Next, "\uE76C", th, f, shadow);
        mx += 2 * S + bs;
        PaintActionsFit(g, mx + 8 * S, Width - pad + 4 * S, by, bs, th, f, shadow);
        if (_w.Collapsed) return;

        var agendaArea = new RectangleF(pad, headerH + 2 * S, split - pad - 8 * S, Height - headerH - pad - 2 * S);
        PaintAgenda(g, agendaArea, th, f, shadow, addButtons: false, skipFirstHeader: true);
        var gridArea = new RectangleF(split + 2 * S, headerH, Width - split - pad - 2 * S, Height - headerH - pad + 4 * S);
        PaintMiniGrid(g, gridArea, th, f.MicroDay, f.MicroDayBold, f.Micro, shadow, showSelection: true);
    }

    // ===================== 미니 앱 (위 작은 달력 + 아래 일정 목록) =====================

    void PaintMiniApp(Graphics g, Theme th, UiFonts f, bool shadow)
    {
        float S = Scale, pad = 12 * S;
        float headerH = HeaderHeight(S);
        PaintHeader(g, th, f, shadow, pad, headerH, 26 * S, MonthTitle, $"{_month}월", f.TitleMini);
        if (_w.Collapsed) return;

        // 달력 높이: 너비에 맞춘 정사각형 칸 (최대 높이의 절반)
        float gridW = Width - pad * 2;
        float weekH = f.Micro.GetHeight() + 8 * S;
        float cell = Math.Min(gridW / 7f * 0.82f, 34 * U);
        float gridH = Math.Min(weekH + VisibleWeeks * cell, (Height - headerH) * 0.55f);
        var gridArea = new RectangleF(pad, headerH, gridW, gridH);
        PaintMiniGrid(g, gridArea, th, f.MiniDaySmall, f.MiniDaySmallBold, f.Micro, shadow, showSelection: true);

        float top = gridArea.Bottom + 6 * S;
        using (var pen = new Pen(th.Grid, Math.Max(1, 0.8f * S))) g.DrawLine(pen, pad, top, Width - pad, top);
        var agendaArea = new RectangleF(pad, top + 4 * S, gridW, Height - top - 4 * S - pad);
        PaintAgenda(g, agendaArea, th, f, shadow, addButtons: true, skipFirstHeader: false);
    }

    // ===================== 일정 목록 =====================

    void PaintAgenda(Graphics g, RectangleF area, Theme th, UiFonts f, bool shadow, bool addButtons, bool skipFirstHeader)
    {
        float S = Scale;
        _hits.Add(new Hit(HitKind.AgendaArea, area));
        if (!_svc.IsSignedIn)
        {
            PaintStatus(g, new RectangleF(area.X, area.Y + 4 * S, area.Width, 22 * U), th, f, shadow, always: false);
            return;
        }

        float headH = 26 * U, evH = 40 * U, emptyH = 24 * U;
        // 전체 높이 계산 → 스크롤 범위
        float content = 0;
        for (int k = 0; k < _agenda.Count; k++)
        {
            if (!(skipFirstHeader && k == 0)) content += headH;
            content += _agenda[k].Events.Count == 0 ? emptyH : _agenda[k].Events.Count * evH;
            content += 4 * S;
        }
        _agendaMaxScroll = Math.Max(0, content - area.Height);
        _agendaScroll = Math.Clamp(_agendaScroll, 0, _agendaMaxScroll);

        var state = g.Save();
        g.SetClip(area);
        float y = area.Y - _agendaScroll;
        var today = DateTime.Today;
        for (int k = 0; k < _agenda.Count; k++)
        {
            var (day, events) = _agenda[k];
            if (!(skipFirstHeader && k == 0))
            {
                var hr = new RectangleF(area.X, y, area.Width, headH);
                string label = DayTitle(day) + (day == today ? " · 오늘" : day == today.AddDays(1) ? " · 내일" : "");
                int dow = (int)day.DayOfWeek;
                var hc = day == today ? th.Accent : dow == 0 ? th.Sunday : dow == 6 ? th.Saturday : th.Fg;
                DrawText(g, label, f.SmallBold, hc, new RectangleF(hr.X + 2 * S, hr.Y, hr.Width - 30 * S, hr.Height), Sf.LeftCenter, shadow);
                if (addButtons && hr.Bottom > area.Y && hr.Y < area.Bottom)
                {
                    var ar = new RectangleF(hr.Right - 26 * S, hr.Y + (headH - 24 * S) / 2, 24 * S, 24 * S);
                    bool hov = _hover.Kind == HitKind.AddDay && _hover.Date == day;
                    if (hov) FillRound(g, ar, 6 * S, th.Hover);
                    DrawText(g, "\uE710", f.SmallIcon, Theme.WithAlpha(th.Fg, hov ? 255 : 190), ar, Sf.Center, shadow);
                    _hits.Add(new Hit(HitKind.AddDay, RectangleF.Intersect(ar, area), Date: day));
                }
                y += headH;
            }
            if (events.Count == 0)
            {
                DrawText(g, "일정 없음", f.Small, Theme.WithAlpha(th.Fg, 120), new RectangleF(area.X + 10 * S, y, area.Width - 10 * S, emptyH), Sf.LeftCenter, shadow);
                y += emptyH;
            }
            foreach (var ev in events)
            {
                var r = new RectangleF(area.X, y, area.Width, evH - 4 * S);
                if (r.Bottom > area.Y && r.Y < area.Bottom)
                {
                    bool hov = _hover.Kind == HitKind.Event && _hover.Event == ev && _hover.Date == day;
                    if (hov) FillRound(g, r, 6 * S, th.Hover);
                    var color = EventColor(ev);
                    FillRound(g, new RectangleF(r.X + 2 * S, r.Y + 4 * S, 3.5f * S, r.Height - 8 * S), 1.75f * S, color);
                    var tx = r.X + 12 * S;
                    DrawText(g, ev.Title, f.EventBold, th.Fg, new RectangleF(tx, r.Y + 2 * S, r.Right - tx - 4 * S, r.Height / 2), Sf.LeftCenter, shadow);
                    DrawText(g, TimeLabel(ev, day), f.Tiny, Theme.WithAlpha(th.Fg, 165), new RectangleF(tx, r.Y + r.Height / 2, r.Right - tx - 4 * S, r.Height / 2 - 2 * S), Sf.LeftCenter, shadow);
                    _hits.Add(new Hit(HitKind.Event, RectangleF.Intersect(r, area), Event: ev, Date: day));
                }
                y += evH;
            }
            y += 4 * S;
        }
        g.Restore(state);

        // 스크롤 위치 표시
        if (_agendaMaxScroll > 0 && _inside)
        {
            float trackH = area.Height;
            float barH = Math.Max(18 * S, trackH * area.Height / (area.Height + _agendaMaxScroll));
            float by = area.Y + (trackH - barH) * (_agendaScroll / _agendaMaxScroll);
            FillRound(g, new RectangleF(area.Right - 3 * S, by, 3 * S, barH), 1.5f * S, Theme.WithAlpha(th.Fg, 70));
        }
    }

    /// <summary>"오전 8:00 – 오전 10:00" / "종일".</summary>
    static string TimeLabel(CalEvent e, DateTime day)
    {
        if (e.AllDay)
        {
            if (!e.IsMultiDay) return "종일";
            var last = e.End.AddDays(-1);
            return $"종일 · {e.Start.Month}/{e.Start.Day} ~ {last.Month}/{last.Day}";
        }
        string start = e.Start.Date == day ? Ampm(e.Start) : $"{e.Start.Month}/{e.Start.Day} {Ampm(e.Start)}";
        string end = e.End.Date == day || (e.End.Date == day.AddDays(1) && e.End.TimeOfDay == TimeSpan.Zero && e.Start.Date == day)
            ? Ampm(e.End) : $"{e.End.Month}/{e.End.Day} {Ampm(e.End)}";
        return $"{start} – {end}";
    }

    static string Ampm(DateTime t)
    {
        int h = t.Hour % 12;
        return $"{(t.Hour < 12 ? "오전" : "오후")} {(h == 0 ? 12 : h)}:{t.Minute:00}";
    }

    // ===================== 그리기 도구 =====================

    void Button(Graphics g, ref float right, float y, float w, float h, Btn id, string glyph, Theme th, UiFonts f,
        bool shadow, bool text = false, bool dim = false, bool active = false)
    {
        var r = new RectangleF(right - w, y, w, h);
        right -= w + 2 * Scale;
        bool hover = _hover.Kind == HitKind.Button && _hover.Button == id;
        if (hover) FillRound(g, r, 6 * Scale, th.Hover);
        var color = active ? th.Accent : Theme.WithAlpha(th.Fg, dim ? 90 : hover ? 255 : 210);
        DrawText(g, glyph, text ? f.ButtonText : f.Icon, color, r, Sf.Center, shadow && !active);
        _hits.Add(new Hit(HitKind.Button, r, Button: id));
    }

    void DrawText(Graphics g, string s, Font font, Color color, RectangleF r, StringFormat sf, bool shadow)
    {
        if (shadow)
        {
            var sr = r;
            sr.Offset(0, Math.Max(1, Scale));
            using var sb = new SolidBrush(Color.FromArgb(color.A * 140 / 255, 0, 0, 0));
            g.DrawString(s, font, sb, sr, sf);
        }
        using var b = new SolidBrush(color);
        g.DrawString(s, font, b, r, sf);
    }

    static void FillRound(Graphics g, RectangleF r, float radius, Color c)
    {
        using var path = RoundRect(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ===================== 글꼴 / 정렬 =====================

    sealed class UiFonts : IDisposable
    {
        public readonly float Scale, FontScale;
        public readonly Font Title, TitleMini, Weekday, Day, DayBold, Event, EventBold, Small, SmallBold, Tiny,
            Micro, MicroDay, MicroDayBold, MiniDay, MiniDayBold, MiniDaySmall, MiniDaySmallBold, Icon, SmallIcon, ButtonText;

        public UiFonts(float s, float fs)
        {
            Scale = s;
            FontScale = fs;
            float t = s * fs;
            Font N(float px, bool bold = false) => Ui.Font(px * t, bold ? FontStyle.Bold : FontStyle.Regular);
            Title = N(19, true);
            TitleMini = N(15, true);
            Weekday = N(11.5f);
            Day = N(12.5f);
            DayBold = N(12.5f, true);
            Event = N(11);
            EventBold = N(12, true);
            Small = N(11);
            SmallBold = N(11.5f, true);
            Tiny = N(10.5f);
            Micro = N(10);
            MicroDay = N(10.5f);
            MicroDayBold = N(10.5f, true);
            MiniDay = N(13.5f);
            MiniDayBold = N(13.5f, true);
            MiniDaySmall = N(12);
            MiniDaySmallBold = N(12, true);
            ButtonText = N(12);
            Icon = new Font(IconFamily, 13 * s * MathF.Sqrt(fs), GraphicsUnit.Pixel);
            SmallIcon = new Font(IconFamily, 11 * t, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            foreach (var f in new[] { Title, TitleMini, Weekday, Day, DayBold, Event, EventBold, Small, SmallBold, Tiny, Micro,
                         MicroDay, MicroDayBold, MiniDay, MiniDayBold, MiniDaySmall, MiniDaySmallBold, Icon, SmallIcon, ButtonText })
                f.Dispose();
        }
    }

    static class Sf
    {
        // GenericTypographic 기반: GDI+ 기본 서식은 한글 자간이 넓게 벌어진다
        public static readonly StringFormat Center = Make(StringAlignment.Center, StringTrimming.None);
        public static readonly StringFormat LeftCenter = Make(StringAlignment.Near, StringTrimming.EllipsisCharacter);

        static StringFormat Make(StringAlignment align, StringTrimming trimming)
        {
            var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            // LineLimit 이 있으면 칸보다 글자가 조금만 커도 통째로 안 그려진다 (큰 글자 크기에서 문제)
            sf.FormatFlags &= ~StringFormatFlags.LineLimit;
            sf.Alignment = align;
            sf.LineAlignment = StringAlignment.Center;
            sf.Trimming = trimming;
            return sf;
        }
    }
}
