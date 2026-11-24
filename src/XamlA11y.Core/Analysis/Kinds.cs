using System.Text.Json;

namespace XamlA11y.Core.Analysis;

public enum Severity { Off, Info, Warn, Fail }

public sealed record KindText(string Title, string Why, string How)
{
    /// <summary>為什麼＋怎麼修。</summary>
    public string Help { get; init; } = $"{Why} {How}";
}

/// <summary>一種問題（data/findings.json 的一列）。</summary>
/// <param name="Check">算在哪一項檢查的統計裡。</param>
/// <param name="Group">給人看的大類：念不出來、鍵盤到不了、看不到焦點、太小。</param>
/// <param name="Criteria">WCAG 2.2 的成功準則（對應理由在 docs/standards.md）。</param>
/// <param name="NameIsEmpty">名稱是空的：EN 301 549 11.5.2.5、Section 508 502.3.1。亂碼名稱不是空的。</param>
public sealed record Kind(string Slug, string Check, string Group, Severity Default, IReadOnlyList<string> Criteria, bool NameIsEmpty, KindText En, KindText Zh)
{
    public string Title => En.Title;
    public string Help => En.Help;

    /// <summary>「4.1.2 (A), 2.5.3 (A)」。</summary>
    public string WcagWithLevels => string.Join(", ", Criteria.Select(c => $"{c} ({Wcag.Level(c)})"));

    /// <summary>怎麼修（GitHub 上的一節）：SARIF 的 helpUri、HTML 的連結。</summary>
    public string Uri(bool zh = false) => $"https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/{(zh ? "%E4%BF%AE%E6%B3%95" : "fixes")}.md#{Slug}";
}

/// <param name="Gate">預設會擋建置的：可解析率只看這幾項。</param>
public sealed record CheckInfo(string Id, string En, string Zh, bool Gate);

public sealed record GroupInfo(string Id, string En, string Zh);

/// <summary>檢查的代號：統計、可解析率、判定都用它分。</summary>
public static class Checks
{
    public const string ControlNames = "control-names";
    public const string InputNames = "input-names";
    public const string ListNames = "list-names";
    public const string ItemNames = "item-names";
    public const string SilentStops = "silent-stops";
    public const string SameNames = "same-names";
    public const string VisibleText = "visible-text";
    public const string Mouse = "mouse";
    public const string TabOrder = "tab-order";
    public const string Focus = "focus";
    public const string TargetSize = "target-size";
}

/// <summary>所有的問題、檢查、分組：讀 data/findings.json（改字只改那裡）。</summary>
public static class Kinds
{
    private static readonly (IReadOnlyList<Kind> Kinds, IReadOnlyList<CheckInfo> Checks, IReadOnlyList<GroupInfo> Groups) Data = Load();

    /// <summary>照表的順序：先分組，組裡面先嚴重的。</summary>
    public static IReadOnlyList<Kind> All => Data.Kinds;
    public static IReadOnlyList<CheckInfo> Checks => Data.Checks;
    public static IReadOnlyList<GroupInfo> Groups => Data.Groups;

    public static readonly Kind MissingName = Get("missing-name");
    public static readonly Kind TextNotName = Get("text-not-in-name");
    public static readonly Kind MeaninglessName = Get("meaningless-name");
    public static readonly Kind SymbolName = Get("symbol-name");
    public static readonly Kind InputNoName = Get("input-no-name");
    public static readonly Kind FocusNotVisible = Get("focus-not-visible");
    public static readonly Kind MouseOnly = Get("mouse-only");
    public static readonly Kind NotInTabOrder = Get("not-in-tab-order");
    public static readonly Kind SmallTarget = Get("small-target");
    public static readonly Kind ListNoName = Get("list-no-name");
    public static readonly Kind WidgetNoName = Get("widget-no-name");
    public static readonly Kind ItemTypeName = Get("item-type-name");
    public static readonly Kind SilentTabStop = Get("silent-tab-stop");
    public static readonly Kind SameName = Get("same-name");
    public static readonly Kind NameDiffers = Get("name-differs");

    public static Kind? BySlug(string slug) => All.FirstOrDefault(k => k.Slug == slug);

    public static bool IsGate(string check) => Check(check).Gate;

    public static CheckInfo Check(string id) => Data.Checks.First(c => c.Id == id);

    /// <summary>會算在這項檢查裡的種類：輸入框的亂碼名稱、只有符號的名稱算輸入框的。</summary>
    public static IEnumerable<Kind> CountedIn(string check) => All.Where(k => k.Check == check || check == Analysis.Checks.InputNames && (k == MeaninglessName || k == SymbolName));

    private static Kind Get(string slug) => BySlug(slug) ?? throw new InvalidOperationException($"findings.json has no '{slug}'");

    private static (IReadOnlyList<Kind>, IReadOnlyList<CheckInfo>, IReadOnlyList<GroupInfo>) Load()
    {
        using var s = typeof(Kinds).Assembly.GetManifestResourceStream("XamlA11y.Core.findings.json")
                      ?? throw new InvalidOperationException("embedded findings.json is missing");
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;
        var kinds = root.GetProperty("findings").EnumerateArray().Select(f =>
        {
            string Str(string n) => f.GetProperty(n).GetString()!;
            KindText Words(string lang)
            {
                var t = f.GetProperty(lang);
                var (title, why, how) = (t.GetProperty("title").GetString()!, t.GetProperty("why").GetString()!, t.GetProperty("how").GetString()!);
                // 中文的句子之間不空格
                return new KindText(title, why, how) { Help = lang == "zh" ? why + how : $"{why} {how}" };
            }
            return new Kind(Str("slug"), Str("check"), Str("group"), A11yConfig.ParseSeverity(Str("severity"))!.Value,
                [.. f.GetProperty("wcag").EnumerateArray().Select(c => c.GetString()!)], f.TryGetProperty("emptyName", out var empty) && empty.GetBoolean(), Words("en"), Words("zh"));
        }).ToList();
        return (kinds,
            [.. root.GetProperty("checks").EnumerateArray().Select(c => new CheckInfo(c.GetProperty("id").GetString()!, c.GetProperty("en").GetString()!, c.GetProperty("zh").GetString()!, c.TryGetProperty("gate", out var g) && g.GetBoolean()))],
            [.. root.GetProperty("groups").EnumerateArray().Select(g => new GroupInfo(g.GetProperty("id").GetString()!, g.GetProperty("en").GetString()!, g.GetProperty("zh").GetString()!))]);
    }
}
