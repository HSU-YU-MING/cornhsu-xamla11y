using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Analysis;

public enum NameKind
{
    /// <summary>有名稱。</summary>
    Named,
    /// <summary>有設名稱，值要到執行期才知道（綁定資料）。專案改不了「靜態看不到」，不進可解析率的分母。</summary>
    DataBound,
    /// <summary>UIA 的 Name 是空的。</summary>
    Missing,
    /// <summary>名稱是空的，但畫面上有文字 —— 放在面板裡，WPF 不會拿它當名稱。</summary>
    TextNotUsed,
    /// <summary>UIA 的 Name 不是空的，但念出來沒有意義（私用區字元、空白、型別名、Geometry 路徑、圖示字型的字母）。</summary>
    Meaningless,
    /// <summary>只有符號或表情（×、…、🗑）：有名稱，念出來是符號的名字。</summary>
    SymbolOnly,
    /// <summary>讀不出來。</summary>
    Unknown,
}

/// <param name="Text">看得到的名稱文字（Named、TextNotUsed、Meaningless、SymbolOnly）。</param>
/// <param name="Why">給人看的原因。</param>
public readonly record struct NameResult(NameKind Kind, string? Text = null, string? Why = null)
{
    public static NameResult Named(string? text) => new(NameKind.Named, text);
    public static NameResult Missing(string why) => new(NameKind.Missing, null, why);
    public static NameResult Unknown(string why) => new(NameKind.Unknown, null, why);
    public static NameResult Bound(string why) => new(NameKind.DataBound, null, why);
}

/// <summary>
/// 照 WPF 實際的取法算一個元素的 UIA 名稱（docs/WPF實測.md 第 2 節；每一條都進 UiaOracle 比對）。
/// </summary>
public sealed partial class NameResolver(Project project)
{
    private readonly StyleIndex _styles = project.Styles;
    private const int MaxSteps = 2000;
    private int _steps;

    private const string NameProperty = "AutomationProperties.Name";
    private const string LabeledByProperty = "AutomationProperties.LabeledBy";

    /// <param name="control">它照哪個 WPF 控制項算（<see cref="Project.ControlOf"/>）。</param>
    public NameResult Resolve(XElement e, string control, int depth = 0)
    {
        if (depth > 12) return NameResult.Unknown("the labelling chain is too deep");
        // 一個元素最多看這麼多步：深度有上限、分支沒有（觸發器互相引用資源，600 位元組的 XAML 就能跑不完）
        if (depth == 0) _steps = 0;
        if (++_steps > MaxSteps) return NameResult.Unknown("its content is too complex to follow");

        // 1. AutomationProperties.Name
        if (ExplicitName(e, depth) is { } named) return named;

        // 2. 佈景補的名稱（MaterialDesign 的 HintAssist.Hint）
        if (FromTheme(e, control) is { } themed) return themed;

        // 3. LabeledBy
        if (FromLabel(e, depth) is { } labeled) return labeled;

        // code-behind 設名稱：靜態看不到值
        if (project.CodeNames(e)) return NameResult.Unknown("the code-behind sets its accessible name");

        // 4. 自己的純文字
        var source = Wpf.NameSourceOf(control);
        var result = source switch
        {
            NameSource.Text => TextOf(e, depth),
            NameSource.Inlines => InlineText(e, depth),
            NameSource.Content => ContentName(e, "Content", control, depth),
            NameSource.Header => ContentName(e, "Header", control, depth),
            NameSource.NoText or NameSource.Nothing => NameResult.Missing("it has no AutomationProperties.Name and nothing labels it"),
            _ => NameResult.Unknown($"<{e.Name.LocalName}> is not a control this tool knows"),
        };

        // RoutedUICommand 的文字：ButtonBase 的 peer 在名稱是空的時候拿它（內容是圖示也一樣，UiaOracle「Command with icon content」）
        if (result.Kind is NameKind.Missing or NameKind.TextNotUsed && Wpf.Buttons.Contains(control) && CommandText(e) is { } fromCommand)
            return fromCommand;

        // 名稱只在觸發器裡設：有時候有、有時候沒有
        if (result.Kind is NameKind.Missing or NameKind.TextNotUsed or NameKind.Meaningless && TriggerSets(e, NameProperty))
            return NameResult.Unknown("a trigger sets its accessible name");
        return result;
    }

    // ───────────────────────── 明寫的名稱 ─────────────────────────

    private NameResult? ExplicitName(XElement e, int depth)
    {
        var r = _styles.Resolve(e, NameProperty);
        if (r.Kind == ResolveKind.NotSet) return null;
        if (r.IsUnreadable)
        {
            // 資源引用：名稱**有**設，只是讀不到值 → 算有名稱（鍵在不在是 XamlKeys 的事）。
            // 樣式在專案外：不知道它設不設名稱 → 當成沒設，其餘照看（樣式替圖示按鈕設名稱極少見，當「不知道」會讓每個套外部樣式的按鈕都不判）
            if (r.Origin is null) return null;
            return Markup.IsMarkup(r.Value) ? NameResult.Named(null) : NameResult.Unknown("its accessible name is " + r.Why);
        }
        if (r.Element is not null || r.Value is null) return NameResult.Named(null);
        if (r.IsNull) return null;

        if (Markup.IsMarkup(r.Value))
        {
            // 綁到自己的 ToolTip／Tag／Content：那個屬性沒設就等於沒設名稱
            if (Markup.SelfBindingPath(r.Value) is { Length: > 0 } path && Regex.IsMatch(path, @"^\(?[\w:.]+\)?$", RegexOptions.CultureInvariant))
            {
                var name = StyleIndex.NormalizeProperty(path);
                if (name is "Command.Text" or "Text") return NameResult.Bound("the name is bound to " + path);
                var target = _styles.Resolve(e, name);
                if (target.Kind == ResolveKind.NotSet) return null;
                if (target.IsUnreadable) return NameResult.Named(null);
                if (target.Element is { } te)
                    return te.Name.LocalName is "ToolTip" or "TextBlock" ? NameResult.Named(null) : NameResult.Unknown($"the name is bound to {path}, which is an object");
                return Markup.IsMarkup(target.Value) ? NameResult.Bound("the name is bound to " + path)
                    : string.IsNullOrEmpty(target.Value) ? null : Judge(target.Value!, null);
            }
            return NameResult.Bound("the name comes from " + Markup.ExtensionName(r.Value));
        }

        var literal = Markup.Literal(r.Value);
        if (literal.Length == 0) return null;
        return Judge(literal, null);
    }

    internal bool TriggerSets(XElement e, string property)
    {
        bool Sets(IEnumerable<XElement> triggers) => triggers.SelectMany(t => t.Descendants()).Any(s =>
            s.Name.LocalName == "Setter" && s.Attribute("TargetName") is null
            && s.Attribute("Property")?.Value is { } p && StyleIndex.NormalizeProperty(p) == property);

        if (_styles.ChainsOf(e).Any(c => c.Styles.Any(s => Sets(s.Triggers)))) return true;
        return _styles.TemplatesOf(e).Any(t => t.Template is { } template
            && Sets(template.Elements().Where(x => x.Name.LocalName == "ControlTemplate.Triggers")));
    }

    private NameResult? FromTheme(XElement e, string control)
    {
        foreach (var lib in KnownControls.Libraries)
            foreach (var (property, controls) in lib.NameFrom)
            {
                if (!controls.Contains(control, StringComparer.Ordinal)) continue;
                var r = _styles.Resolve(e, property);
                // 樣式在專案外的「讀不出來」不算設了（Origin 是 null）：語料有 880 個輸入框因此被誤當成有 Hint
                if (!r.IsSet || r.Origin is null || r is { IsValue: true, Value: "" }) continue;

                // 名稱是佈景的**樣式**給的：樣式被換掉、佈景沒合併就沒有（UiaOracle themes「hint, …」）
                var keeps = _styles.KeepsThemeStyle(e);
                if (keeps == false) continue;
                if (keeps is null && !(Markup.ResourceKey((string?)e.Attribute("Style")) is { } key && key.StartsWith("MaterialDesign", StringComparison.Ordinal)))
                    return NameResult.Unknown($"its style is outside the project, so whether {lib.Package} still names it from {property} is unknown");
                if (!project.ThemesOf(StyleIndex.FileOf(e)).Any(t => t.LibraryId == lib.Id))
                    return NameResult.Unknown($"{lib.Package} names it from {property} only where its theme is merged, and that was not found in the XAML");
                var versions = project.PackageVersions(lib.Package);
                if (versions.Count > 0 && versions.All(v => KnownLibrary.MajorMinorOf(v) == lib.MajorMinor))
                    return r.IsValue && r.Value is { } v && !Markup.IsMarkup(v) ? NameResult.Named(v) : NameResult.Named(null);
                return NameResult.Unknown(versions.Count == 0
                    ? $"{lib.Package} {lib.Version} names it from {property}; the project's {lib.Package} version was not found"
                    : $"{lib.Package} {lib.Version} names it from {property}; the project uses {string.Join(", ", versions.Order(StringComparer.Ordinal))}, which was not measured");
            }
        return null;
    }

    // ───────────────────────── LabeledBy ─────────────────────────

    private NameResult? FromLabel(XElement e, int depth)
    {
        var r = _styles.Resolve(e, LabeledByProperty);
        if (!r.IsValue) return null;
        if (r.Element is { } inline)
        {
            if (project.ControlOf(inline) is { } c) return Through(inline, c);
            return NameResult.Unknown("LabeledBy is set to an element this tool does not know");
        }
        if (Markup.ReferencedElement(r.Value) is not { } name)
            return r.IsNull || string.IsNullOrEmpty(r.Value) ? null : NameResult.Unknown("LabeledBy is set to something this tool cannot follow");

        var found = FindNamed(e, name);
        if (found is null) return NameResult.Unknown($"LabeledBy points at '{name}', which is not in this file");
        if (project.ControlOf(found) is not { } control) return NameResult.Unknown($"LabeledBy points at <{found.Name.LocalName}>, which this tool does not know");
        return Through(found, control);

        // 指到的東西沒有名稱 → 等於沒有 LabeledBy（往下看自己的文字）
        NameResult? Through(XElement target, string control)
        {
            // 沒有 AutomationPeer 的元素（Border、面板、圖形、ContentControl…）寫了 Name 也沒有人念得到
            if (!Wpf.HasPeer(control)) return null;
            var label = Resolve(target, control, depth + 1);
            return label.Kind is NameKind.Missing or NameKind.TextNotUsed ? null : label;
        }
    }

    /// <summary>同一份文件裡 x:Name 是這個名字的元素。範本各有自己的名稱範圍：先找同一個範本裡的。</summary>
    internal static XElement? FindNamed(XElement from, string name)
    {
        static bool IsTemplate(XElement e) => e.Name.LocalName is "ControlTemplate" or "DataTemplate" or "HierarchicalDataTemplate" or "ItemsPanelTemplate";
        // 由內往外一層一層找；裡面的範本是別的名稱範圍，看不進去
        for (var scope = from.Ancestors().FirstOrDefault(IsTemplate) ?? from.Document?.Root; scope is not null;
             scope = scope.Parent is null ? null : scope.Ancestors().FirstOrDefault(IsTemplate) ?? scope.Document?.Root)
        {
            var here = scope;
            var found = here.DescendantsAndSelf().FirstOrDefault(d => Markup.NameOf(d) == name && !d.Ancestors().TakeWhile(a => a != here).Any(IsTemplate));
            if (found is not null) return found;
        }
        return null;
    }

    // ───────────────────────── 名稱與畫面的字（WCAG 2.5.3） ─────────────────────────

    /// <summary>
    /// 另外寫了名稱、畫面上也有字，而且兩個都看得到是什麼：(名稱, 畫面的字)。其他情況回 null。
    /// 綁定、觸發器會換的、範本不畫內容的不比。
    /// </summary>
    public (string Name, string Shown)? NameAndShown(XElement e, string control)
    {
        _steps = 0;
        if (ExplicitName(e, 0) is null && FromLabel(e, 0) is null) return null;
        if (Resolve(e, control) is not { Kind: NameKind.Named, Text: { Length: > 0 } name }) return null;
        _steps = 0;
        if (TriggerSets(e, NameProperty) || project.CodeNames(e)) return null;
        var source = Wpf.NameSourceOf(control);
        if (source is not (NameSource.Content or NameSource.Header or NameSource.Inlines)) return null;
        var property = source == NameSource.Header ? "Header" : "Content";
        if (source != NameSource.Inlines)
        {
            if (TriggerValues(e, property).Any() || project.CodeSets(e, property)) return null;
            // 內容畫在哪裡看範本：讀不到的、沒有 ContentPresenter 的不比
            foreach (var t in _styles.TemplatesOf(e))
                if (t.Template is null ? t.Kind == TemplateKind.Unknown : !t.Template.Descendants().Any(d => d.Name.LocalName is "ContentPresenter" or "GridViewRowPresenter" || d.Name.LocalName.EndsWith("ContentPresenter", StringComparison.Ordinal))) return null;
            if (_styles.Resolve(e, property + "Template") is { IsSet: true, Origin: not null }) return null;
        }
        var shown = source switch { NameSource.Inlines => InlineText(e, 1), NameSource.Header => ContentName(e, "Header", control, 1), _ => ContentName(e, "Content", control, 1) };
        // 資源：好幾種語言的資源，這裡看到的只是其中一份，不能比值
        if (IsResource(e, NameProperty) || source != NameSource.Inlines && IsResource(e, property) || e.Descendants().Any(d => d.Attributes().Any(a => a.Name.LocalName is "Text" or "Content" or "Header" && Markup.IsMarkup(a.Value)))) return null;
        // 圖示字元、型別名不是畫面上的字；字在面板裡的是
        var real = shown.Kind is NameKind.Named or NameKind.TextNotUsed || shown is { Kind: NameKind.Meaningless, Why: { } w } && (w.StartsWith("the text is inside <", StringComparison.Ordinal) || w.StartsWith("it only contains an element", StringComparison.Ordinal));
        if (!real || shown.Text is not { Length: > 0 } text || text == "{…}" || Judge(text, FontOf(e)).Kind != NameKind.Named) return null;
        // 字在元素裡：好幾段、會藏起來、程式碼會換的不比
        if (Markup.ContentElements(e).Count > 0 || e.Elements().Any(c => c.Name.LocalName.EndsWith("." + property, StringComparison.Ordinal) && c.HasElements))
            return SingleShownText(e) is { Literal: true } single && single.Raw.Trim().Length > 0 ? (name, single.Raw.Trim()) : SingleShownText(e) is { } one && !Markup.IsMarkup(one.Raw) ? (name, Markup.Literal(one.Raw)) : null;
        return (name, text);
    }

    private bool IsResource(XElement e, string property)
    {
        if (e.Attributes().FirstOrDefault(a => a.Name.LocalName == property) is { } a) return Markup.IsMarkup(a.Value);
        return _styles.ChainsOf(e).Any(c => c.Setter(property) is { } s && Markup.IsMarkup((string?)s.Attribute("Value")));
    }

    private static readonly string[] ContentHolders = [".Content", ".Header", ".Child", ".Children", ".Inlines"];

    /// <summary>
    /// 元素裡面唯一一段文字，而且一直看得到、沒有人會換。Raw 是原來寫的（屬性值，或元素裡的字：Literal）。
    /// 好幾段、會藏起來（Visibility、Opacity、觸發器）、程式碼提到的、放在別的屬性裡的（CheckedElement）回 null。
    /// </summary>
    internal (XElement At, string Raw, bool Literal)? SingleShownText(XElement e)
    {
        var texts = new List<XElement>();
        foreach (var d in e.Descendants())
        {
            if (project.ControlOf(d) is not ("TextBlock" or "AccessText" or "Label" or "Run")) continue;
            var holder = d.Ancestors().TakeWhile(a => a != e).FirstOrDefault(a => Markup.IsPropertyElement(a) && !ContentHolders.Any(h => a.Name.LocalName.EndsWith(h, StringComparison.Ordinal)));
            if (holder is null) texts.Add(d);
            // 提示、資源、樣式裡的不是畫面上的；其他屬性裡的字什麼時候出現不知道
            else if (!(holder.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal) || holder.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal) || holder.Name.LocalName.EndsWith(".ContextMenu", StringComparison.Ordinal) || holder.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal) || holder.Name.LocalName.EndsWith(".Triggers", StringComparison.Ordinal))) return null;
        }
        // 圖示字型的字不是文字
        texts = [.. texts.Where(d => Raw(d) is not { } r || r.Value.Trim().Length == 0 || Markup.IsMarkup(r.Value) || Judge(r.Value, FontOf(d)).Kind is NameKind.Named or NameKind.SymbolOnly)];
        var said = texts.Where(d => Raw(d) is { } r && r.Value.Trim().Length > 0).ToList();
        // 有裝了別的東西、或字是程式給的 TextBlock：不知道還有什麼字
        if (said.Count != 1 || texts.Any(d => d != said[0] && !said[0].Ancestors().Contains(d) && project.ControlOf(d) != "Run" && (Markup.NameOf(d) is not null || d.HasElements))) return null;
        if (said[0].HasElements && project.ControlOf(said[0]) == "TextBlock") return null;
        var at = said[0];
        foreach (var a in at.AncestorsAndSelf().TakeWhile(a => a != e))
        {
            if (Markup.IsPropertyElement(a)) continue;
            if (_styles.Resolve(a, "Visibility") is { IsSet: true, Origin: not null } || _styles.Resolve(a, "Opacity") is { IsSet: true, Origin: not null } || TriggerSets(a, "Visibility") || TriggerSets(a, "Opacity") || TriggerSets(a, "Text")) return null;
            if (Markup.NameOf(a) is { } n && (project.ChangesLater(a, "Visibility") || project.CodeSets(a, "Text", "Content", "Inlines", "Children", "Child") || e.Document!.Root!.Descendants().Any(d => (string?)d.Attribute("TargetName") == n || d.Attributes().Any(x => x.Name.LocalName == "Storyboard.TargetName" && x.Value == n)))) return null;
        }
        return Raw(at) is { } raw ? (at, raw.Value, raw.Literal) : null;

        (string Value, bool Literal)? Raw(XElement d)
        {
            if ((string?)d.Attribute(project.ControlOf(d) == "Label" ? "Content" : "Text") is { } attribute)
                return project.ControlOf(d) is "AccessText" or "Label" && !Markup.IsMarkup(attribute) ? (RemoveAccessKey(Markup.Literal(attribute)), true) : (attribute, false);
            if (d.HasElements) return null;
            return project.ControlOf(d) is "AccessText" or "Label" ? (RemoveAccessKey(d.Value.Trim()), true) : (d.Value.Trim(), true);
        }
    }

    // ───────────────────────── 純文字 ─────────────────────────

    /// <summary>ContentControl／HeaderedControl：看 Content 或 Header。</summary>
    private NameResult ContentName(XElement e, string property, string control, int depth)
    {
        var value = ContentValue(e, property);
        var result = value is null ? null : (NameResult?)FromObject(value.Value, e, control, depth);

        // 觸發器換內容（播放／暫停）：每一種都要看
        // 本地寫的內容贏過樣式的觸發器：那些觸發器沒作用（UiaOracle round3）
        var alternatives = HasLocalContent(e, property) ? [] : TriggerValues(e, property).Select(v => FromObject(v, e, control, depth)).ToList();
        if (alternatives.Count > 0)
        {
            // 沒有平常的內容：觸發器沒成立的時候就是空的
            var all = alternatives.Append(result ?? NothingFor(control, $"it has no {property} until a trigger sets one")).ToList();
            if (all.FirstOrDefault(x => x.Kind == NameKind.Unknown) is { Kind: NameKind.Unknown } unknown) return unknown;
            var named = all.Count(x => x.Kind is NameKind.Named or NameKind.DataBound or NameKind.SymbolOnly);
            if (named == all.Count || named == 0) return all[0];
            return NameResult.Unknown($"a trigger changes its {property}; some values name it and some do not");
        }

        if (result is { } r) return r;

        if (property == "Content" && e.Name.LocalName is not ("ListBoxItem" or "ComboBoxItem") && project.DocOfClass(e) is { } own)
        {
            // <local:Foo/>：內容寫在它自己的 XAML 裡
            if (project.CodeSets(e, property)) return NameResult.Unknown($"the code-behind sets its {property}");
            // 根上的 AutomationProperties.Name 是它自己的名稱（Resolve 看過了），不是它的純文字
            return project.ControlOf(own.Root) is { } c ? PlainText(own.Root, c, depth + 1) : NameResult.Unknown($"the root of {own.File} is not a control this tool knows");
        }

        if (project.CodeSets(e, property) || project.CodeLoops(e, property)) return NameResult.Unknown($"the code-behind sets its {property}");
        if (project.ClassSets(e, property)) return NameResult.Unknown($"its class sets its {property} in code");

        // MenuItem 沒有 Header：RoutedUICommand 的文字變成 Header
        if (control == "MenuItem" && CommandText(e) is { } header) return header;

        var chains = _styles.ChainsOf(e);
        if (chains.Any(c => c.Unknown is not null && c.Styles.Count == 0) && e.Attribute("Style") is not null)
            return NameResult.Unknown("its style is outside the project and may set the " + property.ToLowerInvariant());

        return Wpf.Items.Contains(control)
            ? new NameResult(NameKind.Meaningless, null, $"it has no {property}, so its name is the type name")
            : NameResult.Missing($"it has no {property} and no AutomationProperties.Name");
    }

    private NameResult PlainText(XElement e, string control, int depth) => depth > 16 ? NameResult.Unknown("its content is nested too deeply") : Wpf.NameSourceOf(control) switch
    {
        NameSource.Text => TextOf(e, depth),
        NameSource.Inlines => InlineText(e, depth),
        NameSource.Content => ContentName(e, "Content", control, depth),
        NameSource.Header => ContentName(e, "Header", control, depth),
        NameSource.NoText or NameSource.Nothing => NameResult.Missing($"<{e.Name.LocalName}> gives no text"),
        _ => NameResult.Unknown($"<{e.Name.LocalName}> is not a control this tool knows"),
    };

    /// <summary>命令是有文字的 RoutedUICommand → 有名稱；自訂的 x:Static 命令 → 不知道；綁定的命令、沒有文字的命令 → null。</summary>
    private NameResult? CommandText(XElement e)
    {
        var command = _styles.Resolve(e, "Command");
        // <RoutedUICommand x:Key="Save" Text="Save"/>（資源、屬性元素）；沒有 Text 的就是沒有
        if (command.Element is { } ce)
            return ce.Name.LocalName == "RoutedUICommand" && (string?)ce.Attribute("Text") is { Length: > 0 } label
                ? Markup.IsMarkup(label) ? NameResult.Named(null) : Judge(label, null)
                : null;
        if (command is { IsUnreadable: true, Origin: not null })
            return NameResult.Unknown("its Command is a resource this tool cannot read, which may be a RoutedUICommand");
        if (command is not { IsValue: true, Value: { } c }) return null;
        if (IsBuiltInCommand(e, c)) return NameResult.Named(null);
        if (IsSystemCommand(e, c)) return null;
        // 專案自己的 {x:Static local:Commands.Save}：看它在 C# 裡宣告成什麼
        if (Markup.ExtensionName(c) == "x:Static" || !Markup.IsMarkup(c))
        {
            var member = (Markup.IsMarkup(c) ? Markup.ExtensionBody(c) : c).Trim();
            var parts = member[(member.IndexOf(':') + 1)..].Split('.');
            if (parts.Length == 2 && project.CommandHasText(parts[0], parts[1]) is { } hasText) return hasText ? NameResult.Named(null) : null;
        }
        return Markup.ExtensionName(c) == "x:Static" || !Markup.IsMarkup(c) ? NameResult.Unknown("its Command may be a RoutedUICommand, whose text would be the name") : null;
    }

    private readonly record struct ContentVal(string? Text, XElement? Element);

    private bool HasLocalContent(XElement e, string property) =>
        _styles.LocalValue(e, property).IsSet || (property == "Content" && (Markup.ContentElements(e).Count > 0 || Markup.OwnText(e).Length > 0));

    /// <summary>Content／Header 設成什麼：屬性、屬性元素、直接內容、樣式。沒設回 null。</summary>
    private ContentVal? ContentValue(XElement e, string property)
    {
        var local = _styles.LocalValue(e, property);
        if (local.IsValue) return local.Element is { } el ? new ContentVal(null, el) : new ContentVal(local.Value ?? "", null);
        if (local.IsUnreadable) return new ContentVal(local.Value ?? "{?}", null);

        if (property == "Content")
        {
            var children = Markup.ContentElements(e);
            var text = Markup.OwnText(e);
            if (children.Count > 0) return new ContentVal(null, children[0]);
            if (text.Length > 0) return new ContentVal(text, null);
        }

        var styled = _styles.Resolve(e, property);
        if (styled.IsValue) return styled.Element is { } el ? new ContentVal(null, el) : new ContentVal(styled.Value ?? "", null);
        if (styled.IsUnreadable && styled.Origin is not null) return new ContentVal(styled.Value ?? "{?}", null);
        return null;
    }

    /// <summary>樣式觸發器裡設的 Content／Header。</summary>
    private IEnumerable<ContentVal> TriggerValues(XElement e, string property)
    {
        foreach (var chain in _styles.ChainsOf(e))
            foreach (var s in chain.Styles)
                foreach (var setter in s.Triggers.SelectMany(t => t.Descendants()).Where(x => x.Name.LocalName == "Setter" && x.Attribute("TargetName") is null))
                    if (setter.Attribute("Property")?.Value is { } p && StyleIndex.NormalizeProperty(p) == property)
                    {
                        var v = _styles.SetterValue(setter);
                        yield return v.Element is { } el ? new ContentVal(null, el) : new ContentVal(v.Value ?? "{?}", null);
                    }
    }

    private NameResult FromObject(ContentVal value, XElement owner, string control, int depth)
    {
        if (value.Element is { } element) return FromElement(element, owner, control, depth);
        var raw = value.Text ?? "";
        if (!Markup.IsMarkup(raw))
        {
            var text = Markup.Literal(raw);
            if (text.Trim().Length == 0)
                return text.Length == 0
                    ? NothingFor(control, "its content is an empty string")
                    : new NameResult(NameKind.Meaningless, text, "its content is only white space");
            return Spoken(text, control, FontOf(owner));
        }

        switch (Markup.ExtensionName(raw))
        {
            case "x:Null":
                return control == "MenuItem" && CommandText(owner) is { } fromCommand ? fromCommand : NothingFor(control, "its content is null");
            case "Binding" or "MultiBinding" or "PriorityBinding" or "x:Bind" or "TemplateBinding" or "CompiledBinding":
                if (Markup.SelfBindingPath(raw) == "Command.Text") return NameResult.Named(null);
                return NameResult.Bound($"its content is bound ({raw})");
            case "x:Static":
                return raw.Contains("Symbol", StringComparison.OrdinalIgnoreCase) || raw.Contains("Icon", StringComparison.OrdinalIgnoreCase) || raw.Contains("Glyph", StringComparison.OrdinalIgnoreCase)
                    ? NameResult.Unknown($"its content is {raw}, which looks like an icon")
                    : NameResult.Named(null);
            case "StaticResource" or "DynamicResource" or "ThemeResource":
                // 解得到的在 StyleIndex 就換成值了；走到這裡是專案裡沒有這個鍵（或同名的定義不一致）
                return NameResult.Unknown($"its content is {raw}, which this tool cannot resolve");
            case { } ext:
                var local = ext[(ext.LastIndexOf(':') + 1)..];
                if (IsIconExtension(owner, ext)) return NothingFor(control, $"its content is an icon ({{{ext}}})");
                return LocalizationRegex().IsMatch(local) ? NameResult.Named(null) : NameResult.Unknown($"its content comes from the markup extension {{{ext} …}}");
            default: return NameResult.Unknown($"its content is {raw}");
        }
    }

    /// <summary>{materialDesign:PackIcon X}、{ui:SymbolIcon X}：回傳的是圖示元素。</summary>
    private static bool IsIconExtension(XElement at, string extension)
    {
        var colon = extension.IndexOf(':');
        if (colon <= 0) return false;
        var local = extension[(colon + 1)..];
        if (at.GetNamespaceOfPrefix(extension[..colon]) is not { } ns) return false;
        return KnownControls.LibraryOf(ns.NamespaceName) is { } lib
               && (lib.IconExtensions.Contains(local, StringComparer.Ordinal) || lib.IconPrefix is { } p && local.StartsWith(p, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^(Loc|Localize|Localization|LocText|Localized\w*|L10n|I18n|Lang\w*|Tr|T|Translate\w*|Translation\w*|Text|ResX|Resx|Res|String\w*|Str)(Extension)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalizationRegex();

    private NameResult FromElement(XElement c, XElement owner, string control, int depth)
    {
        // 互相引用的資源（<StaticResource x:Key="a" ResourceKey="b"/> 兩個繞圈）會堆疊溢位
        if (depth > 16 || ++_steps > MaxSteps) return NameResult.Unknown("its content is nested or aliased too deeply");
        var local = c.Name.LocalName;
        if (c.Name.NamespaceName.StartsWith("clr-namespace:System", StringComparison.Ordinal) && local is "String")
        {
            var t = c.Value.Trim();
            return t.Length == 0 ? NothingFor(control, "its content is an empty string") : Spoken(t, control, FontOf(owner));
        }
        // 其他值型別：ToString()
        if (c.Name.NamespaceName.StartsWith("clr-namespace:System;", StringComparison.Ordinal) && !c.HasElements) return NameResult.Named(c.Value.Trim());

        // 綁定寫成元素、資源引用寫成元素
        if (local is "Binding" or "MultiBinding" or "PriorityBinding" or "TemplateBinding") return NameResult.Bound("its content is bound");
        if (local is "StaticResource" or "DynamicResource" or "StaticResourceExtension" or "DynamicResourceExtension")
        {
            if ((string?)c.Attribute("ResourceKey") is not { } key) return NameResult.Unknown("its content is a resource this tool cannot read");
            var found = _styles.FindResources(c, key);
            return found.Count == 0 ? NameResult.Unknown($"resource '{key}' is not defined in the project") : FromElement(found[0], owner, control, depth + 1);
        }

        // 不是 FrameworkElement 的物件：名稱是 ToString()
        if (Markup.IsWpfNamespace(c.Name.NamespaceName) && IsDrawingObject(local))
            return new NameResult(NameKind.Meaningless, null, local is "Geometry" or "PathGeometry" or "StreamGeometry"
                ? "its content is a Geometry, so its name is the path data"
                : $"its content is a {local}, so its name is the type name, a color code or a file path");

        var known = project.ControlOf(c);
        if (known is null)
        {
            return LooksLikeIcon(local)
                ? NothingFor(control, $"its content is only <{local}>, which looks like an icon")
                : NameResult.Unknown($"its content is <{local}>, and this tool does not know what text that gives");
        }

        switch (Wpf.NameSourceOf(known))
        {
            case NameSource.Text:
                var text = TextOf(c, depth + 1);
                return text.Kind == NameKind.Missing ? NothingFor(control, text.Why ?? "its content is an empty TextBlock") : text;
            case NameSource.Content or NameSource.Header:
                // 內層的 ContentControl：只看它的純文字，不看它的 AutomationProperties.Name
                var inner = ContentName(c, Wpf.NameSourceOf(known) == NameSource.Content ? "Content" : "Header", known, depth + 1);
                return inner.Kind is NameKind.Missing or NameKind.TextNotUsed ? NothingFor(control, inner.Why ?? "", inner.Text) : inner;
            case NameSource.NoText when known == "TextBox":
                // TextBox 當內容：它的 Text 是純文字（窮舉抓到的）
                var typed = _styles.Resolve(c, "Text");
                if (typed is { IsValue: true, Value: { } tv })
                    return Markup.IsMarkup(tv) ? NameResult.Bound("its content is a TextBox with bound text") : tv.Length == 0 ? NothingFor(control, "its content is an empty TextBox") : Judge(tv, null);
                return NameResult.Unknown("its content is a TextBox, whose text becomes the name");
            case NameSource.NoText when known is "ComboBox" or "RichTextBox":
                return NameResult.Unknown($"its content is a {known}, whose text becomes the name");
            default:
                var visible = VisibleText(c);
                return NothingFor(control, visible is null
                        ? $"its content is <{local}>, which gives no text"
                        : $"the text is inside <{local}>; WPF only takes the name from a string, a TextBlock or a ContentControl",
                    visible);
        }
    }

    /// <summary>WPF 的繪圖物件（不是元素）。WPF 沒有任何元素的型別名以這幾個字結尾，Image 除外。</summary>
    private static bool IsDrawingObject(string local) =>
        local.EndsWith("Geometry", StringComparison.Ordinal) || local.EndsWith("Brush", StringComparison.Ordinal)
        || local.EndsWith("Drawing", StringComparison.Ordinal) || local.EndsWith("Bitmap", StringComparison.Ordinal)
        || local is "GeometryGroup" or "DrawingGroup" or "DrawingImage" or "BitmapImage" or "Color";

    /// <summary>內容不給文字。項目容器的名稱變成型別名（不是空的），其他的是空名稱。</summary>
    private static NameResult NothingFor(string control, string why, string? visible = null) =>
        Wpf.Items.Contains(control)
            ? new NameResult(NameKind.Meaningless, visible, why + "; its name becomes the type name")
            : visible is null ? NameResult.Missing(why) : new NameResult(NameKind.TextNotUsed, visible, why);

    /// <summary>元素底下看得到的文字（面板裡的 TextBlock）；綁定的文字寫成 "{…}"。沒有文字回 null。</summary>
    private string? VisibleText(XElement root)
    {
        foreach (var d in root.DescendantsAndSelf())
        {
            // 資源裡的元素：root 自己就在 .Resources 底下，只看 root 以內
            if (Markup.IsPropertyElement(d) || d.AncestorsAndSelf().TakeWhile(a => a != root).Any(a => a.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal) || a.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
                continue;
            var c = project.ControlOf(d);
            if (c is "TextBlock" or "AccessText" or "Label")
            {
                var raw = (string?)d.Attribute(c == "Label" ? "Content" : "Text") ?? (d.HasElements ? null : d.Value.Trim());
                if (raw is null && d.Elements().Any(x => x.Name.LocalName == "Run")) raw = string.Concat(d.Elements().Where(x => x.Name.LocalName == "Run").Select(x => (string?)x.Attribute("Text") ?? x.Value));
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (Markup.IsMarkup(raw)) return "{…}";
                if (Judge(raw, FontOf(d)).Kind == NameKind.Named) return raw.Trim();
            }
        }
        return null;
    }

    private static bool LooksLikeIcon(string local) =>
        local.Contains("Icon", StringComparison.OrdinalIgnoreCase) || local.Contains("Glyph", StringComparison.OrdinalIgnoreCase)
        || local.Contains("Awesome", StringComparison.OrdinalIgnoreCase) || local.StartsWith("Svg", StringComparison.OrdinalIgnoreCase)
        || local is "SymbolIcon" or "CachedImage" or "ImageEx";

    /// <summary>TextBlock 的文字（Text 屬性、直接文字、Run）。</summary>
    private NameResult TextOf(XElement e, int depth)
    {
        var r = _styles.Resolve(e, "Text");
        if (r.IsUnreadable) return Markup.IsMarkup(r.Value) ? NameResult.Named(null) : NameResult.Unknown("its Text is set by a style this tool cannot read");
        if (r is { IsValue: true, Value: { } v })
        {
            if (Markup.IsMarkup(v)) return Markup.ExtensionName(v) is "Binding" or "TemplateBinding" or "x:Bind" ? NameResult.Bound("its text is bound") : NameResult.Named(null);
            var t = Markup.Literal(v);
            return t.Length == 0 ? NameResult.Missing("its text is empty") : Judge(t, FontOf(e));
        }
        if (r.IsValue) return NameResult.Bound("its text is set with an object");
        if (TriggerValues(e, "Text").Any()) return NameResult.Bound("a trigger sets its text");
        if (project.CodeSets(e, "Text", "Inlines")) return NameResult.Unknown("the code-behind sets its text");
        if (project.ClassSets(e, "Text") || project.ClassSets(e, "Inlines")) return NameResult.Unknown("its class sets its text in code");
        return InlineText(e, depth);
    }

    /// <summary>行內的文字：文字節點與 Run。InlineUIContainer 裡的東西不算（WPF 給一個空白）。</summary>
    private NameResult InlineText(XElement e, int depth)
    {
        // 一段一段看：每個 Run 有自己的字型，有的是綁定 —— 「圖示字元＋文字」「綁定的數字＋圖示」都很常見
        var parts = new List<(string Text, XElement At)>();
        bool bound = false, container = false, named = false, coded = false, outside = false;
        void Visit(XElement x)
        {
            foreach (var node in x.Nodes())
            {
                if (node is XText t) parts.Add((t.Value, x));
                else if (node is XElement holder && Markup.IsPropertyElement(holder))
                {
                    // <Hyperlink.Inlines>（Playnite）、<Run.Text>
                    if (holder.Name.LocalName.EndsWith(".Inlines", StringComparison.Ordinal)) Visit(holder);
                    else if (holder.Name.LocalName == "Run.Text")
                    {
                        if (holder.HasElements) bound = true; else parts.Add((holder.Value, x));
                    }
                }
                else if (node is XElement el)
                {
                    if (el.Name.LocalName is "InlineUIContainer") { container = true; continue; }
                    if (el.Name.LocalName == "LineBreak") { parts.Add((" ", x)); continue; }
                    if (project.CodeSets(el, "Text", "Inlines")) coded = true;
                    // 文字也可以來自 Run 的樣式與它的觸發器（NuGet Package Explorer）
                    if (el.Name.LocalName == "Run" && TriggerValues(el, "Text").Any()) bound = true;
                    if (el.Name.LocalName == "Run" && _styles.Resolve(el, "Text") is { IsSet: true } run)
                    {
                        // 隱含樣式套不套得到 Run 要看它在哪裡（按鈕內容裡的套不到，UiaOracle round3）：不猜
                        if (run is { IsUnreadable: true, Origin: null } || (!_styles.LocalValue(el, "Text").IsSet && el.Attribute("Style") is null && !el.Elements().Any(c => c.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal)))) { if (el.Nodes().All(n => n is System.Xml.Linq.XComment)) outside = true; Visit(el); continue; }
                        if (run.Value is { } text && !Markup.IsMarkup(text)) parts.Add((Markup.Literal(text), el));
                        // 綁資料的不知道內容；x:Static、{loc:T …}、專案外的資源：有文字，只是讀不到
                        else if (Markup.ExtensionName(run.Value) is null or "Binding" or "TemplateBinding" or "x:Bind" or "MultiBinding" or "PriorityBinding") bound = true;
                        else if (run.Value!.Trim() != "{x:Null}") named = true;
                        if (el.Elements().All(c => c.Name.LocalName == "Run.Text")) continue;
                    }
                    if (el.Name.LocalName is "Run" or "Bold" or "Italic" or "Underline" or "Span" or "Hyperlink" or "Paragraph") Visit(el);
                    else if (el.Name.LocalName is "Binding" or "MultiBinding") bound = true;
                    else if (!Markup.IsWpf(el)) bound = true;         // 專案自己的 Run 子類別
                    else container = true;                            // 直接放 UIElement：WPF 包進 InlineUIContainer
                }
            }
        }
        Visit(e);
        var found = string.Concat(parts.Select(p => p.Text)).Trim();
        var judged = parts.Where(p => p.Text.Trim().Length > 0).Select(p => Judge(p.Text, FontOf(p.At))).ToList();
        if (named || judged.Any(j => j.Kind == NameKind.Named)) return NameResult.Named(found);
        if (bound) return NameResult.Bound("its text is bound");
        if (coded) return NameResult.Unknown("the code-behind sets its text");
        if (outside) return NameResult.Unknown("a style may set the text of its Run");
        if (found.Length > 0)
            return judged.FirstOrDefault(j => j.Kind == NameKind.Meaningless) is { Kind: NameKind.Meaningless } odd ? odd with { Text = Escape(found) } : Judge(found, FontOf(e));
        if (project.CodeSets(e, "Text", "Inlines")) return NameResult.Unknown("the code-behind sets its text");
        return container
            ? new NameResult(NameKind.Meaningless, VisibleText(e) ?? " ", "it only contains an element, and WPF turns that into a single space")
            : NameResult.Missing("it has no text");
    }

    // ───────────────────────── 文字有沒有意義 ─────────────────────────

    /// <summary>元素的字型家族（本地、樣式；沿祖先找繼承值）。</summary>
    private string? FontOf(XElement e)
    {
        for (XElement? cur = e; cur is not null; cur = cur.Parent)
        {
            if (Markup.IsPropertyElement(cur)) continue;
            foreach (var property in (ReadOnlySpan<string>)["FontFamily"])
            {
                var r = _styles.Resolve(cur, property);
                if (r is { IsValue: true, Value: { } v }) return v;
                if (r is { IsValue: true, Element: { } el }) return (string?)el.Attribute("Source") ?? el.Value;
                if (r.IsUnreadable) return Markup.ResourceKey(r.Value) ?? r.Value;
            }
            foreach (var a in cur.Attributes())
                if (a.Name.LocalName is "TextElement.FontFamily" or "TextBlock.FontFamily") return a.Value;
            if (cur.Name.LocalName is "ControlTemplate" or "DataTemplate") return null;
        }
        return null;
    }

    /// <summary>看得到的文字 → 名稱有沒有意義。</summary>
    internal static NameResult Judge(string text, string? font)
    {
        var t = text.Trim();
        if (t.Length == 0) return new NameResult(NameKind.Meaningless, text, "its name is only white space");

        bool anyLetter = false, anyPrivate = false;
        foreach (var rune in t.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat == UnicodeCategory.PrivateUse) anyPrivate = true;
            // 組合字元自己不算：⚙ 後面的 U+FE0F（表情的樣式）也是組合字元
            else if (Rune.IsLetterOrDigit(rune)) anyLetter = true;
        }
        if (anyPrivate && !anyLetter) return new NameResult(NameKind.Meaningless, Escape(t), "its name is an icon-font character from the private use area");
        if (font is not null && Wpf.IsIconFont(font)) return new NameResult(NameKind.Meaningless, Escape(t), $"its text is drawn as an icon by the font '{font}'; a screen reader reads the raw characters");
        if (!anyLetter) return new NameResult(NameKind.SymbolOnly, t, "its name is only symbols");
        return new NameResult(NameKind.Named, t);
    }

    internal static string Escape(string s) => string.Concat(s.EnumerateRunes().Select(r =>
        Rune.GetUnicodeCategory(r) == UnicodeCategory.PrivateUse ? $"U+{r.Value:X4}" : r.ToString()));

    private static NameResult Spoken(string text, string control, string? font)
    {
        var t = RemoveAccessKey(text);
        return t.Trim().Length == 0 ? new NameResult(NameKind.Meaningless, t, "its content is only white space") : Judge(t, font);
    }

    private static string RemoveAccessKey(string text)
    {
        // 照 AccessText.RemoveAccessKeyMarker：只拿掉第一個後面還有字的底線，再把兩個底線變一個
        for (var i = text.IndexOf('_', StringComparison.Ordinal); i >= 0; i = i + 2 < text.Length ? text.IndexOf('_', i + 2) : -1)
            if (i + 1 < text.Length && text[i + 1] != '_') { text = text.Remove(i, 1); break; }
        return text.Replace("__", "_", StringComparison.Ordinal);
    }

    /// <summary>SystemCommands.CloseWindowCommand：RoutedCommand，沒有文字（UiaOracle round3）。</summary>
    private static bool IsSystemCommand(XElement at, string command)
    {
        var c = command.Trim();
        if (Markup.ExtensionName(c) == "x:Static") c = Markup.ExtensionBody(c);
        if (Markup.IsMarkup(c) || c.LastIndexOf('.') is not (> 0 and var dot)) return false;
        var owner = c[..dot];
        var colon = owner.IndexOf(':');
        if (colon < 0) return owner == "SystemCommands";
        return owner[(colon + 1)..] == "SystemCommands" && at.GetNamespaceOfPrefix(owner[..colon]) is { } ns
               && (Markup.IsWpfNamespace(ns.NamespaceName) || ns.NamespaceName.StartsWith("clr-namespace:Microsoft.Windows.Shell", StringComparison.Ordinal) || ns.NamespaceName.StartsWith("http://schemas.microsoft.com/winfx/2006/xaml/presentation/shell", StringComparison.Ordinal));
    }

    /// <summary>WPF 內建、有文字的 RoutedUICommand。</summary>
    private static bool IsBuiltInCommand(XElement at, string command)
    {
        var c = command.Trim();
        if (Markup.ExtensionName(c) == "x:Static") c = Markup.ExtensionBody(c);
        if (Markup.IsMarkup(c)) return false;
        var dot = c.LastIndexOf('.');
        if (dot < 0) return c is "Copy" or "Cut" or "Paste" or "Undo" or "Redo" or "Delete" or "Close" or "New" or "Open" or "Save" or "SaveAs" or "Print" or "PrintPreview" or "Find" or "Replace" or "SelectAll" or "Help" or "Properties" or "Stop";
        var owner = c[..dot];
        var colon = owner.LastIndexOf(':');
        if (colon >= 0)
        {
            // local:ApplicationCommands 是專案自己的類別（Prism 的慣例），不是 WPF 的
            if (colon == 0 || at.GetNamespaceOfPrefix(owner[..colon]) is not { } ns || !Markup.IsWpfNamespace(ns.NamespaceName)) return false;
            owner = owner[(colon + 1)..];
        }
        return owner is "ApplicationCommands" or "EditingCommands" or "NavigationCommands" or "MediaCommands" or "ComponentCommands";
    }
}
