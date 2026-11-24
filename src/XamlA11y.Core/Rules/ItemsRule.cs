using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// item-type-name：資料產生的清單項目，名稱是資料物件的 ToString()（實測 facts/items.txt）。沒覆寫的類別念出來是型別名。
/// DisplayMemberPath、ItemTemplate、TextSearch.TextPath 都只管畫面，不管名稱；
/// 只有容器樣式設的 AutomationProperties.Name（或 Content／Header）才算。
/// </summary>
internal static class ItemsRule
{
    /// <summary>清單 → 容器、容器的內容屬性。</summary>
    private static (string Container, string Content)? ContainerOf(string control) => control switch
    {
        "ListBox" => ("ListBoxItem", "Content"),
        "ListView" => ("ListViewItem", "Content"),
        "ComboBox" => ("ComboBoxItem", "Content"),
        "TreeView" => ("TreeViewItem", "Header"),
        "TabControl" => ("TabItem", "Header"),
        "Menu" or "ContextMenu" or "MenuItem" => ("MenuItem", "Header"),
        "DataGrid" => ("DataGridRow", ""),
        _ => null,
    };

    public static void Check(Context ctx, XElement e)
    {
        if (!Markup.IsWpf(e) || e.Parent is null || ContainerOf(e.Name.LocalName) is not var (container, content)) return;
        var styles = ctx.Project.Styles;
        var source = styles.LocalValue(e, "ItemsSource");
        // 直接寫在裡面的資料物件（<local:Note Title="…"/>）跟 ItemsSource 一樣
        var inline = source.IsSet ? [] : Markup.ContentElements(e).Concat(Markup.PropertyElement(e, "Items")?.Elements() ?? [])
            .Where(c => !Markup.IsWpf(c) && KnownControls.LibraryOf(c) is null && ctx.Project.BaseOf(c) is null).ToList();
        if (!source.IsSet && inline.Count == 0) return;
        // 範本裡的清單：容器樣式常由控制項給
        if (Context.ControlTemplateOf(e) is not null) { ctx.NotJudged(Checks.ItemNames, e, "a template part"); return; }

        if (Named(ctx, e, container, content) is { } named)
        {
            if (named) ctx.Pass(Checks.ItemNames, e);
            else ctx.NotJudged(Checks.ItemNames, e, "its container style may name the items");
            return;
        }

        var (type, reading) = source.IsSet ? ItemType(ctx, e, source)
            : inline.Select(c => ctx.Project.Code.ReadingOf(c.Name.LocalName)).Distinct().ToList() is [var only] ? (inline[0].Name.LocalName, only) : (null, Reading.Unknown);
        switch (reading)
        {
            case Reading.Text: ctx.Pass(Checks.ItemNames, e); return;
            case Reading.Unknown: ctx.NotJudged(Checks.ItemNames, e, "what its items are is not visible"); return;
        }

        // 樣式在專案外時每個屬性都「讀不出來」，那不是有設
        bool Has(string property) => styles.Resolve(e, property) is { IsSet: true, IsNull: false, Origin: not null };
        var shown = Has("DisplayMemberPath") ? "; DisplayMemberPath only changes what is shown"
            : Has("ItemTemplate") ? "; the ItemTemplate only changes what is shown" : "";
        ctx.Report(Kinds.ItemTypeName, e,
            $"<{e.Name.LocalName}> names every item with its type name{(type is null ? "" : $" ({Context.Show(type)})")}: no style of {container} sets AutomationProperties.Name, and the class does not override ToString(){shown}");
    }

    /// <summary>容器樣式有沒有給名稱：true 有、false 可能有（看不懂）、null 沒有。</summary>
    private static bool? Named(Context ctx, XElement e, string container, string content)
    {
        var styles = ctx.Project.Styles;
        if (styles.LocalValue(e, "ItemContainerStyleSelector").IsSet || styles.LocalValue(e, "RowStyleSelector").IsSet) return false;
        if (ctx.Project.CodeSets(e, "ItemContainerStyle", "RowStyle", "ItemContainerStyleSelector")) return false;
        // 清單的樣式在專案外：它可能設了 ItemContainerStyle。量過的佈景不會在裡面設名稱
        var maybe = styles.Resolve(e, "ItemContainerStyle") is { IsUnreadable: true, Origin: null } && !ctx.Project.ThemesOf(StyleIndex.FileOf(e)).Any(t => t.LibraryId is not null);
        foreach (var chain in styles.ContainerChains(e, container))
        {
            foreach (var property in (ReadOnlySpan<string>)["AutomationProperties.Name", "AutomationProperties.LabeledBy", content])
            {
                if (property.Length == 0 || chain.Setter(property) is not { } setter) continue;
                var v = styles.SetterValue(setter);
                // {Binding} 綁整個物件：還是 ToString()
                if (v.IsNull || v is { IsValue: true, Value: "" } || Markup.BindingPath(v.Value) is "" or "." && Markup.Argument(v.Value!, "Converter") is null && Markup.Argument(v.Value!, "StringFormat") is null) continue;
                return true;
            }
            // 觸發器裡才設的：某些狀態有名稱
            maybe |= chain.Styles.Any(s => s.Triggers.SelectMany(t => t.Descendants()).Any(x => x.Name.LocalName == "Setter" && (string?)x.Attribute("Property") is { } p && StyleIndex.NormalizeProperty(p) == "AutomationProperties.Name"));
            if (chain.Unknown is not null && chain.Styles.Count == 0 && styles.LocalValue(e, "ItemContainerStyle").IsUnreadable) maybe = true;
        }
        return maybe ? false : null;
    }

    private static (string? Type, Reading Reading) ItemType(Context ctx, XElement e, Resolved source)
    {
        // 範本寫明了型別的最準
        var template = ctx.Project.Styles.Resolve(e, "ItemTemplate");
        if (template.Element is { } t && Markup.TypeName((string?)t.Attribute("DataType")) is { } declared)
            return (declared, ctx.Project.Code.ReadingOf(declared));
        if (source.Element is not null || source.Value is not { } raw || Markup.ExtensionName(raw) is not ("Binding" or "x:Bind")) return (null, Reading.Unknown);
        // 綁到別的元素、靜態資源的：路徑不是檢視模型的屬性
        if (Markup.Arguments(raw).Any(a => a.Key is "ElementName" or "Source" or "Converter" or "XPath")) return (null, Reading.Unknown);
        var path = Markup.BindingPath(raw) ?? "";
        var last = path[(path.LastIndexOf('.') + 1)..];
        return last.Length == 0 || !last.All(c => char.IsLetterOrDigit(c) || c == '_') ? (null, Reading.Unknown) : ctx.Project.Code.ItemsOf(last);
    }
}
