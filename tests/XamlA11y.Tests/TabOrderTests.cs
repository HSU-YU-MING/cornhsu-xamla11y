using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>Tab 到不了的控制項，與別的入口。</summary>
public sealed class TabOrderTests
{
    [Fact]
    public void Not_in_tab_order_is_a_warning() => Assert.Equal(Severity.Warn, Assert.Single(Kit.Audit("<Button Content=\"x\" IsTabStop=\"False\"/>").Findings).Severity);

    [Theory]
    [InlineData("<Button Content=\"_x\" IsTabStop=\"False\"/>")]
    [InlineData("<Button Content=\"x\" IsTabStop=\"False\" IsCancel=\"True\"/>")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"{x:Static SystemCommands.CloseWindowCommand}\"/>")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\" IsEditable=\"True\" IsTabStop=\"False\"/>")]
    [InlineData("<TextBox AutomationProperties.Name=\"n\" IsReadOnly=\"True\" IsTabStop=\"False\"/>")]
    [InlineData("<Button Content=\"x\" IsTabStop=\"True\"/>")]
    public void Other_ways_in_are_accepted(string xaml) => Assert.Empty(Kit.Kinds(xaml));

    [Theory]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource Ext}\" KeyboardNavigation.IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<Button Content=\"x\" Focusable=\"{Binding F}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource Ext}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"x\" KeyboardNavigation.IsTabStop=\"True\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Style></Button.Style></Button>", "Pass")]
    [InlineData("<Button Content=\"x\"/>", "")]
    public void Tab_order_only_counts_what_it_read(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "tab-order"));

    [Theory]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"{Binding Open}\"/>", "", "not-in-tab-order")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"{Binding Open}\"/><Grid><Grid.InputBindings><KeyBinding Key=\"F2\" Command=\"{Binding Open}\"/></Grid.InputBindings></Grid>", "", "Pass")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"ApplicationCommands.Copy\"/>", "", "Pass")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"ApplicationCommands.Close\"/>", "", "not-in-tab-order")]
    [InlineData("<Button IsTabStop=\"False\"><AccessText Text=\"_Open\"/></Button>", "", "Pass")]
    [InlineData("<Button IsTabStop=\"False\"><AccessText Text=\"Open\"/></Button>", "", "not-in-tab-order")]
    [InlineData("<Label Content=\"_Name\" Target=\"{Binding ElementName=n}\"/><TextBox x:Name=\"n\" IsTabStop=\"False\"/>", "", "Pass")]
    [InlineData("<Label Content=\"Name\" Target=\"{Binding ElementName=n}\"/><TextBox x:Name=\"n\" IsTabStop=\"False\"/>", "", "not-in-tab-order")]
    [InlineData("<Button Content=\"x\" IsTabStop=\"False\" Click=\"Go\"/><Button Content=\"y\" Click=\"Go\"/>", "", "Pass")]
    [InlineData("", "<DataTemplate x:Key=\"d\"><Button Content=\"x\" IsTabStop=\"False\"/></DataTemplate>", "NotJudged")]
    [InlineData("<ToolBar><Button Content=\"x\"/></ToolBar>", "<Style TargetType=\"Button\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Style>", "")]
    public void Other_ways_in_from_the_keyboard_count(string body, string resources, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit(body, resources), "tab-order"));

    [Theory]
    [InlineData("IsEditable=\"True\"", "Pass", "Pass")]
    [InlineData("", "focus-not-visible", "not-in-tab-order")]
    public void An_editable_combo_box_has_a_text_box_of_its_own(string attribute, string focus, string tab)
    {
        Assert.Equal(focus, Kit.Verdicts(Kit.Audit($"<ComboBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{{x:Null}}\" {attribute}/>"), "focus"));
        Assert.Equal(tab, Kit.Verdicts(Kit.Audit($"<ComboBox AutomationProperties.Name=\"n\" IsTabStop=\"False\" {attribute}/>"), "tab-order"));
    }

    [Fact]
    public void Being_editable_only_matters_on_a_combo_box() =>
        Assert.Equal("not-in-tab-order", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" IsTabStop=\"False\"/><ComboBox AutomationProperties.Name=\"n\"/>"), "tab-order"));

    [Theory]
    [InlineData("Copy")]
    [InlineData("Cut")]
    [InlineData("Paste")]
    [InlineData("Undo")]
    [InlineData("Redo")]
    [InlineData("Delete")]
    [InlineData("Find")]
    [InlineData("Replace")]
    [InlineData("SelectAll")]
    [InlineData("Help")]
    [InlineData("New")]
    [InlineData("Open")]
    [InlineData("Save")]
    [InlineData("Print")]
    [InlineData("PrintPreview")]
    [InlineData("Properties")]
    [InlineData("ContextMenu")]
    public void Commands_with_a_gesture_of_their_own_are_reachable(string command) =>
        Assert.Equal("Pass", Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" IsTabStop=\"False\" Command=\"{command}\"/>"), "tab-order"));

    [Theory]
    [InlineData("SaveAs")]
    [InlineData("Close")]
    [InlineData("Stop")]
    [InlineData("local:ApplicationCommands.Copy")]
    public void Commands_without_one_are_not(string command) =>
        Assert.Equal("not-in-tab-order", Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" IsTabStop=\"False\" Command=\"{command}\"/>"), "tab-order"));

    [Theory]
    [InlineData("<Slider AutomationProperties.Name=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<RichTextBox AutomationProperties.Name=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<PasswordBox AutomationProperties.Name=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<RadioButton Content=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<TextBlock><Hyperlink IsTabStop=\"False\">n</Hyperlink></TextBlock>", "not-in-tab-order")]
    [InlineData("<TextBox AutomationProperties.Name=\"n\" IsTabStop=\"False\" IsReadOnly=\"True\"/>", "NotJudged")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"False\" IsDefault=\"True\"/>", "Pass")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"False\" IsCancel=\"True\"/>", "Pass")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"{Binding T}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"{StaticResource nowhere}\"/>", "Blocked")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"True\"/>", "Pass")]
    public void Tab_order(string xaml, string expected) => Assert.Equal(expected, Kit.Verdict("tab-order", xaml));
}
