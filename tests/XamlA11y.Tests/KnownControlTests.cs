using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>認得哪些控制項：WPF 的型別清單、看起來像控制項的、treatAs、專案自己的類別。</summary>
public sealed class KnownControlTests
{
    [Fact]
    public void An_unknown_control_is_blocked_and_listed()
    {
        var r = Kit.Audit($"<local:FancyButton>{Kit.Icon}</local:FancyButton>");
        Assert.Empty(r.Findings);
        Assert.Equal(("FancyButton", 1), Assert.Single(r.UnknownControls));
        Assert.Equal(0, r.Resolution);
    }

    [Fact]
    public void TreatAs_judges_it() =>
        Assert.Equal("missing-name", Assert.Single(Kit.Audit($"<local:FancyButton>{Kit.Icon}</local:FancyButton>", treatAs: new Dictionary<string, string> { ["FancyButton"] = "Button" }).Findings).Kind.Slug);

    [Fact]
    public void A_class_in_the_projects_source_is_followed_to_its_base()
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window($"<local:FancyButton>{Kit.Icon}</local:FancyButton><local:Derived Content=\"ok\"/>"))
            .Add("FancyButton.cs", "namespace App; public class FancyButton : Button { }")
            .Add("Derived.cs", "namespace App; public sealed partial class Derived : FancyButton, IThing { }");
        Assert.Equal(["missing-name"], p.Audit().Findings.Select(f => f.Kind.Slug));
    }

    [Theory]
    [InlineData("public class FancyButton : Button { protected override AutomationPeer OnCreateAutomationPeer() => null; }")]
    [InlineData("public class FancyButton : Button { public FancyButton() { AutomationProperties.SetName(this, \"x\"); } }")]
    [InlineData("public class FancyButton : Button { } class Other { class FancyButton : Slider { } }")]
    [InlineData("public class FancyButton : SomethingUnknown { }")]
    public void A_class_that_names_itself_or_is_ambiguous_is_unknown(string code)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<local:FancyButton>{Kit.Icon}</local:FancyButton>")).Add("F.cs", code);
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Fact]
    public void TreatAs_does_not_override_WPF_itself() =>
        Assert.Equal("missing-name", Kit.Verdicts(Kit.Audit($"<Button>{Kit.Icon}</Button>", treatAs: new Dictionary<string, string> { ["Button"] = "Border" }), "control-names"));

    [Fact]
    public void Classes_are_found_through_the_projects_own_xml_namespace()
    {
        const string xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:c=\"http://myapp/controls\"><Button><c:Cap/></Button></Window>";
        var cap = $"<UserControl {Kit.Ns} x:Class=\"App.Cap\"><TextBlock Text=\"Save\"/></UserControl>";
        using var with = new Folder().Add("Main.xaml", xaml).Add("Cap.xaml", cap).Add("A.cs", "[assembly: XmlnsDefinition(\"http://myapp/controls\", \"App\")]");
        Assert.Equal("Pass", Kit.Verdicts(with.Audit(), "control-names"));
        using var without = new Folder().Add("Main.xaml", xaml).Add("Cap.xaml", cap);
        Assert.Equal("Blocked", Kit.Verdicts(without.Audit(), "control-names"));
    }

    [Fact]
    public void WPF_types_written_with_a_CLR_namespace_are_WPF_types()
    {
        const string ns = "xmlns:c=\"clr-namespace:System.Windows.Controls;assembly=PresentationFramework\"";
        var r = Auditor.Run(XamlA11y.Core.Analysis.Project.FromText($"<Window {Kit.Ns} {ns}><c:StackPanel><c:Button/><c:Border MouseLeftButtonUp=\"H\"/></c:StackPanel></Window>"));
        Assert.Equal(["missing-name", "mouse-only"], r.Findings.Select(f => f.Kind.Slug).Order());
    }

    [Fact]
    public void Unmeasured_WPF_controls_are_unknown_not_silent() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<RibbonButton Label=\"Save\"/>"), "control-names"));

    [Theory]
    [InlineData("<Button/>")]
    [InlineData("<ToggleButton/>")]
    [InlineData("<RepeatButton/>")]
    [InlineData("<CheckBox/>")]
    [InlineData("<RadioButton/>")]
    [InlineData("<Menu><MenuItem/></Menu>")]
    [InlineData("<TextBlock><Hyperlink/></TextBlock>")]
    [InlineData("<TabControl><TabItem/></TabControl>")]
    [InlineData("<Expander/>")]
    [InlineData("<TreeView><TreeViewItem/></TreeView>")]
    [InlineData("<ListBox><ListBoxItem/></ListBox>")]
    [InlineData("<ListView><ListViewItem/></ListView>")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"><ComboBoxItem/></ComboBox>")]
    public void Every_interactive_control_is_checked(string xaml) =>
        Assert.Contains(Kit.Audit(xaml).Findings, f => f.Kind.Slug is "missing-name" or "meaningless-name");

    [Theory]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    [InlineData("RichTextBox")]
    [InlineData("ComboBox")]
    [InlineData("Slider")]
    [InlineData("DatePicker")]
    public void Every_input_is_checked(string control) => Assert.Equal(["input-no-name"], Kit.Kinds($"<{control}/>"));

    [Theory]
    [InlineData("FancyButton", "control-names")]
    [InlineData("FancyCheckBox", "control-names")]
    [InlineData("FancyToggleSwitch", "control-names")]
    [InlineData("FancyMenuItem", "control-names")]
    [InlineData("FancyTabItem", "control-names")]
    [InlineData("FancyExpander", "control-names")]
    [InlineData("FancyHyperlink", "control-names")]
    [InlineData("FancyTextBox", "input-names")]
    [InlineData("FancyPasswordBox", "input-names")]
    [InlineData("FancyComboBox", "input-names")]
    [InlineData("FancySlider", "input-names")]
    [InlineData("FancyDatePicker", "input-names")]
    [InlineData("FancyNumericUpDown", "input-names")]
    [InlineData("FancyNumberBox", "input-names")]
    [InlineData("DecimalUpDown", "input-names")]
    public void Unknown_controls_are_recognised_by_their_names(string control, string rule) =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit($"<local:{control}/>"), rule));

    [Theory]
    [InlineData("Button,ToggleButton,RepeatButton,CheckBox,RadioButton,Label,ContentControl,UserControl,ListBoxItem,ListViewItem,ComboBoxItem,StatusBarItem,ToolTip,Frame,ScrollViewer,ButtonBase,GridViewColumnHeader,DataGridColumnHeader,DataGridRowHeader,DataGridCell")]
    [InlineData("MenuItem,TabItem,Expander,TreeViewItem,GroupBox,HeaderedContentControl,HeaderedItemsControl,ToolBar")]
    [InlineData("TextBox,PasswordBox,RichTextBox,ComboBox,Slider,DatePicker,ListBox,ListView,TreeView,DataGrid,Calendar,ProgressBar,ItemsControl,TabControl,Menu,ContextMenu,ScrollBar,Thumb,GridSplitter")]
    [InlineData("Separator,StatusBar,Control,DatePickerTextBox,InkCanvas,MediaElement,WebBrowser,FlowDocumentScrollViewer")]
    [InlineData("Grid,StackPanel,DockPanel,WrapPanel,Canvas,UniformGrid,VirtualizingStackPanel,TabPanel,ToolBarPanel,Border,Viewbox,Decorator,BulletDecorator,AdornerDecorator,Image,Path,Rectangle,Ellipse,Line,Polygon,Polyline")]
    [InlineData("AccessText,ContentPresenter,ItemsPresenter,Popup,Glyphs,Viewport3D,InkPresenter,TickBar,Track,ScrollContentPresenter,TextBlock,Hyperlink")]
    [InlineData("DataGridRow,Window,Page,FrameworkElement,TextElement,Inline")]
    public void The_WPF_types_are_known(string types)
    {
        foreach (var type in types.Split(','))
            Assert.True(A11yConfig.Parse($"{{\"treatAs\":{{\"X\":\"{type}\"}}}}").Problems.Count == 0, type);
    }

    [Fact]
    public void Unknown_content_is_listed() =>
        Assert.Equal("FooChart", Assert.Single(Kit.Audit("<Button><local:FooChart/></Button>").UnknownControls).Element);

    [Theory]
    [InlineData("class MyButton : System.Windows.Controls.Button { }", "missing-name")]
    [InlineData("class MyButton : global::System.Windows.Controls.Button { }", "missing-name")]
    [InlineData("class MyButton : Button, IDisposable { }", "missing-name")]
    [InlineData("class MyButton<T> : Button { }", "missing-name")]
    [InlineData("class MyButton(int x) : Button(x) { }", "missing-name")]
    public void Bases_are_read_however_they_are_written(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<local:MyButton/>")).Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }
}
