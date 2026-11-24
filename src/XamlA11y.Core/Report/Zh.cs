using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>報告的語言（主控台、Markdown、HTML）。JSON、SARIF、MSBuild 與錯誤訊息一律英文：給程式讀的不隨設定變。</summary>
public sealed record Lang(string Id)
{
    public static readonly Lang En = new("en");
    public static readonly Lang ZhHant = new("zh-Hant");

    public static Lang? ById(string id) => id switch { "en" => En, "zh-Hant" => ZhHant, _ => null };
    public static string Ids => "en, zh-Hant";

    public bool IsZh => Id == "zh-Hant";

    public string T(string en, string zh) => IsZh ? zh : en;

    public string Title(Kind k) => IsZh ? Zh.Kinds[k.Slug].Title : k.Title;
    public string Help(Kind k) => IsZh ? $"{Zh.Kinds[k.Slug].Why}{Zh.Kinds[k.Slug].How}" : k.Help;
    public string RuleTitle(string rule) => IsZh ? Zh.Rules.GetValueOrDefault(rule, rule) : Analysis.Kinds.RuleTitle(rule);
    public string Criterion(Criterion c) => IsZh ? c.ZhName : c.Name;
    public string Message(string m) => IsZh ? Zh.Message(m) : m;
    public string Reason(string r) => IsZh ? Zh.Reason(r) : r;
    public string Ignore(string s) => IsZh ? Zh.Ignore(s) : s;
    public string Scope => IsZh ? Zh.ScopeText : Summary.Scope;
}

/// <summary>
/// 繁體中文。種類的「是什麼、為什麼、怎麼修」是手寫的；每一筆的細節由英文訊息照樣式翻
/// （訊息只在規則裡組一次，翻譯不進規則）。認不得的樣式照英文印 —— 測試逐一核對規則寫得出來的訊息都翻得到。
/// </summary>
public static partial class Zh
{
    /// <summary>種類 → 標題、為什麼、怎麼修。</summary>
    public static readonly IReadOnlyDictionary<string, (string Title, string Why, string How)> Kinds = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
    {
        ["missing-name"] = ("控制項沒有名稱", "螢幕閱讀器只能念「按鈕」「核取方塊」，說不出是做什麼的。", "設 AutomationProperties.Name，或用 AutomationProperties.LabeledBy 指到有文字的元素。ToolTip 不是名稱：WPF 把它當說明文字，有的螢幕閱讀器會念在型別後面。"),
        ["text-not-in-name"] = ("畫面有字，名稱卻是空的", "字放在面板、Border 或 AccessText 裡，WPF 只從字串、TextBlock、ContentControl 拿名稱，所以名稱是空的。", "把同樣的字設到 AutomationProperties.Name。"),
        ["meaningless-name"] = ("名稱念不出意思", "名稱是圖示字型的字元、空白、型別名（System.Windows.Controls.TabItem）或圖形的路徑資料。", "設 AutomationProperties.Name。"),
        ["symbol-name"] = ("名稱只有符號", "名稱只有標點或表情符號，螢幕閱讀器念的是符號自己的名字（× 念「乘以」）。", "把 AutomationProperties.Name 設成它做的事。"),
        ["input-no-name"] = ("輸入框沒有名稱", "螢幕閱讀器說不出這一格要填什麼。旁邊的 TextBlock 不算標籤；<Label Target=…> 只會用快速鍵移焦點，WPF 不拿它當名稱。", "用 AutomationProperties.LabeledBy 指到標籤，或設 AutomationProperties.Name。"),
        ["focus-not-visible"] = ("看不到鍵盤焦點在哪", "FocusVisualStyle 被拿掉，範本與樣式裡也沒有別的東西表示焦點（沒有 IsKeyboardFocused、IsFocused 觸發器，也沒有 Focused 狀態）。", "留著預設的 FocusVisualStyle，或在範本裡畫出焦點。"),
        ["mouse-only"] = ("滑鼠能用，鍵盤不能", "不是控制項的元素處理了滑鼠點按，鍵盤沒有辦法做同一件事。", "改用 Button（要的話換外觀）。只讓 Border 能聚焦不夠：Border 永遠不在 UI Automation 的樹上。"),
        ["not-in-tab-order"] = ("Tab 到不了", "IsTabStop=\"False\" 或 Focusable=\"False\" 讓 Tab 跳過它。", "同一件事有快速鍵或別的入口就沒關係，沒有的話把它放回 Tab 順序。"),
        ["small-target"] = ("點擊範圍小於 24 × 24", "大小設得比 24 DIP 小，而且離隔壁太近，用不了 WCAG 2.5.8 的間距例外。", "放大到 24，或拉開距離。"),
        ["list-no-name"] = ("清單、表格或樹沒有名稱", "焦點進去時螢幕閱讀器只念「清單」「資料格」「樹狀檢視」，同一個視窗有兩個就分不出來。外面的 GroupBox 與 <Label Target=…> 都不會給它名稱。", "用 AutomationProperties.LabeledBy 指到上面的標題，或設 AutomationProperties.Name。"),
        ["widget-no-name"] = ("進度條、月曆、選單或工具列沒有名稱", "螢幕閱讀器只念「進度列」「工具列」。", "用 AutomationProperties.LabeledBy 指到旁邊的文字，或設 AutomationProperties.Name。選單與工具列只當 info：一個視窗一個主選單，不用名稱也分得出來。"),
        ["item-type-name"] = ("清單的項目念成類別名", "資料產生的項目，名稱是物件的 ToString()；沒覆寫的類別，每一項都念成 MyApp.Models.Note。DisplayMemberPath 與 ItemTemplate 只管畫面。", "在類別裡覆寫 ToString()，或在項目的樣式（ItemContainerStyle，DataGrid 是 RowStyle）加一個 AutomationProperties.Name 的 Setter —— 已經有樣式就加進那一個，再寫一個會整個取代。"),
        ["silent-tab-stop"] = ("Tab 停得到，螢幕閱讀器卻沒聲音", "元素拿得到鍵盤焦點，但沒有 AutomationPeer，不在 UI Automation 的樹上；設 AutomationProperties.Name 也沒用。", "只是放別的控制項的，設 Focusable=\"False\"（程式會叫它拿焦點的用 IsTabStop=\"False\"）；自己是一個控制項的，覆寫 OnCreateAutomationPeer，或改成繼承有 peer 的控制項。"),
        ["same-name"] = ("並排的控制項念起來可能一樣", "名稱一樣、做的事不一樣。會念出來的東西（群組、標籤）分得出來就沒關係。", "分不出來的話，在 AutomationProperties.Name 補上各自是做什麼的（「瀏覽輸入資料夾」）。"),
    };

    public static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["R1"] = "控制項的名稱",
        ["R2"] = "輸入框的名稱",
        ["R3"] = "焦點看得見",
        ["R4"] = "只能用滑鼠",
        ["R5"] = "Tab 順序",
        ["R6"] = "點擊範圍",
        ["R7"] = "清單的名稱",
        ["R8"] = "資料項目的名稱",
        ["R9"] = "沒聲音的停留點",
        ["R10"] = "名稱一樣",
    };

    public const string ScopeText =
        "靜態檢查：讀 XAML，什麼都沒有執行；程式碼只看型別與屬性的宣告、每個 XAML 自己的 code-behind（有沒有設名稱、內容、鍵盤處理器）。沒有問題不等於符合 WCAG 或任何法規。";

    /// <summary>一筆 finding 的訊息。</summary>
    public static string Message(string m) => TryMessage(m, out var zh) ? zh : m;

    public static bool TryMessage(string m, out string zh)
    {
        foreach (var (regex, make) in Messages)
            if (regex.Match(m) is { Success: true } match)
            {
                var ok = true;
                zh = make(match, r => { ok &= TryReason(r, out var t); return t; });
                if (ok) return true;
            }
        zh = m;
        return false;
    }

    /// <summary>「看不到」「不判」的原因。</summary>
    public static string Reason(string r) => TryReason(r, out var zh) ? zh : r;

    public static bool TryReason(string r, out string zh)
    {
        if (Plain.TryGetValue(r, out var plain)) { zh = plain; return true; }
        // 接在後面的補充一段一段翻：「…; its name becomes the type name」「…; its ToolTip is the help text, not the name」
        foreach (var (regex, tail) in Tails)
            if (regex.Match(r) is { Success: true } m && TryReason(m.Groups["head"].Value, out var head))
            {
                zh = head + "；" + m.Result(tail);
                return true;
            }
        foreach (var (regex, text) in Patterns)
            if (regex.Match(r) is { Success: true } m)
            {
                zh = m.Result(text);
                return true;
            }
        zh = r;
        return false;
    }

    // NonBacktracking：訊息裡有 repo 的字，不能讓它把樣式比對拖垮
    private static Regex R(string pattern) => new($"^{pattern}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.Singleline);

    private const string E = "<(?<e>[^<>]+)>";

    private static readonly (Regex, Func<Match, Func<string, string>, string>)[] Messages =
    [
        (R($"{E} shows bound text but has no accessible name: (?<why>.+)"), (m, why) => $"<{m.Groups["e"]}> 畫面上有綁定的文字，名稱卻是空的：{why(m.Groups["why"].Value)}"),
        (R($"{E} shows \"(?<t>.*)\" but has no accessible name: (?<why>.+)"), (m, why) => $"<{m.Groups["e"]}> 畫面上有「{m.Groups["t"]}」，名稱卻是空的：{why(m.Groups["why"].Value)}"),
        (R($"{E} has no accessible name"), (m, _) => $"<{m.Groups["e"]}> 沒有名稱"),
        (R($"{E} has no accessible name; (?<hint>.+)"), (m, why) => $"<{m.Groups["e"]}> 沒有名稱；{why(m.Groups["hint"].Value)}"),
        (R($"{E} has no accessible name: (?<why>.+)"), (m, why) => $"<{m.Groups["e"]}> 沒有名稱：{why(m.Groups["why"].Value)}"),
        (R($"{E} has an unreadable accessible name: (?<why>.+)"), (m, why) => $"<{m.Groups["e"]}> 的名稱念不出意思：{why(m.Groups["why"].Value)}"),
        (R($"{E} has an unreadable accessible name \\((?<t>.*?)\\): (?<why>.+)"), (m, why) => $"<{m.Groups["e"]}> 的名稱念不出意思（{m.Groups["t"]}）：{why(m.Groups["why"].Value)}"),
        (R($"{E} is named only by the symbol \"(?<t>.*)\""), (m, _) => $"<{m.Groups["e"]}> 的名稱只有符號「{m.Groups["t"]}」"),
        (R("The style (?<s>'.*?'|for .*?) removes FocusVisualStyle and nothing else shows keyboard focus \\(as used by the style (?<u>'.*?'|for .*?)\\): (?<why>.+)"), (m, why) => $"樣式{Style(m.Groups["s"].Value)}拿掉 FocusVisualStyle，又沒有別的東西表示焦點（樣式{Style(m.Groups["u"].Value)}用到它）：{why(m.Groups["why"].Value)}"),
        (R("The style (?<s>'.*?'|for .*?) removes FocusVisualStyle and nothing else shows keyboard focus: (?<why>.+)"), (m, why) => $"樣式{Style(m.Groups["s"].Value)}拿掉 FocusVisualStyle，又沒有別的東西表示焦點：{why(m.Groups["why"].Value)}"),
        (R($"{E} removes FocusVisualStyle and nothing else shows keyboard focus: (?<why>.+)"), (m, why) => $"<{m.Groups["e"]}> 拿掉 FocusVisualStyle，又沒有別的東西表示焦點：{why(m.Groups["why"].Value)}"),
        (R($"{E} reacts to (?<g>.+) but the keyboard cannot reach it"), (m, _) => $"<{m.Groups["e"]}> 處理 {Gesture(m.Groups["g"].Value)}，鍵盤到不了"),
        (R($"{E} has (?<p>Focusable|IsTabStop)=\"False\", so Tab never reaches it"), (m, _) => $"<{m.Groups["e"]}> 設了 {m.Groups["p"]}=\"False\"，Tab 到不了"),
        (R($"{E} has (?<p>Focusable|IsTabStop)=\"False\" \\(from the style at (?<at>.+)\\), so Tab never reaches it"), (m, _) => $"<{m.Groups["e"]}> 設了 {m.Groups["p"]}=\"False\"（樣式在 {m.Groups["at"]}），Tab 到不了"),
        (R($"{E} is (?<n>[0-9.]+) DIP (?<axis>wide|high) and (?<gap>[0-9.]+) DIP away from <(?<o>[^<>]+)>(?<id> '.*')?; targets under 24 need more space around them"),
            (m, _) => $"<{m.Groups["e"]}> {(m.Groups["axis"].Value == "wide" ? "寬" : "高")} {m.Groups["n"]} DIP，離 <{m.Groups["o"]}>{m.Groups["id"]} 只有 {m.Groups["gap"]} DIP；小於 24 的要留更多空間"),
        (R($"{E} names every item with its type name(?: \\((?<t>[^()]*)\\))?: no style of (?<c>\\w+) sets AutomationProperties\\.Name, and the class does not override ToString\\(\\)(?<tail>; DisplayMemberPath only changes what is shown|; the ItemTemplate only changes what is shown)?"),
            (m, _) => $"<{m.Groups["e"]}> 的每一項都念成型別名{(m.Groups["t"].Success ? $"（{m.Groups["t"]}）" : "")}：沒有 {m.Groups["c"]} 的樣式設 AutomationProperties.Name，類別也沒覆寫 ToString(){(m.Groups["tail"].Value.Contains("Display", StringComparison.Ordinal) ? "；DisplayMemberPath 只管畫面" : m.Groups["tail"].Success ? "；ItemTemplate 只管畫面" : "")}"),
        (R($"{E} takes a Tab stop but is not in the UI Automation tree(?: \\(it derives from (?<b>\\w+) and has no AutomationPeer\\))?, so a screen reader says nothing there(?<n>; its AutomationProperties\\.Name is not used)?"),
            (m, _) => $"<{m.Groups["e"]}> Tab 停得到，但不在 UI Automation 的樹上{(m.Groups["b"].Success ? $"（它繼承 {m.Groups["b"]}，沒有 AutomationPeer）" : "")}，螢幕閱讀器停在這裡沒聲音{(m.Groups["n"].Success ? "；它的 AutomationProperties.Name 沒有作用" : "")}"),
        (R($"(?<n>[0-9]+) {E} here may sound alike: the accessible name \"(?<t>.*)\" at lines (?<l>[0-9, …]+), and they do different things"), (m, _) => $"這裡 {m.Groups["n"]} 個 <{m.Groups["e"]}> 念起來可能一樣：名稱都是「{m.Groups["t"]}」（第 {m.Groups["l"]} 行），做的事卻不同"),
    ];

    private static string Style(string s) => s.StartsWith("for ", StringComparison.Ordinal) ? $"（{s[4..]} 的）" : $"「{s.Trim((char)39)}」";

    private static string Gesture(string g) => MouseBindingRegex().Match(g) is { Success: true } m ? $"{m.Groups["g"].Value} 的 MouseBinding{m.Groups["rest"].Value}" : g;

    [GeneratedRegex(@"^a (?<g>\S+) MouseBinding(?<rest>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex MouseBindingRegex();

    private static readonly (Regex, string)[] Tails =
    [
        (R("(?<head>.+); a warning because it cannot take focus"), "不能聚焦，所以只算警告"),
        (R("(?<head>.+); a warning because it is disabled"), "停用的，所以只算警告"),
        (R("(?<head>.+); a warning because it is hidden"), "藏起來的，所以只算警告"),
        (R("(?<head>.+); its name becomes the type name"), "名稱會變成型別名"),
        (R("(?<head>.+); its ToolTip is the help text, not the name"), "ToolTip 是說明文字，不是名稱"),
        (R("(?<head>.+); its (?<p>\\w+) property belongs to the class and is not the name"), "${p} 是它自己的屬性，不是名稱"),
    ];

    /// <summary>沒作用的 ignore（「檔:行: 為什麼」）。</summary>
    public static string Ignore(string s) => s
        .Replace("xamla11y-ignore has a commented-out element or text after it, so it does nothing (put it right before the element it is for)", "xamla11y-ignore 後面是被註解掉的元素或文字，沒有作用（要放在它管的元素正前面）", StringComparison.Ordinal)
        .Replace("xamla11y-ignore has no reason, so it does nothing (write xamla11y-ignore: why)", "xamla11y-ignore 沒寫理由，沒有作用（寫成 xamla11y-ignore: 為什麼）", StringComparison.Ordinal);

    private static readonly Dictionary<string, string> Plain = new(StringComparer.Ordinal)
    {
        // 名稱
        ["its ToolTip is the help text, not the name"] = "ToolTip 是說明文字，不是名稱",
        ["a warning because it cannot take focus"] = "不能聚焦，所以只算警告",
        ["a warning because it is disabled"] = "停用的，所以只算警告",
        ["a warning because it is hidden"] = "藏起來的，所以只算警告",
        ["it has no AutomationProperties.Name and nothing labels it"] = "沒有 AutomationProperties.Name，也沒有東西標示它",
        ["its content is an empty string"] = "內容是空字串",
        ["its content is only white space"] = "內容只有空白",
        ["its content is null"] = "內容是 null",
        ["its content is bound"] = "內容是綁定的",
        ["its content is an empty TextBlock"] = "內容是空的 TextBlock",
        ["its content is an empty TextBox"] = "內容是空的 TextBox",
        ["its content is a TextBox with bound text"] = "內容是文字綁定的 TextBox",
        ["its content is a TextBox, whose text becomes the name"] = "內容是 TextBox，它的文字會變成名稱",
        ["its content is a Geometry, so its name is the path data"] = "內容是 Geometry，名稱是路徑資料",
        ["its name is only white space"] = "名稱只有空白",
        ["its name is an icon-font character from the private use area"] = "名稱是圖示字型的私用區字元",
        ["its name is only symbols"] = "名稱只有符號",
        ["its text is empty"] = "文字是空的",
        ["its text is bound"] = "文字是綁定的",
        ["its text is set with an object"] = "文字用物件設定",
        ["a trigger sets its text"] = "觸發器設它的文字",
        ["it has no text"] = "沒有文字",
        ["it only contains an element, and WPF turns that into a single space"] = "裡面只有元素，WPF 把它變成一個空白",
        ["the labelling chain is too deep"] = "LabeledBy 繞太多層",
        ["its content is too complex to follow"] = "內容太複雜，跟不下去",
        ["its content is nested too deeply"] = "內容包太多層",
        ["its content is nested or aliased too deeply"] = "內容包太多層，或資源繞太多次",
        ["its content is a resource this tool cannot read"] = "內容是這支讀不懂的資源",
        ["the code-behind sets its accessible name"] = "code-behind 設了它的名稱",
        ["the code-behind sets its text"] = "code-behind 設了它的文字",
        ["its class sets its text in code"] = "它的類別在程式碼裡設文字",
        ["a trigger sets its accessible name"] = "觸發器設它的名稱",
        ["its Text is set by a style this tool cannot read"] = "Text 由這支讀不懂的樣式設",
        ["a style may set the text of its Run"] = "樣式可能設了 Run 的文字",
        ["LabeledBy is set to an element this tool does not know"] = "LabeledBy 設成這支不認得的元素",
        ["LabeledBy is set to something this tool cannot follow"] = "LabeledBy 設成這支跟不到的東西",
        ["its Command is a resource this tool cannot read, which may be a RoutedUICommand"] = "Command 是這支讀不懂的資源，可能是 RoutedUICommand",
        ["its Command may be a RoutedUICommand, whose text would be the name"] = "Command 可能是 RoutedUICommand，它的文字會是名稱",
        ["the name comes from data at run time"] = "名稱來自執行時的資料",
        ["a class that sets its own accessible name in code"] = "類別在程式碼裡設自己的名稱",
        ["a control this tool does not know (declare it in treatAs)"] = "這支不認得的控制項（用 treatAs 宣告）",
        ["a control that was not measured"] = "沒量過的控制項",
        ["a template part that cannot take focus"] = "不能聚焦的範本零件",
        ["a template part that its control names in code"] = "由控制項的程式碼給名稱的範本零件",
        ["a template part that its control fills in code"] = "由控制項的程式碼填內容的範本零件",
        ["a collapsed item (its header is never shown)"] = "收合的項目（標題不會出現）",
        // 焦點
        ["FocusVisualStyle is set to a style this tool cannot read"] = "FocusVisualStyle 設成這支讀不懂的樣式",
        ["a style that nothing uses"] = "沒有人用的樣式",
        ["not a control the keyboard goes to"] = "鍵盤不會到的元素",
        ["the element sets its own FocusVisualStyle"] = "元素自己設了 FocusVisualStyle",
        ["a template part (the whole control may show focus)"] = "範本零件（焦點可能由整個控制項表示）",
        ["it cannot take keyboard focus"] = "不能拿鍵盤焦點",
        ["a style for template parts"] = "給範本零件用的樣式",
        ["a style without a target type that nothing uses"] = "沒寫目標型別、也沒有人用的樣式",
        ["a list item or tab (the selection shows where focus is)"] = "清單項目或頁籤（選取就看得出焦點）",
        ["a trigger changes whether it can take focus"] = "觸發器會改它能不能聚焦",
        ["a focus trigger also depends on another condition"] = "焦點觸發器還要看別的條件",
        ["another property sets the focus visual"] = "別的屬性設了焦點外觀",
        ["its template contains controls this tool does not know, or a focus trigger with another condition"] = "範本裡有這支不認得的控制項，或焦點觸發器還要看別的條件",
        ["its template has no focus trigger and no Focused state"] = "範本沒有焦點觸發器，也沒有 Focused 狀態",
        ["a ToolBar gives it a style of its own"] = "ToolBar 會換上自己的樣式",
        ["several themes apply and they differ"] = "套到好幾個佈景，而且不一樣",
        ["the Template is not a ControlTemplate this tool can read"] = "Template 不是這支讀得懂的 ControlTemplate",
        ["a resource key this tool cannot read"] = "這支讀不懂的資源鍵",
        ["a resource with a type key"] = "用型別當鍵的資源",
        ["the ItemContainerStyle of its list is something this tool cannot read"] = "清單的 ItemContainerStyle 是這支讀不懂的東西",
        ["the BasedOn chain is too deep"] = "BasedOn 疊太多層",
        ["a style in the chain has a BasedOn this tool cannot read"] = "疊下來的樣式裡有這支讀不懂的 BasedOn",
        ["a Setter without a Value"] = "沒有 Value 的 Setter",
        ["its container style may name the items"] = "容器樣式可能給了名稱",
        ["what its items are is not visible"] = "項目是什麼看不出來",
        ["the code sets whether it can take focus"] = "程式碼設它能不能聚焦",
        ["a focus handler may pass the focus on"] = "拿到焦點的處理器可能把焦點轉出去",
        ["inside a container that limits Tab"] = "在限制 Tab 的容器裡",
        ["a style of its class keeps it out of the tab order"] = "這個類別的樣式不讓 Tab 進來",
        ["the same class, reported once"] = "同一個類別，只報一次",
        // 滑鼠
        ["a handler on the root element"] = "處理器在根元素上",
        ["a handler that moves the window (the system menu does it from the keyboard)"] = "用來移動視窗的處理器（鍵盤有系統選單）",
        ["looks like a drag gesture (it also handles mouse movement)"] = "看起來是拖曳（也處理滑鼠移動）",
        ["a container around real controls"] = "包著真正控制項的容器",
        ["inside a control that the keyboard reaches (whether both do the same is not visible)"] = "在鍵盤到得了的控制項裡（做的是不是同一件事看不出來）",
        ["has its own keyboard handler (which keys it handles is not visible)"] = "自己有鍵盤處理器（處理哪些鍵看不出來）",
        ["window chrome"] = "視窗外框",
        ["a part of a control template (the control may handle the keyboard itself)"] = "控制項範本的一部分（控制項可能自己處理鍵盤）",
        ["inside a list item template (the list may handle Enter or Space)"] = "在清單項目的範本裡（清單可能處理 Enter 或空白鍵）",
        ["only a Preview (tunnelling) handler"] = "只有 Preview 處理器",
        ["a control in the same file that the keyboard reaches has the same action"] = "同一個檔案裡有鍵盤到得了的控制項做同一件事",
        ["the code-behind handles keys for it"] = "code-behind 替它處理按鍵",
        // Tab 與大小
        ["its style is outside the project"] = "樣式在專案外",
        ["Focusable or IsTabStop is set to something this tool cannot read"] = "Focusable 或 IsTabStop 設成這支讀不懂的值",
        ["Focusable or IsTabStop is bound"] = "Focusable 或 IsTabStop 是綁定的",
        ["a template part"] = "範本零件",
        ["read-only text"] = "唯讀文字",
        ["a window caption button (the system menu does the same)"] = "視窗標題列按鈕（系統選單做得到）",
        ["inside a list item template (the list may handle the keys)"] = "在清單項目的範本裡（清單可能處理按鍵）",
        ["only part of its size is set in the XAML"] = "XAML 只設了一部分的大小",
        ["it is inside a Viewbox or has a transform"] = "在 Viewbox 裡，或有變形",
        ["its neighbours' positions are not known (WCAG 2.5.8 allows small targets with enough space around them)"] = "隔壁的位置不知道（WCAG 2.5.8 允許旁邊夠空的小目標）",
    };

    private static readonly (Regex, string)[] Patterns =
    [
        (R("it has no (?<p>\\w+) and no AutomationProperties\\.Name"), "沒有 ${p}，也沒有 AutomationProperties.Name"),
        (R("it has no (?<p>\\w+) until a trigger sets one"), "觸發器成立之前沒有 ${p}"),
        (R("it has no (?<p>\\w+), so its name is the type name"), "沒有 ${p}，名稱是型別名"),
        (R("its content is only <(?<t>[^<>]+)>, which looks like an icon"), "內容只有 <${t}>，看起來是圖示"),
        (R("its content is <(?<t>[^<>]+)>, which gives no text"), "內容是 <${t}>，給不出文字"),
        (R("<(?<t>[^<>]+)> gives no text"), "<${t}> 給不出文字"),
        (R("the text is inside <(?<t>[^<>]+)>; WPF only takes the name from a string, a TextBlock or a ContentControl"), "字在 <${t}> 裡面，WPF 只從字串、TextBlock、ContentControl 拿名稱"),
        (R("its content is an icon \\((?<t>.+)\\)"), "內容是圖示（${t}）"),
        (R("its content is a (?<t>\\w+), so its name is the type name, a color code or a file path"), "內容是 ${t}，名稱是型別名、色碼或檔案路徑"),
        (R("its content is a (?<t>\\w+), whose text becomes the name"), "內容是 ${t}，它的文字會變成名稱"),
        (R("its content is bound \\((?<t>.+)\\)"), "內容是綁定的（${t}）"),
        (R("its content is (?<t>.+), which looks like an icon"), "內容是 ${t}，看起來是圖示"),
        (R("its content is (?<t>.+), which this tool cannot resolve"), "內容是 ${t}，這支解不出來"),
        (R("its content is <(?<t>[^<>]+)>, and this tool does not know what text that gives"), "內容是 <${t}>，這支不知道它給什麼文字"),
        (R("its content comes from the markup extension (?<t>.+)"), "內容來自標記延伸 ${t}"),
        (R("its text is drawn as an icon by the font '(?<t>.+)'; a screen reader reads the raw characters"), "文字用字型「${t}」畫成圖示，螢幕閱讀器念的是原本的字元"),
        (R("<Label Target> at line (?<n>\\d+) does not give it one \\(use AutomationProperties\\.LabeledBy\\)"), "第 ${n} 行的 <Label Target> 不會給它名稱（改用 AutomationProperties.LabeledBy）"),
        (R("the GroupBox header \"(?<t>.*)\" around it does not give it one"), "外面 GroupBox 的標題「${t}」不會給它名稱"),
        (R("if the (?<e>TextBlock|Label) \"(?<t>.*)\" at line (?<n>[0-9]+) is its label, use AutomationProperties\\.LabeledBy"), "第 ${n} 行的 ${e}「${t}」如果是它的標籤，用 AutomationProperties.LabeledBy 接起來"),
        (R("if the (?<e>TextBlock|Label) at line (?<n>[0-9]+) is its label, use AutomationProperties\\.LabeledBy"), "第 ${n} 行的 ${e} 如果是它的標籤，用 AutomationProperties.LabeledBy 接起來"),
        (R("<(?<t>[^<>]+)> has a Style this tool cannot read"), "<${t}> 的 Style 這支讀不懂"),
        (R("style '(?<k>.*)' is not defined in the project"), "樣式「${k}」不在專案裡"),
        (R("based on '(?<k>.*)', which is not defined in the project"), "BasedOn「${k}」，專案裡沒有"),
        (R("more than (?<n>[0-9]+) style definitions could apply"), "可能套到的樣式超過 ${n} 份"),
        (R("<(?<t>[^<>]+)> is not a control this tool knows"), "這支不認得 <${t}>"),
        (R("its accessible name is (?<t>.+)"), "名稱是讀不懂的東西（${t}）"),
        (R("the name is bound to (?<t>.+), which is an object"), "名稱綁到 ${t}，那是物件"),
        (R("the name is bound to (?<t>.+)"), "名稱綁到 ${t}"),
        (R("the name comes from (?<t>.+)"), "名稱來自 ${t}"),
        (R("its style is outside the project, so whether (?<p>.+) still names it from (?<t>.+) is unknown"), "樣式在專案外，${p} 還會不會拿 ${t} 當名稱不知道"),
        (R("(?<p>\\S+) names it from (?<t>\\S+) only where its theme is merged, and that was not found in the XAML"), "${p} 要合併了佈景才會拿 ${t} 當名稱，XAML 裡找不到"),
        (R("(?<p>\\S+ \\S+) names it from (?<t>\\S+); the project's (?<q>\\S+) version was not found"), "${p} 會拿 ${t} 當名稱；專案用的 ${q} 版本找不到"),
        (R("(?<p>\\S+ \\S+) names it from (?<t>\\S+); the project uses (?<v>.+), which was not measured"), "${p} 會拿 ${t} 當名稱；專案用的是 ${v}，沒量過"),
        (R("LabeledBy points at '(?<t>.*)', which is not in this file"), "LabeledBy 指到「${t}」，這個檔案裡沒有"),
        (R("LabeledBy points at <(?<t>[^<>]+)>, which this tool does not know"), "LabeledBy 指到 <${t}>，這支不認得"),
        (R("a trigger changes its (?<p>\\w+); some values name it and some do not"), "觸發器會改 ${p}，有的值有名稱、有的沒有"),
        (R("the code-behind sets its (?<p>\\w+)"), "code-behind 設了它的 ${p}"),
        (R("its class sets its (?<p>\\w+) in code"), "它的類別在程式碼裡設 ${p}"),
        (R("the root of (?<t>.+) is not a control this tool knows"), "${t} 的根不是這支認得的控制項"),
        (R("its style is outside the project and may set the (?<p>\\w+)"), "樣式在專案外，可能設了 ${p}"),
        (R("resource '(?<k>.*)' is not defined in the project"), "資源「${k}」不在專案裡"),
        (R("resource '(?<k>.*)' is defined differently in (?<n>\\d+) places"), "資源「${k}」有 ${n} 份，內容不一樣"),
        (R("defined differently in (?<n>\\d+) styles with the same key or target"), "同一個鍵或目標有 ${n} 份樣式，寫得不一樣"),
        (R("a part of a (?<t>\\S+) template \\(not exposed as a control of its own\\)"), "${t} 範本的零件（自己不算一個控制項）"),
        (R("not a control the keyboard goes to, or a base type \\((?<t>.+)\\)"), "鍵盤不會到的元素，或基底型別（${t}）"),
        (R("its (?<t>\\S+) template may show focus on its parts"), "${t} 的範本可能由零件表示焦點"),
        (R("the default (?<t>\\S+) template shows focus only through FocusVisualStyle"), "${t} 的預設範本只靠 FocusVisualStyle 表示焦點"),
        (R("the (?<t>\\S+) template of the theme '(?<n>.*)' shows focus only through FocusVisualStyle \\(measured\\)"), "佈景「${n}」的 ${t} 範本只靠 FocusVisualStyle 表示焦點（量過）"),
        (R("the template comes from the theme '(?<n>.*)', which was not measured for (?<t>\\S+)"), "範本來自佈景「${n}」，它的 ${t} 沒量過"),
        (R("the theme '(?<n>.*)' was only measured with (?<t>.+)"), "佈景「${n}」只量過 ${t}"),
        (R("the template comes from the theme '(?<n>.*)'; measured with (?<m>.+), the project uses an unknown version"), "範本來自佈景「${n}」；量的是 ${m}，專案用的版本不知道"),
        (R("the template comes from the theme '(?<n>.*)'; measured with (?<m>.+), the project uses (?<v>.+)"), "範本來自佈景「${n}」；量的是 ${m}，專案用的是 ${v}"),
        (R("(?<k>[a-z-]+) is turned off"), "${k} 關掉了"),
        (R("(?<k>[a-z-]+) is not in (?<s>.+)"), "${s} 沒有 ${k} 這一條"),
    ];
}
