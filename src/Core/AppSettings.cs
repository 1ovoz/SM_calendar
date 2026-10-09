using System.Text.Json;

namespace SMCalendar.Core;

public enum WidgetKind { Month, MiniMonth, MiniAgenda, MiniApp }

/// <summary>위젯 하나의 설정 (위치, 크기, 모양, 고정 방식).</summary>
public sealed class WidgetSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WidgetKind Kind { get; set; }
    /// <summary>화면 좌표(물리 픽셀). int.MinValue 면 기본 위치.</summary>
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    /// <summary>96 DPI 기준 논리 크기 (펼친 상태).</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>배경 불투명도 0.0 ~ 1.0 (글자는 항상 선명하게 표시).</summary>
    public double Opacity { get; set; } = 0.45;
    /// <summary>글자 크기 배율 (0.85 ~ 1.55).</summary>
    public double FontScale { get; set; } = 1.0;
    public bool Locked { get; set; }
    public bool PinToDesktop { get; set; } = true;
    /// <summary>항상 다른 창 위에 표시 (켜면 바탕화면 고정보다 우선).</summary>
    public bool AlwaysOnTop { get; set; }
    /// <summary>접힌 상태: 머리글만 표시.</summary>
    public bool Collapsed { get; set; }
    /// <summary>꺼진 위젯 (트레이 아이콘으로 다시 켤 수 있음). 꺼진 동안 창을 만들지 않는다.</summary>
    public bool Hidden { get; set; }
    /// <summary>위젯 종류별로 마지막에 쓰던 크기 (종류를 바꿨다 돌아오면 복원).</summary>
    public Dictionary<string, int[]> SizeByKind { get; set; } = new();

    /// <summary>위젯 종류 변경: 지금 크기를 기억하고, 새 종류의 크기를 복원한다.</summary>
    public void ChangeKind(WidgetKind kind)
    {
        if (kind == Kind) return;
        SizeByKind[Kind.ToString()] = [Width, Height];
        Kind = kind;
        var def = DefaultSize(kind);
        var size = SizeByKind.TryGetValue(kind.ToString(), out var v) && v.Length == 2 ? v : [def.Width, def.Height];
        Width = size[0];
        Height = size[1];
    }

    public static Size DefaultSize(WidgetKind kind) => kind switch
    {
        WidgetKind.MiniMonth => new Size(290, 300),
        WidgetKind.MiniAgenda => new Size(450, 190),
        WidgetKind.MiniApp => new Size(310, 560),
        _ => new Size(560, 480),
    };

    public static Size MinimumSize(WidgetKind kind) => kind switch
    {
        WidgetKind.MiniMonth => new Size(220, 220),
        WidgetKind.MiniAgenda => new Size(360, 150),
        WidgetKind.MiniApp => new Size(250, 380),
        _ => new Size(380, 280),
    };

    public static string KindName(WidgetKind kind) => kind switch
    {
        WidgetKind.MiniMonth => "미니 월간 캘린더",
        WidgetKind.MiniAgenda => "미니 어젠다",
        WidgetKind.MiniApp => "미니 앱",
        _ => "월간 캘린더",
    };

    public static WidgetSettings Create(WidgetKind kind)
    {
        var size = DefaultSize(kind);
        return new WidgetSettings { Kind = kind, Width = size.Width, Height = size.Height };
    }
}

public sealed class AppSettings
{
    /// <summary>위젯 설정 (항상 하나). 비어 있고 WidgetsInitialized 가 false 면 이전 설정에서 만든다.</summary>
    public List<WidgetSettings> Widgets { get; set; } = new();
    public bool WidgetsInitialized { get; set; }

    // ---- 아래 X ~ PinToDesktop 은 위젯이 하나뿐이던 이전 버전 설정 (첫 위젯으로 옮겨진다)
    /// <summary>화면 좌표(물리 픽셀). int.MinValue 면 기본 위치.</summary>
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    /// <summary>96 DPI 기준 논리 크기.</summary>
    public int Width { get; set; } = 560;
    public int Height { get; set; } = 480;

    /// <summary>배경 불투명도 0.0 ~ 1.0 (글자는 항상 선명하게 표시).</summary>
    public double Opacity { get; set; } = 0.45;
    public bool DarkTheme { get; set; } = true;
    /// <summary>글꼴: "Pretendard"(기본, 앱에 포함) 또는 "Malgun Gothic".</summary>
    public string FontName { get; set; } = "Pretendard";
    public bool Locked { get; set; }
    public bool PinToDesktop { get; set; } = true;
    public bool WeekStartsMonday { get; set; }
    public int SyncMinutes { get; set; } = 5;
    public bool FirstRunDone { get; set; }
    public string? DefaultCalendarId { get; set; }

    /// <summary>사용자가 직접 숨긴/표시한 캘린더. 둘 다 없으면 Google 의 표시 설정을 따른다.</summary>
    public List<string> HiddenCalendars { get; set; } = new();
    public List<string> ShownCalendars { get; set; } = new();

    public bool IsCalendarVisible(CalendarInfo c) =>
        ShownCalendars.Contains(c.Id) || (!HiddenCalendars.Contains(c.Id) && c.SelectedInGoogle);

    public void ToggleCalendar(CalendarInfo c)
    {
        bool visible = IsCalendarVisible(c);
        HiddenCalendars.Remove(c.Id);
        ShownCalendars.Remove(c.Id);
        (visible ? HiddenCalendars : ShownCalendars).Add(c.Id);
    }

    static string FilePath => AppPaths.File("settings.json");

    public static AppSettings Load()
    {
        AppSettings s = new();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), JsonCtx.Default.AppSettings) ?? new();
        }
        catch { /* 손상된 설정은 무시하고 기본값 */ }

        // 위젯은 하나만 쓴다 (종류는 '위젯 변경'으로 바꿈). 이전에 여러 개였다면 켜져 있던 첫 위젯만 남긴다.
        if (s.Widgets.Count > 1)
            s.Widgets = [s.Widgets.FirstOrDefault(w => !w.Hidden) ?? s.Widgets[0]];
        if (s.WidgetsInitialized && s.Widgets.Count == 0)
            s.Widgets.Add(WidgetSettings.Create(WidgetKind.Month));

        if (!s.WidgetsInitialized)
        {
            // 처음 실행 또는 이전 버전: 기존 위치/모양으로 월간 캘린더 위젯 하나를 만든다
            s.Widgets.Add(new WidgetSettings
            {
                Kind = WidgetKind.Month, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height,
                Opacity = s.Opacity, Locked = s.Locked, PinToDesktop = s.PinToDesktop,
            });
            s.WidgetsInitialized = true;
        }
        foreach (var w in s.Widgets)
        {
            var min = WidgetSettings.MinimumSize(w.Kind);
            if (w.Width < min.Width || w.Height < min.Height)
            {
                var def = WidgetSettings.DefaultSize(w.Kind);
                w.Width = Math.Max(w.Width, def.Width);
                w.Height = Math.Max(w.Height, def.Height);
            }
            w.FontScale = Math.Clamp(w.FontScale, 0.8, 1.6);
        }
        return s;
    }

    public void Save()
    {
        try { AppPaths.WriteAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(this, JsonCtx.Default.AppSettings)); }
        catch { }
    }
}
