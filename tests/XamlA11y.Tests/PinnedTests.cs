using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>
/// 變異測試（2026-11-24：故意改壞 120 處，看哪幾處沒有測試發現）找到沒人守的地方。
/// 多數是清單：少一個型別不會有別的測試變紅，使用者的專案就默默少查一種控制項。
/// </summary>
public class PinnedTests
{
    // ───────── 查哪些控制項 ─────────
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
    [InlineData("FancyButton", "R1")]
    [InlineData("FancyCheckBox", "R1")]
    [InlineData("FancyToggleSwitch", "R1")]
    [InlineData("FancyMenuItem", "R1")]
    [InlineData("FancyTabItem", "R1")]
    [InlineData("FancyExpander", "R1")]
    [InlineData("FancyHyperlink", "R1")]
    [InlineData("FancyTextBox", "R2")]
    [InlineData("FancyPasswordBox", "R2")]
    [InlineData("FancyComboBox", "R2")]
    [InlineData("FancySlider", "R2")]
    [InlineData("FancyDatePicker", "R2")]
    [InlineData("FancyNumericUpDown", "R2")]
    [InlineData("FancyNumberBox", "R2")]
    [InlineData("DecimalUpDown", "R2")]
    public void Unknown_controls_are_recognised_by_their_names(string control, string rule) =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit($"<local:{control}/>"), rule));

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
        Assert.Equal("NotJudged", Kit.Verdicts(Kit.Audit($"<Grid><Border MouseLeftButtonDown=\"H\">{inside}</Border></Grid>"), "R4"));

    [Theory]
    [InlineData("<Border.ContextMenu><ContextMenu><MenuItem Header=\"x\"/></ContextMenu></Border.ContextMenu>")]
    [InlineData("<Border.ToolTip><Button Content=\"x\"/></Border.ToolTip>")]
    [InlineData("<Border.Resources><Button x:Key=\"b\" Content=\"x\"/></Border.Resources>")]
    public void Controls_that_are_not_inside_it_do_not_count(string inside) =>
        Assert.Equal("mouse-only", Kit.Verdicts(Kit.Audit($"<Grid><Border MouseLeftButtonDown=\"H\">{inside}</Border></Grid>"), "R4"));

    // ───────── 屬性的擁有者 ─────────
    [Theory]
    [InlineData("Control")]
    [InlineData("FrameworkElement")]
    [InlineData("UIElement")]
    [InlineData("ContentElement")]
    [InlineData("FrameworkContentElement")]
    [InlineData("ContentControl")]
    [InlineData("ButtonBase")]
    [InlineData("Button")]
    [InlineData("ToggleButton")]
    [InlineData("CheckBox")]
    [InlineData("TextBoxBase")]
    [InlineData("TextBox")]
    [InlineData("ComboBox")]
    [InlineData("MenuItem")]
    [InlineData("TabItem")]
    [InlineData("Window")]
    [InlineData("Popup")]
    [InlineData("Hyperlink")]
    [InlineData("Ellipse")]
    [InlineData("Shape")]
    [InlineData("Border")]
    [InlineData("Image")]
    public void A_setter_may_name_any_base_as_the_owner(string owner) =>
        Assert.Equal("not-in-tab-order", Kit.Verdicts(Kit.Audit("<Button Content=\"x\"/>", $"<Style TargetType=\"Button\"><Setter Property=\"{owner}.Focusable\" Value=\"False\"/></Style>"), "R5"));

    [Theory]
    [InlineData("hc:IconElement.Focusable")]
    [InlineData("local:Own.Focusable")]
    public void A_foreign_owner_is_another_property(string property) =>
        Assert.Equal("", Kit.Verdicts(Kit.Audit("<Button Content=\"x\"/>", $"<Style TargetType=\"Button\"><Setter Property=\"{property}\" Value=\"False\"/></Style>"), "R5"));

    // ───────── 焦點框畫不畫得出來 ─────────
    [Theory]
    [InlineData("<Rectangle Stroke=\"Red\"/>", "Pass")]
    [InlineData("<Ellipse Stroke=\"Red\"/>", "Pass")]
    [InlineData("<Path Stroke=\"Red\" Data=\"M0,0 L9,9\"/>", "Pass")]
    [InlineData("<Line Stroke=\"Red\" X2=\"9\"/>", "Pass")]
    [InlineData("<Polygon Stroke=\"Red\" Points=\"0,0 9,9\"/>", "Pass")]
    [InlineData("<Polyline Fill=\"Red\" Points=\"0,0 9,9\"/>", "Pass")]
    [InlineData("<Polygon/>", "focus-not-visible")]
    [InlineData("<Polyline/>", "focus-not-visible")]
    [InlineData("<Line/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"Transparent\"/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"#00FF0000\"/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"#0F00\"/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"Red\" StrokeThickness=\"0\"/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"Red\" Opacity=\"0\"/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"Red\" Visibility=\"Hidden\"/>", "focus-not-visible")]
    [InlineData("<Rectangle Stroke=\"Red\" Visibility=\"Collapsed\"/>", "focus-not-visible")]
    [InlineData("<Border BorderBrush=\"Red\" BorderThickness=\"1\"/>", "Pass")]
    [InlineData("<Border BorderBrush=\"Red\"/>", "focus-not-visible")]
    [InlineData("<Border BorderThickness=\"1\"/>", "focus-not-visible")]
    [InlineData("<Border Background=\"Red\"/>", "Pass")]
    [InlineData("<Border Background=\"Transparent\"/>", "focus-not-visible")]
    [InlineData("<Grid Background=\"Red\"/>", "Pass")]
    [InlineData("<Grid Background=\"Transparent\"/>", "focus-not-visible")]
    [InlineData("<Grid/>", "focus-not-visible")]
    public void A_focus_visual_counts_only_if_it_draws(string part, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit("<Button Content=\"x\" FocusVisualStyle=\"{StaticResource f}\"/>",
            $"<Style x:Key=\"f\"><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate>{part}</ControlTemplate></Setter.Value></Setter></Style>"), "R3"));

    [Theory]
    [InlineData("{Binding F}", "Blocked")]
    [InlineData("{StaticResource nowhere}", "Blocked")]
    [InlineData("{x:Null}", "focus-not-visible")]
    public void A_focus_visual_that_cannot_be_read_is_unknown(string value, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" FocusVisualStyle=\"{value}\"/>"), "R3"));

    [Fact]
    public void A_focus_visual_based_on_a_style_outside_the_project_is_unknown() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" FocusVisualStyle=\"{StaticResource f}\"/>", "<Style x:Key=\"f\" BasedOn=\"{StaticResource nowhere}\"/>"), "R3"));

    [Theory]
    [InlineData("IsEditable=\"True\"", "Pass", "Pass")]
    [InlineData("", "focus-not-visible", "not-in-tab-order")]
    public void An_editable_combo_box_has_a_text_box_of_its_own(string attribute, string focus, string tab)
    {
        Assert.Equal(focus, Kit.Verdicts(Kit.Audit($"<ComboBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{{x:Null}}\" {attribute}/>"), "R3"));
        Assert.Equal(tab, Kit.Verdicts(Kit.Audit($"<ComboBox AutomationProperties.Name=\"n\" IsTabStop=\"False\" {attribute}/>"), "R5"));
    }

    [Fact]
    public void Being_editable_only_matters_on_a_combo_box() =>
        Assert.Equal("not-in-tab-order", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" IsTabStop=\"False\"/><ComboBox AutomationProperties.Name=\"n\"/>"), "R5"));

    // ───────── 有內建快速鍵的命令 ─────────
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
        Assert.Equal("Pass", Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" IsTabStop=\"False\" Command=\"{command}\"/>"), "R5"));

    [Theory]
    [InlineData("SaveAs")]
    [InlineData("Close")]
    [InlineData("Stop")]
    [InlineData("local:ApplicationCommands.Copy")]
    public void Commands_without_one_are_not(string command) =>
        Assert.Equal("not-in-tab-order", Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" IsTabStop=\"False\" Command=\"{command}\"/>"), "R5"));

    [Theory]
    [InlineData("<Border.InputBindings><MouseBinding MouseAction=\"LeftClick\" Command=\"{Binding A}\"/></Border.InputBindings>")]
    public void A_mouse_binding_elsewhere_is_not_a_keyboard_way(string inside) =>
        Assert.Equal("mouse-only,mouse-only", Kit.Verdicts(Kit.Audit($"<Grid><Border>{inside}</Border><Border>{inside}</Border></Grid>"), "R4"));

    // ───────── 名稱的讀法 ─────────
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
    [InlineData("<StackPanel><TextBlock Text=\"Name\"/><TextBox/></StackPanel>", "Name")]
    [InlineData("<StackPanel><Label Content=\"Name\"/><TextBox/></StackPanel>", "Name")]
    [InlineData("<StackPanel><TextBlock Text=\"Name\"/><StackPanel.ToolTip>t</StackPanel.ToolTip><TextBox/></StackPanel>", "Name")]
    public void The_text_before_an_input_is_suggested(string xaml, string label) =>
        Assert.Contains($"\"{label}\" before it", Kit.Audit(xaml).Findings.Single(f => f.Kind.Slug == "input-no-name").Message, StringComparison.Ordinal);

    [Fact]
    public void Resources_of_one_key_that_differ_are_unknown()
    {
        static string Verdict(string a, string b) => Kit.Verdicts(Auditor.Run(Project.FromText(Kit.Window("<Button Content=\"{StaticResource k}\"/>"), null,
            ("A.xaml", $"<ResourceDictionary {Kit.Ns}>{a}</ResourceDictionary>"), ("B.xaml", $"<ResourceDictionary {Kit.Ns}>{b}</ResourceDictionary>"))), "R1");
        const string text = "<s:String xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\">Save</s:String>", other = "<s:String xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\">儲存</s:String>";
        const string icon = "<Path x:Key=\"k\"/>", block = "<TextBlock x:Key=\"k\" Text=\"Save\"/>";
        Assert.Equal("Pass", Verdict(text, other));
        Assert.Equal("missing-name", Verdict(icon, icon));
        Assert.Equal("Blocked", Verdict(icon, block));
        Assert.Equal("Blocked", Verdict(text, icon));
    }

    [Fact]
    public void Styles_of_one_key_that_set_different_names_count_as_named_or_unknown_never_as_one_of_them()
    {
        static string Style(string value) => $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"k\" TargetType=\"Button\">{value}</Style></ResourceDictionary>";
        static string Verdict(string a, string b) => Kit.Verdicts(Auditor.Run(Project.FromText(Kit.Window($"<Button Style=\"{{StaticResource k}}\">{Kit.Icon}</Button>"), null, ("A.xaml", Style(a)), ("B.xaml", Style(b)))), "R1");
        const string named = "<Setter Property=\"AutomationProperties.Name\" Value=\"Save\"/>";
        Assert.Equal("Pass", Verdict(named, named));
        Assert.Equal("Blocked", Verdict(named, ""));
        Assert.Equal("missing-name", Verdict("", ""));
    }

    // ───────── 讀程式碼 ─────────
    [Theory]
    [InlineData("var c = '\"'; b.Content = 1; var d = '\"';", "Blocked")]                  // 字元常數裡的引號不是字串的開頭
    [InlineData("var c = '\\''; b.Content = 1;", "Blocked")]
    [InlineData("var s = \"b.Content = 1\";", "missing-name")]
    [InlineData("var s = @\"a\\\"; b.Content = 1;", "Blocked")]                             // 逐字字串裡的反斜線不是跳脫
    [InlineData("var s = $@\"a\\\"; b.Content = 1;", "Blocked")]
    [InlineData("var s = \"\"\" \" b.Content = 1 \"\"\";", "missing-name")]
    [InlineData("/* b.Content = 1 */", "missing-name")]
    public void Strings_and_comments_are_not_code(string statement, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button x:Name=\"b\"/>")).Add("Main.xaml.cs", $"class M {{ void F() {{ {statement} }} }}");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    // ───────── baseline、設定、報告 ─────────
    [Theory]
    [InlineData("x:Name=\"a\"", "a")]
    [InlineData("AutomationProperties.AutomationId=\"a\"", "AutomationProperties.AutomationId=a")]
    [InlineData("Command=\"{Binding A}\"", "Command={Binding A}")]
    [InlineData("Click=\"A\"", "Click=A")]
    [InlineData("MouseLeftButtonDown=\"A\"", "MouseLeftButtonDown=A")]
    [InlineData("MouseLeftButtonUp=\"A\"", "MouseLeftButtonUp=A")]
    [InlineData("MouseDown=\"A\"", "MouseDown=A")]
    [InlineData("MouseUp=\"A\"", "MouseUp=A")]
    [InlineData("ToolTip=\"A\"", "ToolTip=A")]
    [InlineData("IsChecked=\"{Binding A}\"", "IsChecked={Binding A}")]
    public void The_baseline_recognises_an_element_by(string attribute, string identity) =>
        Assert.Equal(identity, Kit.Audit($"<ToggleButton {attribute}>{Kit.Icon}</ToggleButton>").Findings.Select(f => f.Identity).Single());

    [Fact]
    public void Equal_elements_are_numbered_in_the_baseline()
    {
        var keys = Kit.Audit($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button>").Findings.Select(f => f.BaselineKey).ToList();
        Assert.Equal(3, keys.Distinct().Count());
        Assert.EndsWith("|#2", keys[1], StringComparison.Ordinal);
        Assert.EndsWith("|#3", keys[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"a:B\":\"Button\"")]
    [InlineData("\"B\":\"a:Button\"")]
    [InlineData("\" \":\"Button\"")]
    [InlineData("\"B\":\"\"")]
    [InlineData("\"B\":\"Nothing\"")]
    public void A_wrong_treatAs_is_an_error(string entry) => Assert.NotEmpty(A11yConfig.Parse($"{{\"treatAs\":{{{entry}}}}}").Problems);

    [Fact]
    public void The_markdown_report_has_a_heading_per_severity()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><Button Content=\"x\" IsTabStop=\"False\"/>");
        var md = MarkdownReport.Create(r, r.Findings, 1, 0, null);
        Assert.Contains("### Fail (1)", md, StringComparison.Ordinal);
        Assert.Contains("### Warn (1)", md, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a", "`a`")]
    [InlineData("a`b", "`` a`b ``")]
    [InlineData("a|b", "`a\\|b`")]
    [InlineData("  ", "`(empty)`")]
    public void Code_spans_are_fenced(string text, string expected) => Assert.Equal(expected, MarkdownReport.Code(text));
}
