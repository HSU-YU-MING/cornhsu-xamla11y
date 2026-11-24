namespace XamlA11y.Core.Analysis;

/// <param name="Since">第一次出現在哪一版 WCAG。</param>
/// <param name="ZhName">台灣「網站無障礙規範」115 年修正版的準則名稱。</param>
public sealed record Criterion(string Number, string Level, string Since, string Name, string ZhName);

/// <summary>用到的 WCAG 2.2 成功準則（規則的與人工檢查清單的）。等級、名稱、版本照 W3C 的資料（tests/XamlA11y.Tests/wcag22.json 守著）。</summary>
public static class Wcag
{
    public const string Version = "2.2";

    public static readonly IReadOnlyDictionary<string, Criterion> Criteria = new Criterion[]
    {
        new("1.1.1", "A", "2.0", "Non-text Content", "非文字內容"),
        new("1.3.1", "A", "2.0", "Info and Relationships", "資訊與關連性"),
        new("1.3.2", "A", "2.0", "Meaningful Sequence", "有意義的序列"),
        new("1.4.1", "A", "2.0", "Use of Color", "色彩使用"),
        new("1.4.3", "AA", "2.0", "Contrast (Minimum)", "對比(最小)"),
        new("1.4.4", "AA", "2.0", "Resize Text", "調整文字尺寸"),
        new("1.4.11", "AA", "2.1", "Non-text Contrast", "非文字對比"),
        new("1.4.13", "AA", "2.1", "Content on Hover or Focus", "懸浮或焦點內容"),
        new("2.1.1", "A", "2.0", "Keyboard", "鍵盤"),
        new("2.1.2", "A", "2.0", "No Keyboard Trap", "無鍵盤操作陷阱"),
        new("2.1.4", "A", "2.1", "Character Key Shortcuts", "快捷鍵"),
        new("2.4.3", "A", "2.0", "Focus Order", "焦點順序"),
        new("2.4.6", "AA", "2.0", "Headings and Labels", "標題和標籤"),
        new("2.4.7", "AA", "2.0", "Focus Visible", "焦點可視"),
        new("2.4.11", "AA", "2.2", "Focus Not Obscured (Minimum)", "焦點不遮蔽(最小)"),
        new("2.5.3", "A", "2.1", "Label in Name", "標籤名稱"),
        new("2.5.7", "AA", "2.2", "Dragging Movements", "拖曳動作"),
        new("2.5.8", "AA", "2.2", "Target Size (Minimum)", "目標尺寸(最小)"),
        new("3.2.1", "A", "2.0", "On Focus", "焦點"),
        new("3.2.2", "A", "2.0", "On Input", "輸入"),
        new("3.3.1", "A", "2.0", "Error Identification", "識別錯誤"),
        new("3.3.3", "AA", "2.0", "Error Suggestion", "錯誤建議"),
        new("3.3.7", "A", "2.2", "Redundant Entry", "冗餘輸入"),
        new("3.3.8", "AA", "2.2", "Accessible Authentication (Minimum)", "無障礙認證(最小)"),
        new("4.1.2", "A", "2.0", "Name, Role, Value", "名稱、角色和值"),
        new("4.1.3", "AA", "2.1", "Status Messages", "狀態訊息"),
    }.ToDictionary(c => c.Number, StringComparer.Ordinal);

    public static string Level(string criterion) => Criteria[criterion].Level;
}

/// <summary>
/// 可以選的標準。規則本身不變（量的是 WPF 怎麼做），變的是「算不算在內」與報告上寫哪一條。
/// 條號查證（2026-11-24）：EN 301 549 V3.2.1（2021-03）與 V4.1.1（2026-09）的 PDF 第 11 章、Section 508 的 ICT 標準。
/// </summary>
/// <param name="Wcag">採用的 WCAG 版本。</param>
/// <param name="Clauses">WCAG 準則 → 這個標準的條號；沒有自己條號的標準是空的。</param>
/// <param name="EmptyName">名稱是空的時候另外還有哪一條（物件資訊）。</param>
public sealed record Standard(string Id, string Title, string Wcag, IReadOnlyDictionary<string, string> Clauses, string? EmptyName = null)
{
    public bool HasClauses => Clauses.Count > 0 || EmptyName is not null;

    private static readonly Dictionary<string, string> None = [];

    // V3.2.1 的軟體章把「非封閉功能」另編一層（.1）；V4.1.1 拿掉了那一層
    private static readonly Dictionary<string, string> En3 = new(StringComparer.Ordinal)
    {
        ["1.1.1"] = "11.1.1.1.1", ["1.3.1"] = "11.1.3.1.1", ["2.1.1"] = "11.2.1.1.1", ["2.4.7"] = "11.2.4.7", ["2.5.3"] = "11.2.5.3.1", ["4.1.2"] = "11.4.1.2.1",
    };

    private static readonly Dictionary<string, string> En4 = new(StringComparer.Ordinal)
    {
        ["1.1.1"] = "11.1.1.1", ["1.3.1"] = "11.1.3.1", ["2.1.1"] = "11.2.1.1", ["2.4.7"] = "11.2.4.7", ["2.5.3"] = "11.2.5.3", ["2.5.8"] = "11.2.5.8", ["4.1.2"] = "11.4.1.2",
    };

    // 軟體的 508 走 E207.2（整套 WCAG 2.0），沒有逐條的編號
    private static readonly Dictionary<string, string> S508 = new(StringComparer.Ordinal)
    {
        ["1.1.1"] = "E207.2", ["1.3.1"] = "E207.2", ["2.1.1"] = "E207.2", ["2.4.7"] = "E207.2", ["4.1.2"] = "E207.2",
    };

    public static readonly Standard Wcag22 = new("wcag22", "WCAG 2.2", "2.2", None);
    public static readonly Standard Wcag21 = new("wcag21", "WCAG 2.1", "2.1", None);
    public static readonly Standard Wcag20 = new("wcag20", "WCAG 2.0", "2.0", None);
    public static readonly Standard En301549 = new("en301549", "EN 301 549 V3.2.1", "2.1", En3, "11.5.2.5");
    public static readonly Standard En301549V4 = new("en301549-v4", "EN 301 549 V4.1.1", "2.2", En4, "11.5.2.5");
    public static readonly Standard Section508 = new("section508", "Section 508", "2.0", S508, "502.3.1");

    public static readonly IReadOnlyList<Standard> All = [Wcag22, Wcag21, Wcag20, En301549, En301549V4, Section508];

    public static Standard? ById(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>這個標準有沒有這一條：看它採用的 WCAG 版本。</summary>
    public bool Has(string criterion) => string.CompareOrdinal(Analysis.Wcag.Criteria[criterion].Since, Wcag) <= 0;

    /// <summary>這一種 finding 在這個標準的條號。</summary>
    public IReadOnlyList<string> ClausesOf(IEnumerable<string> criteria, bool nameIsEmpty)
    {
        var list = criteria.Where(Clauses.ContainsKey).Select(c => Clauses[c]).ToList();
        if (EmptyName is not null && list.Count > 0 && nameIsEmpty) list.Add(EmptyName);
        return [.. list.Distinct(StringComparer.Ordinal)];
    }
}

/// <summary>這次檢查的範圍：哪個標準、哪個等級。預設 WCAG 2.2 的 A 與 AA。</summary>
public sealed record Scope(Standard Standard, string Level)
{
    public static readonly Scope Default = new(Standard.Wcag22, "AA");

    public bool Has(string criterion) => Standard.Has(criterion) && (Level == "AA" || Analysis.Wcag.Level(criterion) == "A");

    /// <summary>「WCAG 2.2 A + AA」「EN 301 549 V3.2.1 (WCAG 2.1 A + AA)」。</summary>
    public string Title
    {
        get
        {
            var levels = Level == "A" ? "A" : "A + AA";
            return Standard.HasClauses ? $"{Standard.Title} (WCAG {Standard.Wcag} {levels})" : $"{Standard.Title} {levels}";
        }
    }

    public IReadOnlyList<string> CriteriaOf(Kind kind) => [.. kind.Criteria.Where(Has)];

    /// <summary>範圍裡的準則與等級；一條都沒有（設定檔硬打開的）是「–」。</summary>
    public string WcagOf(Kind kind) => CriteriaOf(kind) is { Count: > 0 } list ? string.Join(", ", list.Select(c => $"{c} ({Analysis.Wcag.Level(c)})")) : "–";

    public IReadOnlyList<string> ClausesOf(Kind kind) => Standard.ClausesOf(CriteriaOf(kind), kind.NameIsEmpty);

    public IReadOnlyList<string> ClausesOf(Finding f) => Standard.ClausesOf(CriteriaOf(f.Kind), f.NameIsEmpty);

    /// <summary>在範圍裡：至少有一條準則。</summary>
    public bool Covers(Kind kind) => kind.Criteria.Any(Has);
}

/// <summary>工具查不到、要人來看的裡面，桌面軟體最常出問題的 —— **不是** WCAG 的全部（docs/standards.md 與 標準對照.md 的表就是這一份，測試守著）。</summary>
public sealed record ManualCheck(IReadOnlyList<string> Criteria, string En, string Zh)
{
    public static readonly IReadOnlyList<ManualCheck> All =
    [
        new(["1.3.2"], "Read the window with a screen reader: does the order make sense?", "用螢幕閱讀器念一遍視窗，順序通不通。"),
        new(["1.4.1"], "Is anything (an error, a state) shown only by color?", "有沒有東西（錯誤、狀態）只靠顏色表示。"),
        new(["1.4.3"], "Text against its background, 4.5:1 (large text 3:1). XamlContrast checks it.", "文字跟背景 4.5:1（大字 3:1）。XamlContrast 會查。"),
        new(["1.4.4"], "WPF does not follow the Windows text size. Does the app offer its own scaling to 200 %?", "WPF 不跟 Windows 的文字大小走，App 自己有沒有放大到 200% 的辦法。"),
        new(["1.4.11"], "Borders, icons and the focus indicator against their background, 3:1.", "框線、圖示、焦點框跟背景 3:1。"),
        new(["1.4.13"], "Can a ToolTip or popup be dismissed, hovered, and does it stay?", "ToolTip、彈出視窗能不能關掉、滑鼠移得上去、不會自己消失。"),
        new(["2.1.1"], "Put the mouse away and do every task. Handlers attached in code are invisible to this tool.", "把滑鼠收起來，每件事做一遍。程式碼裡掛的事件這支看不到。"),
        new(["2.1.2"], "Can Tab or Esc leave every control, also editors and grids?", "每個控制項（編輯器、表格也是）用 Tab 或 Esc 出不出得來。"),
        new(["2.1.4"], "Single-letter shortcuts: can they be turned off?", "單一字母的快速鍵關不關得掉。"),
        new(["2.4.3"], "Does Tab follow the meaning? Where is focus after a dialog closes?", "Tab 走的順序合不合理；對話框關掉之後焦點在哪。"),
        new(["2.4.6"], "Do the names say what the control does? A name that exists passes this tool.", "名稱說不說得清楚做什麼。這支只看有沒有名稱。"),
        new(["2.4.11"], "Does a sticky bar or popup cover the focused control?", "固定的工具列、彈出視窗會不會蓋住有焦點的控制項。"),
        new(["2.5.7"], "Is there a way without dragging?", "不拖曳做不做得到。"),
        new(["3.2.1", "3.2.2"], "Does something unexpected happen when a control gets focus or a value?", "拿到焦點或改值的時候會不會冒出意外的事。"),
        new(["3.3.1", "3.3.3"], "Is the error said in text, and tied to the field?", "錯誤有沒有用文字說、連到那個欄位。"),
        new(["3.3.7", "3.3.8"], "Is anything asked twice? Does signing in need a puzzle?", "同一個資料要不要輸入兩次；登入要不要解謎。"),
        new(["4.1.2"], "Roles, states and values of custom controls: OnCreateAutomationPeer.", "自訂控制項的角色、狀態、值：OnCreateAutomationPeer。"),
        new(["4.1.3"], "Is \"Saved\" announced? AutomationProperties.LiveSetting and RaiseAutomationEvent.", "「已儲存」有沒有念出來：AutomationProperties.LiveSetting 與 RaiseAutomationEvent。"),
    ];

    /// <summary>範圍裡的：準則全不在範圍裡的不列（WCAG 2.0 沒有 2.5.7）。</summary>
    public static IReadOnlyList<(ManualCheck Check, IReadOnlyList<Criterion> Criteria)> In(Scope scope) =>
        [.. All.Select(m => (m, (IReadOnlyList<Criterion>)[.. m.Criteria.Where(scope.Has).Select(c => Wcag.Criteria[c])])).Where(x => x.Item2.Count > 0)];
}
