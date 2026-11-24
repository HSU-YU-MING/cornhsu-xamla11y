using System.Globalization;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// R10：同一個容器裡、同一種控制項、名稱一樣、做的事不一樣（「…」「…」、兩顆「編輯」）。只是 info：前後文可能分得出來。
/// 只看名稱的寫法一模一樣（同樣的字、同一個資源）、而且看得出來做的事不同的；會輪流出現的（Visibility、觸發器）、同一個動作放兩份的都不算。
/// 一組報一次，鍵是這一組的名稱：多加一顆同名的，舊的那一筆不會變成新的。
/// </summary>
internal static class SameNameRule
{
    private static readonly HashSet<string> Changing = new(StringComparer.Ordinal) { "Visibility", "IsEnabled", "Content", "Header", "AutomationProperties.Name" };

    public static void Check(Context ctx, XElement parent)
    {
        if (Markup.IsPropertyElement(parent) && !parent.Name.LocalName.EndsWith(".Children", StringComparison.Ordinal)) return;
        Dictionary<(string Control, string Name, string Group), List<(XElement Element, string Action)>>? groups = null;
        foreach (var e in parent.Elements())
        {
            if (Markup.IsPropertyElement(e) || ctx.Project.BaseOf(e) is not { } control) continue;
            if (!Wpf.Inputs.Contains(control) && (!Wpf.Interactive.Contains(control) || Wpf.Items.Contains(control))) continue;
            if (ActionOf(e) is not { } action || Context.ControlTemplateOf(e) is not null || Varies(ctx, e) || ctx.IsFalse(e, "Focusable")) continue;
            if (ctx.Names.Resolve(e, control).Kind is not (NameKind.Named or NameKind.SymbolOnly) || Source(ctx, e) is not { } source) continue;
            groups ??= [];
            // 選項按鈕：不同組的「是／否」各有自己的題目
            var key = (control, source, (string?)e.Attribute("GroupName") ?? "");
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add((e, action));
        }
        if (groups is null) return;
        foreach (var ((control, source, _), list) in groups)
        {
            if (list.Select(x => x.Action).Distinct(StringComparer.Ordinal).Count() < 2) continue;
            var lines = string.Join(", ", list.Take(6).Select(x => Context.LineOf(x.Element).ToString(CultureInfo.InvariantCulture))) + (list.Count > 6 ? ", …" : "");
            ctx.Report(Kinds.SameName, list[1].Element,
                $"{list.Count.ToString(CultureInfo.InvariantCulture)} <{list[1].Element.Name.LocalName}> here may sound alike: the accessible name \"{Context.Show(source)}\" at lines {lines}, and they do different things",
                identity: $"{control}={Context.Cut(source, 80)}");
        }
    }

    /// <summary>會藏起來、停用、換字的：兩個不一定同時在。</summary>
    private static bool Varies(Context ctx, XElement e)
    {
        var styles = ctx.Project.Styles;
        if (styles.LocalValue(e, "Visibility").IsSet || string.Equals((string?)e.Attribute("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase)) return true;
        return styles.ChainsOf(e).Any(c => c.Setter("Visibility") is not null || c.Styles.Any(s => s.Triggers.SelectMany(t => t.Descendants()).Any(x =>
            x.Name.LocalName == "Setter" && (string?)x.Attribute("Property") is { } p && Changing.Contains(StyleIndex.NormalizeProperty(p)))));
    }

    /// <summary>
    /// 名稱寫的樣子，照 WPF 的順序：LabeledBy（不比）、AutomationProperties.Name（本地、樣式）、內容。
    /// 資源要比鍵，不能比值：好幾種語言的資源，這裡看到的只是其中一份。綁定的不算。
    /// </summary>
    private static string? Source(Context ctx, XElement e)
    {
        var styles = ctx.Project.Styles;
        if (styles.Resolve(e, "AutomationProperties.LabeledBy").IsSet) return null;
        if (Raw("AutomationProperties.Name") is { } named) return Plain(named, accessKeys: false);
        if (styles.Resolve(e, "AutomationProperties.Name").IsSet) return null;        // 讀不懂的樣式可能有名稱
        foreach (var property in (ReadOnlySpan<string>)["Content", "Header"])
            if (e.Attributes().FirstOrDefault(a => a.Name.LocalName == property) is { } a) return Plain(a.Value, accessKeys: true);
        var content = Markup.ContentElements(e);
        if (content.Count == 0 && Markup.OwnText(e) is { Length: > 0 } own) return Plain(own, accessKeys: true);
        // 只有一段字的 TextBlock；裡面還有 Run、Span 的要接起來才知道
        if (content is [{ Name.LocalName: "TextBlock", HasElements: false } text]) return Plain((string?)text.Attribute("Text") ?? Markup.OwnText(text), accessKeys: false);
        return content.Count == 0 && (Raw("Content") ?? Raw("Header")) is { } styled ? Plain(styled, accessKeys: true) : null;

        string? Raw(string property) =>
            e.Attributes().FirstOrDefault(a => a.Name.LocalName == property)?.Value
            ?? (styles.ChainsOf(e) is [var chain] ? (string?)chain.Setter(property)?.Attribute("Value") : null);

        static string? Plain(string s, bool accessKeys) =>
            Markup.IsMarkup(s) ? Markup.ExtensionName(s) is "StaticResource" or "DynamicResource" or "x:Static" ? s.Trim() : null
            : (accessKeys ? s.Replace("_", "", StringComparison.Ordinal) : s).Trim() is { Length: > 0 } t ? t : null;
    }

    private static string? ActionOf(XElement e)
    {
        var extra = $"|{(string?)e.Attribute("CommandParameter")}|{(string?)e.Attribute("CommandTarget")}|{(string?)e.Attribute("Tag")}";
        if ((string?)e.Attribute("Command") is { } command) return command + extra;
        foreach (var a in (ReadOnlySpan<string>)["Click", "IsChecked", "Text", "SelectedItem", "SelectedValue", "SelectedIndex", "Value", "SelectedDate"])
            if ((string?)e.Attribute(a) is { } v && (a == "Click" || Markup.IsMarkup(v))) return v + extra;
        return null;
    }
}
