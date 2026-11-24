using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>R7：清單、表格、樹的名稱（警告）。WPF 的答案在 uia-facts.json 的 lists.txt。</summary>
public class ListRuleTests
{
    private static string Audit(string body, string resources = "") => Kit.Verdicts(Kit.Audit(body, resources), "R7");

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGrid")]
    public void Every_list_is_checked(string type)
    {
        Assert.Equal("list-no-name", Audit($"<{type}/>"));
        Assert.Equal("Pass", Audit($"<{type} AutomationProperties.Name=\"Files\"/>"));
        Assert.Equal("Pass", Audit($"<TextBlock x:Name=\"l\" Text=\"Files\"/><{type} AutomationProperties.LabeledBy=\"{{Binding ElementName=l}}\"/>"));
        Assert.Equal("Pass", Audit($"<{type}/>", $"<Style TargetType=\"{type}\"><Setter Property=\"AutomationProperties.Name\" Value=\"Files\"/></Style>"));
        Assert.Equal("NotJudged", Audit($"<{type} AutomationProperties.Name=\"{{Binding Title}}\"/>"));
    }

    [Theory]
    [InlineData("<TabControl/>")]
    [InlineData("<ItemsControl/>")]
    [InlineData("<Menu/>")]
    [InlineData("<StatusBar/>")]
    [InlineData("<ProgressBar/>")]
    public void Other_containers_are_not(string xaml) => Assert.Equal("", Audit(xaml));

    [Fact]
    public void It_is_a_warning_and_does_not_fail_the_run()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/><ListBox/>"));
        Assert.Equal(0, p.Run("audit", p.Root).Exit);
        Assert.Equal(1, p.Run("audit", p.Root, "--fail-on", "warn").Exit);
        p.Add("xamla11y.config.json", "{\"rules\":{\"list-no-name\":\"fail\"}}");
        Assert.Equal(1, p.Run("audit", p.Root).Exit);
        p.Add("xamla11y.config.json", "{\"rules\":{\"list-no-name\":\"off\"}}");
        Assert.Equal(0, p.Run("audit", p.Root, "--fail-on", "warn").Exit);
    }

    [Theory]
    [InlineData("Focusable=\"False\"")]
    [InlineData("IsEnabled=\"False\"")]
    public void A_list_the_keyboard_does_not_reach_is_only_information(string attribute) =>
        Assert.Equal(Severity.Info, Assert.Single(Kit.Audit($"<ListBox {attribute}/>").Findings).Severity);

    [Fact]
    public void It_is_not_part_of_the_resolution()
    {
        var r = Kit.Audit("<Button Content=\"x\"/><local:FancyListBox/><local:FancyTreeView/><local:FancyListView/><local:FancyDataGrid/>");
        Assert.Equal(100, r.Resolution);
        Assert.Equal("Blocked,Blocked,Blocked,Blocked", Kit.Verdicts(r, "R7"));
        Assert.Equal(4, r.UnknownControls.Count);
    }

    [Theory]
    [InlineData("<Label Content=\"Files\" Target=\"{Binding ElementName=p}\"/><ListBox x:Name=\"p\"/>", "<Label Target> at line")]
    [InlineData("<GroupBox Header=\"Files\"><ListBox/></GroupBox>", "the GroupBox header \"Files\" around it does not give it one")]
    [InlineData("<GroupBox><ListBox/></GroupBox>", "has no accessible name")]
    [InlineData("<GroupBox Header=\"{Binding H}\"><ListBox/></GroupBox>", "has no accessible name")]
    [InlineData("<ListBox ToolTip=\"Files\"/>", "its ToolTip is the help text")]
    [InlineData("<TextBlock Text=\"Files\"/><ListBox/>", "the TextBlock \"Files\" before it")]
    [InlineData("<TextBlock Text=\"{Binding Title}\"/><ListBox/>", "; the TextBlock before it could label it")]
    [InlineData("<ListBox AutomationProperties.Name=\" \"/>", "unreadable accessible name")]
    public void The_message_says_what_is_nearby(string xaml, string expected) =>
        Assert.Contains(expected, Kit.Audit(xaml).Findings.Single(f => f.Kind.Slug == "list-no-name").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"ComboBox\"><ListBox/></ControlTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"local:Own\"><ListBox x:Name=\"PART_List\"/></ControlTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"local:Own\"><ListBox Focusable=\"False\"/></ControlTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"local:Own\"><ListBox/></ControlTemplate>", "list-no-name")]
    public void Template_parts(string resource, string expected) => Assert.Equal(expected, Audit("", resource));

    [Theory]
    [InlineData("class Files : ListBox { }", "list-no-name")]
    [InlineData("class Files : ListBox { protected override AutomationPeer OnCreateAutomationPeer() => new P(this); }", "Blocked")]
    [InlineData("class Files : DataGrid { }", "list-no-name")]
    public void Classes_of_the_project(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<local:Files/>")).Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R7"));
    }

    [Fact]
    public void Code_that_names_it_makes_it_unknown()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<ListBox x:Name=\"files\"/>")).Add("Main.xaml.cs", "class M { void F() { AutomationProperties.SetName(files, \"x\"); } }");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "R7"));
    }

    [Fact]
    public void It_can_be_ignored_and_baselined()
    {
        Assert.Equal(1, Kit.Audit("<!-- xamla11y-ignore: the only list in the window --><ListBox/>").Suppressed);
        using var p = new Folder().Add("Main.xaml", Kit.Window("<ListBox x:Name=\"a\"/>"));
        p.Run("audit", p.Root, "--write-baseline", p.At("b.json"));
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", p.At("b.json"), "--fail-on", "warn").Exit);
    }
}
