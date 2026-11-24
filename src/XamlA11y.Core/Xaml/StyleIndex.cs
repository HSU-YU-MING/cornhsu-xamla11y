using System.Xml.Linq;

namespace XamlA11y.Core.Xaml;

/// <summary>屬性解析的三種結果。⚠ <see cref="Unreadable"/> 與 <see cref="NotSet"/> 一定要分開。</summary>
public enum ResolveKind { NotSet, Value, Unreadable }

/// <param name="Value">原始寫法（字面值或標記延伸）。資源引用**已經**解成資源的值（解得到的話）。</param>
/// <param name="Element">值是物件時：那個元素（&lt;Setter.Value&gt;、屬性元素、資源裡的物件）。</param>
/// <param name="Origin">設定它的地方（屬性所在的元素，或樣式的 Setter）。</param>
public readonly record struct Resolved(ResolveKind Kind, string? Value = null, XElement? Element = null, string? Why = null, XElement? Origin = null)
{
    public static readonly Resolved NotSet = new(ResolveKind.NotSet);
    public bool IsValue => Kind == ResolveKind.Value;
    public bool IsUnreadable => Kind == ResolveKind.Unreadable;
    public bool IsSet => Kind != ResolveKind.NotSet;
    public bool IsFalse => IsValue && string.Equals(Value?.Trim(), "False", StringComparison.OrdinalIgnoreCase);
    public bool IsTrue => IsValue && string.Equals(Value?.Trim(), "True", StringComparison.OrdinalIgnoreCase);
    public bool IsNull => IsValue && Value?.Trim() == "{x:Null}";
}

/// <summary>掛在 XDocument 上：這份文件來自哪個檔。</summary>
public sealed record SourceFile(string Path);

/// <summary>一個樣式。</summary>
public sealed class StyleRecord
{
    public required XElement Style { get; init; }
    public required string File { get; init; }
    public string? Key { get; init; }
    public string? TargetType { get; init; }
    /// <summary>TargetType 的識別（<see cref="Markup.TypeId"/>）：隱含樣式用它比對，不用區域名稱。</summary>
    public string? TargetId { get; init; }
    public string? BasedOnKey { get; init; }
    /// <summary>BasedOn 的型別識別。</summary>
    public string? BasedOnType { get; init; }
    public bool BasedOnUnreadable { get; init; }
    /// <summary>屬性名 → Setter。附加屬性留著擁有者（AutomationProperties.Name），自己的屬性只留最後一段。</summary>
    public required Dictionary<string, XElement> Setters { get; init; }
    /// <summary>資源字典檔或 App 裡的（不在語彙範圍內）。</summary>
    public bool IsGlobal { get; init; }

    public IEnumerable<XElement> Triggers =>
        Style.Elements().Where(e => e.Name.LocalName == "Style.Triggers").SelectMany(e => e.Elements());

    public IEnumerable<XElement> EventSetters => Style.Elements().Where(e => e.Name.LocalName == "EventSetter");
}

/// <summary>一條展開的樣式鏈：衍生的在前、基底在後。</summary>
/// <param name="Unknown">鏈的某一段看不到（基底在專案外…）：沒在已知的部分設定的屬性都是「不知道」。</param>
public sealed record StyleChain(IReadOnlyList<StyleRecord> Styles, string? Unknown)
{
    public static readonly StyleChain Empty = new([], null);

    public XElement? Setter(string property)
    {
        foreach (var s in Styles)
            if (s.Setters.TryGetValue(property, out var setter)) return setter;
        return null;
    }
}

public enum TemplateKind { Default, Custom, Unknown }

public readonly record struct TemplateInfo(TemplateKind Kind, XElement? Template, string? Why);

/// <summary>
/// 樣式、資源、範本的解析。演算法沿用 XamlFit 的 StyleIndex（作用域、同檔優先、同 App 優先、互相以型別為基底的隱含樣式），
/// 差別在這裡一次展開成「樣式鏈」：無障礙規則除了屬性值還要看觸發器、範本、EventSetter。
///
///   1. 語彙範圍：元素自己與祖先的 Resources，最近的贏。
///   2. 全域：根元素是 Application 或 ResourceDictionary 的檔案裡的頂層資源。同名的多個定義都是候選，**答案一致才採用**。
///   3. 都找不到的鍵：定義在專案外，值是「不知道」，不是「沒設定」。
/// </summary>
public sealed class StyleIndex
{
    private readonly Dictionary<string, List<StyleRecord>> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StyleRecord>> _implicit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string File, XElement Entry)>> _resources = new(StringComparer.Ordinal);
    private List<string> _appDirs = [];

    /// <summary>(樣式所在的字典, 元素所在的檔案) → 那本字典有沒有合併進來（由 Project 提供；null = 都看得到）。</summary>
    public Func<string, string, bool>? IsVisible { get; set; }

    /// <summary>專案自己的類別（x:Class）：它 XAML 的根元素 —— 根上寫的屬性是建構時設的本地值，用它的地方沒寫就輪到它。</summary>
    public Func<XElement, XElement?>? OwnRoot { get; set; }

    /// <summary>元素行為上是哪個 WPF 控制項（專案自己的類別照它的基底）。沒接的時候只認 WPF 的。</summary>
    public Func<XElement, string?>? BaseOf { get; set; }

    private string? ControlOf(XElement e) => BaseOf is { } f ? f(e) : Markup.IsWpf(e) ? e.Name.LocalName : null;

    /// <summary>
    /// 「Control.FontSize」「ButtonBase.Content」這種寫法的擁有者：屬性是元素自己的，前綴可以去掉。
    /// 其他擁有者都是附加屬性，一定要留著 —— HandyControl 的 <c>hc:IconElement.Height</c> 是圖示的高度，
    /// 截成 Height 之後變成「按鈕 12 高」（語料抓到 166 筆誤報）。
    /// </summary>
    private static readonly HashSet<string> OwnOwners = new(StringComparer.Ordinal)
    {
        "Control", "FrameworkElement", "FrameworkContentElement", "UIElement", "ContentElement", "ContentControl", "HeaderedContentControl",
        "ItemsControl", "HeaderedItemsControl", "ButtonBase", "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton",
        "TextBlock", "TextElement", "TextBoxBase", "TextBox", "PasswordBox", "RichTextBox", "ComboBox", "ComboBoxItem", "MenuItem", "ListBox", "ListBoxItem",
        "ListView", "ListViewItem", "TreeViewItem", "TabItem", "Expander", "Slider", "RangeBase", "Selector", "Label", "Shape", "Image", "Border",
        "Hyperlink", "Window", "Popup", "DatePicker", "UserControl", "Rectangle", "Ellipse", "Path",
    };

    /// <summary>同一個鍵展開出的候選鏈上限；超過就是不知道。</summary>
    private const int MaxChains = 32;

    // ───────────────────────── 建立 ─────────────────────────

    public void SetAppDirectories(IEnumerable<string> dirs) =>
        _appDirs = [.. dirs.Distinct(StringComparer.Ordinal).OrderByDescending(d => d.Length)];

    public string AppOf(string file) => _appDirs.FirstOrDefault(d => file.StartsWith(d, StringComparison.Ordinal)) ?? "";

    public void Add(XDocument doc, string file)
    {
        if (doc.Root is not { } root) return;
        if (root.Name.LocalName == "ResourceDictionary")
            foreach (var e in Flatten(root)) Index(e, file);
        else if (XamlFiles.IsApplication(root, file))
            foreach (var holder in root.Elements().Where(IsResources))
                foreach (var e in EntriesOf(holder)) Index(e, file);
    }

    private void Index(XElement entry, string file)
    {
        if (entry.Name.LocalName == "Style")
        {
            var r = Read(entry, file, isGlobal: true);
            entry.AddAnnotation(r);
            if (r.Key is not null) Append(_byKey, r.Key, r);
            else if (r.TargetId is not null) Append(_implicit, r.TargetId, r);
            return;
        }
        if (entry.Attribute(Markup.XKey)?.Value is { Length: > 0 } key) Append(_resources, key, (file, entry));
    }

    private static void Append<T>(Dictionary<string, List<T>> map, string key, T item)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(item);
    }

    private static bool IsResources(XElement e) => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal);

    private static IEnumerable<XElement> EntriesOf(XElement holder)
    {
        foreach (var child in holder.Elements())
        {
            if (child.Name.LocalName == "ResourceDictionary")
                foreach (var e in Flatten(child)) yield return e;
            else yield return child;
        }
    }

    private static IEnumerable<XElement> Flatten(XElement dictionary)
    {
        foreach (var child in dictionary.Elements())
        {
            var local = child.Name.LocalName;
            if (local.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
            {
                foreach (var merged in child.Elements().Where(m => m.Name.LocalName == "ResourceDictionary"))
                    foreach (var e in Flatten(merged)) yield return e;
                continue;
            }
            if (local.Contains('.', StringComparison.Ordinal)) continue;
            yield return child;
        }
    }

    /// <summary>「Control.FontSize」→ FontSize；「AutomationProperties.Name」留著。</summary>
    public static string NormalizeProperty(string property)
    {
        var dot = property.LastIndexOf('.');
        if (dot < 0) return property.Trim();
        var owner = property[..dot].Trim('(', ' ');
        var name = property[(dot + 1)..].Trim(')', ' ');
        if (OwnOwners.Contains(owner)) return name;
        if (owner == "KeyboardNavigation" && name == "IsTabStop") return name;      // Control.IsTabStop 就是它
        if (owner == "ToolTipService" && name == "ToolTip") return name;            // FrameworkElement.ToolTip 就是它（UiaOracle round3）
        var colon = owner.LastIndexOf(':');
        return (colon >= 0 ? owner[(colon + 1)..] : owner) + "." + name;
    }

    private static StyleRecord Read(XElement style, string file, bool isGlobal)
    {
        var setters = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var setter in style.Elements().Concat(style.Elements().Where(e => e.Name.LocalName == "Style.Setters").SelectMany(e => e.Elements()))
                     .Where(e => e.Name.LocalName == "Setter"))
            if (setter.Attribute("Property")?.Value is { } p) setters[NormalizeProperty(p)] = setter;

        var raw = style.Attribute("BasedOn")?.Value;
        var key = Markup.ResourceKey(raw);
        var type = key is null ? Markup.TypeId(style, Markup.TypeKey(raw)) : null;
        return new StyleRecord
        {
            Style = style,
            File = file,
            Key = KeyOf(style),
            TargetType = Markup.TypeName(style.Attribute("TargetType")?.Value),
            TargetId = Markup.TypeId(style, style.Attribute("TargetType")?.Value) ?? Markup.TypeId(style, Markup.TypeKey(style.Attribute(Markup.XKey)?.Value)),
            BasedOnKey = key,
            BasedOnType = type,
            BasedOnUnreadable = (raw is not null && key is null && type is null)
                                || style.Elements().Any(e => e.Name.LocalName == "Style.BasedOn"),
            Setters = setters,
            IsGlobal = isGlobal,
        };
    }

    /// <summary>樣式的鍵。x:Key="{x:Type Button}" 等於沒有鍵（隱含樣式）。</summary>
    private static string? KeyOf(XElement entry)
    {
        var key = entry.Attribute(Markup.XKey)?.Value;
        return key is null || Markup.TypeKey(key) is not null ? null : key;
    }

    public static StyleRecord RecordOf(XElement style)
    {
        if (style.Annotation<StyleRecord>() is { } cached) return cached;
        var r = Read(style, FileOf(style), isGlobal: false);
        style.AddAnnotation(r);
        return r;
    }

    public static string FileOf(XElement e) => e.Document?.Annotation<SourceFile>()?.Path ?? "";

    // ───────────────────────── 語彙範圍 ─────────────────────────

    private sealed class Scope
    {
        public static readonly Scope Empty = new();
        public Dictionary<string, XElement> ByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, XElement> Implicit { get; } = new(StringComparer.Ordinal);
    }

    private static Scope ScopeOf(XElement owner)
    {
        if (owner.Annotation<Scope>() is { } cached) return cached;
        var scope = Scope.Empty;
        foreach (var holder in owner.Elements().Where(IsResources))
            foreach (var entry in EntriesOf(holder))
            {
                if (ReferenceEquals(scope, Scope.Empty)) scope = new Scope();
                if (entry.Name.LocalName == "Style" && KeyOf(entry) is null)
                {
                    if (RecordOf(entry).TargetId is { } type) scope.Implicit[type] = entry;
                }
                else if (entry.Attribute(Markup.XKey)?.Value is { Length: > 0 } key) scope.ByKey[key] = entry;
            }
        owner.AddAnnotation(scope);
        return scope;
    }

    private static XElement? LexicalResource(XElement from, string key)
    {
        for (XElement? cur = from; cur is not null; cur = cur.Parent)
            if (ScopeOf(cur).ByKey.TryGetValue(key, out var found)) return found;
        return null;
    }

    private static XElement? LexicalImplicit(XElement from, string type, XElement? skip)
    {
        for (XElement? cur = from; cur is not null; cur = cur.Parent)
            if (ScopeOf(cur).Implicit.TryGetValue(type, out var found) && !ReferenceEquals(found, skip)) return found;
        return null;
    }

    private List<T> Nearest<T>(List<T> all, Func<T, string> fileOf, string file)
    {
        if (all.Count <= 1) return all;
        var same = all.Where(s => fileOf(s) == file).ToList();
        if (same.Count > 0) return same;
        if (_appDirs.Count > 0)
        {
            var app = AppOf(file);
            var sameApp = all.Where(s => AppOf(fileOf(s)) == app).ToList();
            if (sameApp.Count > 0) return sameApp;
        }
        return all;
    }

    // ───────────────────────── 資源 ─────────────────────────

    /// <summary>從 <paramref name="from"/> 的位置看得到的資源元素（樣式也算）。多個候選時全部回傳；找不到回空清單。</summary>
    public IReadOnlyList<XElement> FindResources(XElement from, string key)
    {
        if (LexicalResource(from, key) is { } lexical) return [lexical];
        var file = FileOf(from);
        var list = new List<XElement>();
        if (_resources.TryGetValue(key, out var all)) list.AddRange(Nearest(all, x => x.File, file).Select(x => x.Entry));
        if (_byKey.TryGetValue(key, out var styles)) list.AddRange(Nearest(styles, s => s.File, file).Select(s => s.Style));
        return list;
    }

    private List<StyleRecord>? StylesByKey(XElement from, string key)
    {
        if (LexicalResource(from, key) is { } lexical)
            return lexical.Name.LocalName == "Style" ? [RecordOf(lexical)] : null;
        return _byKey.TryGetValue(key, out var all) ? Nearest(all, s => s.File, FileOf(from)) : null;
    }

    private List<StyleRecord> ImplicitStyles(XElement from, string type, StyleRecord? self)
    {
        if (LexicalImplicit(from, type, self?.Style) is { } lexical) return [RecordOf(lexical)];
        if (!_implicit.TryGetValue(type, out var declared)) return [];
        // 沒有任何 XAML 或程式碼把它合併進來的字典：裡面的隱含樣式套不到任何人
        var here = FileOf(from);
        var all = declared.Where(s => s.File == here || (IsVisible?.Invoke(s.File, here) ?? true)).ToList();
        var others = self is null ? all : all.Where(s => !ReferenceEquals(s, self)).ToList();
        // N 份全域隱含樣式互相以型別為基底：它們是同一個位置的不同版本，彼此不是基底（不限制的話路徑數是 N 的階乘）
        if (self is not null && BasesItself(self)) others = [.. others.Where(s => !BasesItself(s))];
        return others.Count == 0 ? [] : Nearest(others, s => s.File, FileOf(from));
    }

    private static bool BasesItself(StyleRecord r) => r.IsGlobal && r.Key is null && r.BasedOnType is not null && r.BasedOnType == r.TargetId;

    // ───────────────────────── 樣式鏈 ─────────────────────────

    private sealed record ChainCache(IReadOnlyList<StyleChain> Chains, bool HasExplicit);

    /// <summary>
    /// 會套到這個元素身上的樣式鏈（同名定義有好幾份時是好幾條）。明寫的 Style 整個取代隱含樣式。
    /// 帶 x:Class 的根元素是子類別：只有 Themes/Generic.xaml 的隱含樣式套得到。
    /// </summary>
    public IReadOnlyList<StyleChain> ChainsOf(XElement element)
    {
        if (element.Annotation<ChainCache>() is { } cached) return cached.Chains;
        var chains = ComputeChains(element);
        element.AddAnnotation(new ChainCache(chains, true));
        return chains;
    }

    private List<StyleChain> ComputeChains(XElement element)
    {
        var local = element.Name.LocalName;
        if (element.Attribute("Style")?.Value is { } raw)
        {
            if (raw.Trim() == "{x:Null}") return [StyleChain.Empty];
            if (Markup.ResourceKey(raw) is not { } key)
                return Markup.ExtensionName(raw) is "StaticResource" or "DynamicResource" && Markup.TypeId(element, Markup.TypeKey(raw)) is { } type
                    ? Expand(ImplicitStyles(element, type, null), element)
                    : [new StyleChain([], $"<{local}> has a Style this tool cannot read")];
            return StylesByKey(element, key) is { } styles
                ? Expand(styles, element)
                : [new StyleChain([], $"style '{key}' is not defined in the project")];
        }

        // <Button.Style>，也可以寫成基底的名字：<FrameworkElement.Style>（ILSpy 反編譯出來的 BAML 就是）
        if (element.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal)) is { } holder)
            return holder.Elements().FirstOrDefault(e => e.Name.LocalName == "Style") is { } inline
                ? Expand([RecordOf(inline)], element)
                : [new StyleChain([], $"<{local}> has a Style this tool cannot read")];

        // 清單的 ItemContainerStyle：直接寫在裡面的項目也會套，而且贏過隱含樣式（UiaOracle round3）
        if (ItemsOwner(element) is { } list)
        {
            var r = Resolve(list, "ItemContainerStyle");
            if (r.Element is { } itemStyle && itemStyle.Name.LocalName == "Style") return Expand([RecordOf(itemStyle)], element);
            if ((r.IsValue && !r.IsNull) || (r.IsUnreadable && r.Origin is not null))
                return [new StyleChain([], "the ItemContainerStyle of its list is something this tool cannot read")];
        }

        // ToolBar 的直接子元素：隱含樣式被 ToolBar.ButtonStyleKey 那一組取代（明寫的 Style 不會）
        if (ToolBarStyleKey(element) is { } toolBar)
            return _byKey.Keys.Concat(_resources.Keys).FirstOrDefault(k => k.Contains(toolBar, StringComparison.Ordinal)) is { } own && StylesByKey(element, own) is { } replaced
                ? Expand(replaced, element)
                : LexicalToolBar(element, toolBar) is { } lexical ? Expand([RecordOf(lexical)], element) : [StyleChain.Empty];

        if (element.Attribute(Markup.XClass) is not null)
            return _implicit.TryGetValue(Markup.TypeIdOf(element), out var all)
                ? Expand(Nearest([.. all.Where(s => IsThemeStyleFile(s.File))], s => s.File, FileOf(element)), element)
                : [StyleChain.Empty];

        return Expand(ImplicitStyles(element, Markup.TypeIdOf(element), null), element);
    }

    public bool HasKeyContaining(string part) => _byKey.Keys.Any(k => k.Contains(part, StringComparison.Ordinal));

    private static XElement? VisualParent(XElement e) =>
        e.Parent is { } p && Markup.IsPropertyElement(p) && p.Name.LocalName.EndsWith(".Items", StringComparison.Ordinal) ? p.Parent : e.Parent;

    /// <summary>直接放在清單裡、自己就是容器的項目 → 那個清單。巢狀的 TreeViewItem 看上一層的 TreeViewItem（不會往下傳，實測）。</summary>
    private XElement? ItemsOwner(XElement e)
    {
        if (VisualParent(e) is not { } p || Markup.IsPropertyElement(p) || ControlOf(p) is not { } owner) return null;
        var item = ControlOf(e);
        return (owner, item) switch
        {
            ("TabControl", "TabItem") or ("ListBox", "ListBoxItem") or ("ListView", "ListViewItem") or ("ComboBox", "ComboBoxItem")
                or ("Menu" or "ContextMenu" or "MenuItem", "MenuItem") or ("TreeView" or "TreeViewItem", "TreeViewItem") or ("StatusBar", "StatusBarItem") => p,
            _ => null,
        };
    }

    /// <summary>ToolBar.PrepareContainerForItemOverride 認的型別（WPF 比的是確切的型別，子類別不算）。</summary>
    private string? ToolBarStyleKey(XElement e)
    {
        if (!Markup.IsWpf(e)) return null;
        var local = e.Name.LocalName;
        // 工具列裡的 Menu：MenuStyleKey 的樣式自己帶了 MenuItem 的樣式
        if (local == "MenuItem")
        {
            var top = e.Ancestors().FirstOrDefault(a => !Markup.IsPropertyElement(a) && a.Name.LocalName != "MenuItem");
            return top is { Name.LocalName: "Menu" } && VisualParent(top) is { } bar && ControlOf(bar) == "ToolBar" ? "ToolBar.MenuStyleKey/MenuItem" : null;
        }
        return local is "Button" or "CheckBox" or "ComboBox" or "Menu" or "RadioButton" or "Separator" or "TextBox" or "ToggleButton"
               && VisualParent(e) is { } p && !Markup.IsPropertyElement(p) && ControlOf(p) == "ToolBar" ? $"ToolBar.{local}StyleKey" : null;
    }

    /// <summary>直接放在 ToolBar 裡（XAML 看得到的）。</summary>
    public static bool InToolBar(XElement e) => VisualParent(e) is { Name.LocalName: "ToolBar" };

    private static XElement? LexicalToolBar(XElement from, string key)
    {
        for (XElement? cur = from; cur is not null; cur = cur.Parent)
            foreach (var (k, entry) in ScopeOf(cur).ByKey)
                if (k.Contains(key, StringComparison.Ordinal) && entry.Name.LocalName == "Style") return entry;
        return null;
    }

    /// <summary>
    /// 這個元素的樣式有沒有疊在佈景的隱含樣式上。專案自己的樣式沒寫 BasedOn、或 Style="{x:Null}" 時，佈景的整個被取代
    /// （UiaOracle check-theme-focus、themes「hint, …」）。null = 不知道。
    /// </summary>
    public bool? KeepsThemeStyle(XElement element)
    {
        bool? answer = null;
        foreach (var chain in ChainsOf(element))
        {
            bool? one = (string?)element.Attribute("Style") == "{x:Null}" ? false : KeepsThemeStyle(chain);
            if (one is null) return null;
            if (answer is { } a && a != one) return null;
            answer = one;
        }
        return answer;
    }

    public static bool? KeepsThemeStyle(StyleChain chain) =>
        chain.Unknown is not null ? null : chain.Styles.Count == 0 || chain.Styles[^1].BasedOnType is not null;

    private static bool IsThemeStyleFile(string file) =>
        file.EndsWith("Themes/Generic.xaml", StringComparison.OrdinalIgnoreCase) || file.Equals("Generic.xaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>一個樣式自己的鏈（不看誰用它）。</summary>
    public IReadOnlyList<StyleChain> ChainsOfStyle(StyleRecord style) => Expand([style], style.Style);

    private List<StyleChain> Expand(List<StyleRecord> styles, XElement from)
    {
        if (styles.Count == 0) return [StyleChain.Empty];
        var result = new List<StyleChain>();
        foreach (var s in styles)
        {
            Walk(s, from, [], new HashSet<object>(ReferenceEqualityComparer.Instance), result);
            if (result.Count > MaxChains) return [new StyleChain([], $"more than {MaxChains} style definitions could apply")];
        }
        return result;
    }

    private void Walk(StyleRecord s, XElement from, List<StyleRecord> path, HashSet<object> seen, List<StyleChain> result)
    {
        if (result.Count > MaxChains) return;
        if (path.Count > 16) { result.Add(new StyleChain([.. path], "the BasedOn chain is too deep")); return; }
        if (!seen.Add(s.Style)) { result.Add(new StyleChain([.. path], null)); return; }   // 繞回來了：沒有更多可看的

        List<StyleRecord> here = [.. path, s];
        if (s.BasedOnUnreadable) { result.Add(new StyleChain(here, "a style in the chain has a BasedOn this tool cannot read")); return; }

        List<StyleRecord> bases;
        if (s.BasedOnKey is { } key)
        {
            if (StylesByKey(s.Style, key) is not { } found)
            {
                result.Add(new StyleChain(here, $"based on '{key}', which is not defined in the project"));
                return;
            }
            bases = [.. found.Where(b => !ReferenceEquals(b.Style, s.Style))];
        }
        else if (s.BasedOnType is { } type) bases = ImplicitStyles(s.Style, type, s);
        else bases = [];

        if (bases.Count == 0) { result.Add(new StyleChain(here, null)); return; }
        foreach (var b in bases) Walk(b, s.Style, here, new HashSet<object>(seen, ReferenceEqualityComparer.Instance), result);
    }

    // ───────────────────────── 屬性 ─────────────────────────

    /// <summary>元素身上這個屬性的生效值：本地（屬性、屬性元素）&gt; 樣式鏈。不看觸發器。</summary>
    public Resolved Resolve(XElement element, string property)
    {
        var local = LocalValue(element, property);
        if (local.IsSet) return local;
        if (OwnRoot?.Invoke(element) is { } root && !ReferenceEquals(root, element) && LocalValue(root, property) is { IsSet: true } own) return own;

        var chains = ChainsOf(element);
        Resolved? first = null;
        foreach (var chain in chains)
        {
            Resolved r;
            if (chain.Setter(property) is { } setter) r = SetterValue(setter);
            else if (chain.Unknown is { } why) r = new Resolved(ResolveKind.Unreadable, Why: why);
            else r = Resolved.NotSet;

            if (first is null) first = r;
            // 都沒設，只是有的鏈看得到底、有的看不到底：合起來是「不知道設不設」，不是「設得不一樣」
            else if (Undecided(first.Value) && Undecided(r))
            {
                if (r.IsUnreadable) first = r;
            }
            else if (first.Value.Kind != r.Kind || first.Value.Value != r.Value || first.Value.Element?.Name.LocalName != r.Element?.Name.LocalName)
                // Origin 不是 null：有人設了、只是答案不一致（跟「樣式在專案外、不知道設不設」分開）
                return new Resolved(ResolveKind.Unreadable, Why: $"defined differently in {chains.Count} styles with the same key or target", Origin: element);
        }
        return first ?? Resolved.NotSet;
    }

    private static bool Undecided(Resolved r) => r.Kind == ResolveKind.NotSet || r is { IsUnreadable: true, Origin: null };

    public Resolved LocalValue(XElement element, string property)
    {
        var dotted = property.Contains('.', StringComparison.Ordinal);
        XAttribute? attr = null;
        foreach (var a in element.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            var n = a.Name.LocalName;
            if (a.Name.NamespaceName == Markup.XamlNamespace) continue;
            // 沒有擁有者的屬性不會有前綴；有前綴又沒有點的是別的東西（設計期、標記相容性）
            if (!dotted && a.Name.NamespaceName.Length > 0) continue;
            if (n == property || (n.Contains('.', StringComparison.Ordinal) && n.EndsWith(property, StringComparison.Ordinal) && NormalizeProperty(n) == property))
            { attr = a; break; }
        }
        if (attr is not null) return ValueOf(attr.Value, element, element);
        if (Markup.PropertyElement(element, property) is { } holder) return ObjectValue(holder, element);
        return Resolved.NotSet;
    }

    public Resolved SetterValue(XElement setter)
    {
        if (setter.Attribute("Value")?.Value is { } raw) return ValueOf(raw, setter, setter);
        if (setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value") is { } holder) return ObjectValue(holder, setter);
        return new Resolved(ResolveKind.Unreadable, Why: "a Setter without a Value", Origin: setter);
    }

    private static Resolved ObjectValue(XElement holder, XElement origin)
    {
        var content = Markup.ContentElements(holder);
        if (content.Count == 0)
        {
            var text = Markup.OwnText(holder);
            return new Resolved(ResolveKind.Value, text, null, null, origin);
        }
        // <Binding Path="X"/>、<MultiBinding>…、<DynamicResource ResourceKey="K"/>：還原成標記寫法
        var only = content[0];
        if (only.Name.LocalName is "Binding" or "MultiBinding" or "PriorityBinding" or "TemplateBinding")
            return new Resolved(ResolveKind.Value, only.Name.LocalName == "Binding" && (string?)only.Attribute("Path") is { } p ? $"{{Binding {p}}}" : $"{{{only.Name.LocalName}}}", null, null, origin);
        if (only.Name.LocalName == "Null" && only.Name.NamespaceName == Markup.XamlNamespace)
            return new Resolved(ResolveKind.Value, "{x:Null}", null, null, origin);
        return new Resolved(ResolveKind.Value, null, only, null, origin);
    }

    /// <summary>屬性的原始寫法 → 值。資源引用解開（一致才採用）；其他標記延伸原樣留著給規則判斷。</summary>
    private Resolved ValueOf(string raw, XElement from, XElement origin, int depth = 0)
    {
        if (Markup.ExtensionName(raw) is not ("StaticResource" or "DynamicResource" or "ThemeResource"))
            return new Resolved(ResolveKind.Value, raw, null, null, origin);
        if (Markup.ResourceKey(raw) is not { } key)
            return Markup.TypeKey(raw) is null
                ? new Resolved(ResolveKind.Unreadable, raw, null, "a resource key this tool cannot read", origin)
                : new Resolved(ResolveKind.Unreadable, raw, null, "a resource with a type key", origin);

        var found = FindResources(from, key);
        if (found.Count == 0)
            return new Resolved(ResolveKind.Unreadable, raw, null, $"resource '{key}' is not defined in the project", origin);

        // 有文字值的資源（String、Boolean、Double、FontFamily…）→ 文字；物件 → 元素
        Resolved One(XElement e) =>
            e.Name.LocalName == "String" && !e.HasElements
                ? new Resolved(ResolveKind.Value, Markup.IsMarkup(e.Value) ? "{}" + e.Value : e.Value, null, null, origin)
            : !e.HasElements && e.Name.LocalName is not ("Style" or "ControlTemplate" or "DataTemplate") && !string.IsNullOrWhiteSpace(e.Value) && !LooksLikeObject(e)
                ? new Resolved(ResolveKind.Value, Markup.IsMarkup(e.Value) ? "{}" + e.Value : e.Value, null, null, origin)
                : new Resolved(ResolveKind.Value, null, e, null, origin);

        var first = One(found[0]);
        if (found.Count > 1 && first.Element is null && found.Skip(1).Select(One).Any(o => o.Value != first.Value || o.Element is not null))
        {
            // 每個語言一份的字串字典：值不同但都是字串 → 有值（取第一份）
            if (found.All(f => f.Name.LocalName == "String")) return first;
            return new Resolved(ResolveKind.Unreadable, raw, null, $"resource '{key}' is defined differently in {found.Count} places", origin);
        }
        // 同名的物件：種類一樣（深淺佈景各一張圖）就當成一樣，不一樣（一份 TextBlock、一份 Path）是不知道
        if (first.Element is { } kind && found.Skip(1).Any(f => f.Name.LocalName != kind.Name.LocalName))
            return new Resolved(ResolveKind.Unreadable, raw, null, $"resource '{key}' is defined differently in {found.Count} places", origin);
        return first;
    }

    /// <summary>資源裡有文字、但文字是建構物件用的（Geometry 的路徑語法、TextBlock 的內容）：要當成物件看。</summary>
    private static bool LooksLikeObject(XElement e) => e.Name.LocalName is
        "Geometry" or "PathGeometry" or "StreamGeometry" or "Color" or "TextBlock" or "Button" or "Label" or "BitmapImage" or "AccessText" or "Run" or "Hyperlink";

    // ───────────────────────── 範本 ─────────────────────────

    /// <summary>這個元素用的控制項範本。好幾條鏈時全部回傳。</summary>
    public IReadOnlyList<TemplateInfo> TemplatesOf(XElement element)
    {
        var local = LocalValue(element, "Template");
        if (local.IsSet) return [FromValue(local)];
        return [.. ChainsOf(element).Select(TemplateOfChain).Distinct()];
    }

    public TemplateInfo TemplateOfChain(StyleChain chain)
    {
        if (chain.Setter("Template") is { } setter) return FromValue(SetterValue(setter));
        return chain.Unknown is { } why ? new TemplateInfo(TemplateKind.Unknown, null, why) : new TemplateInfo(TemplateKind.Default, null, null);
    }

    private static TemplateInfo FromValue(Resolved r)
    {
        if (r.Element is { } e && e.Name.LocalName == "ControlTemplate") return new TemplateInfo(TemplateKind.Custom, e, null);
        return new TemplateInfo(TemplateKind.Unknown, null, r.Why ?? "the Template is not a ControlTemplate this tool can read");
    }
}
