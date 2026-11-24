using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>規則的判定（uia-facts.json 蓋不到的：多檔、程式碼、第三方、語料抓到的誤報）。</summary>
public class RuleTests
{
    // ───────── R1／R2 ─────────

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
    [InlineData("<Button Content=\"{Binding Title}\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"{Binding Title}\"/>")]
    [InlineData("<Button><TextBlock Text=\"{Binding Title}\"/></Button>")]
    [InlineData("<Button><Binding Path=\"Title\"/></Button>")]
    public void Data_bound_names_are_not_judged_and_not_in_the_denominator(string xaml)
    {
        var r = Kit.Audit(xaml + "<Button Content=\"ok\"/>");
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.Rules.First(s => s.Rule == "R1").NotJudged);
        Assert.Equal(100, r.Rules.First(s => s.Rule == "R1").Resolution);
    }

    [Fact]
    public void A_resource_name_counts_as_named_even_when_the_key_is_elsewhere() =>
        Assert.Empty(Kit.Audit($"<Button AutomationProperties.Name=\"{{DynamicResource Not.Here}}\">{Kit.Icon}</Button>").Findings);

    [Fact]
    public void Content_from_a_resource_outside_the_project_is_blocked() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button Content=\"{StaticResource Not.Here}\"/>"), "R1"));

    [Fact]
    public void A_resource_dictionary_in_another_file_is_found()
    {
        var dict = $"<ResourceDictionary {Kit.Ns} xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"><sys:String x:Key=\"Save\">Save</sys:String><Image x:Key=\"Icon\"/></ResourceDictionary>";
        Assert.Empty(Kit.Audit("<Button Content=\"{DynamicResource Save}\"/>", more: ("Strings.xaml", dict)).Findings);
        Assert.Equal("missing-name", Assert.Single(Kit.Audit("<Button Content=\"{StaticResource Icon}\"/>", more: ("Strings.xaml", dict)).Findings).Kind.Slug);
    }

    [Fact]
    public void Style_in_another_windows_resources_does_not_apply()
    {
        var other = Kit.Window("", "<Style TargetType=\"Button\"><Setter Property=\"AutomationProperties.Name\" Value=\"Other\"/></Style>");
        Assert.Equal("missing-name", Assert.Single(Kit.Audit($"<Button>{Kit.Icon}</Button>", more: ("Other.xaml", other)).Findings).Kind.Slug);
    }

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
    [InlineData("<TreeView><TreeViewItem Header=\"h\"><TreeViewItem.Template><ControlTemplate TargetType=\"TreeViewItem\"><ToggleButton Focusable=\"False\"/></ControlTemplate></TreeViewItem.Template></TreeViewItem></TreeView>")]
    [InlineData("<TabControl><TabItem Visibility=\"Collapsed\"/></TabControl>")]
    public void Template_parts_and_collapsed_tabs_are_not_judged(string xaml) => Assert.Empty(Kit.Kinds(xaml));

    [Fact]
    public void An_icon_button_inside_another_template_is_judged() =>
        Assert.Equal(["missing-name"], Kit.Kinds($"<TextBox AutomationProperties.Name=\"n\"><TextBox.Template><ControlTemplate TargetType=\"TextBox\"><Button>{Kit.Icon}</Button></ControlTemplate></TextBox.Template></TextBox>"));

    // ───────── 第三方與專案自己的控制項 ─────────

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
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void Code_behind_that_sets_the_content_or_the_name_makes_it_unknown()
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window("<Button x:Name=\"a\"/><Button x:Name=\"b\"/><Button x:Name=\"c\"/>"))
            .Add("Main.xaml.cs", "void M() { a.Content = \"x\"; AutomationProperties.SetName(b, \"y\"); }");
        Assert.Equal("Blocked,Blocked,missing-name", Kit.Verdicts(p.Audit(), "R1"));
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
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R2"));
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
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R2"));
    }

    [Fact]
    public void An_external_style_does_not_look_like_a_hint() =>
        Assert.Equal("input-no-name", Assert.Single(Kit.Audit("<TextBox Style=\"{DynamicResource External}\"/>").Findings).Kind.Slug);

    // ───────── R4 ─────────

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
    public void The_same_action_on_a_keyboard_control_is_a_way_in(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "R4"));

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

    // ───────── R5 ─────────

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

    // ───────── R3 ─────────

    [Fact]
    public void One_style_is_one_finding_at_its_setter()
    {
        var r = Kit.Audit("<Button Content=\"a\" Style=\"{StaticResource s}\"/><Button Content=\"b\" Style=\"{StaticResource s}\"/>",
            "<Style x:Key=\"s\" TargetType=\"Button\">\n<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>");
        var f = Assert.Single(r.Findings);
        Assert.Equal("focus-not-visible", f.Kind.Slug);
        Assert.Equal(2, f.Line);
    }

    [Fact]
    public void A_base_style_whose_derived_styles_draw_focus_is_fine()
    {
        const string derived = "<Style x:Key=\"b\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>" +
                               "<Style x:Key=\"d\" TargetType=\"Button\" BasedOn=\"{StaticResource b}\"><Style.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"BorderBrush\" Value=\"Red\"/></Trigger></Style.Triggers></Style>";
        Assert.Empty(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource d}\"/>", derived).Findings);
        Assert.Single(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource b}\"/><Button Content=\"y\" Style=\"{StaticResource d}\"/>", derived).Findings);
    }

    [Theory]
    [InlineData("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"Cursor\" Value=\"Hand\"/></Trigger>", "focus-not-visible")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsMouseOver\" Value=\"True\"/></MultiTrigger.Conditions><Setter Property=\"Background\" Value=\"Red\"/></MultiTrigger>", "focus-not-visible")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsDefault\" Value=\"True\"/></MultiTrigger.Conditions><Setter Property=\"Background\" Value=\"Red\"/></MultiTrigger>", "Blocked")]
    [InlineData("<Trigger Property=\"IsFocused\" Value=\"True\"><Setter Property=\"Background\" Value=\"Red\"/></Trigger>", "Pass")]
    [InlineData("<EventTrigger RoutedEvent=\"GotFocus\"><BeginStoryboard><Storyboard><DoubleAnimation/></Storyboard></BeginStoryboard></EventTrigger>", "Pass")]
    public void Focus_triggers(string trigger, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource s}\"/>",
            $"<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/><Style.Triggers>{trigger}</Style.Triggers></Style>"), "R3"));

    [Fact]
    public void A_visible_focus_style_drawn_through_another_style_is_not_removed() =>
        Assert.Equal("Pass", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource s}\"/>",
            "<Style x:Key=\"r\" TargetType=\"Rectangle\"><Setter Property=\"Stroke\" Value=\"Red\"/></Style>" +
            "<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\"><Setter.Value><Style><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle Style=\"{StaticResource r}\"/></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter></Style>"), "R3"));

    [Fact]
    public void Under_an_unmeasured_theme_the_default_template_is_unknown_and_the_config_can_vouch_for_it()
    {
        const string app = "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/Some.Theme;component/T.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>";
        (string, string)[] more = [("App.xaml", app)];
        const string button = "<Button Content=\"x\" FocusVisualStyle=\"{x:Null}\"/>";
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit(button, more: more), "R3"));
        Assert.Equal("Pass", Kit.Verdicts(Kit.Audit(button, options: new AuditOptions { ThemeShowsFocus = true }, more: more), "R3"));
        // 自己的樣式沒有 BasedOn：佈景的範本整個被取代
        Assert.Equal("focus-not-visible", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>", more: more), "R3"));
    }

    // ───────── R6 ─────────

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

    // ───────── ignore ─────────

    [Theory]
    [InlineData("<!-- xamla11y-ignore: decorative -->", 1)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- xamlfit-ignore: fixed -->", 1)]
    [InlineData("<!-- xamlfit-ignore: fixed; xamla11y-ignore: decorative -->", 1)]
    [InlineData("<!-- xamla11y-ignore -->", 0)]
    [InlineData("<!-- xamla11y-ignore: xamlfit-ignore: only the other tool gave a reason -->", 0)]
    [InlineData("<!-- xamlfit-ignore: fixed -->", 0)]
    public void Ignore_comments_need_a_reason_and_stack_with_the_other_tools(string comment, int suppressed)
    {
        var r = Kit.Audit($"{comment}<Button>{Kit.Icon}</Button>");
        Assert.Equal(suppressed, r.Suppressed);
        Assert.Equal(1 - suppressed, r.Findings.Count);
    }

    [Theory] // 被註解掉的元素也是隔了元素：ignore 原本管的是它，不能因為它被註解掉就改壓下一個（四件套一致）
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- <Button Content='x'/> -->", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!--\n  <Button Content='x'/>\n--><!-- xamlfit-ignore: fixed -->", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- note: the <Button/> below is an icon -->", 1)] // 在談元素，不是註解掉的元素
    [InlineData("<!-- <Button Content='x'/> --><!-- xamla11y-ignore: decorative -->", 1)]
    // 文字也是內容，停；處理指令不是，跳過
    [InlineData("<!-- xamla11y-ignore: decorative -->text", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- xamlfit-ignore: fixed -->text<!-- note -->", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><![CDATA[text]]>", 0)]
    [InlineData("text<!-- xamla11y-ignore: decorative -->", 1)]
    [InlineData("<!-- xamla11y-ignore: decorative --><?note x?>", 1)]
    public void Ignore_comments_do_not_reach_past_a_commented_out_element_or_text(string comment, int suppressed)
    {
        var r = Kit.Audit($"{comment}<Button>{Kit.Icon}</Button>");
        Assert.Equal(suppressed, r.Suppressed);
        Assert.Equal(1 - suppressed, r.Findings.Count);
        // 停下要講出來；不然使用者只看到問題重新報出來，看不出自己的註解為什麼沒生效
        if (suppressed > 0) Assert.Empty(r.InvalidIgnores);
        else Assert.Contains("commented-out element or text", Assert.Single(r.InvalidIgnores), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<!-- xamla11y-ignore -->", "has no reason")]
    [InlineData("<!-- xamla11y-ignore --><!-- xamlfit-ignore: f -->", "has no reason")]
    [InlineData("<!-- xamla11y-ignore: far --><!-- xamla11y-ignore -->", "has no reason")] // 最近的說了算
    [InlineData("<!-- xamla11y-ignore: r --><!-- <Button/> -->", "commented-out element")]
    public void An_ignore_comment_that_does_nothing_says_why(string comment, string why)
    {
        var r = Kit.Audit($"{comment}<Button>{Kit.Icon}</Button>");
        Assert.Equal(0, r.Suppressed);
        Assert.Contains(why, Assert.Single(r.InvalidIgnores), StringComparison.Ordinal);
    }

    [Theory] // 沒有標記停在那裡，就沒有東西要講
    [InlineData("<!-- <Button/> -->")]
    [InlineData("<!-- xamlfit-ignore: f --><!-- <Button/> -->")]
    [InlineData("<!-- xamlfit-ignore: f -->text")]
    [InlineData("<!-- xamla11y-ignore: r --><Border/><!-- <Button/> -->")]
    public void A_commented_out_element_alone_is_not_warned_about(string comment)
        => Assert.Empty(Kit.Audit($"<StackPanel>{comment}<Button>{Kit.Icon}</Button></StackPanel>").InvalidIgnores);

    [Fact]
    public void Severities_can_be_changed_or_turned_off()
    {
        var off = Kit.Audit("<TextBox/>", options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Off } });
        Assert.Empty(off.Findings);
        Assert.Equal(1, off.NotJudged);
        Assert.Equal(Severity.Info, Assert.Single(Kit.Audit("<TextBox/>", options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Info } }).Findings).Severity);
    }
}
