using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>清單一項一個案例（第二輪變異測試，300 處）。少一項不會讓別的測試變紅，使用者的專案就默默少查一種寫法。</summary>
public class PinnedListsTests
{
    private static string Audit(string rule, string body, string resources = "") => Kit.Verdicts(Kit.Audit(body, resources), rule);

    private static string Template(string triggers) =>
        $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border x:Name=\"b\"><ContentPresenter/></Border>{triggers}</ControlTemplate></Button.Template></Button>";

    // ───────── WPF 的型別（treatAs 只收這些） ─────────
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

    [Theory]
    [InlineData("Grid,StackPanel,DockPanel,WrapPanel,Canvas,UniformGrid,VirtualizingStackPanel,TabPanel,ToolBarPanel,Border,Viewbox,Decorator,BulletDecorator,AdornerDecorator,Image,Path,Rectangle,Ellipse,Line,Polygon,Polyline,Popup,InkPresenter,TickBar,Glyphs,Viewport3D")]
    [InlineData("Calendar,ProgressBar,ItemsControl,TabControl,Menu,ScrollBar,Thumb,GridSplitter,Separator,StatusBar,InkCanvas,MediaElement,WebBrowser,ListBox,TreeView,DataGrid")]
    public void Content_that_gives_no_text_leaves_the_button_without_a_name(string types)
    {
        foreach (var type in types.Split(','))
            Assert.True(Audit("R1", $"<Button><{type}/></Button>").StartsWith("missing-name", StringComparison.Ordinal), type);
    }

    // ───────── 屬性的擁有者 ─────────
    [Theory]
    [InlineData("Control,FrameworkElement,FrameworkContentElement,UIElement,ContentElement,ContentControl,HeaderedContentControl,ItemsControl,HeaderedItemsControl,ButtonBase,Button,ToggleButton,RepeatButton,CheckBox,RadioButton")]
    [InlineData("TextBlock,TextElement,TextBoxBase,TextBox,PasswordBox,RichTextBox,ComboBox,ComboBoxItem,MenuItem,ListBox,ListBoxItem,ListView,ListViewItem,TreeViewItem,TabItem,Expander,Slider,RangeBase,Selector")]
    [InlineData("Label,Shape,Image,Border,Hyperlink,Window,Popup,DatePicker,UserControl,Rectangle,Ellipse,Path")]
    public void Any_base_may_be_named_as_the_owner(string owners)
    {
        foreach (var owner in owners.Split(','))
            Assert.True(Audit("R5", "<Button Content=\"x\"/>", $"<Style TargetType=\"Button\"><Setter Property=\"{owner}.Focusable\" Value=\"False\"/></Style>") == "not-in-tab-order", owner);
    }

    [Theory]
    [InlineData("UIElement")]
    [InlineData("Mouse")]
    [InlineData("Control")]
    [InlineData("FrameworkElement")]
    [InlineData("ContentElement")]
    public void An_event_may_be_written_with_its_owner(string owner) =>
        Assert.Equal("mouse-only", Audit("R4", $"<Grid><Border {owner}.MouseDown=\"H\"/></Grid>"));

    [Theory]
    [InlineData("KeyDown")]
    [InlineData("KeyUp")]
    [InlineData("PreviewKeyDown")]
    [InlineData("PreviewKeyUp")]
    [InlineData("Keyboard.KeyDown")]
    public void A_key_handler_on_a_focusable_element_is_not_judged(string name)
    {
        Assert.Equal("NotJudged", Audit("R4", $"<Grid><Border Focusable=\"True\" MouseDown=\"H\" {name}=\"K\"/></Grid>"));
        Assert.Equal("mouse-only", Audit("R4", $"<Grid><Border MouseDown=\"H\" {name}=\"K\"/></Grid>"));
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
        Assert.Equal("mouse-only", Audit("R4", $"<Grid><Border {name}=\"H\"/></Grid>"));
        Assert.Equal("mouse-only", Audit("R4", "<Grid><Border/></Grid>", $"<Style TargetType=\"Border\"><EventSetter Event=\"{name}\" Handler=\"H\"/></Style>"));
    }

    [Theory]
    [InlineData("PreviewMouseLeftButtonDown")]
    [InlineData("PreviewMouseLeftButtonUp")]
    [InlineData("PreviewMouseDown")]
    [InlineData("PreviewMouseUp")]
    public void Preview_events_are_not_judged(string name) => Assert.Equal("NotJudged", Audit("R4", $"<Grid><Border {name}=\"H\"/></Grid>"));

    [Theory]
    [InlineData("MouseMove")]
    [InlineData("PreviewMouseMove")]
    public void Moving_is_a_gesture(string name) => Assert.Equal("NotJudged", Audit("R4", $"<Grid><Border MouseDown=\"H\" {name}=\"M\"/></Grid>"));

    // ───────── 焦點 ─────────
    [Theory]
    [InlineData("Cursor", "Hand")]
    [InlineData("ToolTip", "x")]
    [InlineData("Tag", "x")]
    [InlineData("IsTabStop", "True")]
    [InlineData("Focusable", "True")]
    [InlineData("Panel.ZIndex", "1")]
    [InlineData("SnapsToDevicePixels", "True")]
    [InlineData("OverridesDefaultStyle", "True")]
    [InlineData("IsHitTestVisible", "True")]
    [InlineData("AutomationProperties.Name", "x")]
    [InlineData("AutomationProperties.HelpText", "x")]
    [InlineData("KeyboardNavigation.IsTabStop", "True")]
    public void Properties_nobody_sees_do_not_show_focus(string property, string value) =>
        Assert.Equal("focus-not-visible", Audit("R3", Template($"<ControlTemplate.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"{property}\" Value=\"{value}\"/></Trigger></ControlTemplate.Triggers>")));

    [Theory]
    [InlineData("Foreground,FontWeight,FontStyle,FontSize,FontFamily,FontStretch,Opacity,OpacityMask,Effect,RenderTransform,LayoutTransform,Visibility,Width,Height,MinWidth,MinHeight,Margin,Template,Content,ContentTemplate,Clip,TextElement.Foreground,TextBlock.Foreground")]
    public void Properties_that_are_always_drawn_show_focus(string properties)
    {
        foreach (var property in properties.Split(','))
            Assert.True(Audit("R3", $"<Button Content=\"x\" Style=\"{{StaticResource s}}\"/>",
                "<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><Border/></ControlTemplate></Setter.Value></Setter>"
                + $"<Style.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"{property}\" Value=\"x\"/></Trigger></Style.Triggers></Style>") == "Pass", property);
    }

    [Theory]
    [InlineData("<ListBox><ListBoxItem Content=\"x\" FocusVisualStyle=\"{x:Null}\"/></ListBox>")]
    [InlineData("<ListView><ListViewItem Content=\"x\" FocusVisualStyle=\"{x:Null}\"/></ListView>")]
    [InlineData("<TreeView><TreeViewItem Header=\"x\" FocusVisualStyle=\"{x:Null}\"/></TreeView>")]
    [InlineData("<TabControl><TabItem Header=\"x\" FocusVisualStyle=\"{x:Null}\"/></TabControl>")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"><ComboBoxItem Content=\"x\" FocusVisualStyle=\"{x:Null}\"/></ComboBox>")]
    public void The_selection_shows_where_focus_is(string xaml) =>
        Assert.Equal("a list item or tab (the selection shows where focus is)", Kit.Audit(xaml).Judgements.Single(j => j.Rule == "R3").Reason);

    [Theory]
    [InlineData("DataGridCell")]
    [InlineData("DataGridRow")]
    [InlineData("DataGridRowHeader")]
    [InlineData("DataGridColumnHeader")]
    public void The_selection_shows_where_focus_is_in_a_grid_too(string type) =>
        Assert.Equal("a list item or tab (the selection shows where focus is)",
            Kit.Audit("<Grid/>", $"<Style TargetType=\"{type}\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/></Style>").Judgements.Single(j => j.Rule == "R3").Reason);

    [Theory]
    [InlineData("Button")]
    [InlineData("ToggleButton")]
    [InlineData("RepeatButton")]
    [InlineData("CheckBox")]
    [InlineData("RadioButton")]
    [InlineData("ComboBox")]
    [InlineData("Slider")]
    public void Controls_whose_template_shows_focus_only_through_the_visual(string type)
    {
        Assert.Equal("focus-not-visible", Audit("R3", $"<{type} AutomationProperties.Name=\"n\" FocusVisualStyle=\"{{x:Null}}\"/>"));
        Assert.Equal("focus-not-visible", Audit("R3", $"<{type} AutomationProperties.Name=\"n\"/>", $"<Style TargetType=\"{type}\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/></Style>"));
    }

    [Theory]
    [InlineData("<TextBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<PasswordBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<RichTextBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<Menu><MenuItem Header=\"x\" FocusVisualStyle=\"{x:Null}\"/></Menu>")]
    [InlineData("<Expander Header=\"x\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<DatePicker AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    public void Controls_that_show_focus_by_themselves(string xaml) => Assert.Equal("Pass", Audit("R3", xaml));

    [Theory]
    [InlineData("<ComboBox IsEditable=\"True\" AutomationProperties.Name=\"n\"/>", "Pass")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"/>", "focus-not-visible")]
    public void A_style_is_judged_by_the_combo_boxes_that_use_it(string xaml, string expected) =>
        Assert.Equal(expected, Audit("R3", xaml, "<Style TargetType=\"ComboBox\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"));

    [Theory]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"Focused\"><Storyboard><ColorAnimation/></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>", "Pass")]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"Focused\"/></VisualStateGroup></VisualStateManager.VisualStateGroups>", "focus-not-visible")]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"Focused\"><Storyboard/></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>", "focus-not-visible")]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"MouseOver\"><Storyboard><ColorAnimation/></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>", "focus-not-visible")]
    public void A_focused_state_counts_only_if_it_does_something(string state, string expected) =>
        Assert.Equal(expected, Audit("R3", $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Grid>{state}<ContentPresenter/></Grid></ControlTemplate></Button.Template></Button>"));

    [Theory]
    [InlineData("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", "Pass")]
    [InlineData("<Trigger Property=\"IsFocused\" Value=\"True\">", "Pass")]
    [InlineData("<Trigger Property=\"IsKeyboardFocusWithin\" Value=\"True\">", "Pass")]
    [InlineData("<Trigger Property=\"IsMouseOver\" Value=\"True\">", "focus-not-visible")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsMouseOver\" Value=\"True\"/></MultiTrigger.Conditions>", "focus-not-visible")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsMouseOver\" Value=\"False\"/></MultiTrigger.Conditions>", "Pass")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsEnabled\" Value=\"True\"/></MultiTrigger.Conditions>", "Pass")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsDefaulted\" Value=\"True\"/></MultiTrigger.Conditions>", "Blocked")]
    public void Focus_triggers(string open, string expected)
    {
        var close = open.StartsWith("<Multi", StringComparison.Ordinal) ? "</MultiTrigger>" : "</Trigger>";
        Assert.Equal(expected, Audit("R3", Template($"<ControlTemplate.Triggers>{open}<Setter TargetName=\"b\" Property=\"Background\" Value=\"Red\"/>{close}</ControlTemplate.Triggers>")));
    }

    [Theory]
    [InlineData("<DataTrigger Binding=\"{Binding IsFocused, RelativeSource={RelativeSource AncestorType=Button}}\" Value=\"True\">", "</DataTrigger>", "Pass")]
    [InlineData("<DataTrigger Binding=\"{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Button}}\" Value=\"True\">", "</DataTrigger>", "focus-not-visible")]
    public void A_nested_template_may_watch_the_focus_of_the_control(string open, string close, string expected) =>
        Assert.Equal(expected, Audit("R3", "<Button Content=\"x\" FocusVisualStyle=\"{x:Null}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Thumb Template=\"{StaticResource t}\"/></ControlTemplate></Button.Template></Button>",
            $"<ControlTemplate x:Key=\"t\" TargetType=\"Thumb\"><Border x:Name=\"g\"/><ControlTemplate.Triggers>{open}<Setter TargetName=\"g\" Property=\"Background\" Value=\"Red\"/>{close}</ControlTemplate.Triggers></ControlTemplate>"));

    [Fact]
    public void A_derived_style_that_puts_the_focus_back_does_not_excuse_the_other()
    {
        const string resources = "<Style x:Key=\"base\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"
            + "<Style x:Key=\"good\" TargetType=\"Button\" BasedOn=\"{StaticResource base}\"><Setter Property=\"FocusVisualStyle\" Value=\"{DynamicResource {x:Static SystemParameters.FocusVisualStyleKey}}\"/></Style>";

        const string bad = "<Style x:Key=\"bad\" TargetType=\"Button\" BasedOn=\"{StaticResource base}\"/>";
        Assert.Equal("NotJudged,Pass", string.Join(",", Audit("R3", "<Button Content=\"x\" Style=\"{StaticResource good}\"/>", resources).Split(',').Order()));
        Assert.Contains("focus-not-visible", Audit("R3", "<Button Content=\"x\" Style=\"{StaticResource bad}\"/>", resources + bad), StringComparison.Ordinal);
        Assert.Contains("focus-not-visible", Audit("R3", "<Button Content=\"x\" Style=\"{StaticResource good}\"/><Button Content=\"x\" Style=\"{StaticResource bad}\"/>", resources + bad), StringComparison.Ordinal);
        // 基底自己也有人用：就要判
        Assert.Contains("focus-not-visible", Audit("R3", "<Button Content=\"x\" Style=\"{StaticResource base}\"/>", resources), StringComparison.Ordinal);
    }

    // ───────── 當內容的資源 ─────────
    [Theory]
    [InlineData("<TextBlock x:Key=\"k\">Save</TextBlock>", "Pass")]
    [InlineData("<Label x:Key=\"k\">Save</Label>", "Pass")]
    [InlineData("<Button x:Key=\"k\">Save</Button>", "Pass")]
    [InlineData("<AccessText x:Key=\"k\">Save</AccessText>", "text-not-in-name")]
    [InlineData("<Geometry x:Key=\"k\">M0,0 L9,9</Geometry>", "meaningless-name")]
    [InlineData("<PathGeometry x:Key=\"k\">M0,0 L9,9</PathGeometry>", "meaningless-name")]
    [InlineData("<StreamGeometry x:Key=\"k\">M0,0 L9,9</StreamGeometry>", "meaningless-name")]
    [InlineData("<BitmapImage x:Key=\"k\">a.png</BitmapImage>", "meaningless-name")]
    [InlineData("<DrawingImage x:Key=\"k\"/>", "meaningless-name")]
    [InlineData("<SolidColorBrush x:Key=\"k\"/>", "meaningless-name")]
    [InlineData("<s:String xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\">Save</s:String>", "Pass")]
    [InlineData("<s:String xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\"></s:String>", "missing-name")]
    [InlineData("<s:Double xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\">5</s:Double>", "Pass")]
    public void Resources_as_content(string resource, string expected) => Assert.Equal(expected, Audit("R1", "<Button Content=\"{StaticResource k}\"/>", resource).Split(',')[^1]);

    [Theory]
    [InlineData("Text")]
    [InlineData("Title")]
    [InlineData("Label")]
    [InlineData("Caption")]
    [InlineData("Header")]
    public void The_text_of_a_custom_control_is_pointed_out(string property)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<local:MyButton {property}=\"Save\"/>")).Add("C.cs", "class MyButton : Button { }");
        Assert.Contains($"its {property} property belongs to the class", Assert.Single(p.Audit().Findings).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<Button><Run Text=\"{x:Null}\"/></Button>")]
    [InlineData("<Button><TextBlock><Run Text=\"{x:Null}\"/></TextBlock></Button>")]
    public void A_null_run_gives_nothing(string xaml) => Assert.DoesNotContain("Pass", Audit("R1", xaml), StringComparison.Ordinal);

    [Fact]
    public void A_file_prefers_its_own_resource()
    {
        static string Dictionary(string name) => $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"k\" TargetType=\"Button\">{name}</Style></ResourceDictionary>";
        var main = $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"k\" TargetType=\"Button\"><Setter Property=\"AutomationProperties.Name\" Value=\"n\"/></Style><DataTemplate x:Key=\"d\"><Button Style=\"{{StaticResource k}}\">{Kit.Icon}</Button></DataTemplate></ResourceDictionary>";
        Assert.Equal("Pass", Kit.Verdicts(Auditor.Run(Project.FromText(main, null, ("Other.xaml", Dictionary("")))), "R1"));
    }

    // ───────── 設定、報告 ─────────
    [Theory]
    [InlineData("-1", false)]
    [InlineData("-0.5", false)]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("100.5", false)]
    [InlineData("\"NaN\"", false)]
    public void The_coverage_setting_is_a_percentage(string value, bool valid) => Assert.Equal(valid, A11yConfig.Parse($"{{\"minCoverage\":{value}}}").Problems.Count == 0);

    [Fact]
    public void The_json_summary_counts_by_severity()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button Content=\"x\" IsTabStop=\"False\"/><Button Content=\"×\"/>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 1));
        var text = json.RootElement.GetProperty("summary").GetRawText();
        Assert.Contains("\"fail\": 2", text, StringComparison.Ordinal);
        Assert.Contains("\"warn\": 1", text, StringComparison.Ordinal);
        Assert.Contains("\"info\": 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_markdown_only_mentions_what_there_is()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button>");
        var md = MarkdownReport.Create(r, r.Findings, 1, 0, null);
        Assert.DoesNotContain("non-WPF", md, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be parsed", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Blocked_reasons_are_counted()
    {
        var r = Kit.Audit("<local:AButton/><local:BButton/><local:CButton/>");
        Assert.Equal(3, Assert.Single(r.Rules.First(s => s.Rule == "R1").BlockedReasons).Value);
    }

    [Theory]
    [InlineData("\"a\"\"b\"\nplay.Content = 1", "Blocked")]        // VB 的引號跳脫
    [InlineData("\"play.Content = 1\"", "missing-name")]
    [InlineData("' play.Content = 1", "missing-name")]
    [InlineData("\"a ' b\"\nplay.Content = 1", "Blocked")]
    public void Visual_Basic_strings_and_comments(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button x:Name=\"play\"/>")).Add("Main.xaml.vb", "Class M\nSub F()\nDim s = " + code + "\nEnd Sub\nEnd Class");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }
}
