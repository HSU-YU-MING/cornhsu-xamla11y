namespace UiaOracle;

/// <summary>清單項目的資料（facts/items.txt 用；XAML 裡的 o:Row）。</summary>
public class Row { public string Title { get; set; } = ""; }
public class NamedRow { public string Title { get; set; } = ""; public override string ToString() => Title; }
public record RecordRow { public string Title { get; set; } = ""; }
public class EmptyRow { public string Title { get; set; } = ""; public override string ToString() => ""; }
public struct StructRow { public string Title { get; set; } }

// 自訂控制項（facts/silent.txt）：哪些基底有 AutomationPeer
public class MyControl : System.Windows.Controls.Control;
public class MyContent : System.Windows.Controls.ContentControl;
public class MyButtonBase : System.Windows.Controls.Primitives.ButtonBase;
public class MyButton : System.Windows.Controls.Button;
public class MyRange : System.Windows.Controls.Primitives.RangeBase;
public class MySelector : System.Windows.Controls.Primitives.Selector;
public class MyMulti : System.Windows.Controls.Primitives.MultiSelector;
public class MyItems : System.Windows.Controls.ItemsControl;
public class MyHeadered : System.Windows.Controls.HeaderedItemsControl;
public class MyUser : System.Windows.Controls.UserControl;
public class MyTextBase : System.Windows.Controls.TextBox;
public class MyThumb : System.Windows.Controls.Primitives.Thumb;
public class MyElement : System.Windows.FrameworkElement;
public class MyPanel : System.Windows.Controls.Panel;
/// <summary>docs/fixes.md 的寫法：照抄進來，編得過、UIA 看得到。</summary>
public class Card : System.Windows.Controls.ContentControl
{
    public void Open() { }
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new CardAutomationPeer(this);
}

sealed class CardAutomationPeer(Card owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner), System.Windows.Automation.Provider.IInvokeProvider
{
    protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Button;
    protected override string GetClassNameCore() => nameof(Card);
    public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface pattern) => pattern == System.Windows.Automation.Peers.PatternInterface.Invoke ? this : base.GetPattern(pattern);
    public void Invoke() => ((Card)Owner).Open();
}

public class MyPeered : System.Windows.Controls.Control
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
}

/// <summary>資料產生的項目叫什麼名字：UIA 樹上每個項目的名稱。</summary>
internal static class Items
{
    private static readonly string[] Types = ["ListItem", "DataItem", "TreeItem", "TabItem", "MenuItem"];

    /// <summary>上面那些類別的原始碼（工具要讀的 C#），加上每一種資料的集合屬性。</summary>
    public static string Code
    {
        get
        {
            using var s = typeof(Items).Assembly.GetManifestResourceStream("Items.cs")!;
            var text = new System.IO.StreamReader(s).ReadToEnd();
            text = text[..text.IndexOf("internal static class Items", StringComparison.Ordinal)];
            return text + "public class ViewModel { " + string.Concat(Data.Select(t => $"public System.Collections.Generic.List<{t}> {Plural(t)} {{ get; }} = new(); ")) + "}\n";
        }
    }

    private static readonly string[] Data = ["Row", "NamedRow", "RecordRow", "EmptyRow", "StructRow", "String"];
    private static string Plural(string t) => t + "Items";

    /// <summary>工具那一邊：寫在裡面的資料物件換成 ItemsSource 的綁定。</summary>
    public static string ToolXaml(string body)
    {
        var m = System.Text.RegularExpressions.Regex.Match(body, "<(?:o|sys):(?<t>\\w+)(?: Title=\"Inbox\"/>|>Inbox</sys:String>)");
        return m.Success ? body.Replace(m.Value, "").Replace("x:Name=\"p\"", $"x:Name=\"p\" ItemsSource=\"{{Binding {Plural(m.Groups["t"].Value)}}}\"") : body;
    }

    /// <summary>產生出來的項目的名稱（第一個）。</summary>
    public static string? ItemName(IEnumerable<Node> tree, string title) =>
        tree.Where(n => Types.Contains(n.Type) && (n.Type != "MenuItem" || title.StartsWith("Menu", StringComparison.Ordinal)) && n.Name != "System").Select(n => n.Name).FirstOrDefault();

    public const string O = "xmlns:o=\"clr-namespace:UiaOracle;assembly=uiaoracle\"";

    /// <summary>Tab 停不停、UIA 樹上有沒有。</summary>
    public static void Silent()
    {
        Console.WriteLine("\n== silent tab stops");
        foreach (var c in Probe.Load("silent.txt"))
        {
            try
            {
                using var shown = Host.Show("<Button x:Name=\"a\"/>" + c.Body.Replace("[O]", O) + "<Button x:Name=\"z\"/>", c.Resources);
                var node = Host.Tree(shown.Handle).FirstOrDefault(n => n.Id == "p");
                var stops = Host.TabStops(shown);
                Console.WriteLine($"  {c.Title,-42} stop={(stops.Contains("p") ? 1 : 0)} uia={(node is null ? "-" : $"{node.Type} ctl={(node.Control ? 1 : 0)} {node.NameShown}"),-30} {(stops.Contains("p") && node is null ? "SILENT" : "")}");
            }
            catch (Exception ex) { Console.WriteLine($"  {c.Title,-42} !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
        }
    }

    public static void Run()
    {
        Console.WriteLine("\n== items from data");
        foreach (var c in Probe.Load("items.txt"))
        {
            try
            {
                using var shown = Host.Show(c.Body.Replace("[O]", O), c.Resources.Replace("[O]", O));
                var names = Host.Tree(shown.Handle).Where(n => Types.Contains(n.Type) && n.Type != "MenuItem" || n.Type == "MenuItem" && c.Title.StartsWith("Menu")).Select(n => $"{n.Type}={n.NameShown}").Take(3);
                Console.WriteLine($"  {c.Title,-58} {string.Join("  ", names)}");
            }
            catch (Exception ex) { Console.WriteLine($"  {c.Title,-58} !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
        }
    }
}
