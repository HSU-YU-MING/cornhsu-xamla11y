using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>只能用滑鼠的元素：哪些算、哪些不判。</summary>
public sealed class MouseTests
{
    [Theory]
    [InlineData("<Border MouseLeftButtonUp=\"Open_Click\"/>")]
    [InlineData("<TextBlock MouseDown=\"Open_Click\" Text=\"x\"/>")]
    [InlineData("<TextBlock><Run MouseDown=\"Open_Click\">x</Run></TextBlock>")]
    [InlineData("<Image><Image.InputBindings><MouseBinding Gesture=\"LeftClick\" Command=\"{Binding C}\"/></Image.InputBindings></Image>")]
    [InlineData("<Border><Border.Style><Style TargetType=\"Border\"><EventSetter Event=\"MouseLeftButtonUp\" Handler=\"Open_Click\"/></Style></Border.Style></Border>")]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><Border MouseLeftButtonUp=\"Open_Click\"/></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>")]
    [InlineData("<Border Focusable=\"True\" MouseLeftButtonUp=\"Open_Click\"/>")]
    public void Mouse_only_elements(string xaml) => Assert.Equal(["mouse-only"], Kit.Kinds(xaml));

    [Theory]
    [InlineData("<Border MouseLeftButtonDown=\"D\" MouseMove=\"M\"/>")]
    [InlineData("<Border MouseLeftButtonDown=\"D\"><Button Content=\"x\"/></Border>")]
    [InlineData("<Border MouseLeftButtonDown=\"D\"><local:FancyButton/></Border>")]
    [InlineData("<Border PreviewMouseDown=\"D\"/>")]
    [InlineData("<Border Focusable=\"True\" MouseLeftButtonDown=\"D\" KeyDown=\"K\"/>")]
    [InlineData("<Border MouseLeftButtonDown=\"DragMove_MouseDown\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"n\"><Border MouseLeftButtonDown=\"D\"/></Button>")]
    [InlineData("<ListBox AutomationProperties.Name=\"n\"><ListBox.ItemTemplate><DataTemplate><Border MouseLeftButtonUp=\"D\"/></DataTemplate></ListBox.ItemTemplate></ListBox>")]
    [InlineData("<Border><Border.InputBindings><MouseBinding Gesture=\"RightClick\" Command=\"{Binding C}\"/></Border.InputBindings></Border>")]
    public void Not_mouse_only(string xaml) => Assert.DoesNotContain("mouse-only", Kit.Kinds(xaml));

    [Theory]
    [InlineData("<Button Content=\"x\" Command=\"{Binding  Open}\"/><Border><Border.InputBindings><MouseBinding Gesture=\"LeftClick\" Command=\"{Binding Open}\"/></Border.InputBindings></Border>", "NotJudged")]
    [InlineData("<Button Content=\"x\" Click=\"Open\"/><Border MouseLeftButtonUp=\"Open\"/>", "NotJudged")]
    [InlineData("<Border MouseLeftButtonUp=\"Open\"/><Border MouseLeftButtonUp=\"Open\"/>", "mouse-only,mouse-only")]
    [InlineData("<Button Content=\"x\" Click=\"Open\" Focusable=\"False\"/><Border MouseLeftButtonUp=\"Open\"/>", "mouse-only")]
    [InlineData("<Grid><Grid.InputBindings><KeyBinding Key=\"F5\" Command=\"{Binding Open}\"/></Grid.InputBindings></Grid><Border><Border.InputBindings><MouseBinding Gesture=\"LeftClick\" Command=\"{Binding Open}\"/></Border.InputBindings></Border>", "NotJudged")]
    public void The_same_action_on_a_keyboard_control_is_a_way_in(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "mouse"));

    [Theory]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { if (e.ClickCount == 1) { DragMove(); } }", false)]
    [InlineData("void Down(object s, MouseButtonEventArgs e) => DragMove();", false)]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { box.CaptureMouse(); }", false)]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Open(); } void Other() { DragMove(); }", true)]
    public void A_handler_that_moves_the_window_or_drags_is_recognised_in_the_code_behind(string code, bool reported)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Border MouseLeftButtonDown=\"Down\"/>")).Add("Main.xaml.cs", code);
        Assert.Equal(reported, p.Audit().Findings.Any(f => f.Kind.Slug == "mouse-only"));
    }

    [Theory]
    [InlineData("<Border MouseLeftButtonUp=\"O\"><Border.ContextMenu><ContextMenu><MenuItem Header=\"Copy\"/></ContextMenu></Border.ContextMenu><TextBlock Text=\"Open\"/></Border>", "mouse-only")]
    [InlineData("<Border Drop=\"D\" MouseLeftButtonUp=\"Browse\"/>", "mouse-only")]
    [InlineData("<UserControl MouseLeftButtonUp=\"O\"/>", "mouse-only")]
    [InlineData("<GroupBox Header=\"x\" MouseDown=\"O\"/>", "mouse-only")]
    [InlineData("<Border UIElement.MouseLeftButtonUp=\"O\"/>", "mouse-only")]
    [InlineData("<Border Mouse.MouseDown=\"O\"/>", "mouse-only")]
    [InlineData("<Button AutomationProperties.Name=\"t\"><Grid><Image MouseLeftButtonUp=\"CloseTab\"/></Grid></Button>", "NotJudged")]
    [InlineData("<UserControl MouseLeftButtonUp=\"O\"><Button Content=\"x\"/></UserControl>", "NotJudged")]
    public void Mouse_rule_cases(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "mouse"));

    [Theory]
    [InlineData("<hc:Shield2 MouseLeftButtonDown=\"H\"/>", "Blocked")]
    [InlineData("<local:SvgViewbox MouseLeftButtonUp=\"H\"/>", "Blocked")]
    [InlineData("<local:FancyButton MouseLeftButtonUp=\"H\"/>", "")]             // 控制項的名稱 已經數了
    [InlineData("<StatusBar><StatusBarItem MouseLeftButtonUp=\"H\"/></StatusBar>", "mouse-only")]
    [InlineData("<MediaElement MouseLeftButtonUp=\"H\"/>", "mouse-only")]
    [InlineData("<TextBlock><Underline MouseLeftButtonUp=\"H\">x</Underline></TextBlock>", "mouse-only")]
    public void Clicks_on_elements_this_tool_does_not_know_are_counted(string body, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit(body), "mouse"));

    [Theory]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { SendMessage(helper.Handle, 161, 2, 0); }", "NotJudged")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { SendMessage(h, 0xA1, (IntPtr)2, 0); }", "NotJudged")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Commit(); } void Commit() { DragMove(); }", "NotJudged")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) => DragAndCommit(); ", "NotJudged")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { owner?.StartWordBorderMoveResize(this); }", "NotJudged")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Begin(); } void Begin() { x.CaptureMouse(); }", "NotJudged")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Open(161); }", "mouse-only")]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Open(); } void Open() { }", "mouse-only")]
    public void Dragging_through_a_helper_is_still_dragging(string method, string expected)
    {
        using var p = Kit.Files("<Grid><Border MouseLeftButtonDown=\"Down\"/></Grid>", $"class M {{ {method} }}");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "mouse"));
    }

    [Theory]
    [InlineData("<Button Content=\"x\" Command=\"{Binding Open}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"x\" Command=\"{Binding Other}\"/>", "mouse-only")]
    public void Behaviors_that_invoke_a_command_count_as_the_same_action(string other, string expected)
    {
        const string ns = "xmlns:i=\"http://schemas.microsoft.com/xaml/behaviors\"";
        var r = Auditor.Run(XamlA11y.Core.Analysis.Project.FromText($"<Window {Kit.Ns} {ns}><StackPanel>{other}<Border><i:Interaction.Triggers><i:EventTrigger EventName=\"MouseLeftButtonUp\"><i:InvokeCommandAction Command=\"{{Binding Open}}\"/></i:EventTrigger></i:Interaction.Triggers></Border></StackPanel></Window>"));
        Assert.Equal(expected, Kit.Verdicts(r, "mouse"));
    }

    [Theory]
    [InlineData("Border")]
    [InlineData("Grid")]
    [InlineData("StackPanel")]
    [InlineData("DockPanel")]
    [InlineData("WrapPanel")]
    [InlineData("Canvas")]
    [InlineData("UniformGrid")]
    [InlineData("Viewbox")]
    [InlineData("Decorator")]
    [InlineData("Image")]
    [InlineData("Path")]
    [InlineData("Rectangle")]
    [InlineData("Ellipse")]
    [InlineData("Line")]
    [InlineData("Polygon")]
    [InlineData("Polyline")]
    [InlineData("TextBlock")]
    [InlineData("Label")]
    [InlineData("ContentControl")]
    [InlineData("ContentPresenter")]
    [InlineData("GroupBox")]
    [InlineData("ItemsControl")]
    [InlineData("HeaderedContentControl")]
    [InlineData("MediaElement")]
    [InlineData("AccessText")]
    public void Every_non_control_that_is_clicked_is_checked(string element) =>
        Assert.Equal(["mouse-only"], Kit.Kinds($"<Grid><{element} MouseLeftButtonDown=\"H\"/></Grid>"));

    [Theory]
    [InlineData("<Button Content=\"x\"/>")]
    [InlineData("<TextBox AutomationProperties.Name=\"n\"/>")]
    [InlineData("<ListBox/>")]
    [InlineData("<ListView/>")]
    [InlineData("<TreeView/>")]
    [InlineData("<DataGrid/>")]
    [InlineData("<ScrollViewer/>")]
    [InlineData("<ItemsControl/>")]
    [InlineData("<StackPanel><Slider AutomationProperties.Name=\"n\"/></StackPanel>")]
    [InlineData("<local:FancyButton/>")]
    [InlineData("<local:FancyTextBox/>")]
    public void A_clickable_container_around_controls_is_not_judged(string inside) =>
        Assert.Equal("NotJudged", Kit.Verdicts(Kit.Audit($"<Grid><Border MouseLeftButtonDown=\"H\">{inside}</Border></Grid>"), "mouse"));

    [Theory]
    [InlineData("<Border.ContextMenu><ContextMenu><MenuItem Header=\"x\"/></ContextMenu></Border.ContextMenu>")]
    [InlineData("<Border.ToolTip><Button Content=\"x\"/></Border.ToolTip>")]
    [InlineData("<Border.Resources><Button x:Key=\"b\" Content=\"x\"/></Border.Resources>")]
    public void Controls_that_are_not_inside_it_do_not_count(string inside) =>
        Assert.Equal("mouse-only", Kit.Verdicts(Kit.Audit($"<Grid><Border MouseLeftButtonDown=\"H\">{inside}</Border></Grid>"), "mouse"));

    [Theory]
    [InlineData("<Border.InputBindings><MouseBinding MouseAction=\"LeftClick\" Command=\"{Binding A}\"/></Border.InputBindings>")]
    public void A_mouse_binding_elsewhere_is_not_a_keyboard_way(string inside) =>
        Assert.Equal("mouse-only,mouse-only", Kit.Verdicts(Kit.Audit($"<Grid><Border>{inside}</Border><Border>{inside}</Border></Grid>"), "mouse"));

    [Theory]
    [InlineData("UIElement")]
    [InlineData("Mouse")]
    [InlineData("Control")]
    [InlineData("FrameworkElement")]
    [InlineData("ContentElement")]
    public void An_event_may_be_written_with_its_owner(string owner) =>
        Assert.Equal("mouse-only", Kit.Verdict("mouse", $"<Grid><Border {owner}.MouseDown=\"H\"/></Grid>"));

    [Theory]
    [InlineData("KeyDown")]
    [InlineData("KeyUp")]
    [InlineData("PreviewKeyDown")]
    [InlineData("PreviewKeyUp")]
    [InlineData("Keyboard.KeyDown")]
    public void A_key_handler_on_a_focusable_element_is_not_judged(string name)
    {
        Assert.Equal("NotJudged", Kit.Verdict("mouse", $"<Grid><Border Focusable=\"True\" MouseDown=\"H\" {name}=\"K\"/></Grid>"));
        Assert.Equal("mouse-only", Kit.Verdict("mouse", $"<Grid><Border MouseDown=\"H\" {name}=\"K\"/></Grid>"));
    }

    [Theory]
    [InlineData("MouseLeftButtonDown")]
    [InlineData("MouseLeftButtonUp")]
    [InlineData("MouseDown")]
    [InlineData("MouseUp")]
    [InlineData("TouchDown")]
    [InlineData("TouchUp")]
    public void Every_click_event_is_seen(string name)
    {
        Assert.Equal("mouse-only", Kit.Verdict("mouse", $"<Grid><Border {name}=\"H\"/></Grid>"));
        Assert.Equal("mouse-only", Kit.Verdict("mouse", "<Grid><Border/></Grid>", $"<Style TargetType=\"Border\"><EventSetter Event=\"{name}\" Handler=\"H\"/></Style>"));
    }

    [Theory]
    [InlineData("PreviewMouseLeftButtonDown")]
    [InlineData("PreviewMouseLeftButtonUp")]
    [InlineData("PreviewMouseDown")]
    [InlineData("PreviewMouseUp")]
    public void Preview_events_are_not_judged(string name) => Assert.Equal("NotJudged", Kit.Verdict("mouse", $"<Grid><Border {name}=\"H\"/></Grid>"));

    [Theory]
    [InlineData("MouseMove")]
    [InlineData("PreviewMouseMove")]
    public void Moving_is_a_gesture(string name) => Assert.Equal("NotJudged", Kit.Verdict("mouse", $"<Grid><Border MouseDown=\"H\" {name}=\"M\"/></Grid>"));

    [Theory]
    [InlineData("<Grid><Border MouseDown=\"\"/></Grid>", "")]
    [InlineData("<TextBlock><Hyperlink><Run MouseDown=\"H\" Text=\"x\"/></Hyperlink></TextBlock>", "Pass")]
    [InlineData("<TextBlock><Run MouseDown=\"H\" Text=\"x\"/></TextBlock>", "mouse-only")]
    [InlineData("<ListView><ListViewItem><Border MouseDown=\"H\"/></ListViewItem></ListView>", "NotJudged")]
    [InlineData("<ListBox><ListBoxItem><Border MouseDown=\"H\"/></ListBoxItem></ListBox>", "NotJudged")]
    [InlineData("<TreeView><TreeViewItem><TreeViewItem.Header><Border MouseDown=\"H\"/></TreeViewItem.Header></TreeViewItem></TreeView>", "NotJudged")]
    [InlineData("<TabControl><TabItem><TabItem.Header><Border MouseDown=\"H\"/></TabItem.Header></TabItem></TabControl>", "NotJudged")]
    [InlineData("<TabControl><TabItem Header=\"a\"><Border MouseDown=\"H\"/></TabItem></TabControl>", "mouse-only")]
    [InlineData("<Button><Border MouseDown=\"H\"/></Button>", "NotJudged")]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><Border MouseDown=\"H\"/></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>", "mouse-only")]
    [InlineData("<ListBox><ListBox.ItemTemplate><DataTemplate><Border MouseDown=\"H\"/></DataTemplate></ListBox.ItemTemplate></ListBox>", "NotJudged")]
    public void Where_the_click_is(string xaml, string expected) => Assert.Equal(expected, Kit.Verdict("mouse", xaml));
}
