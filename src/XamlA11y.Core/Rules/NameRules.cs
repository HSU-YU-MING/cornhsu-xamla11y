using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>R1（互動元件的名稱）與 R2（輸入框的名稱）。</summary>
internal static class NameRules
{
    /// <summary>這些控制項範本裡的零件不在 UIA 樹上、或 Axe-Windows 不報（實測，docs/M1-UIA實測.md 第 3 節）。</summary>
    private static readonly HashSet<string> SilentTemplates = new(StringComparer.Ordinal) { "ComboBox", "ScrollBar", "Slider" };

    public static void Check(Context ctx, XamlDoc doc, XElement e)
    {
        var control = ctx.Project.ControlOf(e);
        var local = e.Name.LocalName;

        if (control is null)
        {
            // 行為上知道是什麼、名稱不知道（類別自己換了 AutomationPeer、同名類別不只一個）
            var behaves = ctx.Project.BaseOf(e);
            if (behaves is not null)
            {
                if (Wpf.Interactive.Contains(behaves)) ctx.Blocked("R1", e, "a class that sets its own accessible name in code", local);
                else if (Wpf.Inputs.Contains(behaves)) ctx.Blocked("R2", e, "a class that sets its own accessible name in code", local);
                return;
            }
            // 不認得的控制項：名字看起來是互動元件才算候選（其他的連是不是控制項都不知道）
            if (LooksInteractive(local)) ctx.Blocked("R1", e, "a control this tool does not know (declare it in treatAs)", local);
            else if (LooksInput(local)) ctx.Blocked("R2", e, "a control this tool does not know (declare it in treatAs)", local);
            return;
        }

        var rule = Wpf.Interactive.Contains(control) ? "R1" : Wpf.Inputs.Contains(control) ? "R2" : null;
        if (rule is null)
        {
            // WPF 命名空間裡沒量過的控制項（RibbonButton、RibbonTextBox…）：不知道，不是沒事
            if (Markup.IsWpf(e) && Wpf.NameSourceOf(control) == NameSource.Unknown && !Markup.IsPropertyElement(e) && e.Parent is not null)
            {
                if (LooksInteractive(local)) ctx.Blocked("R1", e, "a control that was not measured", local);
                else if (LooksInput(local)) ctx.Blocked("R2", e, "a control that was not measured", local);
            }
            return;
        }

        // x:Class 的根（<Button x:Class="App.IconButton">）是定義，不是用法：用它的地方才判
        if (e.Parent is null) return;

        if (Context.ControlTemplateOf(e) is { } template)
        {
            var target = ctx.TemplateTarget(template);
            if (target is not null && SilentTemplates.Contains(ctx.Project.BaseOfName(target)))
            {
                ctx.NotJudged(rule, e, $"a part of a {target} template (not exposed as a control of its own)");
                return;
            }
            if (ctx.IsFalse(e, "Focusable"))
            {
                ctx.NotJudged(rule, e, "a template part that cannot take focus");
                return;
            }
            if (rule == "R2" && Markup.NameOf(e) is { } part && part.StartsWith("PART_", StringComparison.Ordinal))
            {
                ctx.NotJudged(rule, e, "a template part that its control names in code");
                return;
            }
        }

        var name = ctx.Names.Resolve(e, control);
        switch (name.Kind)
        {
            case NameKind.Named: ctx.Pass(rule, e); return;
            case NameKind.DataBound: ctx.NotJudged(rule, e, "the name comes from data at run time"); return;
            case NameKind.Unknown:
                ctx.Blocked(rule, e, name.Why ?? "unknown", UnknownContent(ctx, e, name));
                return;
            case NameKind.SymbolOnly:
                ctx.Report(Kinds.SymbolName, e, $"<{local}> is named only by the symbol \"{Context.Show(name.Text)}\"", rule: rule);
                return;
        }

        // 範本零件（PART_）沒寫內容：控制項的程式碼會填（Playnite 的 PART_ButtonVersion）
        if (name.Kind == NameKind.Missing && Context.ControlTemplateOf(e) is not null && Markup.NameOf(e) is { } p && p.StartsWith("PART_", StringComparison.Ordinal)
            && Markup.ContentElements(e).Count == 0 && e.Attribute("Content") is null)
        {
            ctx.NotJudged(rule, e, "a template part that its control fills in code");
            return;
        }

        // 收合的項目：藏起標籤列的精靈頁（TabItem Visibility="Collapsed"）、佔位的項目
        if (Wpf.Items.Contains(control) && (string?)e.Attribute("Visibility") is "Collapsed" or "Hidden")
        {
            ctx.NotJudged(rule, e, "a collapsed item (its header is never shown)");
            return;
        }

        // 鍵盤到不了的（Focusable=False、明寫停用）：Axe-Windows 不報，螢幕閱讀器還是碰得到 → 降一級
        Severity? lowered = ctx.IsFalse(e, "Focusable") || string.Equals((string?)e.Attribute("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase) ? Severity.Warn : null;

        if (rule == "R2" && name.Kind != NameKind.Meaningless)
        {
            ctx.Report(Kinds.InputNoName, e, $"<{local}> has no accessible name{HintFor(ctx, e)}", lowered: lowered);
            return;
        }

        if (name.Kind == NameKind.TextNotUsed)
        {
            ctx.Report(Kinds.TextNotName, e, $"<{local}> shows {(name.Text == "{…}" ? "bound text" : $"\"{Context.Show(name.Text)}\"")} but has no accessible name: {name.Why}", lowered: lowered);
            return;
        }

        if (name.Kind == NameKind.Meaningless)
        {
            ctx.Report(Kinds.MeaninglessName, e, $"<{local}> has an unreadable accessible name{(name.Text is { Length: > 0 } t && t.Trim().Length > 0 ? $" ({Context.Show(t)})" : "")}: {name.Why}", lowered: lowered, rule: rule);
            return;
        }

        var tip = ctx.Project.Styles.Resolve(e, "ToolTip");
        // 自訂控制項自己的文字屬性（ScreenToGif 的 ExtendedButton Text="…"）：畫面上有字，WPF 不會拿它當名稱
        var own = Markup.IsWpf(e) ? null : OwnTextProperties.FirstOrDefault(p => !string.IsNullOrWhiteSpace((string?)e.Attribute(p)));
        var onlyTip = own is not null ? $"; its {own} property belongs to the class and is not the name"
            : tip.IsSet ? "; its ToolTip is the help text, not the name" : "";
        ctx.Report(Kinds.MissingName, e, $"<{local}> has no accessible name: {name.Why}{onlyTip}", lowered: lowered);
    }

    private sealed record LabelTargets(Dictionary<string, XElement> ByName);

    private static readonly string[] OwnTextProperties = ["Text", "Title", "Label", "Caption", "Header"];

    private static string? UnknownContent(Context ctx, XElement e, NameResult name)
    {
        if (name.Why is null || !name.Why.Contains("does not know", StringComparison.Ordinal)) return null;
        return Markup.ContentElements(e).FirstOrDefault(c => ctx.Project.ControlOf(c) is null)?.Name.LocalName;
    }

    /// <summary>旁邊有沒有現成的標籤可以接（Label Target 是最常見的「以為夠了」）。</summary>
    private static string HintFor(Context ctx, XElement e)
    {
        if (Markup.NameOf(e) is { Length: > 0 } name && e.Document?.Root is { } root)
        {
            if (e.Document.Annotation<LabelTargets>() is not { } targets)
            {
                targets = new LabelTargets(new Dictionary<string, XElement>(StringComparer.Ordinal));
                foreach (var label in root.Descendants().Where(d => d.Name.LocalName == "Label"))
                    if (Markup.ReferencedElement((string?)label.Attribute("Target")) is { } t) targets.ByName.TryAdd(t, label);
                e.Document.AddAnnotation(targets);
            }
            if (targets.ByName.TryGetValue(name, out var found))
                return $"; <Label Target> at line {Context.LineOf(found)} does not give it one (use AutomationProperties.LabeledBy)";
        }

        if (ctx.Project.Styles.Resolve(e, "ToolTip").IsSet) return "; its ToolTip is the help text, not the name";

        for (var node = Markup.Previous(e); node is not null; node = Markup.Previous(node))
        {
            if (node is not XElement n) continue;
            if (Markup.IsPropertyElement(n)) continue;
            var text = n.Name.LocalName switch
            {
                "TextBlock" => (string?)n.Attribute("Text") ?? n.Value,
                "Label" => (string?)n.Attribute("Content") ?? n.Value,
                _ => null,
            };
            return string.IsNullOrWhiteSpace(text) ? "" : $"; the {n.Name.LocalName} \"{Context.Show(text.Trim())}\" before it could label it (AutomationProperties.LabeledBy)";
        }
        return "";
    }

    internal static bool LooksInteractive(string local) =>
        local.EndsWith("Button", StringComparison.Ordinal) || local.EndsWith("CheckBox", StringComparison.Ordinal)
        || local.EndsWith("ToggleSwitch", StringComparison.Ordinal) || local.EndsWith("MenuItem", StringComparison.Ordinal) || local.EndsWith("TabItem", StringComparison.Ordinal)
        || local.EndsWith("Expander", StringComparison.Ordinal) || local.EndsWith("Hyperlink", StringComparison.Ordinal);

    internal static bool LooksInput(string local) =>
        local.EndsWith("TextBox", StringComparison.Ordinal) || local.EndsWith("PasswordBox", StringComparison.Ordinal) || local.EndsWith("ComboBox", StringComparison.Ordinal)
        || local.EndsWith("Slider", StringComparison.Ordinal) || local.EndsWith("DatePicker", StringComparison.Ordinal) || local.EndsWith("NumericUpDown", StringComparison.Ordinal)
        || local.EndsWith("NumberBox", StringComparison.Ordinal) || local.EndsWith("UpDown", StringComparison.Ordinal);
}
