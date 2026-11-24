using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>寫死藏起來、停用的：沒有東西會打開它才降一級（2026-11-24 審查：語料 65 筆裡 36 筆同一個檔案就有觸發器會打開）。</summary>
public sealed class HiddenAndDisabledTests
{
    private static Severity Of(string body, string code = "", string resources = "")
    {
        using var p = Kit.Files(body, code, resources);
        return Assert.Single(p.Audit().Findings, f => f.Kind == Kinds.MissingName).Severity;
    }

    [Theory]
    [InlineData("<Button Visibility=\"Collapsed\">[I]</Button>")]
    [InlineData("<Button Visibility=\"Hidden\">[I]</Button>")]
    [InlineData("<Button x:Name=\"b\" Visibility=\"Collapsed\">[I]</Button>")]
    [InlineData("<Button IsEnabled=\"False\">[I]</Button>")]
    [InlineData("<Button x:Name=\"b\" IsEnabled=\"False\">[I]</Button>")]
    public void Nothing_opens_it(string xaml) => Assert.Equal(Severity.Warn, Of(xaml.Replace("[I]", Kit.Icon, StringComparison.Ordinal)));

    [Theory]
    [InlineData("b.Visibility = Visibility.Visible;")]
    [InlineData("Show(b);")]
    [InlineData("foreach (var x in new[] { a, b }) x.Visibility = Visibility.Visible;")]
    [InlineData("var x = b;")]
    [InlineData("b.FadeIn();")]
    [InlineData("((Button)FindName(\"b\")).Visibility = Visibility.Visible;")]
    public void Code_opens_it(string statement) =>
        Assert.Equal(Severity.Fail, Of($"<Button x:Name=\"b\" Visibility=\"Collapsed\">{Kit.Icon}</Button>", $"partial class Main {{ void L() {{ {statement} }} }}"));

    [Theory]
    [InlineData("b.Tag = 1;")]
    [InlineData("// b.Visibility = Visibility.Visible;")]
    [InlineData("var x = b.ActualWidth;")]
    [InlineData("bb.Visibility = Visibility.Visible;")]
    public void Code_that_does_not(string statement) =>
        Assert.Equal(Severity.Warn, Of($"<Button x:Name=\"b\" Visibility=\"Collapsed\">{Kit.Icon}</Button>", $"partial class Main {{ void L() {{ {statement} }} }}"));

    /// <summary>別的檔案、別的類別、動畫、Blend 的動作、整個面板的迴圈。</summary>
    [Theory]
    [InlineData("", "partial class Main2 { void L() { other.IsEnabled = true; } }")]
    [InlineData("", "class X { void L(Main m) { m.other.IsEnabled = true; } }")]
    [InlineData("", "class X { void L() { other.ClearValue(UIElement.IsEnabledProperty); } }")]
    [InlineData("", "class X { void L() { other.SetCurrentValue(IsEnabledProperty, true); } }")]
    [InlineData("<Grid><Grid.Triggers><EventTrigger RoutedEvent=\"Loaded\"><BeginStoryboard><Storyboard><ObjectAnimationUsingKeyFrames Storyboard.Target=\"{Binding ElementName=other}\" Storyboard.TargetProperty=\"IsEnabled\"/></Storyboard></BeginStoryboard></EventTrigger></Grid.Triggers></Grid>", "")]
    [InlineData("<Grid><local:ChangePropertyAction TargetName=\"other\" PropertyName=\"IsEnabled\" Value=\"True\"/></Grid>", "")]
    [InlineData("<Grid><local:ChangePropertyAction TargetObject=\"{Binding ElementName=other}\" PropertyName=\"IsEnabled\" Value=\"True\"/></Grid>", "")]
    public void Opened_from_elsewhere(string extra, string code)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button x:Name=\"other\" IsEnabled=\"False\">{Kit.Icon}</Button>{extra}")).Add("Other.cs", code);
        Assert.Equal(Severity.Fail, Assert.Single(p.Audit().Findings, f => f.Kind == Kinds.MissingName).Severity);
    }

    [Fact]
    public void Opened_in_a_loop_over_the_panel()
    {
        using var p = Kit.Files($"<StackPanel x:Name=\"panel\"><Button IsEnabled=\"False\">{Kit.Icon}</Button></StackPanel>", "partial class Main { void L() { foreach (UIElement c in panel.Children) c.IsEnabled = true; } }");
        Assert.Equal(Severity.Fail, Assert.Single(p.Audit().Findings).Severity);
    }

    /// <summary>樣式寫死收合、DataTrigger 打開（WPF 最常見的寫法）：不能說沒有東西會打開。</summary>
    [Fact]
    public void A_style_that_hides_and_shows_is_not_hidden_for_good()
    {
        const string style = "<Style x:Key=\"S\" TargetType=\"ContentControl\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/><Style.Triggers><DataTrigger Binding=\"{Binding V}\" Value=\"True\"><Setter Property=\"Visibility\" Value=\"Visible\"/></DataTrigger></Style.Triggers></Style>";
        Assert.Contains(Kit.Audit("<ContentControl Style=\"{StaticResource S}\"/>", style).Findings, f => f.Kind == Kinds.SilentTabStop);
        Assert.DoesNotContain(Kit.Audit("<ContentControl Style=\"{StaticResource S}\"/>", style.Replace("<DataTrigger Binding=\"{Binding V}\" Value=\"True\"><Setter Property=\"Visibility\"", "<DataTrigger Binding=\"{Binding V}\" Value=\"True\"><Setter Property=\"Opacity\"", StringComparison.Ordinal)).Findings, f => f.Kind == Kinds.SilentTabStop);
    }

    [Fact]
    public void Code_enables_it() =>
        Assert.Equal(Severity.Fail, Of($"<Button x:Name=\"b\" IsEnabled=\"False\">{Kit.Icon}</Button>", "partial class Main { void L() { b.IsEnabled = true; } }"));

    /// <summary>別的檔案的 GetTemplateChild("PART_Close")。</summary>
    [Fact]
    public void A_name_in_a_string_anywhere_in_the_code_counts()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button x:Name=\"CloseIt\" Visibility=\"Collapsed\">{Kit.Icon}</Button>")).Add("Other.cs", "class X { void F() { GetTemplateChild(\"CloseIt\"); } }");
        Assert.Equal(Severity.Fail, Assert.Single(p.Audit().Findings).Severity);
    }

    [Theory]
    [InlineData("<Trigger Property=\"IsSelected\" Value=\"True\"><Setter TargetName=\"c\" Property=\"Visibility\" Value=\"Visible\"/></Trigger>", Severity.Fail)]
    [InlineData("<Trigger Property=\"IsSelected\" Value=\"True\"><Setter TargetName=\"c\" Property=\"UIElement.Visibility\" Value=\"Visible\"/></Trigger>", Severity.Fail)]
    [InlineData("<Trigger Property=\"IsSelected\" Value=\"True\"><Setter TargetName=\"c\" Property=\"Background\" Value=\"Red\"/></Trigger>", Severity.Warn)]
    [InlineData("<Trigger Property=\"IsSelected\" Value=\"True\"><Setter TargetName=\"d\" Property=\"Visibility\" Value=\"Visible\"/></Trigger>", Severity.Warn)]
    [InlineData("<EventTrigger RoutedEvent=\"Mouse.MouseEnter\"><BeginStoryboard><Storyboard><ObjectAnimationUsingKeyFrames Storyboard.TargetName=\"c\" Storyboard.TargetProperty=\"(UIElement.Visibility)\"/></Storyboard></BeginStoryboard></EventTrigger>", Severity.Fail)]
    [InlineData("<EventTrigger RoutedEvent=\"Mouse.MouseEnter\"><BeginStoryboard><Storyboard Storyboard.TargetProperty=\"Visibility\"><ObjectAnimationUsingKeyFrames Storyboard.TargetName=\"c\"/></Storyboard></BeginStoryboard></EventTrigger>", Severity.Fail)]
    [InlineData("<EventTrigger RoutedEvent=\"Mouse.MouseEnter\"><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName=\"c\" Storyboard.TargetProperty=\"Opacity\"/></Storyboard></BeginStoryboard></EventTrigger>", Severity.Warn)]
    public void A_trigger_or_an_animation_opens_it(string trigger, Severity expected) =>
        Assert.Equal(expected, Of("<TabControl AutomationProperties.Name=\"t\"><TabItem Header=\"h\" Template=\"{StaticResource T}\"/></TabControl>", "",
            $"<ControlTemplate x:Key=\"T\" TargetType=\"TabItem\"><Grid><Button x:Name=\"c\" Visibility=\"Hidden\">{Kit.Icon}</Button><Border x:Name=\"d\"/></Grid><ControlTemplate.Triggers>{trigger}</ControlTemplate.Triggers></ControlTemplate>"));

    [Theory]
    [InlineData("<ProgressBar Visibility=\"Collapsed\"/>", "info", "; only an info because it is hidden")]
    [InlineData("<ListBox Visibility=\"Collapsed\"/>", "info", "; only an info because it is hidden")]
    [InlineData("<ListBox IsEnabled=\"False\"/>", "info", "; only an info because it is disabled")]
    [InlineData("<ListBox Focusable=\"False\"/>", "info", "; only an info because it cannot take focus")]
    [InlineData("<Menu/>", "info", "; only an info because one menu or tool bar in a window needs no name")]
    [InlineData("<ProgressBar/>", "warn", "")]
    public void A_lowered_one_says_why(string xaml, string severity, string why)
    {
        var f = Assert.Single(Kit.Audit(xaml).Findings);
        Assert.Equal(severity, Core.Report.Text.Slug(f.Severity));
        Assert.EndsWith("has no accessible name" + why, f.Message, StringComparison.Ordinal);
        Assert.True(Core.Report.Zh.TryMessage(f.Message, out _));
    }

    /// <summary>頁籤收起來、之後會打開的照判；永遠收著的（精靈頁）不判。</summary>
    [Theory]
    [InlineData("", "NotJudged")]
    [InlineData("partial class Main { void L() { t.Visibility = Visibility.Visible; } }", "meaningless-name")]
    public void A_collapsed_tab(string code, string expected)
    {
        using var p = Kit.Files("<TabControl AutomationProperties.Name=\"n\"><TabItem x:Name=\"t\" Visibility=\"Collapsed\"/></TabControl>", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("foreach (ComboBoxItem item in box.Items) { item.Content = Text(item.Tag); }", "Blocked")]
    [InlineData("foreach (var item in box.Items.OfType<ComboBoxItem>()) { if (item.Tag is int n) item.Content = n; }", "Blocked")]
    [InlineData("foreach (var item in this.box.Items) AutomationProperties.SetName(item, \"x\");", "Blocked")]
    [InlineData("foreach (ComboBoxItem item in box.Items) { item.IsSelected = true; }", "meaningless-name")]
    [InlineData("foreach (ComboBoxItem item in other.Items) { item.Content = 1; }", "meaningless-name")]
    [InlineData("foreach (var child in box.Items) { if (child is ComboBoxItem c) c.Content = 1; }", "Blocked")]
    [InlineData("foreach (ComboBoxItem item in box.Items) { item.IsSelected = true; } other.Content = 1;", "meaningless-name")]
    [InlineData("foreach (ComboBoxItem item in box.Items) item.IsSelected = true; item.Content = 1;", "meaningless-name")]
    public void Items_filled_in_a_loop(string code, string expected)
    {
        using var p = Kit.Files("<ComboBox x:Name=\"box\" AutomationProperties.Name=\"n\"><ComboBoxItem Tag=\"1\"/></ComboBox>", $"partial class Main {{ void L() {{ {code} }} }}");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }
}
