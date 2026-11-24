using System.Text.Json;
using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>清單、表格、樹、進度條等的名稱（警告）。WPF 的答案在 uia-facts.json 的 lists.txt。</summary>
public sealed class ListNameTests
{
    [Theory]
    [InlineData("<ProgressBar/>", "widget-no-name:Warn")]
    [InlineData("<Calendar/>", "widget-no-name:Warn")]
    [InlineData("<Menu/>", "widget-no-name:Info")]
    [InlineData("<ToolBar/>", "widget-no-name:Info")]
    [InlineData("<ProgressBar AutomationProperties.Name=\"a\"/>", "")]
    [InlineData("<ToolBar Header=\"a\"/>", "")]
    public void Widgets(string xaml, string expected) => Assert.Equal(expected, string.Join(",", Kit.AuditBody(xaml).Findings.Where(f => f.Kind.Check == "list-names").Select(f => $"{f.Kind.Slug}:{f.Severity}")));

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGrid")]
    public void Every_list_is_checked(string type)
    {
        Assert.Equal("list-no-name", Kit.Verdict("list-names", $"<{type}/>"));
        Assert.Equal("Pass", Kit.Verdict("list-names", $"<{type} AutomationProperties.Name=\"Files\"/>"));
        Assert.Equal("Pass", Kit.Verdict("list-names", $"<TextBlock x:Name=\"l\" Text=\"Files\"/><{type} AutomationProperties.LabeledBy=\"{{Binding ElementName=l}}\"/>"));
        Assert.Equal("Pass", Kit.Verdict("list-names", $"<{type}/>", $"<Style TargetType=\"{type}\"><Setter Property=\"AutomationProperties.Name\" Value=\"Files\"/></Style>"));
        Assert.Equal("NotJudged", Kit.Verdict("list-names", $"<{type} AutomationProperties.Name=\"{{Binding Title}}\"/>"));
    }

    [Theory]
    [InlineData("<TabControl/>")]
    [InlineData("<ItemsControl/>")]
    [InlineData("<StatusBar/>")]
    public void Other_containers_are_not(string xaml) => Assert.Equal("", Kit.Verdict("list-names", xaml));

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
        Assert.Equal("Blocked,Blocked,Blocked,Blocked", Kit.Verdicts(r, "list-names"));
        Assert.Equal(4, r.UnknownControls.Count);
    }

    [Theory]
    [InlineData("<Label Content=\"Files\" Target=\"{Binding ElementName=p}\"/><ListBox x:Name=\"p\"/>", "<Label Target> at line")]
    [InlineData("<GroupBox Header=\"Files\"><ListBox/></GroupBox>", "the GroupBox header \"Files\" around it does not give it one")]
    [InlineData("<GroupBox><ListBox/></GroupBox>", "has no accessible name")]
    [InlineData("<GroupBox Header=\"{Binding H}\"><ListBox/></GroupBox>", "has no accessible name")]
    [InlineData("<ListBox ToolTip=\"Files\"/>", "its ToolTip is the help text")]
    [InlineData("<TextBlock Text=\"Files\"/><ListBox/>", "if the TextBlock \"Files\" at line 1 is its label")]
    [InlineData("<TextBlock Text=\"{Binding Title}\"/><ListBox/>", "; if the TextBlock at line 1 is its label")]
    [InlineData("<ListBox AutomationProperties.Name=\" \"/>", "unreadable accessible name")]
    public void The_message_says_what_is_nearby(string xaml, string expected) =>
        Assert.Contains(expected, Kit.Audit(xaml).Findings.Single(f => f.Kind.Slug == "list-no-name").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"ComboBox\"><ListBox/></ControlTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"local:Own\"><ListBox x:Name=\"PART_List\"/></ControlTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"local:Own\"><ListBox Focusable=\"False\"/></ControlTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"t\" TargetType=\"local:Own\"><ListBox/></ControlTemplate>", "list-no-name")]
    public void Template_parts(string resource, string expected) => Assert.Equal(expected, Kit.Verdict("list-names", "", resource));

    [Theory]
    [InlineData("class Files : ListBox { }", "list-no-name")]
    [InlineData("class Files : ListBox { protected override AutomationPeer OnCreateAutomationPeer() => new P(this); }", "Blocked")]
    [InlineData("class Files : DataGrid { }", "list-no-name")]
    public void Classes_of_the_project(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<local:Files/>")).Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "list-names"));
    }

    [Fact]
    public void Code_that_names_it_makes_it_unknown()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<ListBox x:Name=\"files\"/>")).Add("Main.xaml.cs", "class M { void F() { AutomationProperties.SetName(files, \"x\"); } }");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "list-names"));
    }

    [Fact]
    public void It_can_be_ignored_and_baselined()
    {
        Assert.Equal(1, Kit.Audit("<!-- xamla11y-ignore: the only list in the window --><ListBox/>").Suppressed);
        using var p = new Folder().Add("Main.xaml", Kit.Window("<ListBox x:Name=\"a\"/>"));
        p.Run("audit", p.Root, "--write-baseline", p.At("b.json"));
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", p.At("b.json"), "--fail-on", "warn").Exit);
    }

    /// <summary>名稱存在、念不出意思的清單：不是「名稱要能由程式判定」那一條。</summary>
    [Fact]
    public void An_unreadable_list_name_is_not_an_empty_one()
    {
        var r = Kit.Audit("<ListBox AutomationProperties.Name=\"&#xE74D;\"/><TreeView/>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 0));
        string[] Clauses(int i) => [.. json.RootElement.GetProperty("findings")[i].GetProperty("clauses").GetProperty("section508").EnumerateArray().Select(x => x.GetString()!)];
        var list = r.Findings[0].Element == "ListBox" ? 0 : 1;
        Assert.Equal(["E207.2"], Clauses(list));
        Assert.Equal(["E207.2", "502.3.1"], Clauses(1 - list));
        Assert.Equal(1, Regex.Count(Kit.Html(r), "502\\.3\\.1"));
    }
}
