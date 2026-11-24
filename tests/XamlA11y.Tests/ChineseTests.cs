using System.Diagnostics;
using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>繁體中文：每一句訊息都翻得到，給程式讀的不隨語言變。</summary>
public sealed class ChineseTests
{
    /// <summary>規則真的會寫出來的訊息，每一筆都要翻得到（所有錄過的 WPF 案例、示範專案、Kit.Everything）。</summary>
    [Fact]
    public void Every_message_the_rules_write_has_chinese()
    {
        var facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json"))).RootElement;
        var xaml = facts.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray())
            .Where(x => x.TryGetProperty("xaml", out _)).Select(x => x.GetProperty("xaml").GetString()!).ToList();
        Assert.True(xaml.Count > 300);
        var results = xaml.Select(x => Auditor.Run(Project.FromText(x))).Append(Kit.AuditEverything()).ToList();
        results.Add(Auditor.Run(Project.Load(Path.Combine(ActionContractTests.RepoRoot(), "samples", "demo"), [])));
        var missing = results.SelectMany(r => r.Findings).Select(f => f.Message).Where(m => !Zh.TryMessage(m, out _))
            .Concat(results.SelectMany(r => r.Checks).SelectMany(s => s.BlockedReasons.Keys.Concat(s.NotJudgedReasons.Keys)).Where(r => !Zh.TryReason(r, out _)))
            .Distinct().ToList();
        Assert.Empty(missing);
    }

    /// <summary>程式裡寫的原因字串，一個一個都要翻得到：有些要很特別的 XAML 才走得到，上面那一條抓不到。</summary>
    [Theory]
    [InlineData("the labelling chain is too deep")]
    [InlineData("its content is too complex to follow")]
    [InlineData("the code-behind sets its accessible name")]
    [InlineData("<Foo> is not a control this tool knows")]
    [InlineData("a trigger sets its accessible name")]
    [InlineData("its accessible name is resource 'a' is defined differently in 2 places")]
    [InlineData("the name is bound to Tag, which is an object")]
    [InlineData("the name is bound to Tag")]
    [InlineData("the name comes from Binding")]
    [InlineData("its style is outside the project, so whether MaterialDesignThemes still names it from HintAssist.Hint is unknown")]
    [InlineData("MaterialDesignThemes names it from HintAssist.Hint only where its theme is merged, and that was not found in the XAML")]
    [InlineData("MaterialDesignThemes 5.3 names it from HintAssist.Hint; the project's MaterialDesignThemes version was not found")]
    [InlineData("MaterialDesignThemes 5.3 names it from HintAssist.Hint; the project uses 4.9, 5.1, which was not measured")]
    [InlineData("LabeledBy is set to an element this tool does not know")]
    [InlineData("LabeledBy is set to something this tool cannot follow")]
    [InlineData("LabeledBy points at 'a', which is not in this file")]
    [InlineData("LabeledBy points at <Foo>, which this tool does not know")]
    [InlineData("it has no Content until a trigger sets one")]
    [InlineData("a trigger changes its Content; some values name it and some do not")]
    [InlineData("the code-behind sets its Content")]
    [InlineData("the root of A.xaml is not a control this tool knows")]
    [InlineData("its class sets its Content in code")]
    [InlineData("its style is outside the project and may set the content")]
    [InlineData("its Command is a resource this tool cannot read, which may be a RoutedUICommand")]
    [InlineData("its Command may be a RoutedUICommand, whose text would be the name")]
    [InlineData("its content is {x:Static a:Icons.B}, which looks like an icon")]
    [InlineData("its content is {DynamicResource a}, which this tool cannot resolve")]
    [InlineData("its content is an icon ({md:PackIcon})")]
    [InlineData("its content comes from the markup extension {a:B …}")]
    [InlineData("its content is nested or aliased too deeply")]
    [InlineData("its content is a resource this tool cannot read")]
    [InlineData("resource 'a' is not defined in the project")]
    [InlineData("its content is a LinearGradientBrush, so its name is the type name, a color code or a file path")]
    [InlineData("its content is only <Icon>, which looks like an icon")]
    [InlineData("its content is <Foo>, and this tool does not know what text that gives")]
    [InlineData("its content is a TextBox, whose text becomes the name")]
    [InlineData("its content is a RichTextBox, whose text becomes the name")]
    [InlineData("its content is <Path>, which gives no text; its name becomes the type name")]
    [InlineData("its Text is set by a style this tool cannot read")]
    [InlineData("a style may set the text of its Run")]
    [InlineData("its class sets its text in code")]
    [InlineData("its text is drawn as an icon by the font 'FontAwesome'; a screen reader reads the raw characters")]
    [InlineData("the template comes from the theme 'X', which was not measured for Button")]
    [InlineData("the theme 'X' was only measured with MaterialDesign3.Defaults.xaml")]
    [InlineData("the template comes from the theme 'X'; measured with MahApps.Metro 2.4, the project uses an unknown version")]
    [InlineData("the template comes from the theme 'X'; measured with MahApps.Metro 2.4, the project uses 1.6")]
    [InlineData("the Button template of the theme 'X' shows focus only through FocusVisualStyle (measured)")]
    [InlineData("several themes apply and they differ")]
    [InlineData("its Slider template may show focus on its parts")]
    [InlineData("a ToolBar gives it a style of its own")]
    [InlineData("<CheckBox> has a Style this tool cannot read")]
    [InlineData("style 'a' is not defined in the project")]
    [InlineData("based on 'a', which is not defined in the project")]
    [InlineData("more than 64 style definitions could apply")]
    [InlineData("the BasedOn chain is too deep")]
    [InlineData("a style in the chain has a BasedOn this tool cannot read")]
    [InlineData("the ItemContainerStyle of its list is something this tool cannot read")]
    [InlineData("not a control the keyboard goes to, or a base type ((headered toggle))")]
    [InlineData("a Setter without a Value")]
    [InlineData("a resource key this tool cannot read")]
    [InlineData("a resource with a type key")]
    [InlineData("defined differently in 3 styles with the same key or target")]
    [InlineData("the Template is not a ControlTemplate this tool can read")]
    [InlineData("small-target is not in WCAG 2.1 A + AA")]
    [InlineData("small-target is turned off")]
    public void Each_reason_has_chinese(string reason)
    {
        Assert.True(Zh.TryReason(reason, out var zh), reason);
        Assert.NotEqual(reason, zh);
    }

    [Theory]
    [InlineData("<Button> has no accessible name: its content is <Path>, which gives no text; its ToolTip is the help text, not the name", "<Button> 沒有名稱：內容是 <Path>，給不出文字；ToolTip 是說明文字，不是名稱")]
    [InlineData("<ExtendedButton> has no accessible name: it has no Content and no AutomationProperties.Name; its Text property belongs to the class and is not the name", "<ExtendedButton> 沒有名稱：沒有 Content，也沒有 AutomationProperties.Name；Text 是它自己的屬性，不是名稱")]
    [InlineData("<TextBox> has no accessible name", "<TextBox> 沒有名稱")]
    [InlineData("<TextBox> has no accessible name; if the Label \"a\" at line 3 is its label, use AutomationProperties.LabeledBy", "<TextBox> 沒有名稱；第 3 行的 Label「a」如果是它的標籤，用 AutomationProperties.LabeledBy 接起來")]
    [InlineData("<Border> reacts to a LeftClick MouseBinding ({Binding Open}) but the keyboard cannot reach it", "<Border> 處理 LeftClick 的 MouseBinding ({Binding Open})，鍵盤到不了")]
    [InlineData("<Button> is 18.5 DIP high and 2 DIP away from <Button> 'b'; targets under 24 need more space around them", "<Button> 高 18.5 DIP，離 <Button> 'b' 只有 2 DIP；小於 24 的要留更多空間")]
    [InlineData("<Button> has Focusable=\"False\" (from the style at a b.xaml:3), so Tab never reaches it", "<Button> 設了 Focusable=\"False\"（樣式在 a b.xaml:3），Tab 到不了")]
    [InlineData("The style 'A' removes FocusVisualStyle and nothing else shows keyboard focus (as used by the style for Button): its template has no focus trigger and no Focused state", "樣式「A」拿掉 FocusVisualStyle，又沒有別的東西表示焦點（樣式（Button 的）用到它）：範本沒有焦點觸發器，也沒有 Focused 狀態")]
    public void Messages_in_chinese(string en, string zh) => Assert.Equal(zh, Lang.ZhHant.Message(en));

    /// <summary>認不得的照英文印，不會掉字；訊息裡 repo 的字不能讓翻譯跑不完。</summary>
    [Fact]
    public void An_unknown_message_stays_as_it_is()
    {
        Assert.Equal("something new", Lang.ZhHant.Message("something new"));
        Assert.Equal("<Button> has no accessible name: something new", Lang.ZhHant.Message("<Button> has no accessible name: something new"));
        var evil = "<Button> has no accessible name: " + string.Concat(Enumerable.Repeat("; its ToolTip is the help text, not the name", 2000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(evil, Lang.ZhHant.Message(evil));
        Assert.Equal(evil, Lang.ZhHant.Reason(evil));
        // 規則寫得出來的最長的那種，也要快
        var longest = "<Button> has no accessible name: " + string.Concat(Enumerable.Repeat("; its ToolTip is the help text, not the name", 13));
        for (var i = 0; i < 20; i++) Lang.ZhHant.Message(longest);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
    }

    [Fact]
    public void The_chinese_console_and_markdown_have_no_english_sentences_left()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Kit.Everything + "<FancyButton/>", Kit.EverythingTemplate));
        var md = f.At("r.md");
        var r = f.Run("audit", f.Root, "--lang", "zh-Hant", "--md", md);
        foreach (var text in (string[])[r.Out, File.ReadAllText(md)])
            foreach (var word in (string[])["has no accessible", "which gives", "could not", "finding(s)", "blocked", "judged", "does not", "Check it"])
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
        Assert.Contains("個 XAML 檔，WCAG 2.2 A + AA", r.Out, StringComparison.Ordinal);
        Assert.Contains("要人檢查的 18 項", File.ReadAllText(md), StringComparison.Ordinal);
    }
}
