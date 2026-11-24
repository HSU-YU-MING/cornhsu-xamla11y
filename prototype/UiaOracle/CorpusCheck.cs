using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlMarkup = XamlA11y.Core.Xaml.Markup;

namespace UiaOracle;

/// <summary>
/// 真實專案的形狀：每個要有名稱的元素連同子樹取出來，只留跟名稱有關的屬性，放進真的 WPF 讀 UIA 名稱，
/// 跟工具對**同一段**的判定比。需要專案資源（StaticResource）或第三方控制項的取不出來，數出來就好。
/// </summary>
internal static class CorpusCheck
{
    private static readonly HashSet<string> KeepAttributes = new(StringComparer.Ordinal)
    { "Content", "Header", "Text", "ToolTip", "FontFamily", "Orientation", "Data", "AutomationProperties.Name", "AutomationProperties.HelpText" };

    private static readonly XNamespace Wpf = XamlMarkup.WpfNamespace;

    public static int Run(string[] roots)
    {
        int taken = 0, skipped = 0, wrong = 0, ok = 0, unknown = 0, notInTree = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var project = Project.Load(root, []);
            var audit = Auditor.Run(project);
            var batch = new List<(string Where, XElement Element)>();
            foreach (var j in audit.Judgements.Where(j => j.Check is "control-names" or "input-names" or "list-names" && j.Verdict is Verdict.Pass or Verdict.Finding))
            {
                if (Extract(j.Element) is not { } e) { skipped++; continue; }
                if (!seen.Add(e.ToString(SaveOptions.DisableFormatting))) continue;
                taken++;
                batch.Add(($"{Path.GetFileName(root.TrimEnd('/', '\\'))}/{j.File}:{j.Line}", e));
            }

            for (var start = 0; start < batch.Count; start += 40)
            {
                var part = batch.Skip(start).Take(40).ToList();
                for (var i = 0; i < part.Count; i++) part[i].Element.SetAttributeValue(XNamespace.Get(XamlMarkup.XamlNamespace) + "Name", "p" + i);
                Compare(part, ref ok, ref wrong, ref unknown, ref notInTree, ref skipped);
            }
        }
        Console.WriteLine($"corpus shapes: {taken} distinct, ok {ok}, unknown {unknown}, not in tree {notInTree}, could not extract {skipped}, WRONG {wrong}");
        return wrong;
    }

    private static void Compare(List<(string Where, XElement Element)> part, ref int ok, ref int wrong, ref int unknown, ref int notInTree, ref int skipped)
    {
        // 一批裡有一個載不進來就一個一個載
        string Body(IEnumerable<(string, XElement E)> list) => string.Concat(list.Select(x => Wrap(x.E)));
        var xaml = $"<Window {Host.Ns}><StackPanel x:Name=\"host\">{Body(part.Select(p => (p.Where, p.Element)))}</StackPanel></Window>";
        Window window;
        try
        {
            var parsed = XElement.Parse(xaml, LoadOptions.PreserveWhitespace);
            // 只留 WPF 與 x 兩個命名空間；過最後一關才解析（別人的 XAML）
            foreach (var a in parsed.Attributes().Where(a => a.IsNamespaceDeclaration && a.Value != XamlMarkup.WpfNamespace && a.Value != XamlMarkup.XamlNamespace).ToList()) a.Remove();
            window = (Window)SafeMarkup.Parse(parsed, WindowCheck.TypeNamed);
        }
        catch (Exception ex)
        {
            if (part.Count == 1)
            {
                skipped++;
                if (Environment.GetEnvironmentVariable("ORACLE_VERBOSE") is not null) Console.WriteLine($"  not loadable {part[0].Where}: {ex.GetBaseException().Message.Split('\n')[0]}");
                return;
            }
            foreach (var one in part) Compare([one], ref ok, ref wrong, ref unknown, ref notInTree, ref skipped);
            return;
        }

        var result = Auditor.Run(Project.FromText(xaml));
        using var shown = Host.Show(window, 420, 300);
        var tree = Host.Tree(shown.Handle).Where(n => n.Id.Length > 0).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var j in result.Judgements.Where(j => j.Check is "control-names" or "input-names" or "list-names"))
        {
            if (XamlMarkup.NameOf(j.Element) is not { } id || !id.StartsWith('p') || !int.TryParse(id[1..], out var i)) continue;
            if (j.Verdict is Verdict.Blocked or Verdict.NotJudged) { unknown++; continue; }
            if (!tree.TryGetValue(id, out var node)) { notInTree++; continue; }
            if (Check.Agrees(j, node)) { ok++; continue; }
            wrong++;
            Console.WriteLine($"  WRONG {part[i].Where}: tool {j.Verdict} {j.Kind?.Slug} — UIA name {node.NameShown} ({node.Type})\n        {Short(part[i].Element)}");
        }
    }

    private static string Short(XElement e)
    {
        var s = e.ToString(SaveOptions.DisableFormatting);
        return s.Length <= 400 ? s : s[..400];
    }

    /// <summary>項目要放進它的清單裡。</summary>
    private static string Wrap(XElement e)
    {
        var xml = e.ToString(SaveOptions.DisableFormatting);
        return e.Name.LocalName switch
        {
            "MenuItem" => $"<Menu>{xml}</Menu>",
            "TabItem" => $"<TabControl>{xml}</TabControl>",
            "ListBoxItem" => $"<ListBox>{xml}</ListBox>",
            "ListViewItem" => $"<ListView>{xml}</ListView>",
            "TreeViewItem" => $"<TreeView>{xml}</TreeView>",
            "ComboBoxItem" => $"<ComboBox IsDropDownOpen=\"False\">{xml}</ComboBox>",
            "Hyperlink" => $"<TextBlock>{xml}</TextBlock>",
            _ => xml,
        };
    }

    private static XElement? Extract(XElement e)
    {
        if (e.Name.Namespace != Wpf || e.Name.LocalName == "ComboBoxItem") return null;
        // 清單、表格、樹：名稱跟裡面的東西無關，只留自己（欄位定義多半要專案的型別）
        if (e.Name.LocalName is "ListBox" or "ListView" or "TreeView" or "DataGrid") return Clean(new XElement(e.Name, e.Attributes().Where(a => !a.IsNamespaceDeclaration)), e.Name.LocalName);
        var copy = Clean(e, e.Name.LocalName);
        return copy;
    }

    private static XElement? Clean(XElement e, string owner)
    {
        if (e.Name.Namespace != Wpf) return null;
        var copy = new XElement(e.Name);
        foreach (var a in e.Attributes())
        {
            if (a.IsNamespaceDeclaration || !KeepAttributes.Contains(a.Name.LocalName)) continue;
            if (a.Name.LocalName == "Data" && XamlMarkup.IsMarkup(a.Value)) { copy.SetAttributeValue("Data", "M0,0 L9,9 Z"); continue; }
            if (XamlMarkup.IsMarkup(a.Value))
            {
                // 名稱有關的屬性是資源或綁定：換成字面字串（形狀不變，值有了）
                if (a.Name.LocalName is "Content" or "Header" && XamlMarkup.ExtensionName(a.Value) is "StaticResource") return null;
                if (a.Name.LocalName is "FontFamily" or "Orientation") continue;
                copy.SetAttributeValue(a.Name, "value");
                continue;
            }
            copy.SetAttributeValue(a.Name, a.Value);
        }
        foreach (var node in e.Nodes())
        {
            if (node is XText t) { copy.Add(new XText(t.Value)); continue; }
            if (node is not XElement c) continue;
            if (c.Name.Namespace != Wpf) return null;
            if (XamlMarkup.IsPropertyElement(c))
            {
                var property = c.Name.LocalName[(c.Name.LocalName.IndexOf('.') + 1)..];
                if (property is not ("Content" or "Header" or "Inlines" or "Children" or "Child")) continue;
                if (property is "Header" && c.Name.LocalName.StartsWith("ToolTip", StringComparison.Ordinal)) continue;
                var holder = new XElement(c.Name);
                foreach (var inner in c.Elements())
                {
                    if (Clean(inner, owner) is not { } cleaned) return null;
                    holder.Add(cleaned);
                }
                if (!c.HasElements) holder.Add(c.Value);
                copy.Add(holder);
                continue;
            }
            if (c.Name.LocalName is "Style" or "Binding" or "MultiBinding" or "StaticResource" or "DynamicResource" or "DataTemplate") return null;
            // 內容整棵留著；Expander／TabItem 的頁面內容跟名稱無關
            if (owner is "Expander" or "TabItem" or "MenuItem" or "TreeViewItem" && ReferenceEquals(c.Parent, e) && e.Name.LocalName == owner) continue;
            if (Clean(c, owner) is not { } child) return null;
            copy.Add(child);
        }
        return copy;
    }
}
