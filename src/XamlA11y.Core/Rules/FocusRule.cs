using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// R3：FocusVisualStyle 關掉了，而且沒有別的東西畫出焦點。
///
/// 報在「關掉它的那一行」（樣式的 Setter 或元素），不是報在每一個用到樣式的元素上 —— 修的地方只有一個。
/// 樣式要看**實際用到它的元素**（元素可能自己換了範本、把焦點框加回來、或根本不能聚焦），以及沿用它的衍生樣式
/// （可能補了觸發器或範本）。找不到元素用它（控制項庫、ItemContainerStyle）才看樣式自己。
/// </summary>
internal static class FocusRule
{
    /// <summary>預設範本在焦點框關掉之後完全看不出焦點的控制項（實測，docs/M1-UIA實測.md 第 5 節）。</summary>
    private static readonly HashSet<string> Eligible = new(StringComparer.Ordinal)
    { "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "ComboBox", "Slider", "Hyperlink" };

    /// <summary>焦點移動就會選取：看得到的是選取的樣子。</summary>
    private static readonly HashSet<string> SelectionItems = new(StringComparer.Ordinal)
    { "ListBoxItem", "ListViewItem", "TreeViewItem", "TabItem", "ComboBoxItem", "DataGridCell", "DataGridRow", "DataGridRowHeader", "DataGridColumnHeader" };

    /// <summary>預設範本自己就看得到焦點（插入點、框線、反白；Expander 與 DatePicker 的焦點在內部零件上）。</summary>
    private static readonly HashSet<string> ShowsByItself = new(StringComparer.Ordinal)
    { "TextBox", "PasswordBox", "RichTextBox", "MenuItem", "Expander", "DatePicker" };

    /// <summary>改了也看不出來的屬性。</summary>
    private static readonly HashSet<string> NonVisual = new(StringComparer.Ordinal)
    {
        "Cursor", "ToolTip", "Tag", "FocusVisualStyle", "IsTabStop", "Focusable", "Panel.ZIndex", "ZIndex", "SnapsToDevicePixels",
        "OverridesDefaultStyle", "IsHitTestVisible", "AutomationProperties.Name", "AutomationProperties.HelpText", "KeyboardNavigation.IsTabStop", "CaretBrush",
    };

    private enum Seen { Yes, No, Unknown, Skip }

    public static void Check(Context ctx)
    {
        var styles = new List<StyleRecord>();
        var usedKeys = new Dictionary<string, (int All, int InTemplates)>(StringComparer.Ordinal);
        void Use(string key, XElement by)
        {
            var (all, parts) = usedKeys.GetValueOrDefault(key);
            usedKeys[key] = (all + 1, parts + (by.Name.LocalName != "Setter" && Context.ControlTemplateOf(by) is not null ? 1 : 0));
        }
        foreach (var doc in ctx.Project.Docs)
            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                if (e.Name.LocalName == "Style") styles.Add(StyleIndex.RecordOf(e));
                foreach (var a in e.Attributes())
                    if (a.Name.LocalName.EndsWith("Style", StringComparison.Ordinal) && a.Name.LocalName != "BasedOn" && Markup.ResourceKey(a.Value) is { } k) Use(k, e);
                if (e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") is { } p && p.EndsWith("Style", StringComparison.Ordinal) && Markup.ResourceKey((string?)e.Attribute("Value")) is { } sk) Use(sk, e);
            }

        // 樣式 → 直接用它的元素（明寫的 Style 或隱含樣式）與那一條鏈
        var users = new Dictionary<StyleRecord, List<(XElement Element, StyleChain Chain)>>(ReferenceEqualityComparer.Instance);
        foreach (var doc in ctx.Project.Docs)
            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                if (Markup.IsPropertyElement(e) || !ctx.IsElement(e) || e.Parent is null) continue;
                foreach (var chain in ctx.Project.Styles.ChainsOf(e))
                {
                    if (chain.Styles.Count == 0) continue;
                    if (!users.TryGetValue(chain.Styles[0], out var list)) users[chain.Styles[0]] = list = [];
                    list.Add((e, chain));
                }
            }

        var chains = styles.ToDictionary(s => s, s => ctx.Project.Styles.ChainsOfStyle(s));
        foreach (var s in styles) CheckStyle(ctx, s, styles, chains, usedKeys, users);

        foreach (var doc in ctx.Project.Docs)
            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                if (Markup.IsPropertyElement(e) || !ctx.IsElement(e)) continue;
                var local = ctx.Project.Styles.LocalValue(e, "FocusVisualStyle");
                if (local.IsSet) CheckElement(ctx, doc, e, local);
            }
    }

    // ───────────────────────── 樣式 ─────────────────────────

    private static void CheckStyle(Context ctx, StyleRecord s, List<StyleRecord> all, Dictionary<StyleRecord, IReadOnlyList<StyleChain>> chains,
        Dictionary<string, (int All, int InTemplates)> usedKeys, Dictionary<StyleRecord, List<(XElement Element, StyleChain Chain)>> users)
    {
        if (!s.Setters.TryGetValue("FocusVisualStyle", out var setter)) return;
        if (Removed(ctx, ctx.Project.Styles.SetterValue(setter)) is not { } removed) { ctx.Blocked("R3", setter, "FocusVisualStyle is set to a style outside the project"); return; }
        if (!removed) { ctx.Pass("R3", setter); return; }

        bool Applied(StyleRecord r, bool hasDerived) => r.Key is null || usedKeys.ContainsKey(r.Key) || !hasDerived;
        var derived = all.Where(d => d != s && chains[d].Any(c => c.Styles.Contains(s) && c.Setter("FocusVisualStyle") == setter)).ToList();
        var leaves = new List<StyleRecord>();
        if (Applied(s, derived.Count > 0)) leaves.Add(s);
        leaves.AddRange(derived.Where(d => Applied(d, all.Any(x => x != d && chains[x].Any(c => c.Styles.Contains(d))))));

        string? unknown = null, skipped = null;
        var passed = false;
        foreach (var leaf in leaves)
        {
            var uses = users.GetValueOrDefault(leaf);
            var answers = uses is { Count: > 0 }
                ? uses.Where(u => u.Chain.Setter("FocusVisualStyle") == setter).Take(500).Select(u => JudgeUse(ctx, u.Element, u.Chain))
                : Alone(ctx, leaf, chains[leaf].Where(c => c.Setter("FocusVisualStyle") == setter), usedKeys);
            foreach (var (seen, why) in answers)
                switch (seen)
                {
                    case Seen.No:
                        var name = leaf == s ? "" : $" (as used by the style {Describe(leaf)})";
                        ctx.Report(Kinds.FocusNotVisible, setter, $"The style {Describe(s)} removes FocusVisualStyle and nothing else shows keyboard focus{name}: {why}",
                            identity: "style=" + (s.Key ?? s.TargetType));
                        return;
                    case Seen.Unknown: unknown ??= why; break;
                    case Seen.Yes: passed = true; break;
                    default: skipped ??= why; break;
                }
        }

        if (unknown is not null) ctx.Blocked("R3", setter, unknown, unknown == UnknownControl ? s.TargetType : null);
        else if (passed) ctx.Pass("R3", setter);
        else ctx.NotJudged("R3", setter, skipped ?? "a style that nothing uses");
    }

    private const string UnknownControl = "a control this tool does not know (declare it in treatAs)";

    /// <summary>一個元素套了這條鏈之後看不看得到焦點。</summary>
    private static (Seen, string?) JudgeUse(Context ctx, XElement e, StyleChain chain)
    {
        var styles = ctx.Project.Styles;
        if (styles.LocalValue(e, "FocusVisualStyle").IsSet) return (Seen.Skip, "the element sets its own FocusVisualStyle");
        if (Context.ControlTemplateOf(e) is not null) return (Seen.Skip, "a template part (the whole control may show focus)");
        if (styles.Resolve(e, "Focusable").IsFalse || styles.Resolve(e, "IsTabStop").IsFalse) return (Seen.Skip, "it cannot take keyboard focus");
        if (ctx.Project.BaseOf(e) is not { } control)
            return NameRules.LooksInteractive(e.Name.LocalName) || NameRules.LooksInput(e.Name.LocalName) ? (Seen.Unknown, UnknownControl) : (Seen.Skip, "not a control the keyboard goes to");
        if (control == "ComboBox" && styles.Resolve(e, "IsEditable").IsTrue) return (Seen.Yes, null);
        return Judge(ctx, chain, e, control, StyleIndex.FileOf(e));
    }

    /// <summary>沒有元素直接用它（ItemContainerStyle、控制項庫給別人用的樣式）：只看樣式自己。</summary>
    private static IEnumerable<(Seen, string?)> Alone(Context ctx, StyleRecord leaf, IEnumerable<StyleChain> chains, Dictionary<string, (int All, int InTemplates)> usedKeys)
    {
        foreach (var chain in chains)
        {
            // 只給範本零件用的樣式：看得到焦點的是整個控制項（WPF-UI 註明 Focus by parent element）
            if (leaf.Key is { } key && usedKeys.TryGetValue(key, out var use) && use.All == use.InTemplates) yield return (Seen.Skip, "a style for template parts");
            else if (chain.Setter("Focusable") is { } f && ctx.Project.Styles.SetterValue(f).IsFalse) yield return (Seen.Skip, "it cannot take keyboard focus");
            else if (chain.Setter("IsTabStop") is { } t && ctx.Project.Styles.SetterValue(t).IsFalse) yield return (Seen.Skip, "it cannot take keyboard focus");
            else if (leaf.TargetType is null) yield return (Seen.Skip, "a style without a target type that nothing uses");
            else if (ctx.Project.ControlOfStyle(leaf) is { } control) yield return Judge(ctx, chain, null, control, leaf.File.Length > 0 ? leaf.File : StyleIndex.FileOf(leaf.Style));
            else yield return NameRules.LooksInteractive(leaf.TargetType) || NameRules.LooksInput(leaf.TargetType) ? (Seen.Unknown, UnknownControl) : (Seen.Skip, "not a control the keyboard goes to");
        }
    }

    /// <summary>控制項的種類 + 樣式鏈 + 範本 → 看不看得到焦點。</summary>
    private static (Seen, string?) Judge(Context ctx, StyleChain chain, XElement? element, string control, string file)
    {
        // 插入點本身就看得到，範本換成什麼都一樣
        if (control is "TextBox" or "PasswordBox" or "RichTextBox") return (Seen.Yes, null);
        if (SelectionItems.Contains(control)) return (Seen.Skip, "a list item or tab (the selection shows where focus is)");
        if (!Eligible.Contains(control) && !ShowsByItself.Contains(control)) return (Seen.Skip, $"not a control the keyboard goes to, or a base type ({control})");

        // 觸發器會關掉焦點（DataGrid 裡的 CheckBox：不在編輯時不能聚焦，看得到的是儲存格）
        if (chain.Styles.Any(x => x.Triggers.SelectMany(t => t.Descendants()).Any(d => d.Name.LocalName == "Setter" && (string?)d.Attribute("Property") is "Focusable" or "IsTabStop")))
            return (Seen.Unknown, "a trigger changes whether it can take focus");

        var (seen, why) = ShowsFocus(ctx, chain, element, control, file);
        if (!ShowsByItself.Contains(control) || seen != Seen.No) return (seen, why);

        // MenuItem、Expander、DatePicker：WPF 的範本自己看得到（反白、內部零件的焦點框）；專案換過的範本看不出來
        return Templates(ctx, chain, element).Kind == TemplateKind.Default ? (Seen.Yes, null) : (Seen.Unknown, $"its {control} template may show focus on its parts");
    }

    private static string Describe(StyleRecord s) => s.Key is { } k ? $"'{k}'" : $"for {s.TargetType}";

    // ───────────────────────── 元素 ─────────────────────────

    private static void CheckElement(Context ctx, XamlDoc doc, XElement e, Resolved value)
    {
        if (e.Parent is null) return;       // x:Class 的根是定義，用它的地方才判
        if (Removed(ctx, value) is not { } removed) { ctx.Blocked("R3", e, "FocusVisualStyle is set to a style outside the project"); return; }
        if (!removed) { ctx.Pass("R3", e); return; }

        var styles = ctx.Project.Styles;
        if (ctx.Project.BaseOf(e) is not { } control)
        {
            if (NameRules.LooksInteractive(e.Name.LocalName) || NameRules.LooksInput(e.Name.LocalName)) ctx.Blocked("R3", e, UnknownControl, e.Name.LocalName);
            else ctx.NotJudged("R3", e, "not a control the keyboard goes to");
            return;
        }
        if (styles.Resolve(e, "Focusable").IsFalse || styles.Resolve(e, "IsTabStop").IsFalse) { ctx.NotJudged("R3", e, "it cannot take keyboard focus"); return; }
        if (control == "ComboBox" && styles.Resolve(e, "IsEditable").IsTrue) { ctx.Pass("R3", e); return; }
        if (Context.ControlTemplateOf(e) is not null) { ctx.NotJudged("R3", e, "a template part (the whole control may show focus)"); return; }

        string? unknown = null, skipped = null;
        foreach (var chain in styles.ChainsOf(e))
        {
            var (seen, why) = Judge(ctx, chain, e, control, doc.File);
            if (seen == Seen.No)
            {
                ctx.Report(Kinds.FocusNotVisible, e, $"<{e.Name.LocalName}> removes FocusVisualStyle and nothing else shows keyboard focus: {why}");
                return;
            }
            if (seen == Seen.Unknown) unknown ??= why;
            if (seen == Seen.Skip) skipped ??= why;
        }
        if (unknown is not null) ctx.Blocked("R3", e, unknown);
        else if (skipped is not null) ctx.NotJudged("R3", e, skipped);
        else ctx.Pass("R3", e);
    }

    // ───────────────────────── 焦點框還在嗎 ─────────────────────────

    /// <summary>FocusVisualStyle 是不是等於沒有（null、空樣式、空範本、畫出來是透明的）。null = 讀不出來。</summary>
    private static bool? Removed(Context ctx, Resolved r)
    {
        if (r.IsUnreadable) return null;
        if (r.IsNull) return true;
        if (r.Element is not { } e) return r.Value is null || Markup.IsMarkup(r.Value) ? null : false;
        if (e.Name.LocalName != "Style") return null;

        foreach (var chain in ctx.Project.Styles.ChainsOfStyle(StyleIndex.RecordOf(e)))
        {
            if (chain.Setter("Template") is not { } setter)
            {
                if (chain.Unknown is not null) return null;
                continue;
            }
            var t = ctx.Project.Styles.SetterValue(setter);
            if (t.IsNull) continue;
            if (t.Element is not { } template) return null;
            if (template.Descendants().Any(d => !Markup.IsPropertyElement(d) && Draws(ctx, d))) return false;
        }
        return true;
    }

    /// <summary>焦點框範本裡的元素畫不畫得出東西。讀不出來的（綁定、資源）當成畫得出來。</summary>
    private static bool Draws(Context ctx, XElement e)
    {
        // 值可能寫在樣式裡（HandyControl 的 <Rectangle Style="{StaticResource RectangleFocusVisual}"/>）
        Resolved Of(string property) => ctx.Project.Styles.Resolve(e, property);
        bool Set(string property) => Of(property).IsSet;
        bool Invisible(string property) => Of(property) is { IsValue: true, Value: { } v } && (v.Trim() is "Transparent" or "{x:Null}" or "0" or "0.0" || NoAlpha(v.Trim()));
        if (Invisible("Opacity") || Of("Visibility") is { IsValue: true, Value: "Collapsed" or "Hidden" }) return false;
        return e.Name.LocalName switch
        {
            "Grid" or "StackPanel" or "Canvas" or "DockPanel" => Set("Background") && !Invisible("Background"),
            "Rectangle" or "Ellipse" or "Path" or "Line" or "Polygon" or "Polyline" =>
                (Set("Stroke") && !Invisible("Stroke") && !Invisible("StrokeThickness")) || (Set("Fill") && !Invisible("Fill")),
            "Border" => (Set("BorderBrush") && !Invisible("BorderBrush") && Set("BorderThickness") && !Invisible("BorderThickness"))
                        || (Set("Background") && !Invisible("Background")),
            "Style" or "Setter" or "SolidColorBrush" or "ControlTemplate" => false,
            _ => true,
        };
    }

    /// <summary>#00RRGGBB、#0RGB：完全透明。</summary>
    private static bool NoAlpha(string color) =>
        (color.Length == 9 && color.StartsWith("#00", StringComparison.Ordinal)) || (color.Length == 5 && color.StartsWith("#0", StringComparison.Ordinal));

    // ───────────────────────── 有沒有別的東西畫出焦點 ─────────────────────────

    private static TemplateInfo Templates(Context ctx, StyleChain chain, XElement? element)
    {
        var local = element is null ? Resolved.NotSet : ctx.Project.Styles.LocalValue(element, "Template");
        if (!local.IsSet && element is not null && ctx.Project.Styles.OwnRoot?.Invoke(element) is { } root) local = ctx.Project.Styles.LocalValue(root, "Template");
        if (!local.IsSet) return ctx.Project.Styles.TemplateOfChain(chain);
        return local.Element is { Name.LocalName: "ControlTemplate" } ct ? new TemplateInfo(TemplateKind.Custom, ct, null) : new TemplateInfo(TemplateKind.Unknown, null, "its Template cannot be read");
    }

    private static (Seen, string?) ShowsFocus(Context ctx, StyleChain chain, XElement? element, string control, string file)
    {
        var template = Templates(ctx, chain, element);
        string? partial = null;
        foreach (var s in chain.Styles)
            foreach (var t in s.Triggers)
                switch (FocusTrigger(t))
                {
                    case Seen.Yes:
                        // 樣式觸發器改了一個專案範本沒用到的屬性（BorderBrush，但範本沒綁它）：看不出來有沒有變
                        if (template.Kind != TemplateKind.Custom || TemplateUses(template.Template!, t)) return (Seen.Yes, null);
                        partial = "a focus trigger sets a property its template may not draw";
                        break;
                    case Seen.Unknown: partial ??= "a focus trigger also depends on another condition"; break;
                }

        // 函式庫自己的焦點框（MahApps 的 mah:FocusVisualHelper.FocusVisualStyle）：看不到它畫什麼
        if (chain.Styles.Any(s => s.Setters.Keys.Any(k => k != "FocusVisualStyle" && k.Contains("FocusVisual", StringComparison.Ordinal)))
            || element?.Attributes().Any(a => a.Name.LocalName != "FocusVisualStyle" && a.Name.LocalName.Contains("FocusVisual", StringComparison.Ordinal)) == true)
            return (Seen.Unknown, "another property sets the focus visual");

        // 元素自己的事件觸發器（<Button.Triggers><EventTrigger RoutedEvent="GotFocus">）
        if (element?.Elements().Where(x => x.Name.LocalName.EndsWith(".Triggers", StringComparison.Ordinal)).SelectMany(x => x.Elements()).Any(t => FocusTrigger(t) == Seen.Yes) == true)
            return (Seen.Yes, null);

        switch (template.Kind)
        {
            case TemplateKind.Unknown: return (Seen.Unknown, template.Why);
            case TemplateKind.Custom:
                var seen = TemplateShows(ctx, template.Template!);
                if (seen == Seen.Yes) return (Seen.Yes, null);
                if (seen == Seen.Unknown || partial is not null) return (Seen.Unknown, partial ?? "its template contains controls this tool does not know, or a focus trigger with another condition");
                return (Seen.No, "its template has no focus trigger and no Focused state");
            default:
                if (partial is not null) return (Seen.Unknown, partial);
                var themes = ctx.Project.ThemesOf(file);
                // 專案自己的樣式沒寫 BasedOn（或 Style="{x:Null}"）：佈景的隱含樣式整個被取代，用的是 WPF 內建的範本（Oracle 抓到的）
                var replaced = (string?)element?.Attribute("Style") == "{x:Null}" || StyleIndex.KeepsThemeStyle(chain) == false;
                if (themes.Count == 0 || replaced) return (Seen.No, $"the default {control} template shows focus only through FocusVisualStyle");
                if (ctx.Options.ThemeShowsFocus) return (Seen.Yes, null);
                return FromTheme(ctx, themes, control);
        }
    }

    /// <summary>不必範本配合就看得到的屬性（會繼承的文字屬性、整個元素的外觀）。</summary>
    private static readonly HashSet<string> AlwaysDrawn = new(StringComparer.Ordinal)
    {
        "Foreground", "FontWeight", "FontStyle", "FontSize", "FontFamily", "FontStretch", "Opacity", "OpacityMask", "Effect", "RenderTransform", "LayoutTransform",
        "Visibility", "Width", "Height", "MinWidth", "MinHeight", "Margin", "Template", "Content", "ContentTemplate", "Clip", "FocusVisualStyle",
        "TextElement.Foreground", "TextBlock.Foreground",
    };

    private static bool TemplateUses(XElement template, XElement trigger)
    {
        if (trigger.Descendants().Any(d => d.Name.LocalName is "Storyboard" or "BeginStoryboard")) return true;
        foreach (var setter in trigger.Descendants().Where(d => d.Name.LocalName == "Setter"))
        {
            if ((string?)setter.Attribute("Property") is not { } p) continue;
            var name = StyleIndex.NormalizeProperty(p);
            if (NonVisual.Contains(name)) continue;
            if (AlwaysDrawn.Contains(name)) return true;
            var last = name[(name.LastIndexOf('.') + 1)..];
            if (template.Descendants().Attributes().Any(a => Markup.IsMarkup(a.Value) && a.Value.Contains(last, StringComparison.Ordinal))) return true;
        }
        return false;
    }

    private static (Seen, string?) FromTheme(Context ctx, IReadOnlyList<ThemeUse> themes, string control)
    {
        var answers = new List<bool>();
        foreach (var t in themes)
        {
            if (t.LibraryId is null || KnownControls.Libraries.FirstOrDefault(l => l.Id == t.LibraryId) is not { } lib
                || !KnownControls.ThemeFocus.TryGetValue(t.LibraryId, out var table) || !table.TryGetValue(control, out var shows))
                return (Seen.Unknown, $"the template comes from the theme '{t.Name}', which was not measured for {control}");
            // MaterialDesign 2 與 3 的控制項不一樣：量的是 3
            if (KnownControls.ThemeRequires.TryGetValue(t.LibraryId, out var required) && !themes.Any(x => x.Source?.EndsWith("/" + required, StringComparison.OrdinalIgnoreCase) == true))
                return (Seen.Unknown, $"the theme '{t.Name}' was only measured with {required}");
            var versions = ctx.Project.PackageVersions(lib.Package);
            if (versions.Count == 0 || versions.Any(v => KnownLibrary.MajorMinorOf(v) != lib.MajorMinor))
                return (Seen.Unknown, $"the template comes from the theme '{t.Name}'; measured with {lib.Package} {lib.Version}, the project uses {(versions.Count == 0 ? "an unknown version" : string.Join(", ", versions.Order(StringComparer.Ordinal)))}");
            answers.Add(shows);
        }
        return answers.All(a => a) ? (Seen.Yes, null)
            : answers.All(a => !a) ? (Seen.No, $"the {control} template of the theme '{themes[0].Name}' shows focus only through FocusVisualStyle (measured)")
            : (Seen.Unknown, "several themes apply and they differ");
    }

    /// <summary>範本裡引用的別的範本與樣式（Slider 的 Thumb 範本）：裡面的 DataTrigger 可以綁到外層控制項的焦點。</summary>
    private static bool NestedShows(Context ctx, XElement template, int depth, HashSet<XElement> seen)
    {
        if (depth > 4) return false;
        foreach (var d in template.Descendants())
            foreach (var a in d.Attributes())
            {
                if (Markup.ResourceKey(a.Value) is not { } key) continue;
                foreach (var found in ctx.Project.Styles.FindResources(d, key))
                {
                    if (found.Name.LocalName is not ("ControlTemplate" or "Style" or "DataTemplate") || !seen.Add(found)) continue;
                    foreach (var x in found.Descendants())
                    {
                        if (x.Name.LocalName is "DataTrigger" or "MultiDataTrigger" && FocusTrigger(x) != Seen.No) return true;
                        if (x.Attributes().Any(b => Markup.ExtensionName(b.Value) == "Binding" && Markup.BindingPath(b.Value) is { } path && MentionsFocus(path) && x.Name.LocalName is not ("DataTrigger" or "Condition"))) return true;
                    }
                    if (NestedShows(ctx, found, depth + 1, seen)) return true;
                }
            }
        return false;
    }

    private static Seen TemplateShows(Context ctx, XElement template)
    {
        if (NestedShows(ctx, template, 0, new HashSet<XElement>(ReferenceEqualityComparer.Instance))) return Seen.Yes;
        var partial = false;
        foreach (var t in template.Elements().Where(x => x.Name.LocalName == "ControlTemplate.Triggers").SelectMany(x => x.Elements()))
            switch (FocusTrigger(t))
            {
                case Seen.Yes: return Seen.Yes;
                case Seen.Unknown: partial = true; break;
            }

        var unknownElement = false;
        foreach (var d in template.Descendants())
        {
            if (d.Name.LocalName == "VisualState" && Markup.NameOf(d) is { } state && state.StartsWith("Focused", StringComparison.Ordinal) && d.Descendants().Any(x => x.Name.LocalName == "Storyboard" && x.HasElements))
                return Seen.Yes;
            foreach (var a in d.Attributes())
                if (Markup.ExtensionName(a.Value) is "Binding" && Markup.BindingPath(a.Value) is { } path && MentionsFocus(path)) return Seen.Yes;
            // 範本裡的元素自己的樣式觸發器（DataTrigger 綁到 TemplatedParent 的 IsKeyboardFocused）
            if (d.Name.LocalName is "DataTrigger" or "MultiDataTrigger" && FocusTrigger(d) == Seen.Yes) return Seen.Yes;
            if (!Markup.IsPropertyElement(d) && !Markup.IsWpf(d) && d.Name.NamespaceName != Markup.XamlNamespace && !d.Name.NamespaceName.StartsWith("clr-namespace:System", StringComparison.Ordinal)
                && ctx.IsElement(d) && ctx.Project.ControlOf(d) is null && !d.Name.LocalName.EndsWith("Converter", StringComparison.Ordinal))
                unknownElement = true;
        }
        return partial || unknownElement ? Seen.Unknown : Seen.No;
    }

    private static bool MentionsFocus(string s) =>
        s.Contains("IsFocused", StringComparison.Ordinal) || s.Contains("IsKeyboardFocus", StringComparison.Ordinal) || s.Contains("IsFocusVisible", StringComparison.Ordinal);

    /// <summary>
    /// 這個觸發器會不會在鍵盤焦點到的時候改變畫面。
    /// 多重條件裡還要滑鼠停著（IsMouseOver=True）的不算；另有別的條件的是「有時候」→ 不知道。
    /// </summary>
    private static Seen FocusTrigger(XElement trigger)
    {
        bool focus = false, mouse = false, other = false;
        void Condition(string? property, string? binding, string? value)
        {
            var subject = property is not null ? StyleIndex.NormalizeProperty(property) : binding ?? "";
            var off = string.Equals(value?.Trim(), "False", StringComparison.OrdinalIgnoreCase);
            if (MentionsFocus(subject)) { focus = true; return; }
            if (subject is "IsMouseOver" or "IsPressed" or "IsMouseDirectlyOver" && !off) { mouse = true; return; }
            if (subject is "IsEnabled" && !off || subject is "IsMouseOver" or "IsPressed") return;
            other = true;
        }

        switch (trigger.Name.LocalName)
        {
            case "Trigger": Condition((string?)trigger.Attribute("Property"), null, (string?)trigger.Attribute("Value")); break;
            case "DataTrigger": Condition(null, (string?)trigger.Attribute("Binding") ?? string.Concat(trigger.Elements().Where(x => x.Name.LocalName == "DataTrigger.Binding").SelectMany(x => x.Descendants().Attributes().Select(a => a.Value))), (string?)trigger.Attribute("Value")); break;
            case "MultiTrigger" or "MultiDataTrigger":
                foreach (var c in trigger.Descendants().Where(x => x.Name.LocalName == "Condition"))
                    Condition((string?)c.Attribute("Property"), (string?)c.Attribute("Binding"), (string?)c.Attribute("Value"));
                break;
            case "EventTrigger":
                if ((string?)trigger.Attribute("RoutedEvent") is { } ev && (ev.EndsWith("GotFocus", StringComparison.Ordinal) || ev.EndsWith("GotKeyboardFocus", StringComparison.Ordinal))) focus = true;
                break;
            default: return Seen.No;
        }
        if (!focus || mouse || !Visible(trigger)) return Seen.No;
        return other ? Seen.Unknown : Seen.Yes;
    }

    private static bool Visible(XElement trigger)
    {
        foreach (var d in trigger.Descendants())
        {
            if (d.Name.LocalName == "Setter" && (string?)d.Attribute("Property") is { } p && !NonVisual.Contains(StyleIndex.NormalizeProperty(p))) return true;
            if (d.Name.LocalName == "Setter" && (string?)d.Attribute("Property") is "FocusVisualStyle" && (string?)d.Attribute("Value") is { } v && v.Trim() != "{x:Null}") return true;
            if (d.Name.LocalName == "Storyboard" && d.HasElements) return true;
            if (d.Name.LocalName == "BeginStoryboard" && d.Attribute("Storyboard") is not null) return true;
        }
        return false;
    }
}
