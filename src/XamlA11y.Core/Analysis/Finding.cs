using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Analysis;

public enum Severity { Off, Info, Warn, Fail }

/// <summary>一條規則的說明（SARIF、--help、文件都從這裡來）。</summary>
/// <param name="Rule">規劃書的規則編號（R1…R7）：報告依它分組算可解析率。</param>
/// <param name="Wcag">WCAG 2.2 的成功準則（對應理由在 docs/standards.md）。</param>
public sealed record Kind(string Slug, string Rule, Severity Default, string Wcag, string Title, string Help)
{
    /// <summary>「4.1.2 (A), 2.5.3 (A)」。</summary>
    public string WcagWithLevels => string.Join(", ", Criteria.Select(c => $"{c} ({Analysis.Wcag.Level(c)})"));

    public IReadOnlyList<string> Criteria => Wcag.Split(", ");

    /// <summary>怎麼修（GitHub 上的一節）：SARIF 的 helpUri、HTML 的連結。</summary>
    public string Uri(bool zh = false) => $"https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/{(zh ? "%E4%BF%AE%E6%B3%95" : "fixes")}.md#{Slug}";

    /// <summary>這一種是「名稱是空的」：EN 301 549 11.5.2.5、Section 508 502.3.1（名稱要能由程式判定）。亂碼名稱不是空的。</summary>
    public bool NameIsEmpty => Slug is "missing-name" or "text-not-in-name" or "input-no-name" or "list-no-name" or "widget-no-name";
}

public static class Kinds
{
    public static readonly Kind MissingName = new("missing-name", "R1", Severity.Fail, "4.1.2, 1.1.1",
        "Control has no accessible name",
        "UI Automation has no name for this control, so all a screen reader can call it is \"button\" (or \"check box\", \"tab\"…). Set AutomationProperties.Name, or AutomationProperties.LabeledBy to an element that has text. A ToolTip is not a name: WPF exposes it as the help text, which some screen readers read as a description after the type.");

    public static readonly Kind TextNotName = new("text-not-in-name", "R1", Severity.Fail, "4.1.2, 2.5.3",
        "Visible text is not the accessible name",
        "The control shows text, but the text sits inside a panel, a Border or an AccessText, and WPF only takes the name from a string, a TextBlock or a ContentControl. The accessible name is empty. Set AutomationProperties.Name to the same text.");

    public static readonly Kind MeaninglessName = new("meaningless-name", "R1", Severity.Fail, "4.1.2, 1.1.1",
        "Accessible name is not readable text",
        "The accessible name is an icon-font character, white space, a type name such as System.Windows.Controls.TabItem, or geometry path data. Set AutomationProperties.Name.");

    public static readonly Kind SymbolName = new("symbol-name", "R1", Severity.Info, "1.1.1",
        "Accessible name is only a symbol",
        "The accessible name is only punctuation or an emoji, and a screen reader reads the symbol's own name (\"times\" for ×). Set AutomationProperties.Name to what the control does.");

    public static readonly Kind InputNoName = new("input-no-name", "R2", Severity.Fail, "4.1.2, 1.3.1",
        "Input has no accessible name",
        "A screen reader cannot say what this field is for. A TextBlock next to it is not a label, and <Label Target=…> only moves focus with the access key: WPF does not use it for the name. Set AutomationProperties.LabeledBy to the label, or AutomationProperties.Name.");

    public static readonly Kind FocusNotVisible = new("focus-not-visible", "R3", Severity.Fail, "2.4.7",
        "Keyboard focus is not visible",
        "FocusVisualStyle is removed and nothing else shows focus: the template and styles have no IsKeyboardFocused or IsFocused trigger and no Focused visual state. Keep the default FocusVisualStyle, or draw focus in the template.");

    public static readonly Kind MouseOnly = new("mouse-only", "R4", Severity.Fail, "2.1.1",
        "Works with the mouse but not the keyboard",
        "An element that is not a control handles mouse clicks, and the keyboard has no way to do the same. Use a Button (restyle it if needed): making a Border focusable fixes Tab, but a Border is never in the UI Automation tree.");

    public static readonly Kind NotInTabOrder = new("not-in-tab-order", "R5", Severity.Warn, "2.1.1",
        "Interactive control is not in the tab order",
        "IsTabStop=\"False\" or Focusable=\"False\" keeps Tab from reaching this control. Fine when the same action has a keyboard shortcut or another focusable entry point; otherwise keyboard users cannot use it.");

    public static readonly Kind SmallTarget = new("small-target", "R6", Severity.Warn, "2.5.8",
        "Click target is smaller than 24 by 24",
        "The target's size is set to less than 24 DIP and it is too close to its neighbour for the spacing exception of WCAG 2.5.8.");

    public static readonly Kind ListNoName = new("list-no-name", "R7", Severity.Warn, "4.1.2, 1.3.1",
        "List, grid or tree has no accessible name",
        "A screen reader announces this as just \"list\", \"data grid\" or \"tree\" when focus enters it, so two of them in one window sound the same. Set AutomationProperties.LabeledBy to the heading above it, or AutomationProperties.Name. A GroupBox around it and a <Label Target=…> do not name it.");

    public static readonly Kind WidgetNoName = new("widget-no-name", "R7", Severity.Warn, "4.1.2",
        "Progress bar, calendar, menu or tool bar has no accessible name",
        "A screen reader announces this as just \"progress bar\" or \"tool bar\". Set AutomationProperties.LabeledBy to the text next to it, or AutomationProperties.Name. A menu or tool bar is only an info: one main menu per window needs no name.");

    public static readonly Kind ItemTypeName = new("item-type-name", "R8", Severity.Warn, "4.1.2",
        "List items are read as their type name",
        "An item made from data is named by the object's ToString(), and a class without one gives MyApp.Models.Note for every item. DisplayMemberPath and ItemTemplate only change what is shown. Override ToString() in the class, or add a Setter for AutomationProperties.Name to the style of the items (ItemContainerStyle, RowStyle for a DataGrid; add it to the one that is there, a second one replaces it).");

    public static readonly Kind SilentTabStop = new("silent-tab-stop", "R9", Severity.Warn, "4.1.2",
        "Tab stops here and a screen reader says nothing",
        "The element takes keyboard focus but has no AutomationPeer, so it is not in the UI Automation tree; AutomationProperties.Name does not help. When it only holds other controls, set Focusable=\"False\" (IsTabStop=\"False\" when your code focuses it). When it is a control of its own, override OnCreateAutomationPeer, or derive from a control that has a peer.");

    public static readonly Kind SameName = new("same-name", "R10", Severity.Info, "2.4.6",
        "Controls side by side may sound alike",
        "They have the same accessible name and do different things. Fine when something that is announced tells them apart (a group, a label). Otherwise add what each one is for to AutomationProperties.Name (\"Browse for the input folder\").");

    public static readonly IReadOnlyList<Kind> All =
        [MissingName, TextNotName, MeaninglessName, SymbolName, InputNoName, FocusNotVisible, MouseOnly, NotInTabOrder, SmallTarget, ListNoName, WidgetNoName, ItemTypeName, SilentTabStop, SameName];

    public static readonly IReadOnlyList<string> Rules = ["R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9", "R10"];

    /// <summary>會算在這條規則裡的種類：輸入框的亂碼名稱、只有符號的名稱算 R2。</summary>
    public static IEnumerable<Kind> CountedIn(string rule) => All.Where(k => k.Rule == rule || rule == "R2" && (k == MeaninglessName || k == SymbolName));

    public static string RuleTitle(string rule) => rule switch
    {
        "R1" => "names of controls",
        "R2" => "names of inputs",
        "R3" => "visible focus",
        "R4" => "mouse-only elements",
        "R5" => "tab order",
        "R6" => "target size",
        "R7" => "names of lists",
        "R8" => "names of data items",
        "R9" => "silent tab stops",
        "R10" => "same names",
        _ => rule,
    };

    public static Kind? BySlug(string slug) => All.FirstOrDefault(k => k.Slug == slug);
}

public sealed record Finding(Kind Kind, Severity Severity, string File, int Line, string Element, string Message)
{
    /// <summary>認得出是哪一個元素的東西（x:Name、命令、處理器…），不含行號。</summary>
    public string Identity { get; init; } = "";

    /// <summary>同一個檔案裡同樣的鍵第幾個（1 起算）。</summary>
    public int Ordinal { get; set; } = 1;

    /// <summary>baseline 的鍵：不含行號 —— 行號會隨無關的編輯漂移，一次格式化就讓整份 baseline 失效。</summary>
    /// <remarks>降過級的（不能聚焦、停用）記「~」：之後變成能聚焦、升回 fail，就是新的一筆，不能被舊的蓋住。</remarks>
    public string BaselineKey => $"{Kind.Slug}{(Lowered ? "~" : "")}|{Escape(KeyFile ?? File)}|{(KeyFile is null ? Element : "")}|{Escape(Identity)}{(Ordinal > 1 ? "|#" + Ordinal : "")}";

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>名稱規則的：當成哪一個 WPF 控制項（NVDA 的念法要用）。別的規則是 null。</summary>
    public string? Control { get; init; }

    /// <summary>HelpText 或 ToolTip：NVDA 當描述念在型別後面。沒有是 ""；不確定（綁定、物件）是 null。</summary>
    public string? Description { get; init; }

    /// <summary>NVDA 會念的名稱（只有確定的：項目容器的型別名）；空名稱是 null。</summary>
    public string? SpokenName { get; init; }

    /// <summary>baseline 的鍵用這個、不用檔名：報在類別上的，多一個用它的檔案不能變成新的一筆。</summary>
    public string? KeyFile { get; init; }

    /// <summary>因為元素不能聚焦而降了一級。</summary>
    public bool Lowered { get; init; }

    /// <summary>名稱是空的。清單的亂碼名稱也報 list-no-name，但它不是空的。</summary>
    public bool NameIsEmpty => Kind.NameIsEmpty && !Unreadable;

    /// <summary>名稱有，只是念不出意思（R7 沒有另外一種）。</summary>
    public bool Unreadable { get; init; }

    public string Site => $"{File}:{Line}";
}

/// <summary>候選的判定結果。</summary>
public enum Verdict
{
    Pass,
    Finding,
    /// <summary>該判得出來卻卡住了（第三方控制項、專案外的樣式）：進可解析率的分母。</summary>
    Blocked,
    /// <summary>靜態不可能知道，或不該判（綁定資料、範本零件）：不進分母，但要數出來。</summary>
    NotJudged,
    Suppressed,
}

public sealed record RuleStats(string Rule)
{
    /// <summary>會算在這條規則裡的每一種都關掉了（設定檔，或選的標準沒有這一條）。它的數字不進總數與可解析率。</summary>
    public bool Off { get; init; }

    public int Pass { get; set; }
    public int Findings { get; set; }
    public int Blocked { get; set; }
    public int NotJudged { get; set; }
    public int Suppressed { get; set; }
    public Dictionary<string, int> BlockedReasons { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> NotJudgedReasons { get; } = new(StringComparer.Ordinal);

    public int Candidates => Pass + Findings + Blocked + NotJudged + Suppressed;
    public double Resolution => Pass + Findings + Suppressed + Blocked == 0 ? 100 : 100.0 * (Pass + Findings + Suppressed) / (Pass + Findings + Suppressed + Blocked);
}

public sealed class AuditResult
{
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required IReadOnlyList<RuleStats> Rules { get; init; }
    public required int Files { get; init; }

    /// <summary>依哪個標準、哪個等級。</summary>
    public Scope Scope { get; init; } = Scope.Default;

    public required IReadOnlyList<string> ParseErrors { get; init; }
    public IReadOnlyDictionary<string, string> ParseWhy { get; init; } = new Dictionary<string, string>();
    public required IReadOnlyList<(string File, string Dialect)> Foreign { get; init; }
    public required IReadOnlyList<ThemeUse> Themes { get; init; }
    /// <summary>不認得的控制項 → 卡住幾個位置。</summary>
    public required IReadOnlyList<(string Element, int Count)> UnknownControls { get; init; }
    /// <summary>沒作用的 xamla11y-ignore 與原因（「檔:行: 為什麼」）：沒寫理由、或停在被註解掉的元素前面。</summary>
    public required IReadOnlyList<string> InvalidIgnores { get; init; }

    /// <summary>沒關掉的規則。</summary>
    public IEnumerable<RuleStats> On => Rules.Where(r => !r.Off);

    public int Pass => On.Sum(r => r.Pass);
    public int Blocked => On.Sum(r => r.Blocked);
    public int NotJudged => On.Sum(r => r.NotJudged);
    public int Suppressed => On.Sum(r => r.Suppressed);
    public int Judged => On.Sum(r => r.Pass + r.Findings + r.Suppressed);
    public int Candidates => On.Sum(r => r.Candidates);

    /// <summary>
    /// 可解析率：判得出來的 ÷（判得出來的 + 卡住的）。「靜態不可能知道」的不在分母。
    /// 只看預設會擋的四條（R1～R4）：其他的預設不擋，也幾乎不會卡住，算進來會把名稱整批看不到的專案墊高過關。
    /// 關掉的規則不算：不看的東西看不到，不是結果不可信。
    /// </summary>
    public double Resolution
    {
        get
        {
            var gate = On.Where(r => r.Rule is "R1" or "R2" or "R3" or "R4").ToList();
            double judged = gate.Sum(r => r.Pass + r.Findings + r.Suppressed), blocked = gate.Sum(r => r.Blocked);
            return judged + blocked == 0 ? (Judged > 0 || (Blocked == 0 && NotJudged > 0) ? 100 : 0) : 100.0 * judged / (judged + blocked);
        }
    }

    public ScanGaps Gaps { get; init; } = new();

    /// <summary>每個候選的判定（Oracle 比對用；--json 不含）。</summary>
    public IReadOnlyList<Judgement> Judgements { get; init; } = [];
}

/// <param name="Element">XAML 元素。</param>
public sealed record Judgement(string Rule, Verdict Verdict, System.Xml.Linq.XElement Element, string File, int Line, string? Reason, Kind? Kind);
