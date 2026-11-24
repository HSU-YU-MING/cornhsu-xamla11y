using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>名稱有沒有包含畫面上的字（WCAG 2.5.3）。WPF 的答案在 uia-facts.json 的 labels。</summary>
public sealed class NameDiffersTests
{
    private static readonly JsonElement Facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json"))).RootElement;

    public static TheoryData<string> Titles => [.. Facts.GetProperty("labels").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];

    /// <summary>工具說不包含的，UIA 的名稱真的不包含畫出來的字；說包含的真的包含。</summary>
    [Theory, MemberData(nameof(Titles))]
    public void It_never_contradicts_what_WPF_shows_and_names(string title)
    {
        var c = Facts.GetProperty("labels").EnumerateArray().Single(x => x.GetProperty("title").GetString() == title);
        var judged = Auditor.Run(Project.FromText(c.GetProperty("xaml").GetString()!)).Judgements.Where(j => j.Check == "visible-text" && Core.Xaml.Markup.NameOf(j.Element) == "p").ToList();
        var shown = string.Join(" ", c.GetProperty("shows").EnumerateArray().Select(x => x.GetString()));
        if (shown.Length == 0 && !title.Contains("ContentPresenter", StringComparison.Ordinal)) return;      // 超連結的字畫在外面的 TextBlock
        if (title.Contains("not a label", StringComparison.Ordinal) || title.Contains("without a ContentPresenter", StringComparison.Ordinal) || title.Contains("from resources", StringComparison.Ordinal)) { Assert.Empty(judged); return; }
        static string Letters(string s) => string.Concat(s.ToLowerInvariant().Where(char.IsLetterOrDigit));
        var contains = Letters(c.GetProperty("name").GetString()!).Contains(Letters(shown), StringComparison.Ordinal) && title != "Button only part of a word";
        Assert.Equal(contains ? Verdict.Pass : Verdict.Finding, Assert.Single(judged).Verdict);
    }

    [Theory]
    [InlineData("store the document", "save", false)]
    [InlineData("save the document", "save", true)]
    [InlineData("cancel", "can", false)]
    [InlineData("the can", "can", true)]
    [InlineData("瀏覽輸入資料夾", "瀏覽", true)]
    [InlineData("瀏覽 輸入", "瀏覽輸入", true)]
    [InlineData("保留", "儲存", false)]
    [InlineData("cancel all", "can", false)]
    public void Contains(string name, string shown, bool expected) => Assert.Equal(expected, LabelRule.Contains(name, shown));

    [Theory]
    [InlineData("Browse…", "browse")]
    [InlineData("  Don't  ask -- again! ", "dont ask again")]
    [InlineData("Ｓａｖｅ", "save")]
    [InlineData("×", "")]
    [InlineData("Set\u200Btings", "settings")]
    [InlineData("Save & close", "save and close")]
    [InlineData("Café 2", "café 2")]
    public void Normalized(string text, string expected) => Assert.Equal(expected, LabelRule.Normalize(text));

    /// <summary>有一邊不知道是什麼字的、不是標籤的不比。</summary>
    [Theory]
    [InlineData("<Button Content=\"{Binding T}\" AutomationProperties.Name=\"Save\"/>")]
    [InlineData("<Button Content=\"Save\" AutomationProperties.Name=\"{Binding N}\"/>")]
    [InlineData("<Button Content=\"Save\"/>")]
    [InlineData("<Button Content=\"x\" AutomationProperties.Name=\"Close\"/>")]
    [InlineData("<Button Content=\"×\" AutomationProperties.Name=\"Close\"/>")]
    [InlineData("<Button FontFamily=\"Segoe MDL2 Assets\" Content=\"&#xE74D;\" AutomationProperties.Name=\"Delete\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"Delete\"><Image Source=\"a.png\"/></Button>")]
    [InlineData("<Button Content=\"Save\" AutomationProperties.Name=\"Keep\" ContentTemplate=\"{StaticResource T}\"/>")]
    [InlineData("<Button Content=\"Save\" AutomationProperties.Name=\"Keep\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><DataTrigger Binding=\"{Binding P}\" Value=\"1\"><Setter Property=\"AutomationProperties.Name\" Value=\"Save\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button>")]
    [InlineData("<Button AutomationProperties.Name=\"Keep\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"Content\" Value=\"Save\"/><Style.Triggers><DataTrigger Binding=\"{Binding P}\" Value=\"1\"><Setter Property=\"Content\" Value=\"Keep\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button>")]
    [InlineData("<Button Content=\"Save\" AutomationProperties.Name=\"Keep\" Style=\"{StaticResource NotHere}\"/>")]
    [InlineData("<Button Content=\"This is a whole sentence that explains what happens when the button is pressed\" AutomationProperties.Name=\"Go\"/>")]
    [InlineData("<TextBox Text=\"Save\" AutomationProperties.Name=\"Name\"/>")]
    [InlineData("<Button Content=\"{StaticResource a}\" AutomationProperties.Name=\"Keep\"/>")]
    [InlineData("<Button Content=\"Save\" AutomationProperties.Name=\"{DynamicResource a}\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"Keep\"><TextBlock Text=\"{StaticResource a}\"/></Button>")]
    [InlineData("<Button AutomationProperties.Name=\"Keep\"><TextBlock Visibility=\"Collapsed\" Text=\"Save\"/></Button>")]
    [InlineData("<Button AutomationProperties.Name=\"Keep\"><StackPanel><TextBlock Opacity=\"0\" Text=\"Save\"/></StackPanel></Button>")]
    [InlineData("<Button AutomationProperties.Name=\"Keep\"><Grid><TextBlock Text=\"Turn on\"/><TextBlock Text=\"Turn off\"/></Grid></Button>")]
    public void Not_compared(string xaml) => Assert.Equal("", Kit.Verdict("visible-text", xaml, "<DataTemplate x:Key=\"T\"><TextBlock Text=\"y\"/></DataTemplate><x:String x:Key=\"a\">Save</x:String>"));

    [Theory]
    [InlineData("Save (Ctrl+S)", "Save", "Pass")]
    [InlineData("_Save", "Save", "Pass")]
    [InlineData("Save [beta]", "Save", "Pass")]
    [InlineData("儲存（不問）", "儲存", "Pass")]
    [InlineData("Save as", "Save", "name-differs")]
    [InlineData("E-mail address", "Email address", "Pass")]
    [InlineData("Email", "E-mail to a friend", "Pass")]
    [InlineData("Set-up", "Set up", "Pass")]
    [InlineData("Re-do", "Redo all", "Pass")]
    public void What_is_in_parentheses_is_not_the_label(string content, string name, string expected) =>
        Assert.Equal(expected, Kit.Verdict("visible-text", $"<Button Content=\"{content}\" AutomationProperties.Name=\"{name}\"/>"));

    [Fact]
    public void Code_that_sets_one_of_them_is_not_judged()
    {
        using var p = Kit.Files("<Button x:Name=\"b\" Content=\"Save\" AutomationProperties.Name=\"Keep\"/>", "class M { void L() { b.Content = \"Keep\"; } }");
        Assert.Empty(p.Audit().Findings);
        using var q = Kit.Files("<Button x:Name=\"b\" Content=\"Save\" AutomationProperties.Name=\"Keep\"/>", "class M { void L() { AutomationProperties.SetName(b, \"Save\"); } }");
        Assert.Empty(q.Audit().Findings);
        using var t = Kit.Files("<Button AutomationProperties.Name=\"Keep\"><TextBlock x:Name=\"t\" Text=\"Save\"/></Button>", "class M { void L() { t.Text = \"Keep\"; } }");
        Assert.Empty(t.Audit().Findings);
    }

    [Fact]
    public void It_is_a_warning_that_says_both()
    {
        var f = Assert.Single(Kit.Audit("<Button Content=\"Save\" AutomationProperties.Name=\"Keep\"/>").Findings);
        Assert.Equal((Kinds.NameDiffers, Severity.Warn), (f.Kind, f.Severity));
        Assert.Equal("<Button> shows \"Save\" but its accessible name is \"Keep\", which does not contain it", f.Message);
        Assert.Equal("<Button> 畫面上是「Save」，名稱是「Keep」，沒有包含它", Core.Report.Zh.Message(f.Message));
    }
}
