using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>輸入框的名稱：LabeledBy、旁邊的字、Label.Target、佈景的 Hint。</summary>
public sealed class InputNameTests
{
    [Fact]
    public void Label_target_does_not_name_the_field_and_the_message_says_so()
    {
        var f = Assert.Single(Kit.Audit("<Label Content=\"Name\" Target=\"{Binding ElementName=box}\"/><TextBox x:Name=\"box\"/>").Findings);
        Assert.Equal("input-no-name", f.Kind.Slug);
        Assert.Contains("<Label Target>", f.Message);
    }

    [Fact]
    public void The_text_before_an_input_is_suggested_as_its_label()
    {
        var f = Assert.Single(Kit.Audit("<TextBlock Text=\"Email\"/><TextBox/>").Findings);
        Assert.Contains("\"Email\"", f.Message);
        Assert.Contains("LabeledBy", f.Message);
    }

    [Theory]
    [InlineData("5.3.0", "Pass")]
    [InlineData("5.3.2", "Pass")]
    [InlineData("4.9.0", "Blocked")]
    [InlineData(null, "Blocked")]
    public void MaterialDesign_hint_names_the_field_only_in_the_measured_version(string? version, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox md:HintAssist.Hint=\"Name\"/>"))
            .Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><md:BundledTheme/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>");
        if (version is not null) p.Add("App.csproj", $"<Project><ItemGroup><PackageReference Include=\"MaterialDesignThemes\" Version=\"{version}\"/></ItemGroup></Project>");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "input-names"));
    }

    /// <summary>名稱是佈景的樣式給的：樣式被換掉、佈景沒合併就沒有（UiaOracle themes「hint, …」）。</summary>
    [Theory]
    [InlineData("<TextBox md:HintAssist.Hint=\"Name\" Style=\"{x:Null}\"/>", true, "input-no-name")]
    [InlineData("<TextBox md:HintAssist.Hint=\"Name\" Style=\"{StaticResource own}\"/>", true, "input-no-name")]
    [InlineData("<TextBox md:HintAssist.Hint=\"Name\" Style=\"{StaticResource based}\"/>", true, "Pass")]
    [InlineData("<TextBox md:HintAssist.Hint=\"Name\" Style=\"{StaticResource MaterialDesignFloatingHintTextBox}\"/>", true, "Pass")]
    [InlineData("<TextBox md:HintAssist.Hint=\"Name\" Style=\"{StaticResource Elsewhere}\"/>", true, "Blocked")]
    [InlineData("<TextBox md:HintAssist.Hint=\"Name\"/>", false, "Blocked")]
    public void MaterialDesign_hint_comes_from_its_style(string xaml, bool merged, string expected)
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window(xaml, "<Style x:Key=\"own\" TargetType=\"TextBox\"/><Style x:Key=\"based\" TargetType=\"TextBox\" BasedOn=\"{StaticResource {x:Type TextBox}}\"/>"))
            .Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>{(merged ? "<md:BundledTheme/>" : "")}</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("App.csproj", "<Project><ItemGroup><PackageReference Include=\"MaterialDesignThemes\" Version=\"5.3.0\"/></ItemGroup></Project>");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "input-names"));
    }

    [Fact]
    public void An_external_style_does_not_look_like_a_hint() =>
        Assert.Equal("input-no-name", Assert.Single(Kit.Audit("<TextBox Style=\"{DynamicResource External}\"/>").Findings).Kind.Slug);

    [Theory]
    [InlineData("<Border x:Name=\"b\" AutomationProperties.Name=\"X\"/><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=b}\"/>", "input-no-name")]
    [InlineData("<ListBox AutomationProperties.Name=\"l\"><ListBox.ItemTemplate><DataTemplate><TextBlock x:Name=\"b\" Text=\"X\"/></DataTemplate></ListBox.ItemTemplate></ListBox><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=b}\"/>", "Blocked")]
    [InlineData("<TextBlock x:Name=\"b\" Text=\"X\"/><ContentControl><ContentControl.ContentTemplate><DataTemplate><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=b}\"/></DataTemplate></ContentControl.ContentTemplate></ContentControl>", "Pass")]
    public void LabeledBy_needs_a_peer_in_reach(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "input-names"));

    [Theory]
    [InlineData("<StackPanel><TextBlock Text=\"Name\"/><TextBox/></StackPanel>", "Name")]
    [InlineData("<StackPanel><Label Content=\"Name\"/><TextBox/></StackPanel>", "Name")]
    [InlineData("<StackPanel><TextBlock Text=\"Name\"/><StackPanel.ToolTip>t</StackPanel.ToolTip><TextBox/></StackPanel>", "Name")]
    public void The_text_before_an_input_is_suggested(string xaml, string label) =>
        Assert.Contains($"\"{label}\" at line 1 is its label", Kit.Audit(xaml).Findings.Single(f => f.Kind.Slug == "input-no-name").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("clr-namespace:HandyControl.Controls;assembly=HandyControl", "input-no-name")]
    [InlineData("clr-namespace:HandyControl.Controls;assembly=Other", "Blocked")]
    [InlineData("clr-namespace:HandyControl.Controls", "Blocked")]
    public void A_library_is_recognised_by_its_assembly(string ns, string expected) => Assert.Equal(expected, Kit.VerdictNs($"xmlns:h=\"{ns}\"", "input-names", "<h:TextBox/>"));

    [Theory]
    [InlineData("<MediaElement x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "Pass")]
    [InlineData("<Image x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "Pass")]
    [InlineData("<Viewport3D x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "Pass")]
    [InlineData("<Border x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "input-no-name")]
    [InlineData("<StatusBarItem x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "input-no-name")]
    public void Only_elements_with_a_peer_can_label(string label, string expected) =>
        Assert.Equal(expected, Kit.Verdict("input-names", label + "<TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"/>"));

    [Theory]
    [InlineData("", "version was not found")]
    [InlineData("MaterialDesignThemes 4.9.0", "the project uses 4.9.0")]
    [InlineData("MaterialDesignThemes 5.3.2", null)]
    public void A_hint_names_only_in_the_version_that_was_measured(string package, string? why)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><md:BundledTheme/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("Main.xaml", Kit.Window("<TextBox md:HintAssist.Hint=\"Name\"/>")).Add("App.csproj", package.Length > 0 ? Kit.Csproj(package) : "<Project/>");
        var j = p.Audit().Judgements.Single(x => x.Check == "input-names");
        if (why is null) Assert.Equal(Verdict.Pass, j.Verdict);
        else Assert.Contains(why, j.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<TextBlock x:Name=\"a,b\" Text=\"x\"/><TextBox AutomationProperties.LabeledBy=\"{Binding Path='X,Y', ElementName=l}\"/><TextBlock x:Name=\"l\" Text=\"Name\"/>")]
    public void Quotes_inside_a_binding_do_not_split_it(string xaml) => Assert.Equal("Pass", Kit.Verdict("input-names", xaml));

    [Theory]
    [InlineData("<StackPanel><TextBlock Text=\"No data!\"/><ListBox/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"🔍\"/><TextBox/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"Right-click an entry to delete it from the list\"/><TextBox/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"Name\" Visibility=\"Collapsed\"/><TextBox/></StackPanel>")]
    [InlineData("<DockPanel><TextBlock Text=\"Name\" DockPanel.Dock=\"Bottom\"/><TextBox/></DockPanel>")]
    [InlineData("<Grid><TextBlock Text=\"Name\" Grid.Row=\"0\"/><TextBox Grid.Row=\"3\"/></Grid>")]
    public void Text_that_is_not_a_label_is_not_suggested(string xaml) =>
        Assert.DoesNotContain("LabeledBy", Kit.Audit(xaml).Findings.First(x => x.Kind.Check is "input-names" or "list-names").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("<Grid><TextBlock Text=\"Name\"/><TextBox Grid.Column=\"1\"/></Grid>")]
    [InlineData("<Grid><TextBlock Text=\"Name\"/><TextBox Grid.Row=\"1\"/></Grid>")]
    public void A_label_in_the_same_row_or_right_above_is(string xaml) =>
        Assert.Contains("is its label, use AutomationProperties.LabeledBy", Kit.Audit(xaml).Findings.Single(x => x.Kind.Check == "input-names").Message, StringComparison.Ordinal);
}
