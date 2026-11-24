using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>Tab 停得到、UIA 樹上沒有的元素。</summary>
public sealed class SilentStopTests
{
    [Theory]
    [InlineData("ListBoxItem")] [InlineData("TabItem")] [InlineData("TreeViewItem")] [InlineData("ComboBoxItem")] [InlineData("ListViewItem")] [InlineData("MenuItem")]
    [InlineData("ScrollBar")] [InlineData("ScrollViewer")] [InlineData("Slider")] [InlineData("ComboBox")] [InlineData("ToolTip")] [InlineData("DataGridCell")]
    [InlineData("DataGridRow")] [InlineData("GridViewColumnHeader")] [InlineData("DataGridColumnHeader")] [InlineData("DataGridRowHeader")] [InlineData("DataGridRowsPresenter")]
    public void Template_parts_that_Tab_may_not_reach_are_not_judged(string target) =>
        Assert.Equal("NotJudged", Kit.VerdictWithCode("silent-stops", $"<Grid><Grid.Resources><ControlTemplate x:Key=\"t\" TargetType=\"{target}\"><ContentControl/></ControlTemplate></Grid.Resources></Grid>"));

    [Theory]
    [InlineData("Button")]
    [InlineData("GroupBox")]
    [InlineData("Window")]
    public void Other_template_parts_are(string target) =>
        Assert.Equal("Finding", Kit.VerdictWithCode("silent-stops", $"<Grid><Grid.Resources><ControlTemplate x:Key=\"t\" TargetType=\"{target}\"><ContentControl/></ControlTemplate></Grid.Resources></Grid>"));

    /// <summary>AvalonDock 的拖曳指示視窗：ShowActivated=false，Tab 到不了。</summary>
    [Fact]
    public void A_window_that_never_activates() =>
        Assert.Equal("NotJudged", Kit.VerdictWithCode("silent-stops", "<Grid><Grid.Resources><ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:Overlay}\"><ContentControl/></ControlTemplate></Grid.Resources></Grid>", "class Overlay : Window { Overlay() { ShowActivated = false; } }"));

    [Theory]
    [InlineData("Focusable")]
    [InlineData("IsTabStop")]
    public void Set_in_the_code_behind(string member) =>
        Assert.Equal("NotJudged", Kit.VerdictWithCode("silent-stops", "<ContentControl x:Name=\"host\"/>", "", ("Main.xaml.cs", $"partial class Main {{ void Load() {{ host.{member} = false; }} }}")));

    [Theory]
    [InlineData("Focusable", "NotJudged")]
    [InlineData("IsTabStop", "NotJudged")]
    [InlineData("Background", "Finding")]
    public void Changed_by_a_trigger(string property, string expected) =>
        Assert.Equal(expected, Kit.VerdictWithCode("silent-stops", $"<ContentControl><ContentControl.Style><Style TargetType=\"ContentControl\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"{property}\" Value=\"False\"/></Trigger></Style.Triggers></Style></ContentControl.Style></ContentControl>"));

    [Theory]
    [InlineData("Collapsed")]
    [InlineData("Hidden")]
    public void Hidden_elements_are_not_stops(string visibility) => Assert.Equal("", Kit.VerdictWithCode("silent-stops", $"<ContentControl Visibility=\"{visibility}\"/>"));

    /// <summary>有鍵的範本裡的元素是畫面上的；有鍵的元素自己是資源。</summary>
    [Theory]
    [InlineData("<DataTemplate x:Key=\"k\"><ContentControl/></DataTemplate>", "NotJudged")]
    [InlineData("<HierarchicalDataTemplate x:Key=\"k\"><ContentControl/></HierarchicalDataTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"k\" TargetType=\"Button\"><ContentControl/></ControlTemplate>", "Finding")]
    [InlineData("<Style x:Key=\"k\" TargetType=\"Button\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><ContentControl/></ControlTemplate></Setter.Value></Setter></Style>", "Finding")]
    [InlineData("<StackPanel x:Key=\"k\"><ContentControl/></StackPanel>", "")]
    public void In_resources(string resource, string expected) => Assert.Equal(expected, Kit.VerdictWithCode("silent-stops", $"<Grid><Grid.Resources>{resource}</Grid.Resources></Grid>"));

    [Theory]
    [InlineData("GotFocus")] [InlineData("GotKeyboardFocus")] [InlineData("PreviewGotKeyboardFocus")]
    public void A_focus_handler_may_pass_it_on(string handler) => Assert.Equal("NotJudged", Kit.VerdictWithCode("silent-stops", $"<ContentControl {handler}=\"x\"/>"));

    [Theory]
    [InlineData("<ContentControl Content=\"{Binding Page}\"/>")]
    [InlineData("<Border Focusable=\"True\"/>")]
    [InlineData("<local:Fancy/>")]
    public void Silent(string xaml) => Assert.Equal(["silent-tab-stop"], Kit.SlugsOf(Kit.AuditBody(xaml, "class Fancy : ContentControl { }"), "silent-stops"));

    [Theory]
    [InlineData("<ContentControl Focusable=\"False\"/>")]
    [InlineData("<ContentControl IsTabStop=\"False\"/>")]
    [InlineData("<ContentControl KeyboardNavigation.IsTabStop=\"False\"/>")]
    [InlineData("<ContentControl Visibility=\"Collapsed\"/>")]
    [InlineData("<ContentControl IsEnabled=\"False\"/>")]
    [InlineData("<ContentControl Focusable=\"{Binding X}\"/>")]
    [InlineData("<ContentControl GotKeyboardFocus=\"OnFocus\"/>")]
    [InlineData("<ContentControl Style=\"{StaticResource nowhere}\"/>")]
    [InlineData("<ContentControl><ContentControl.Style><Style TargetType=\"ContentControl\"><Style.Triggers><Trigger Property=\"IsEnabled\" Value=\"True\"><Setter Property=\"Focusable\" Value=\"False\"/></Trigger></Style.Triggers></Style></ContentControl.Style></ContentControl>")]
    [InlineData("<StackPanel KeyboardNavigation.TabNavigation=\"None\"><ContentControl/></StackPanel>")]
    [InlineData("<ListBox AutomationProperties.Name=\"a\"><ListBoxItem><ContentControl/></ListBoxItem></ListBox>")]
    [InlineData("<Border/>")]
    [InlineData("<UserControl Focusable=\"True\" IsTabStop=\"True\"/>")]
    [InlineData("<TextBlock Focusable=\"True\" Text=\"a\"/>")]
    [InlineData("<hc:Card/>")]
    [InlineData("<local:Peered/>")]
    [InlineData("<local:Quiet/>")]
    [InlineData("<local:Quieter/>")]
    [InlineData("<local:IsButton/>")]
    [InlineData("<local:Elsewhere/>")]
    [InlineData("<local:Forwards/>")]
    [InlineData("<Popup Focusable=\"True\"/>")]
    [InlineData("<Button Content=\"a\"><Button.ToolTip><ContentControl/></Button.ToolTip></Button>")]
    [InlineData("<StackPanel IsEnabled=\"False\"><ContentControl/></StackPanel>")]
    [InlineData("<Border><Border.Background><VisualBrush><VisualBrush.Visual><ContentControl/></VisualBrush.Visual></VisualBrush></Border.Background></Border>")]
    [InlineData("<ContentControl><ContentControl.Visibility>Collapsed</ContentControl.Visibility></ContentControl>")]
    [InlineData("<DataGrid AutomationProperties.Name=\"a\"><DataGrid.RowDetailsTemplate><DataTemplate><ContentControl/></DataTemplate></DataGrid.RowDetailsTemplate></DataGrid>")]
    [InlineData("<Button Content=\"a\"><Button.Resources><ContentControl x:Key=\"k\"/></Button.Resources></Button>")]
    public void Not_silent_or_not_known(string xaml) => Assert.Empty(Kit.SlugsOf(Kit.AuditBody(xaml,
        "class Peered : Control { protected override AutomationPeer OnCreateAutomationPeer() => null; } class Quiet : Control { Quiet() { Focusable = false; } } class Quieter : Quiet { } class IsButton : Button { } class Forwards : Control { protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { inner.Focus(); } } class Elsewhere : LibraryControl { }"), "silent-stops"));

    /// <summary>自己的類別：修的是類別，報一次；它的樣式不讓 Tab 進來就不報（命名空間怎麼寫都算）。</summary>
    [Fact]
    public void A_class_of_your_own_is_reported_once_at_its_style()
    {
        using var f = new Folder().Add("A.xaml", Kit.Window("<local:Fancy/><local:Fancy/>")).Add("B.xaml", Kit.Window("<local:Fancy/>")).Add("C.cs", "class Fancy : Control { }");
        var once = Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop);
        Assert.Equal("A.xaml", once.File);
        f.Add("Themes/Generic.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:c=\"clr-namespace:Other\"><Style TargetType=\"c:Fancy\"><Setter Property=\"Height\" Value=\"9\"/></Style></ResourceDictionary>");
        Assert.Equal("Themes/Generic.xaml", Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop).File);
        var key = Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop).BaselineKey;
        f.Add("Themes/Generic.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:c=\"clr-namespace:Other\"><Style TargetType=\"c:Fancy\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Style></ResourceDictionary>");
        Assert.DoesNotContain(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop);
        // 函式庫作者的寫法：值在 BasedOn 那一份
        f.Add("Themes/Generic.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:c=\"clr-namespace:Other\"><Style x:Key=\"b\" TargetType=\"c:Fancy\"><Setter Property=\"Focusable\" Value=\"False\"/></Style><Style TargetType=\"c:Fancy\" BasedOn=\"{StaticResource b}\"/></ResourceDictionary>");
        Assert.DoesNotContain(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop);
        // 多一個用它的檔案、樣式拿掉：baseline 的鍵不變
        File.Delete(f.At("Themes/Generic.xaml"));
        f.Add("0.xaml", Kit.Window("<local:Fancy/>"));
        Assert.Equal(key, Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop).BaselineKey);
    }

    /// <summary>壓掉的是那一個用法，不是整個類別。</summary>
    [Fact]
    public void Ignoring_one_use_does_not_hide_the_class()
    {
        using var f = new Folder().Add("A.xaml", Kit.Window("<!-- xamla11y-ignore: decorative here --><local:Fancy/>")).Add("B.xaml", Kit.Window("<local:Fancy/>")).Add("C.cs", "class Fancy : Control { }");
        var r = f.Audit();
        Assert.Equal("B.xaml", Assert.Single(r.Findings, x => x.Kind == Kinds.SilentTabStop).File);
        Assert.Equal(1, r.Checks.Single(x => x.Check == "silent-stops").Suppressed);
    }

    [Fact]
    public void Name_on_something_without_a_peer_is_pointed_out() =>
        Assert.Contains("its AutomationProperties.Name is not used", Assert.Single(Kit.AuditBody("<ContentControl AutomationProperties.Name=\"a\"/>").Findings).Message, StringComparison.Ordinal);
}
