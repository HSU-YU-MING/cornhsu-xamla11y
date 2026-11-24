using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>並排、名稱一樣、做的事不一樣。</summary>
public sealed class SameNameTests
{
    [Theory]
    [InlineData("CheckBox", "IsChecked")] [InlineData("ComboBox", "SelectedItem")] [InlineData("ComboBox", "SelectedValue")] [InlineData("ComboBox", "SelectedIndex")]
    [InlineData("ComboBox", "Text")] [InlineData("Slider", "Value")] [InlineData("DatePicker", "SelectedDate")] [InlineData("TextBox", "Text")]
    public void What_an_input_is_for(string element, string property) =>
        Assert.Equal("Finding", Kit.VerdictWithCode("same-names", $"<{element} AutomationProperties.Name=\"X\" {property}=\"{{Binding A}}\"/><{element} AutomationProperties.Name=\"X\" {property}=\"{{Binding B}}\"/>"));

    [Theory]
    [InlineData("Visibility")] [InlineData("IsEnabled")] [InlineData("Content")] [InlineData("AutomationProperties.Name")]
    public void A_button_that_changes_is_left_out(string property) =>
        Assert.Equal("", Kit.VerdictWithCode("same-names", $"<Button Content=\"Go\" Click=\"A\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"{property}\" Value=\"False\"/></Trigger></Style.Triggers></Style></Button.Style></Button><Button Content=\"Go\" Click=\"B\"/>"));

    [Fact]
    public void A_button_that_only_changes_its_color_is_not() =>
        Assert.Equal("Finding", Kit.VerdictWithCode("same-names", "<Button Content=\"Go\" Click=\"A\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"Background\" Value=\"Red\"/></Trigger></Style.Triggers></Style></Button.Style></Button><Button Content=\"Go\" Click=\"B\"/>"));

    [Fact]
    public void Items_of_a_list_may_sound_alike() =>
        Assert.Equal("", Kit.VerdictWithCode("same-names", "<ListBox AutomationProperties.Name=\"a\"><ListBoxItem Content=\"x\" Tag=\"1\"/><ListBoxItem Content=\"x\" Tag=\"2\"/></ListBox><TabControl><TabItem Header=\"x\" Tag=\"1\"/><TabItem Header=\"x\" Tag=\"2\"/></TabControl>"));

    [Fact]
    public void A_name_from_the_style_counts() =>
        Assert.Equal("Finding", Kit.VerdictWithCode("same-names", "<StackPanel><StackPanel.Resources><Style TargetType=\"Button\"><Setter Property=\"Content\" Value=\"…\"/></Style></StackPanel.Resources><Button Click=\"A\"/><Button Click=\"B\"/></StackPanel>"));

    [Theory]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\"/><Button Content=\"Edit\" Command=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"_Edit\" Click=\"A\"/><Button Content=\"Edit\" Click=\"B\"/>")]
    [InlineData("<Button Content=\"{DynamicResource s}\" Click=\"A\"/><Button Content=\"{DynamicResource s}\" Click=\"B\"/>")]
    [InlineData("<Button Click=\"A\"><TextBlock Text=\"Edit\"/></Button><Button Click=\"B\">Edit</Button>")]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\" CommandParameter=\"1\"/><Button Content=\"Edit\" Command=\"{Binding A}\" CommandParameter=\"2\"/>")]
    [InlineData("<TextBox AutomationProperties.Name=\"X\" Text=\"{Binding A}\"/><TextBox AutomationProperties.Name=\"X\" Text=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"Edit\" Click=\"Go\" Tag=\"in\"/><Button Content=\"Edit\" Click=\"Go\" Tag=\"out\"/>")]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\" CommandTarget=\"{Binding X}\"/><Button Content=\"Edit\" Command=\"{Binding A}\" CommandTarget=\"{Binding Y}\"/>")]
    public void Same_name_different_action(string xaml) => Assert.Equal(["same-name"], Kit.SlugsOf(Kit.AuditBody(xaml, resources: "<sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"s\">Edit</sys:String>"), "same-names"));

    [Theory]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\"/><Button Content=\"Edit\" Command=\"{Binding A}\"/>")]           // 同一個動作
    [InlineData("<Button Content=\"Edit\"/><Button Content=\"Edit\"/>")]                                                            // 做什麼看不出來
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\"/><Button Content=\"Delete\" Command=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"Edit\" Click=\"A\" Visibility=\"{Binding V}\"/><Button Content=\"Edit\" Click=\"B\"/>")]         // 輪流出現
    [InlineData("<Button Content=\"Edit\" Click=\"A\"/><CheckBox Content=\"Edit\" IsChecked=\"{Binding B}\"/>")]                     // 型別不同，念起來不一樣
    [InlineData("<StackPanel><Button Content=\"Edit\" Click=\"A\"/></StackPanel><StackPanel><Button Content=\"Edit\" Click=\"B\"/></StackPanel>")]
    [InlineData("<Button Content=\"{Binding N}\" Click=\"A\"/><Button Content=\"{Binding N}\" Click=\"B\"/>")]
    [InlineData("<Button Content=\"{DynamicResource s}\" Click=\"A\"/><Button Content=\"{DynamicResource t}\" Click=\"B\"/>")]       // 不同的鍵剛好同一個字：別的語言可能不同
    [InlineData("<TextBlock x:Name=\"l\" Text=\"In\"/><TextBlock x:Name=\"m\" Text=\"Out\"/><CheckBox Content=\"On\" IsChecked=\"{Binding A}\" AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"/><CheckBox Content=\"On\" IsChecked=\"{Binding B}\" AutomationProperties.LabeledBy=\"{Binding ElementName=m}\"/>")]
    [InlineData("<Button Click=\"A\"><TextBlock>x<Span>y</Span></TextBlock></Button><Button Content=\"x\" Click=\"B\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"a_b\" Content=\"1\" Click=\"A\"/><Button Content=\"ab\" Click=\"B\"/>")]                  // Name 的底線不是存取鍵
    [InlineData("<Button Content=\"Edit\" Click=\"A\"><Button.Visibility><Binding Path=\"V\"/></Button.Visibility></Button><Button Content=\"Edit\" Click=\"B\"/>")]
    [InlineData("<Button Content=\"Go\" Click=\"A\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><DataTrigger Binding=\"{Binding R}\" Value=\"True\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button><Button Content=\"Go\" Click=\"B\"/>")]
    [InlineData("<RadioButton Content=\"Yes\" GroupName=\"1\" IsChecked=\"{Binding A}\"/><RadioButton Content=\"Yes\" GroupName=\"2\" IsChecked=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"Edit\" Click=\"A\" IsEnabled=\"False\"/><Button Content=\"Edit\" Click=\"B\"/>")]
    public void Not_the_same_name(string xaml) => Assert.Empty(Kit.SlugsOf(Kit.AuditBody(xaml, resources: "<sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"s\">Edit</sys:String><sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"t\">Edit</sys:String>"), "same-names"));

    /// <summary>一組報一次，多加一顆同名的不會變成新的。</summary>
    [Fact]
    public void One_finding_per_group_and_a_stable_key()
    {
        const string two = "<Button Content=\"Edit\" Click=\"A\"/><Button Content=\"Edit\" Click=\"B\"/>";
        var before = Assert.Single(Kit.AuditBody(two).Findings, f => f.Kind == Kinds.SameName);
        var after = Assert.Single(Kit.AuditBody("<Button Content=\"Edit\" Click=\"C\"/>" + two).Findings, f => f.Kind == Kinds.SameName);
        Assert.Equal(before.BaselineKey, after.BaselineKey);
        Assert.Equal(Severity.Info, before.Severity);
        Assert.Contains("3 <Button> here may sound alike", after.Message, StringComparison.Ordinal);
    }
}
