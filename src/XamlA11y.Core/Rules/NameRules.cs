using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>控制項、輸入框、清單與進度條的名稱（最後一項是警告）。</summary>
internal static class NameRules
{
    /// <summary>這些控制項範本裡的零件不在 UIA 樹上、或 Axe-Windows 不報（實測，docs/WPF實測.md 第 3 節）。</summary>
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
                if (Wpf.Interactive.Contains(behaves)) ctx.Blocked(Checks.ControlNames, e, "a class that sets its own accessible name in code", local);
                else if (Wpf.Inputs.Contains(behaves)) ctx.Blocked(Checks.InputNames, e, "a class that sets its own accessible name in code", local);
                else if (Wpf.Lists.Contains(behaves)) ctx.Blocked(Checks.ListNames, e, "a class that sets its own accessible name in code", local);
                return;
            }
            // 不認得的控制項：名字看起來是互動元件才算候選（其他的連是不是控制項都不知道）
            if (LooksInteractive(local)) ctx.Blocked(Checks.ControlNames, e, "a control this tool does not know (declare it in treatAs)", local);
            else if (LooksInput(local)) ctx.Blocked(Checks.InputNames, e, "a control this tool does not know (declare it in treatAs)", local);
            else if (LooksList(local)) ctx.Blocked(Checks.ListNames, e, "a control this tool does not know (declare it in treatAs)", local);
            return;
        }

        // NVDA 把有欄位的 ListView 念成資料格（實跑）
        var spoken = SpokenAs(ctx, e, control);

        var check = Wpf.Interactive.Contains(control) ? Checks.ControlNames : Wpf.Inputs.Contains(control) ? Checks.InputNames : Wpf.Lists.Contains(control) || Wpf.Widgets.Contains(control) ? Checks.ListNames : null;
        if (check is null)
        {
            // WPF 命名空間裡沒量過的控制項（RibbonButton、RibbonTextBox…）：不知道，不是沒事
            if (Markup.IsWpf(e) && Wpf.NameSourceOf(control) == NameSource.Unknown && !Markup.IsPropertyElement(e) && e.Parent is not null)
            {
                if (LooksInteractive(local)) ctx.Blocked(Checks.ControlNames, e, "a control that was not measured", local);
                else if (LooksInput(local)) ctx.Blocked(Checks.InputNames, e, "a control that was not measured", local);
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
                ctx.NotJudged(check, e, $"a part of a {target} template (not exposed as a control of its own)");
                return;
            }
            if (ctx.IsFalse(e, "Focusable"))
            {
                ctx.NotJudged(check, e, "a template part that cannot take focus");
                return;
            }
            if (check is Checks.InputNames or Checks.ListNames && Markup.NameOf(e) is { } part && part.StartsWith("PART_", StringComparison.Ordinal))
            {
                ctx.NotJudged(check, e, "a template part that its control names in code");
                return;
            }
        }

        var name = ctx.Names.Resolve(e, control);
        switch (name.Kind)
        {
            case NameKind.Named: ctx.Pass(check, e); return;
            case NameKind.DataBound: ctx.NotJudged(check, e, "the name comes from data at run time"); return;
            case NameKind.Unknown:
                ctx.Blocked(check, e, name.Why ?? "unknown", UnknownContent(ctx, e, name));
                return;
            case NameKind.SymbolOnly:
                ctx.Report(Kinds.SymbolName, e, $"<{local}> is named only by the symbol \"{Context.Show(name.Text)}\"", check: check);
                return;
        }

        // 範本零件（PART_）沒寫內容：控制項的程式碼會填（Playnite 的 PART_ButtonVersion）
        if (name.Kind == NameKind.Missing && Context.ControlTemplateOf(e) is not null && Markup.NameOf(e) is { } p && p.StartsWith("PART_", StringComparison.Ordinal)
            && Markup.ContentElements(e).Count == 0 && e.Attribute("Content") is null)
        {
            ctx.NotJudged(check, e, "a template part that its control fills in code");
            return;
        }

        // 收合的項目：藏起標籤列的精靈頁（TabItem Visibility="Collapsed"）、佔位的項目
        if (Wpf.Items.Contains(control) && (string?)e.Attribute("Visibility") is "Collapsed" or "Hidden")
        {
            ctx.NotJudged(check, e, "a collapsed item (its header is never shown)");
            return;
        }

        // 鍵盤到不了的（Focusable=False、明寫停用）：Axe-Windows 不報，螢幕閱讀器還是碰得到 → 降一級
        // 寫死收合、程式碼也沒有打開它的：現在沒有人看得到
        var note = ctx.IsFalse(e, "Focusable") ? "; a warning because it cannot take focus"
            : string.Equals((string?)e.Attribute("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase) ? "; a warning because it is disabled"
            : (string?)e.Attribute("Visibility") is "Collapsed" or "Hidden" && !ctx.Project.CodeSets(e, "Visibility") ? "; a warning because it is hidden"
            : "";
        Severity? lowered = note.Length > 0 ? Severity.Warn : null;
        // 警告的種類降下來是 info，不用說「是警告」
        if (check == Checks.ListNames) note = "";

        if (Wpf.Widgets.Contains(control))
        {
            // 範本裡的進度條：控制項的一部分；ContextMenu 不在這裡
            if (Context.ControlTemplateOf(e) is not null) { ctx.NotJudged(check, e, "a template part"); return; }
            ctx.Report(Kinds.WidgetNoName, e, $"<{local}> has no accessible name{(control is "Menu" or "ToolBar" ? "" : HintFor(ctx, e))}", lowered: control is "Menu" or "ToolBar" || lowered is not null ? Severity.Info : null, unreadable: name.Kind == NameKind.Meaningless);
            return;
        }

        if (check == Checks.ListNames)
        {
            ctx.Report(Kinds.ListNoName, e, name.Kind == NameKind.Meaningless
                ? $"<{local}> has an unreadable accessible name: {name.Why}"
                : $"<{local}> has no accessible name{HintFor(ctx, e)}", lowered: lowered is null ? null : Severity.Info, control: name.Kind == NameKind.Meaningless ? null : spoken, unreadable: name.Kind == NameKind.Meaningless);
            return;
        }

        if (check == Checks.InputNames && name.Kind != NameKind.Meaningless)
        {
            ctx.Report(Kinds.InputNoName, e, $"<{local}> has no accessible name{HintFor(ctx, e)}{note}", lowered: lowered, control: spoken);
            return;
        }

        if (name.Kind == NameKind.TextNotUsed)
        {
            ctx.Report(Kinds.TextNotName, e, $"<{local}> shows {(name.Text == "{…}" ? "bound text" : $"\"{Context.Show(name.Text)}\"")} but has no accessible name: {name.Why}{note}", lowered: lowered, control: spoken);
            return;
        }

        if (name.Kind == NameKind.Meaningless)
        {
            // 項目容器沒文字：名稱是型別名（自己的子類別是它自己的型別名，念法不猜）
            var typeName = Wpf.Items.Contains(control) && spoken is not null && name.Text is null && name.Why?.EndsWith("type name", StringComparison.Ordinal) == true;
            ctx.Report(Kinds.MeaninglessName, e, $"<{local}> has an unreadable accessible name{(name.Text is { Length: > 0 } t && t.Trim().Length > 0 ? $" ({Context.Show(t)})" : "")}: {name.Why}{note}", lowered: lowered, check: check, control: typeName ? control : null, spokenName: typeName ? control : null);
            return;
        }

        // 自訂控制項自己的文字屬性（ScreenToGif 的 ExtendedButton Text="…"）：畫面上有字，WPF 不會拿它當名稱
        var own = Markup.IsWpf(e) ? null : OwnTextProperties.FirstOrDefault(p => !string.IsNullOrWhiteSpace((string?)e.Attribute(p)));
        var onlyTip = own is not null ? $"; its {own} property belongs to the class and is not the name"
            : HasToolTip(ctx, e) ? "; its ToolTip is the help text, not the name" : "";
        ctx.Report(Kinds.MissingName, e, $"<{local}> has no accessible name: {name.Why}{onlyTip}{note}", lowered: lowered, control: spoken);
    }

    /// <summary>NVDA 會把它念成哪一種控制項；不確定（不是 WPF 自己的類別、停用或不能聚焦、有快速鍵要念、別的 View）是 null。</summary>
    private static string? SpokenAs(Context ctx, XElement e, string control)
    {
        var styles = ctx.Project.Styles;
        if (!Markup.IsWpf(e) || e.Name.LocalName != control) return null;
        if (ctx.IsFalse(e, "Focusable") || ctx.IsFalse(e, "IsTabStop") || styles.Resolve(e, "IsEnabled") is { Kind: not ResolveKind.NotSet, IsTrue: false }) return null;
        if (styles.Resolve(e, "AutomationProperties.AccessKey").IsSet || styles.Resolve(e, "AutomationProperties.AcceleratorKey").IsSet) return null;
        if (e.Descendants().Any(d => d.Name.LocalName == "AccessText")) return null;
        if (control != "ListView") return control;
        // 有欄位的念成資料格（實跑），別的 View 沒跑過
        var view = styles.Resolve(e, "View");
        return view.Kind == ResolveKind.NotSet || view.IsNull ? control : view.Element is { Name.LocalName: "GridView" } g && Markup.IsWpf(g) ? "DataGrid" : null;
    }

    /// <summary>真的有設 ToolTip。樣式在專案外時每個屬性都「讀不出來」，那不是有設（Origin 是 null）。</summary>
    private static bool HasToolTip(Context ctx, XElement e) => ctx.Project.Styles.Resolve(e, "ToolTip") is { IsSet: true, IsNull: false, Origin: not null };

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

        // <GroupBox Header="Files"><ListBox/></GroupBox>：看起來有標題，UIA 的名稱還是空的（實測）
        if (e.Ancestors().FirstOrDefault(a => !Markup.IsPropertyElement(a)) is { Name.LocalName: "GroupBox" } group && (string?)group.Attribute("Header") is { } header && !string.IsNullOrWhiteSpace(header) && !Markup.IsMarkup(header))
            return $"; the GroupBox header \"{Context.Show(header.Trim())}\" around it does not give it one";

        if (HasToolTip(ctx, e)) return "; its ToolTip is the help text, not the name";

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
            return string.IsNullOrWhiteSpace(text) || !LooksLikeLabel(ctx, n, e, text.Trim()) ? ""
                : $"; if the {n.Name.LocalName} {(Markup.IsMarkup(text) ? "" : $"\"{Context.Show(text.Trim())}\" ")}at line {Context.LineOf(n)} is its label, use AutomationProperties.LabeledBy";
        }
        return "";
    }

    /// <summary>
    /// 前面那段字像不像標籤。不像的不提：照著接上去就看不到問題了，名稱卻是錯的
    /// （實際遇到：「暫無資料！」、「右鍵刪除…」、「不再提醒」的說明）。
    /// </summary>
    private static bool LooksLikeLabel(Context ctx, XElement label, XElement field, string text)
    {
        if (label.Attribute("Visibility") is not null || ctx.Project.CodeSets(label, "Visibility", "Text")) return false;
        if ((string?)label.Attribute("DockPanel.Dock") is "Bottom" or "Right") return false;
        if (Markup.IsMarkup(text)) return true;
        // 表情符號、箭頭；一整句話
        if (!text.Any(char.IsLetter) || text.Length > 30 || text.Contains('\n', StringComparison.Ordinal) || text[^1] is '.' or '。' or '!' or '！' or '…') return false;
        // 格子裡：同一列，或正上方
        int Cell(XElement x, string p) => int.TryParse((string?)x.Attribute(p), out var n) ? n : 0;
        if (label.Parent?.Name.LocalName != "Grid") return true;
        int rows = Cell(field, "Grid.Row") - Cell(label, "Grid.Row"), columns = Cell(field, "Grid.Column") - Cell(label, "Grid.Column");
        return rows == 0 || rows == 1 && columns == 0;
    }

    internal static bool LooksInteractive(string local) =>
        local.EndsWith("Button", StringComparison.Ordinal) || local.EndsWith("CheckBox", StringComparison.Ordinal)
        || local.EndsWith("ToggleSwitch", StringComparison.Ordinal) || local.EndsWith("MenuItem", StringComparison.Ordinal) || local.EndsWith("TabItem", StringComparison.Ordinal)
        || local.EndsWith("Expander", StringComparison.Ordinal) || local.EndsWith("Hyperlink", StringComparison.Ordinal);

    internal static bool LooksList(string local) =>
        local.EndsWith("ListBox", StringComparison.Ordinal) || local.EndsWith("ListView", StringComparison.Ordinal)
        || local.EndsWith("TreeView", StringComparison.Ordinal) || local.EndsWith("DataGrid", StringComparison.Ordinal);

    internal static bool LooksInput(string local) =>
        local.EndsWith("TextBox", StringComparison.Ordinal) || local.EndsWith("PasswordBox", StringComparison.Ordinal) || local.EndsWith("ComboBox", StringComparison.Ordinal)
        || local.EndsWith("Slider", StringComparison.Ordinal) || local.EndsWith("DatePicker", StringComparison.Ordinal) || local.EndsWith("NumericUpDown", StringComparison.Ordinal)
        || local.EndsWith("NumberBox", StringComparison.Ordinal) || local.EndsWith("UpDown", StringComparison.Ordinal);
}
