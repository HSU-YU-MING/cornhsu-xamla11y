using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>看不到鍵盤焦點：樣式、範本、觸發器、佈景。</summary>
public sealed class FocusTests
{
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
            $"<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/><Style.Triggers>{trigger}</Style.Triggers></Style>"), "focus"));

    [Fact]
    public void A_visible_focus_style_drawn_through_another_style_is_not_removed() =>
        Assert.Equal("Pass", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource s}\"/>",
            "<Style x:Key=\"r\" TargetType=\"Rectangle\"><Setter Property=\"Stroke\" Value=\"Red\"/></Style>" +
            "<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\"><Setter.Value><Style><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle Style=\"{StaticResource r}\"/></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter></Style>"), "focus"));

    [Fact]
    public void Under_an_unmeasured_theme_the_default_template_is_unknown_and_the_config_can_vouch_for_it()
    {
        const string app = "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/Some.Theme;component/T.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>";
        (string, string)[] more = [("App.xaml", app)];
        const string button = "<Button Content=\"x\" FocusVisualStyle=\"{x:Null}\"/>";
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit(button, more: more), "focus"));
        Assert.Equal("Pass", Kit.Verdicts(Kit.Audit(button, options: new AuditOptions { ThemeShowsFocus = true }, more: more), "focus"));
        // 自己的樣式沒有 BasedOn：佈景的範本整個被取代
        Assert.Equal("focus-not-visible", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>", more: more), "focus"));
    }

    [Theory]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource S}\" Template=\"{StaticResource T}\"/>", "<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>", "Pass")]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource S}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border/></ControlTemplate></Button.Template></Button>", "<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/><Setter Property=\"Template\" Value=\"{StaticResource T}\"/>", "focus-not-visible")]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource S}\" FocusVisualStyle=\"{StaticResource V}\"/><Button Content=\"y\" Style=\"{StaticResource S}\" Focusable=\"False\"/>", "<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>", "NotJudged,Pass")]
    public void Focus_is_judged_where_the_style_is_used(string body, string setters, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit(body, Kit.FocusTemplate +
            "<Style x:Key=\"V\"><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle Stroke=\"Black\"/></ControlTemplate></Setter.Value></Setter></Style>" +
            $"<Style x:Key=\"S\" TargetType=\"Button\">{setters}</Style>"), "focus"));

    [Fact]
    public void A_style_without_a_target_type_can_remove_focus() =>
        Assert.Equal("focus-not-visible", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource B}\"/>", "<Style x:Key=\"B\"><Setter Property=\"Control.FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"), "focus"));

    [Fact]
    public void The_xaml_namespace_may_have_another_prefix() =>
        Assert.Equal("focus-not-visible", Kit.Verdicts(Kit.AuditNs("xmlns:xaml=\"http://schemas.microsoft.com/winfx/2006/xaml\"", "<Button Content=\"x\"/>",
            "<Style TargetType=\"{xaml:Type Button}\"><Setter Property=\"FocusVisualStyle\" Value=\"{xaml:Null}\"/></Style>"), "focus"));

    [Fact]
    public void A_replaced_menu_item_template_may_not_show_focus() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Menu><MenuItem Header=\"x\"/></Menu>",
            "<Style TargetType=\"MenuItem\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"MenuItem\"><Border/></ControlTemplate></Setter.Value></Setter></Style>"), "focus"));

    [Theory]
    [InlineData("Transparent", "focus-not-visible")]
    [InlineData("#00FFFFFF", "focus-not-visible")]
    [InlineData("#0FFF", "focus-not-visible")]
    [InlineData("#80FFFFFF", "Pass")]
    [InlineData("#000", "Pass")]
    public void A_transparent_focus_rectangle_is_no_focus_rectangle(string stroke, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource S}\"/>",
            $"<Style x:Key=\"S\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\"><Setter.Value><Style><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle Stroke=\"{stroke}\"/></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter></Style>"), "focus"));

    [Theory]
    [InlineData("<Border/>", "Blocked")]
    [InlineData("<Border BorderBrush=\"{TemplateBinding BorderBrush}\"/>", "Pass")]
    public void A_style_trigger_counts_only_if_the_template_draws_what_it_sets(string template, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource S}\"/>",
            $"<Style x:Key=\"S\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\">{template}</ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"BorderBrush\" Value=\"Red\"/></Trigger></Style.Triggers></Style>"), "focus"));

    [Theory]
    [InlineData("", "Pass")]
    [InlineData("<Style x:Key=\"{x:Static SystemParameters.FocusVisualStyleKey}\"/>", "Blocked")]
    public void The_system_focus_visual_is_not_a_removed_one(string resources, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Auditor.Run(XamlA11y.Core.Analysis.Project.FromText(Kit.Window("<Button Content=\"x\" FocusVisualStyle=\"{DynamicResource {x:Static SystemParameters.FocusVisualStyleKey}}\"/>"), null,
            ("App.xaml", $"<Application {Kit.Ns}><Application.Resources>{resources}</Application.Resources></Application>"))), "focus"));

    [Fact]
    public void A_focus_visual_drawn_by_a_foreign_attached_property_is_unknown_not_removed()
    {
        // EarTrumpet：<Rectangle Theme:Brush.Stroke="Text" StrokeThickness="2"/>，原本報 8 筆「移除了焦點框」
        const string ns = "xmlns:t=\"clr-namespace:App.Theme\"";
        static string Style(string stroke) => "<Style x:Key=\"f\"><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle " + stroke + " StrokeThickness=\"2\"/></ControlTemplate></Setter.Value></Setter></Style>";
        string Verdict(string stroke) => Kit.Verdicts(Auditor.Run(XamlA11y.Core.Analysis.Project.FromText(
            $"<Window {Kit.Ns} {ns}><Window.Resources>{Style(stroke)}</Window.Resources><StackPanel><Button Content=\"x\" FocusVisualStyle=\"{{StaticResource f}}\"/></StackPanel></Window>")), "focus");
        Assert.Equal("Blocked", Verdict("t:Brush.Stroke=\"Text\""));
        Assert.Equal("focus-not-visible", Verdict(""));
        Assert.Equal("Pass", Verdict("Stroke=\"Red\""));
    }

    [Theory]
    [InlineData("<Rectangle Stroke=\"Red\" Visibility=\"{TemplateBinding IsKeyboardFocused, Converter={StaticResource v}}\"/>", "Pass")]
    [InlineData("<Rectangle Stroke=\"Red\"><Rectangle.Visibility><Binding Path=\"IsKeyboardFocused\" RelativeSource=\"{RelativeSource TemplatedParent}\"/></Rectangle.Visibility></Rectangle>", "Pass")]
    [InlineData("<Rectangle Stroke=\"Red\" Visibility=\"{TemplateBinding IsMouseOver, Converter={StaticResource v}}\"/>", "focus-not-visible")]
    public void Bindings_to_the_focus_state_show_focus(string part, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Grid><ContentPresenter/>{part}</Grid></ControlTemplate></Button.Template></Button>"), "focus"));

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
            $"<Style x:Key=\"f\"><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate>{part}</ControlTemplate></Setter.Value></Setter></Style>"), "focus"));

    [Theory]
    [InlineData("{Binding F}", "Blocked")]
    [InlineData("{StaticResource nowhere}", "Blocked")]
    [InlineData("{x:Null}", "focus-not-visible")]
    public void A_focus_visual_that_cannot_be_read_is_unknown(string value, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" FocusVisualStyle=\"{value}\"/>"), "focus"));

    [Fact]
    public void A_focus_visual_based_on_a_style_outside_the_project_is_unknown() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" FocusVisualStyle=\"{StaticResource f}\"/>", "<Style x:Key=\"f\" BasedOn=\"{StaticResource nowhere}\"/>"), "focus"));

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
        Assert.Equal("focus-not-visible", Kit.Verdict("focus", Kit.FocusButton($"<ControlTemplate.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"{property}\" Value=\"{value}\"/></Trigger></ControlTemplate.Triggers>")));

    [Theory]
    [InlineData("Foreground,FontWeight,FontStyle,FontSize,FontFamily,FontStretch,Opacity,OpacityMask,Effect,RenderTransform,LayoutTransform,Visibility,Width,Height,MinWidth,MinHeight,Margin,Template,Content,ContentTemplate,Clip,TextElement.Foreground,TextBlock.Foreground")]
    public void Properties_that_are_always_drawn_show_focus(string properties)
    {
        foreach (var property in properties.Split(','))
            Assert.True(Kit.Verdict("focus", $"<Button Content=\"x\" Style=\"{{StaticResource s}}\"/>",
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
        Assert.Equal("a list item or tab (the selection shows where focus is)", Kit.Audit(xaml).Judgements.Single(j => j.Check == "focus").Reason);

    [Theory]
    [InlineData("DataGridCell")]
    [InlineData("DataGridRow")]
    [InlineData("DataGridRowHeader")]
    [InlineData("DataGridColumnHeader")]
    public void The_selection_shows_where_focus_is_in_a_grid_too(string type) =>
        Assert.Equal("a list item or tab (the selection shows where focus is)",
            Kit.Audit("<Grid/>", $"<Style TargetType=\"{type}\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/></Style>").Judgements.Single(j => j.Check == "focus").Reason);

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
        Assert.Equal("focus-not-visible", Kit.Verdict("focus", $"<{type} AutomationProperties.Name=\"n\" FocusVisualStyle=\"{{x:Null}}\"/>"));
        Assert.Equal("focus-not-visible", Kit.Verdict("focus", $"<{type} AutomationProperties.Name=\"n\"/>", $"<Style TargetType=\"{type}\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/></Style>"));
    }

    [Theory]
    [InlineData("<TextBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<PasswordBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<RichTextBox AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<Menu><MenuItem Header=\"x\" FocusVisualStyle=\"{x:Null}\"/></Menu>")]
    [InlineData("<Expander Header=\"x\" FocusVisualStyle=\"{x:Null}\"/>")]
    [InlineData("<DatePicker AutomationProperties.Name=\"n\" FocusVisualStyle=\"{x:Null}\"/>")]
    public void Controls_that_show_focus_by_themselves(string xaml) => Assert.Equal("Pass", Kit.Verdict("focus", xaml));

    [Theory]
    [InlineData("<ComboBox IsEditable=\"True\" AutomationProperties.Name=\"n\"/>", "Pass")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"/>", "focus-not-visible")]
    public void A_style_is_judged_by_the_combo_boxes_that_use_it(string xaml, string expected) =>
        Assert.Equal(expected, Kit.Verdict("focus", xaml, "<Style TargetType=\"ComboBox\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"));

    [Theory]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"Focused\"><Storyboard><ColorAnimation/></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>", "Pass")]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"Focused\"/></VisualStateGroup></VisualStateManager.VisualStateGroups>", "focus-not-visible")]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"Focused\"><Storyboard/></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>", "focus-not-visible")]
    [InlineData("<VisualStateManager.VisualStateGroups><VisualStateGroup><VisualState x:Name=\"MouseOver\"><Storyboard><ColorAnimation/></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>", "focus-not-visible")]
    public void A_focused_state_counts_only_if_it_does_something(string state, string expected) =>
        Assert.Equal(expected, Kit.Verdict("focus", $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Grid>{state}<ContentPresenter/></Grid></ControlTemplate></Button.Template></Button>"));

    [Theory]
    [InlineData("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", "Pass")]
    [InlineData("<Trigger Property=\"IsFocused\" Value=\"True\">", "Pass")]
    [InlineData("<Trigger Property=\"IsKeyboardFocusWithin\" Value=\"True\">", "Pass")]
    [InlineData("<Trigger Property=\"IsMouseOver\" Value=\"True\">", "focus-not-visible")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsMouseOver\" Value=\"True\"/></MultiTrigger.Conditions>", "focus-not-visible")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsMouseOver\" Value=\"False\"/></MultiTrigger.Conditions>", "Pass")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsEnabled\" Value=\"True\"/></MultiTrigger.Conditions>", "Pass")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsDefaulted\" Value=\"True\"/></MultiTrigger.Conditions>", "Blocked")]
    public void Every_focus_trigger(string open, string expected)
    {
        var close = open.StartsWith("<Multi", StringComparison.Ordinal) ? "</MultiTrigger>" : "</Trigger>";
        Assert.Equal(expected, Kit.Verdict("focus", Kit.FocusButton($"<ControlTemplate.Triggers>{open}<Setter TargetName=\"b\" Property=\"Background\" Value=\"Red\"/>{close}</ControlTemplate.Triggers>")));
    }

    [Theory]
    [InlineData("<DataTrigger Binding=\"{Binding IsFocused, RelativeSource={RelativeSource AncestorType=Button}}\" Value=\"True\">", "</DataTrigger>", "Pass")]
    [InlineData("<DataTrigger Binding=\"{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Button}}\" Value=\"True\">", "</DataTrigger>", "focus-not-visible")]
    public void A_nested_template_may_watch_the_focus_of_the_control(string open, string close, string expected) =>
        Assert.Equal(expected, Kit.Verdict("focus", "<Button Content=\"x\" FocusVisualStyle=\"{x:Null}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Thumb Template=\"{StaticResource t}\"/></ControlTemplate></Button.Template></Button>",
            $"<ControlTemplate x:Key=\"t\" TargetType=\"Thumb\"><Border x:Name=\"g\"/><ControlTemplate.Triggers>{open}<Setter TargetName=\"g\" Property=\"Background\" Value=\"Red\"/>{close}</ControlTemplate.Triggers></ControlTemplate>"));

    [Fact]
    public void A_derived_style_that_puts_the_focus_back_does_not_excuse_the_other()
    {
        const string resources = "<Style x:Key=\"base\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"
            + "<Style x:Key=\"good\" TargetType=\"Button\" BasedOn=\"{StaticResource base}\"><Setter Property=\"FocusVisualStyle\" Value=\"{DynamicResource {x:Static SystemParameters.FocusVisualStyleKey}}\"/></Style>";

        const string bad = "<Style x:Key=\"bad\" TargetType=\"Button\" BasedOn=\"{StaticResource base}\"/>";
        Assert.Equal("NotJudged,Pass", string.Join(",", Kit.Verdict("focus", "<Button Content=\"x\" Style=\"{StaticResource good}\"/>", resources).Split(',').Order()));
        Assert.Contains("focus-not-visible", Kit.Verdict("focus", "<Button Content=\"x\" Style=\"{StaticResource bad}\"/>", resources + bad), StringComparison.Ordinal);
        Assert.Contains("focus-not-visible", Kit.Verdict("focus", "<Button Content=\"x\" Style=\"{StaticResource good}\"/><Button Content=\"x\" Style=\"{StaticResource bad}\"/>", resources + bad), StringComparison.Ordinal);
        // 基底自己也有人用：就要判
        Assert.Contains("focus-not-visible", Kit.Verdict("focus", "<Button Content=\"x\" Style=\"{StaticResource base}\"/>", resources), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Focusable=\"False\"", "NotJudged")]
    [InlineData("IsTabStop=\"False\"", "NotJudged")]
    [InlineData("Focusable=\"True\"", "focus-not-visible")]
    public void What_cannot_take_focus_needs_no_visual(string attribute, string expected)
    {
        Assert.Equal(expected, Kit.Verdict("focus", $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\" {attribute}/>"));
        Assert.Equal(expected, Kit.Verdict("focus", $"<Button Content=\"x\" {attribute}/>", "<Style TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"));
    }

    [Theory]
    [InlineData("local:FancyButton")]
    [InlineData("local:FancyTextBox")]
    public void A_style_for_an_unknown_control_lists_it(string type)
    {
        var r = Kit.Audit($"<{type} Style=\"{{StaticResource s}}\"/>", $"<Style x:Key=\"s\" TargetType=\"{type}\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/></Style>");
        Assert.Equal("Blocked", Kit.Verdicts(r, "focus"));
        Assert.Contains(r.UnknownControls, u => u.Element == type[6..] && u.Count == 2);
    }

    [Theory]
    [InlineData("<Setter Property=\"Control.Template\" Value=\"{x:Null}\"/>")]
    [InlineData("<Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate/></Setter.Value></Setter>")]
    [InlineData("")]
    public void An_empty_focus_visual_is_none(string setter) =>
        Assert.Equal("focus-not-visible", Kit.Verdict("focus", "<Button Content=\"x\" FocusVisualStyle=\"{StaticResource f}\"/>", $"<Style x:Key=\"f\">{setter}</Style>"));

    [Theory]
    [InlineData("<Setter Property=\"BorderBrush\" Value=\"Red\"/>", "{TemplateBinding BorderBrush}", "Pass")]
    [InlineData("<Setter Property=\"Border.BorderBrush\" Value=\"Red\"/>", "{TemplateBinding BorderBrush}", "Pass")]
    [InlineData("<Setter Property=\"Background\" Value=\"Red\"/>", "{TemplateBinding BorderBrush}", "Blocked")]
    [InlineData("<Setter Property=\"FocusVisualStyle\" Value=\"{StaticResource other}\"/>", "Red", "Blocked")]
    public void A_style_trigger_needs_a_template_that_draws_it(string setter, string brush, string expected) =>
        Assert.Equal(expected, Kit.Verdict("focus", "<Button Content=\"x\" Style=\"{StaticResource s}\"/>",
            $"<Style x:Key=\"other\"/><Style x:Key=\"s\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><Border BorderBrush=\"{brush}\"/></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property=\"IsFocused\" Value=\"True\">{setter}</Trigger></Style.Triggers></Style>"));

    [Theory]
    [InlineData("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Trigger.EnterActions><BeginStoryboard Storyboard=\"{StaticResource s}\"/></Trigger.EnterActions></Trigger>", "Pass")]
    [InlineData("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Trigger.EnterActions><BeginStoryboard/></Trigger.EnterActions></Trigger>", "focus-not-visible")]
    [InlineData("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Trigger.EnterActions><BeginStoryboard><Storyboard><ColorAnimation/></Storyboard></BeginStoryboard></Trigger.EnterActions></Trigger>", "Pass")]
    [InlineData("<MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsFocused\" Value=\"True\"/><Condition Property=\"IsPressed\" Value=\"True\"/></MultiTrigger.Conditions><Setter Property=\"Opacity\" Value=\"0.5\"/></MultiTrigger>", "focus-not-visible")]
    [InlineData("<DataTrigger Value=\"True\"><DataTrigger.Binding><Binding Path=\"IsKeyboardFocused\" RelativeSource=\"{RelativeSource Self}\"/></DataTrigger.Binding><Setter Property=\"Tag\" Value=\"x\"/></DataTrigger>", "focus-not-visible")]
    [InlineData("<DataTrigger Value=\"True\"><DataTrigger.Binding><Binding Path=\"IsKeyboardFocused\" RelativeSource=\"{RelativeSource Self}\"/></DataTrigger.Binding><Setter Property=\"Opacity\" Value=\"0.5\"/></DataTrigger>", "Pass")]
    [InlineData("<MultiDataTrigger><MultiDataTrigger.Conditions><Condition Binding=\"{Binding IsFocused, RelativeSource={RelativeSource Self}}\" Value=\"True\"/></MultiDataTrigger.Conditions><Setter Property=\"Opacity\" Value=\"0.5\"/></MultiDataTrigger>", "Pass")]
    public void What_a_focus_trigger_has_to_do(string trigger, string expected) =>
        Assert.Equal(expected, Kit.Verdict("focus", $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border/><ControlTemplate.Triggers>{trigger}</ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>"));

    [Theory]
    [InlineData("MaterialDesign3.Defaults.xaml", "MaterialDesignThemes 5.3.0", "Pass")]
    [InlineData("MaterialDesign2.Defaults.xaml", "MaterialDesignThemes 5.3.0", "Blocked")]
    [InlineData("MaterialDesign3.Defaults.xaml", "MaterialDesignThemes 4.9.0", "Blocked")]
    public void A_theme_shows_focus_in_the_version_that_was_measured(string dictionary, string package, string expected)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/{dictionary}\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("Main.xaml", Kit.Window("<Button Content=\"x\" FocusVisualStyle=\"{x:Null}\"/>")).Add("App.csproj", Kit.Csproj(package));
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "focus"));
    }

    [Fact]
    public void Themes_are_reported_once_with_their_scope()
    {
        const string merged = "<ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml\"/><ResourceDictionary Source=\"pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml\"/><md:BundledTheme/><local:MyTheme/></ResourceDictionary.MergedDictionaries>";
        using var p = new Folder().Add("One/App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary>{merged}</ResourceDictionary></Application.Resources></Application>")
            .Add("One/Main.xaml", Kit.Window("<Button Content=\"x\"/>")).Add("Two/App.xaml", $"<Application {Kit.Ns}/>").Add("Two/Main.xaml", Kit.Window("<Button Content=\"x\"/>"));
        var themes = p.Audit().Themes;
        Assert.Equal(["MahApps.Metro", "materialdesign"], themes.Select(t => t.Name).Where(n => n != "MyTheme").Order(StringComparer.Ordinal));
        Assert.All(themes, t => Assert.Equal("One/", t.Scope));
        Assert.Contains(themes, t => t.LibraryId == "mahapps");
        Assert.Contains(themes, t => t.LibraryId == "materialdesign");
    }
}
