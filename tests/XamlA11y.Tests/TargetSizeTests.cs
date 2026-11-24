using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>點擊範圍小於 24 × 24。</summary>
public sealed class TargetSizeTests
{
    [Theory]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Content=\"a\" Width=\"20\" Height=\"20\"/><Button Content=\"b\" Width=\"20\" Height=\"20\"/></StackPanel>", 2)]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Content=\"a\" Width=\"20\" Height=\"20\" Margin=\"4\"/><Button Content=\"b\" Width=\"20\" Height=\"20\" Margin=\"4\"/></StackPanel>", 0)]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Content=\"a\" Width=\"20\" Height=\"20\"/><TextBlock Text=\"b\"/></StackPanel>", 0)]
    [InlineData("<Button Content=\"default size\"/>", 0)]
    [InlineData("<Grid><Button Content=\"a\" Width=\"10\"/><Button Content=\"b\" Width=\"10\"/></Grid>", 0)]
    public void Small_targets(string xaml, int count) => Assert.Equal(count, Kit.Kinds(xaml).Count(k => k == "small-target"));

    /// <summary>附加屬性不是元素自己的屬性（語料：HandyControl 166 筆誤報）。</summary>
    [Fact]
    public void An_attached_height_is_not_the_controls_height() =>
        Assert.DoesNotContain("small-target", Kit.Kinds(
            "<StackPanel><Button Content=\"a\" Style=\"{StaticResource s}\"/><Button Content=\"b\" Style=\"{StaticResource s}\"/></StackPanel>",
            "<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"hc:IconElement.Height\" Value=\"12\"/></Style>"));

    [Theory]
    [InlineData("<WrapPanel ItemWidth=\"60\"><Button Content=\"a\" Width=\"16\"/><Button Content=\"b\" Width=\"16\"/></WrapPanel>", "NotJudged,NotJudged")]
    [InlineData("<Button Content=\"a\" Width=\"40\" Height=\"{DynamicResource Missing}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"a\" MaxWidth=\"300\" MaxHeight=\"300\"/>", "NotJudged")]
    [InlineData("<Button Content=\"a\" Width=\"40\" MinHeight=\"30\"/>", "Pass")]
    public void Sizes_that_are_only_partly_known(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "target-size"));

    [Theory]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "small-target,small-target")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><ComboBox Width=\"16\" Height=\"30\" AutomationProperties.Name=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "small-target,small-target")]
    [InlineData("<ToolBar><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></ToolBar>", "small-target,small-target")]
    [InlineData("<ToolBarTray Orientation=\"Vertical\"><ToolBar><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></ToolBar></ToolBarTray>", "NotJudged,NotJudged")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\" Visibility=\"Collapsed\"/></StackPanel>", "NotJudged,small-target")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><StackPanel.LayoutTransform><ScaleTransform/></StackPanel.LayoutTransform><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "NotJudged,NotJudged")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><StackPanel.RenderTransform><ScaleTransform/></StackPanel.RenderTransform><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "NotJudged,NotJudged")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Width=\"30\" Height=\"30\" Content=\"a\"/><Button Width=\"30\" Height=\"30\" Content=\"b\"/></StackPanel>", "Pass,Pass")]
    public void Target_size(string xaml, string expected) => Assert.Equal(expected, Kit.Verdict("target-size", xaml).Replace("Passed", "Pass", StringComparison.Ordinal));
}
