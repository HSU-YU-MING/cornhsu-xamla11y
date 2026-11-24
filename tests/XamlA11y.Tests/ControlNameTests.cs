using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>控制項的名稱：什麼內容給得出名稱、什麼給不出、訊息怎麼說。WPF 的答案在 uia-facts.json。</summary>
public sealed class ControlNameTests
{
    /// <summary>直接放在 ItemsControl 裡：UIA 的名稱是 ToString()，不是空的（UiaOracle「direct」）。清單、選單、工具列不是。</summary>
    [Theory]
    [InlineData("<ItemsControl><Button><Image/></Button></ItemsControl>", "meaningless-name")]
    [InlineData("<ItemsControl><ItemsControl.Items><Button><Image/></Button></ItemsControl.Items></ItemsControl>", "meaningless-name")]
    [InlineData("<HeaderedItemsControl><Button ToolTip=\"x\"><Image/></Button></HeaderedItemsControl>", "meaningless-name")]
    [InlineData("<ItemsControl><CheckBox/></ItemsControl>", "meaningless-name")]
    [InlineData("<ItemsControl><Button><StackPanel><TextBlock Text=\"Save\"/></StackPanel></Button></ItemsControl>", "text-not-in-name")]
    [InlineData("<ItemsControl><Button Content=\"Save\"/></ItemsControl>", "Pass")]
    [InlineData("<ItemsControl><StackPanel><Button><Image/></Button></StackPanel></ItemsControl>", "missing-name")]
    [InlineData("<StatusBar><Button><Image/></Button></StatusBar>", "missing-name")]
    [InlineData("<ToolBar><Button><Image/></Button></ToolBar>", "missing-name")]
    [InlineData("<ListBox><Button><Image/></Button></ListBox>", "missing-name")]
    public void Directly_in_an_ItemsControl(string xaml, string expected) => Assert.Equal(expected, Kit.Verdict("control-names", xaml));

    [Fact]
    public void Inputs_and_lists_directly_in_an_ItemsControl()
    {
        Assert.Equal("meaningless-name,Pass", Kit.Verdict("input-names", "<ItemsControl><TextBox/><Slider AutomationProperties.Name=\"Volume\"/></ItemsControl>"));
        Assert.Equal("list-no-name", Kit.Verdict("list-names", "<ItemsControl><ListBox/></ItemsControl>"));
        var r = Kit.Audit("<ItemsControl><Button><StackPanel><TextBlock Text=\"Save\"/></StackPanel></Button><TextBox/></ItemsControl>");
        Assert.All(r.Findings, f => Assert.False(f.NameIsEmpty));
        Assert.Contains(r.Findings, f => f.Message == "<TextBox> has an unreadable accessible name: it is an item of an ItemsControl, so its name is the type name");
        Assert.All(r.Findings, f => Assert.Null(XamlA11y.Core.Report.Nvda.Says(f, XamlA11y.Core.Report.Lang.En)));
    }

    [Fact]
    public void Tooltip_is_not_a_name_and_the_message_says_so()
    {
        var f = Assert.Single(Kit.Audit($"<Button ToolTip=\"Save\">{Kit.Icon}</Button>").Findings);
        Assert.Equal("missing-name", f.Kind.Slug);
        Assert.Contains("ToolTip is the help text", f.Message);
    }

    [Fact]
    public void A_custom_controls_own_text_property_is_pointed_out()
    {
        var f = Assert.Single(Kit.Audit("<local:FancyButton Text=\"Save\"/>", treatAs: new Dictionary<string, string> { ["FancyButton"] = "Button" }).Findings);
        Assert.Contains("its Text property belongs to the class", f.Message);
    }

    [Theory]
    [InlineData("<Button Content=\"{Binding Title}\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"{Binding Title}\"/>")]
    [InlineData("<Button><TextBlock Text=\"{Binding Title}\"/></Button>")]
    [InlineData("<Button><Binding Path=\"Title\"/></Button>")]
    public void Data_bound_names_are_not_judged_and_not_in_the_denominator(string xaml)
    {
        var r = Kit.Audit(xaml + "<Button Content=\"ok\"/>");
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.Checks.First(s => s.Check == "control-names").NotJudged);
        Assert.Equal(100, r.Checks.First(s => s.Check == "control-names").Resolution);
    }

    [Fact]
    public void A_resource_name_counts_as_named_even_when_the_key_is_elsewhere() =>
        Assert.Empty(Kit.Audit($"<Button AutomationProperties.Name=\"{{DynamicResource Not.Here}}\">{Kit.Icon}</Button>").Findings);

    [Fact]
    public void Content_from_a_resource_outside_the_project_is_blocked() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button Content=\"{StaticResource Not.Here}\"/>"), "control-names"));

    [Theory]
    [InlineData("<Button><TextBlock Text=\"&#xE74D;\" FontFamily=\"Segoe MDL2 Assets\"/></Button>", "meaningless-name")]
    [InlineData("<Button FontFamily=\"Marlett\" Content=\"r\"/>", "meaningless-name")]
    [InlineData("<Button Content=\"×\"/>", "symbol-name")]
    [InlineData("<Button><StackPanel><TextBlock Text=\"Save\"/></StackPanel></Button>", "text-not-in-name")]
    [InlineData("<TabControl><TabItem><TabItem.Header><Image/></TabItem.Header></TabItem></TabControl>", "meaningless-name")]
    [InlineData("<CheckBox/>", "missing-name")]
    [InlineData("<Slider/>", "input-no-name")]
    [InlineData("<DatePicker/>", "input-no-name")]
    public void Kinds_of_bad_names(string xaml, string kind) => Assert.Equal([kind], Kit.Kinds(xaml));

    [Theory]
    [InlineData("<Button Focusable=\"False\">", "</Button>")]
    [InlineData("<Button IsEnabled=\"False\">", "</Button>")]
    public void Unfocusable_controls_are_reported_one_level_lower(string open, string close)
    {
        var f = Kit.Audit(open + Kit.Icon + close).Findings.First(x => x.Kind.Slug == "missing-name");
        Assert.Equal(Severity.Warn, f.Severity);
    }

    [Theory]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"><ComboBox.Template><ControlTemplate TargetType=\"ComboBox\"><ToggleButton/></ControlTemplate></ComboBox.Template></ComboBox>")]
    [InlineData("<TreeView AutomationProperties.Name=\"n\"><TreeViewItem Header=\"h\"><TreeViewItem.Template><ControlTemplate TargetType=\"TreeViewItem\"><ToggleButton Focusable=\"False\"/></ControlTemplate></TreeViewItem.Template></TreeViewItem></TreeView>")]
    [InlineData("<TabControl><TabItem Visibility=\"Collapsed\"/></TabControl>")]
    public void Template_parts_and_collapsed_tabs_are_not_judged(string xaml) => Assert.Empty(Kit.Kinds(xaml));

    [Fact]
    public void An_icon_button_inside_another_template_is_judged() =>
        Assert.Equal(["missing-name"], Kit.Kinds($"<TextBox AutomationProperties.Name=\"n\"><TextBox.Template><ControlTemplate TargetType=\"TextBox\"><Button>{Kit.Icon}</Button></ControlTemplate></TextBox.Template></TextBox>"));

    [Fact]
    public void Content_that_only_a_trigger_sets_is_unknown() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button><Button.Style><Style TargetType=\"Button\"><Style.Triggers><DataTrigger Binding=\"{Binding P}\" Value=\"True\"><Setter Property=\"Content\" Value=\"Pause\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button>"), "control-names"));

    [Theory]
    [InlineData("{x:Static local:ApplicationCommands.SaveCommand}", "Blocked")]
    [InlineData("{x:Static ApplicationCommands.Save}", "Pass")]
    [InlineData("ApplicationCommands.Save", "Pass")]
    public void A_project_class_called_ApplicationCommands_is_not_the_WPF_one(string command, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button Command=\"{command}\">{Kit.Icon}</Button>"), "control-names"));

    [Theory]
    [InlineData("Segoe UI, Segoe MDL2 Assets", "")]
    [InlineData("Segoe MDL2 Assets, Segoe UI", "meaningless-name")]
    public void Letters_are_drawn_by_the_first_family(string family, string kinds) =>
        Assert.Equal(kinds, string.Join(",", Kit.Kinds($"<Button Content=\"Save\" FontFamily=\"{family}\"/>")));

    [Fact]
    public void Attributes_on_the_root_of_a_class_are_its_local_values()
    {
        using var p = new Folder()
            .Add("IconButton.xaml", $"<Button {Kit.Ns} x:Class=\"App.IconButton\" AutomationProperties.Name=\"Delete\" IsTabStop=\"False\"/>")
            .Add("IconButton.xaml.cs", "namespace App { public partial class IconButton { } }")
            .Add("Main.xaml", Kit.Window($"<local:IconButton>{Kit.Icon}</local:IconButton><local:IconButton IsTabStop=\"True\">{Kit.Icon}</local:IconButton>"));
        var r = p.Audit();
        Assert.Equal("Pass,Pass", Kit.Verdicts(r, "control-names"));
        Assert.Equal("not-in-tab-order,Pass", Kit.Verdicts(r, "tab-order"));
    }

    [Fact]
    public void The_name_of_a_user_control_is_not_the_text_of_the_button_around_it()
    {
        using var p = Kit.Files("<Button><local:Icon/></Button>", "")
            .Add("Icon.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Icon\" AutomationProperties.Name=\"Save\">{Kit.Icon}</UserControl>");
        Assert.Equal("missing-name", Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("class MyCombo : ComboBox {}", "<ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:MyCombo}\"><ToggleButton/></ControlTemplate>", "NotJudged")]
    [InlineData("class MySlider : Slider {}", "<ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:MySlider}\"><RepeatButton/></ControlTemplate>", "NotJudged")]
    [InlineData("class MyCard : ContentControl {}", "<ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:MyCard}\"><ToggleButton/></ControlTemplate>", "missing-name")]
    public void Template_parts_of_derived_controls_are_the_same(string code, string resources, string expected)
    {
        using var p = Kit.Files("", "", resources).Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("pack://application:,,,/Fonts/#FontAwesome", "meaningless-name")]
    [InlineData("/Fonts/#FontAwesome", "meaningless-name")]
    [InlineData("pack://application:,,,/Fonts/#Inter", "Pass")]
    public void Icon_fonts_are_recognised_in_pack_URIs(string family, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button FontFamily=\"{family}\" Content=\"a\"/>"), "control-names"));

    [Theory]
    [InlineData("<TextBlock Text=\"Save\"/>")]
    [InlineData("<TextBlock>Save</TextBlock>")]
    [InlineData("<TextBlock><Run Text=\"Save\"/></TextBlock>")]
    [InlineData("<TextBlock><Run>Sa</Run><Run>ve</Run></TextBlock>")]
    [InlineData("<Label Content=\"Save\"/>")]
    [InlineData("<AccessText Text=\"Save\"/>")]
    public void Text_inside_a_panel_is_shown_in_the_message(string inside)
    {
        var f = Assert.Single(Kit.Audit($"<Button><StackPanel>{inside}</StackPanel></Button>").Findings);
        Assert.Equal("text-not-in-name", f.Kind.Slug);
        Assert.Contains("\"Save\"", f.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_without_text_does_not_talk_about_text() =>
        Assert.Contains("its content is <Grid>, which gives no text", Assert.Single(Kit.Audit($"<Button><Grid>{Kit.Icon}</Grid></Button>").Findings).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("Grid,StackPanel,DockPanel,WrapPanel,Canvas,UniformGrid,VirtualizingStackPanel,TabPanel,ToolBarPanel,Border,Viewbox,Decorator,BulletDecorator,AdornerDecorator,Image,Path,Rectangle,Ellipse,Line,Polygon,Polyline,Popup,InkPresenter,TickBar,Glyphs,Viewport3D")]
    [InlineData("Calendar,ProgressBar,ItemsControl,TabControl,Menu,ScrollBar,Thumb,GridSplitter,Separator,StatusBar,InkCanvas,MediaElement,WebBrowser,ListBox,TreeView,DataGrid")]
    public void Content_that_gives_no_text_leaves_the_button_without_a_name(string types)
    {
        foreach (var type in types.Split(','))
            Assert.True(Kit.Verdict("control-names", $"<Button><{type}/></Button>").StartsWith("missing-name", StringComparison.Ordinal), type);
    }

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
    public void Resources_as_content(string resource, string expected) => Assert.Equal(expected, Kit.Verdict("control-names", "<Button Content=\"{StaticResource k}\"/>", resource).Split(',')[^1]);

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
    public void A_null_run_gives_nothing(string xaml) => Assert.DoesNotContain("Pass", Kit.Verdict("control-names", xaml), StringComparison.Ordinal);

    [Theory]
    [InlineData("Segoe MDL2 Assets,Segoe Fluent Icons,Marlett,Webdings,Wingdings 2,FontAwesome,Material Design Icons,Material Icons,Material Symbols Outlined,iconfont,IcoFont,IcoMoon,Ionicons,/Fonts/#iconfont")]
    public void Icon_fonts(string families)
    {
        foreach (var family in families.Split(','))
            Assert.True(Kit.Verdict("control-names", $"<Button FontFamily=\"{family}\" Content=\"a\"/>") == "meaningless-name", family);
        Assert.Equal("Pass", Kit.Verdict("control-names", "<Button FontFamily=\"Segoe UI, Segoe MDL2 Assets\" Content=\"a\"/>"));
        Assert.Equal("Pass", Kit.Verdict("control-names", "<Button FontFamily=\"\" Content=\"a\"/>"));
    }

    [Theory]
    [InlineData("FooIcon,FooGlyph,FontAwesome,SvgViewbox,SymbolIcon,CachedImage,ImageEx", "missing-name")]
    [InlineData("FooChart,Spinner", "Blocked")]
    public void Unknown_content_that_looks_like_an_icon(string types, string expected)
    {
        foreach (var type in types.Split(','))
            Assert.True(Kit.Verdict("control-names", $"<Button><local:{type}/></Button>").Split(',')[0] == expected, type);
    }

    [Theory]
    [InlineData("Copy,Cut,Paste,Undo,Redo,Delete,Close,New,Open,Save,SaveAs,Print,PrintPreview,Find,Replace,SelectAll,Help,Properties,Stop")]
    [InlineData("ApplicationCommands.Copy,EditingCommands.ToggleBold,NavigationCommands.BrowseBack,MediaCommands.Play,ComponentCommands.MoveDown")]
    public void Commands_that_give_a_name(string commands)
    {
        foreach (var command in commands.Split(','))
            Assert.True(Kit.Verdict("control-names", $"<Button Command=\"{command}\">{Kit.Icon}</Button>") == "Pass", command);
    }

    [Theory]
    [InlineData("xmlns:p=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"", "p:ApplicationCommands.Copy", "Pass")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "p:ApplicationCommands.Copy", "Blocked")]
    [InlineData("xmlns:p=\"clr-namespace:Microsoft.Windows.Shell;assembly=Microsoft.Windows.Shell\"", "p:SystemCommands.CloseWindowCommand", "missing-name")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "p:SystemCommands.CloseWindowCommand", "Blocked")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "q:SystemCommands.CloseWindowCommand", "Blocked")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "SystemCommands.CloseWindowCommand", "missing-name")]
    public void Commands_are_told_apart_by_their_namespace(string ns, string command, string expected) =>
        Assert.Equal(expected, Kit.VerdictNs(ns, "control-names", $"<Button Command=\"{{x:Static {command}}}\">{Kit.Icon}</Button>"));

    [Theory]
    [InlineData("<Menu><MenuItem Header=\"{x:Null}\" Command=\"ApplicationCommands.Copy\"/></Menu>", "Pass")]
    [InlineData("<Menu><MenuItem Header=\"{x:Null}\"/></Menu>", "missing-name")]
    [InlineData("<Button Content=\"{x:Null}\"/>", "missing-name")]
    [InlineData("<Button Content=\"{foo:Icon X}\"/>", "Blocked")]
    [InlineData("<Button Content=\"{md:PackIcon X}\"/>", "missing-name")]
    [InlineData("<Button Content=\"{loc:Loc Save}\"/>", "Pass")]
    [InlineData("<Button><StaticResource ResourceKey=\"nowhere\"/></Button>", "Blocked")]
    [InlineData("<Button><StaticResource ResourceKey=\"k\"/></Button>", "Pass")]
    [InlineData("<Button Content=\"{StaticResource ResourceKey=k}\"/>", "Pass")]
    [InlineData("<Button Content=\"{StaticResource nowhere}\"/>", "Blocked")]
    [InlineData("<Button Style=\"{StaticResource nowhere}\"/>", "Blocked")]
    [InlineData("<Button Style=\"{StaticResource empty}\"/>", "missing-name")]
    [InlineData("<Button Style=\"{StaticResource outside}\"/>", "missing-name")]
    [InlineData("<Button Content=\" \"/>", "meaningless-name")]
    [InlineData("<Button Content=\"\"/>", "missing-name")]
    [InlineData("<Button><TextBlock Text=\"{TemplateBinding Tag}\"/></Button>", "NotJudged")]
    [InlineData("<Button><TextBlock><InlineUIContainer><TextBlock Text=\"Save\"/></InlineUIContainer></TextBlock></Button>", "text-not-in-name")]
    [InlineData("<Button><TextBlock><InlineUIContainer><Image Source=\"a.png\"/></InlineUIContainer></TextBlock></Button>", "meaningless-name")]
    [InlineData("<TabItem><TabItem.Header><StackPanel><TextBlock Text=\"Home\"/></StackPanel></TabItem.Header></TabItem>", "text-not-in-name")]
    [InlineData("<TabItem><TabItem.Header><StackPanel><Image Source=\"a.png\"/></StackPanel></TabItem.Header></TabItem>", "meaningless-name")]
    [InlineData("<TextBlock><Hyperlink><TextBlock Text=\"Download\"/></Hyperlink></TextBlock>", "text-not-in-name")]
    [InlineData("<Button><TextBlock><Run><Run.Text>Save</Run.Text></Run></TextBlock></Button>", "Pass")]
    [InlineData("<Button><TextBlock><Bold>Save</Bold></TextBlock></Button>", "Pass")]
    [InlineData("<Button><TextBlock><Span><Italic><Underline>Save</Underline></Italic></Span></TextBlock></Button>", "Pass")]
    public void Content(string xaml, string expected) =>
        Assert.Equal(expected, Kit.Verdict("control-names", xaml, "<s:String xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\">Save</s:String><Style x:Key=\"empty\" TargetType=\"Button\"/><Style x:Key=\"outside\" TargetType=\"Button\" BasedOn=\"{StaticResource far}\"/>"));

    [Fact]
    public void A_trigger_in_a_base_style_may_set_the_name() =>
        Assert.Equal("Blocked", Kit.Verdict("control-names", $"<Button Style=\"{{StaticResource d}}\">{Kit.Icon}</Button>",
            "<Style x:Key=\"b\" TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsEnabled\" Value=\"True\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Trigger></Style.Triggers></Style><Style x:Key=\"d\" TargetType=\"Button\" BasedOn=\"{StaticResource b}\"/>"));

    [Theory]
    [InlineData("<TreeView><TreeView.ItemContainerStyle><Style TargetType=\"TreeViewItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></TreeView.ItemContainerStyle><TreeViewItem/></TreeView>", "Pass")]
    [InlineData("<TreeView><TreeViewItem Header=\"a\"><TreeViewItem.ItemContainerStyle><Style TargetType=\"TreeViewItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></TreeViewItem.ItemContainerStyle><TreeViewItem/></TreeViewItem></TreeView>", "Pass,Pass")]
    [InlineData("<ContextMenu x:Key=\"c\"><ContextMenu.ItemContainerStyle><Style TargetType=\"MenuItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ContextMenu.ItemContainerStyle><MenuItem/></ContextMenu>", "Pass")]
    [InlineData("<Menu><MenuItem Header=\"a\"><MenuItem.ItemContainerStyle><Style TargetType=\"MenuItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></MenuItem.ItemContainerStyle><MenuItem/></MenuItem></Menu>", "Pass,Pass")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"><ComboBox.ItemContainerStyle><Style TargetType=\"ComboBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ComboBox.ItemContainerStyle><ComboBoxItem/></ComboBox>", "Pass")]
    [InlineData("<ListView><ListView.ItemContainerStyle><Style TargetType=\"ListViewItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ListView.ItemContainerStyle><ListViewItem/></ListView>", "Pass")]
    [InlineData("<ListBox ItemContainerStyle=\"{Binding S}\"><ListBoxItem>x</ListBoxItem></ListBox>", "Pass")]
    public void Item_container_styles(string xaml, string expected) => Assert.Equal(expected, Kit.Verdict("control-names", xaml));

    [Fact]
    public void The_target_of_a_template_may_come_from_its_style() =>
        Assert.Equal("NotJudged", Kit.Verdict("control-names", "", "<Style x:Key=\"s\" TargetType=\"ComboBox\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate><ToggleButton/></ControlTemplate></Setter.Value></Setter></Style>"));

    [Theory]
    [InlineData("<Button Focusable=\"False\">[ICON]</Button>", "; a warning because it cannot take focus")]
    [InlineData("<Button IsEnabled=\"False\">[ICON]</Button>", "; a warning because it is disabled")]
    [InlineData("<Button Visibility=\"Hidden\">[ICON]</Button>", "; a warning because it is hidden")]
    [InlineData("<TextBox Visibility=\"Collapsed\"/>", "; a warning because it is hidden")]
    public void The_message_says_why_it_is_only_a_warning(string xaml, string note)
    {
        var f = Assert.Single(Kit.Audit(xaml.Replace("[ICON]", Kit.Icon, StringComparison.Ordinal)).Findings, x => x.Kind.Check is "control-names" or "input-names");
        Assert.Equal(Severity.Warn, f.Severity);
        Assert.EndsWith(note, f.Message, StringComparison.Ordinal);
        Assert.True(Zh.TryMessage(f.Message, out _));
    }
}
