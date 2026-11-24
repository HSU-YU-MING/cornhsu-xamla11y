using System.Diagnostics;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>
/// 第二輪獨立審查（2026-11-24，50 個公開專案）實跑重現的問題。名稱規則的在 UiaFactsTests（round3-names.txt，真的 WPF 錄的）；
/// 這裡是程式碼、鍵盤、焦點、檔案、報告。
/// </summary>
public class SecondReviewTests
{
    private static Folder Project(string body, string code = "", string resources = "", string ext = ".cs") =>
        new Folder().Add("Main.xaml", Kit.Window(body, resources)).Add("Main.xaml" + ext, code);

    // ───────── code-behind 設內容與名稱的各種寫法 ─────────
    [Theory]
    [InlineData("b.SetResourceReference(ContentControl.ContentProperty, \"Str\");")]
    [InlineData("b.SetBinding(Button.ContentProperty, z);")]
    [InlineData("b.Content ??= \"x\";")]
    [InlineData("BindingOperations.SetBinding(b, ContentControl.ContentProperty, z);")]
    [InlineData("b.SetValue(System.Windows.Controls.Button.ContentProperty, \"x\");")]
    [InlineData("b.SetBinding(AutomationProperties.NameProperty, \"X\");")]
    [InlineData("b.SetResourceReference(AutomationProperties.NameProperty, \"X\");")]
    [InlineData("BindingOperations.SetBinding(b, AutomationProperties.NameProperty, z);")]
    [InlineData("AutomationProperties.SetName(element: b, \"x\");")]
    [InlineData("AutomationProperties.SetName(this.b, \"x\");")]
    public void Code_that_sets_the_content_or_the_name_makes_it_unknown(string statement)
    {
        using var p = Project("<Button x:Name=\"b\"/>", $"class M {{ void F() {{ {statement} }} }}");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Theory]
    [InlineData("var s = b.Content;")]
    [InlineData("if (b.Content == x) { }")]
    [InlineData("b.SetBinding(Button.ToolTipProperty, z);")]
    [InlineData("other.SetBinding(Button.ContentProperty, z);")]
    [InlineData("list.Where(x => b.Content => 1);")]
    public void Code_that_does_not_set_them_changes_nothing(string statement)
    {
        using var p = Project("<Button x:Name=\"b\"/>", $"class M {{ void F() {{ {statement} }} }}");
        Assert.Equal("missing-name", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Theory]
    [InlineData("<Button><TextBlock><Run x:Name=\"v\"/></TextBlock></Button>", "R1")]
    [InlineData("<TextBlock><Hyperlink><Run x:Name=\"v\"/></Hyperlink></TextBlock>", "R1")]
    [InlineData("<TextBlock x:Name=\"l\"><Run x:Name=\"v\"/></TextBlock><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"/>", "R2")]
    public void A_run_filled_by_code_is_unknown(string body, string rule)
    {
        using var p = Project(body, "class M { void F() { v.Text = \"1.0\"; } }");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), rule));
    }

    [Theory]
    [InlineData("play.Content = \"x\"", "Blocked")]
    [InlineData("AutomationProperties.SetName(Me.play, \"x\")", "Blocked")]
    [InlineData("Dim s = other.Content", "missing-name")]
    public void Visual_Basic_is_case_insensitive(string statement, string expected)
    {
        using var p = Project("<Button x:Name=\"Play\"/>", $"Class M\nSub F()\n{statement}\nEnd Sub\nEnd Class", ext: ".vb");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void Visual_Basic_window_drag_is_seen()
    {
        using var p = Project("<Grid><Border MouseLeftButtonDown=\"Bar_Down\"/></Grid>", "Class M\nPrivate Sub Bar_Down(sender As Object, e As MouseButtonEventArgs)\n  Me.DragMove()\nEnd Sub\nEnd Class", ext: ".vb");
        Assert.Equal("NotJudged", Kit.Verdicts(p.Audit(), "R4"));
    }

    [Fact]
    public void Visual_Basic_AddHandler_is_a_key_handler()
    {
        using var p = Project("<Grid><Border x:Name=\"Card\" MouseLeftButtonDown=\"Go\"/></Grid>", "Class M\nSub New()\nAddHandler card.KeyDown, AddressOf K\nEnd Sub\nEnd Class", ext: ".vb");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "R4"));
    }

    // ───────── 類別自己設內容 ─────────
    [Theory]
    [InlineData("class CloseButton : Button { public CloseButton() { Content = \"Close\"; } }", "Blocked")]
    [InlineData("class CloseButton : Button { public CloseButton() { this.Content = Make(a, b); } }", "Blocked")]
    [InlineData("class CloseButton : Button { public CloseButton() { SetBinding(ContentProperty, new Binding()); } }", "Blocked")]
    [InlineData("class Base : Button { public Base() { Content = 1; } } class CloseButton : Base { }", "Blocked")]
    [InlineData("class CloseButton : Button { void F() { tip = new ToolTip { Content = text, Placement = p }; } }", "missing-name")]
    [InlineData("class CloseButton : Button { void F() { tip = new ToolTip { Content = text }; } }", "missing-name")]
    [InlineData("class CloseButton : Button { void F() { other.Content = 1; } }", "missing-name")]
    [InlineData("class CloseButton : Button { public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); } }", "missing-name")]
    [InlineData("class CloseButton : Button { void F() { if (Content == null) { } } }", "missing-name")]
    public void A_class_that_fills_its_own_content_is_unknown(string code, string expected)
    {
        using var p = Project("<local:CloseButton/>", "").Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void A_text_block_class_that_fills_its_own_text_is_unknown()
    {
        using var p = Project("<Button><local:LocText/></Button>", "").Add("C.cs", "class LocText : TextBlock { public LocText() { Text = \"x\"; } }");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void The_name_of_a_user_control_is_not_the_text_of_the_button_around_it()
    {
        using var p = Project("<Button><local:Icon/></Button>", "")
            .Add("Icon.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Icon\" AutomationProperties.Name=\"Save\">{Kit.Icon}</UserControl>");
        Assert.Equal("missing-name", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void Classes_are_found_through_the_projects_own_xml_namespace()
    {
        const string xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:c=\"http://myapp/controls\"><Button><c:Cap/></Button></Window>";
        var cap = $"<UserControl {Kit.Ns} x:Class=\"App.Cap\"><TextBlock Text=\"Save\"/></UserControl>";
        using var with = new Folder().Add("Main.xaml", xaml).Add("Cap.xaml", cap).Add("A.cs", "[assembly: XmlnsDefinition(\"http://myapp/controls\", \"App\")]");
        Assert.Equal("Pass", Kit.Verdicts(with.Audit(), "R1"));
        using var without = new Folder().Add("Main.xaml", xaml).Add("Cap.xaml", cap);
        Assert.Equal("Blocked", Kit.Verdicts(without.Audit(), "R1"));
    }

    // ───────── 專案自己的命令 ─────────
    [Theory]
    [InlineData("static class Cmds { public static RoutedUICommand Go { get; } = new RoutedUICommand(\"Go\", \"Go\", typeof(Cmds)); }", "Pass")]
    [InlineData("static class Cmds { public static readonly RoutedUICommand Go = new(\"Go\", \"Go\", typeof(Cmds)); }", "Pass")]
    [InlineData("static class Cmds { public static RoutedUICommand Go { get; } = new RoutedUICommand(); }", "Blocked")]
    [InlineData("static class Cmds { public static RoutedCommand Go { get; } = new(nameof(Go), typeof(Cmds)); }", "missing-name")]
    [InlineData("static class Cmds { public static readonly RoutedCommand Go = new RoutedUICommand(\"Go\", \"Go\", typeof(Cmds)); }", "Blocked")]
    [InlineData("static class Cmds { public static ICommand Go { get; } = Make(); }", "Blocked")]
    [InlineData("static class Cmds { public static RelayCommand Go { get; } = new(() => { }); }", "missing-name")]
    [InlineData("static class Other { public static RoutedCommand Go = new(); }", "Blocked")]
    [InlineData("static class Cmds { public static RoutedCommand Go = new(); } namespace B { static class Cmds { public static ICommand Go = X; } }", "Blocked")]
    [InlineData("// static class Cmds { public static RoutedCommand Go = new(); }", "Blocked")]
    public void A_command_of_the_project_is_looked_up_in_its_code(string code, string expected)
    {
        using var p = Project($"<Button Command=\"{{x:Static local:Cmds.Go}}\">{Kit.Icon}</Button>", "").Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    [Theory]
    [InlineData("", "Pass")]
    [InlineData("<Style x:Key=\"{x:Static SystemParameters.FocusVisualStyleKey}\"/>", "Blocked")]
    public void The_system_focus_visual_is_not_a_removed_one(string resources, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Auditor.Run(XamlA11y.Core.Analysis.Project.FromText(Kit.Window("<Button Content=\"x\" FocusVisualStyle=\"{DynamicResource {x:Static SystemParameters.FocusVisualStyleKey}}\"/>"), null,
            ("App.xaml", $"<Application {Kit.Ns}><Application.Resources>{resources}</Application.Resources></Application>"))), "R3"));

    // ───────── 範本、字典、命名空間 ─────────
    [Theory]
    [InlineData("class MyCombo : ComboBox {}", "<ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:MyCombo}\"><ToggleButton/></ControlTemplate>", "NotJudged")]
    [InlineData("class MySlider : Slider {}", "<ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:MySlider}\"><RepeatButton/></ControlTemplate>", "NotJudged")]
    [InlineData("class MyCard : ContentControl {}", "<ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:MyCard}\"><ToggleButton/></ControlTemplate>", "missing-name")]
    public void Template_parts_of_derived_controls_are_the_same(string code, string resources, string expected)
    {
        using var p = Project("", "", resources).Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void A_dictionary_merged_only_by_Generic_xaml_is_visible()
    {
        using var p = new Folder()
            .Add("App.xaml", $"<Application {Kit.Ns}/>")
            .Add("Themes/Generic.xaml", $"<ResourceDictionary {Kit.Ns}><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"/App;component/Themes/CloseButton.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary>")
            .Add("Themes/CloseButton.xaml", $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"local:CloseButton\"><Setter Property=\"AutomationProperties.Name\" Value=\"Close\"/></Style></ResourceDictionary>")
            .Add("Main.xaml", Kit.Window("<local:CloseButton/>")).Add("C.cs", "class CloseButton : Button { }");
        Assert.Equal("Pass", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Theory]
    [InlineData("<ResourceDictionary Source=\"D.xaml\"/>")]
    [InlineData("<ResourceDictionary><ResourceDictionary.Source>D.xaml</ResourceDictionary.Source></ResourceDictionary>")]
    public void An_application_subclass_in_App_xaml_is_the_application(string merged)
    {
        using var p = new Folder()
            .Add("App.xaml", $"<local:BootApp {Kit.Ns} x:Class=\"App.App\"><local:BootApp.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries></ResourceDictionary></local:BootApp.Resources></local:BootApp>")
            .Add("D.xaml", $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"TextBox\"><Setter Property=\"AutomationProperties.Name\" Value=\"Search\"/></Style></ResourceDictionary>")
            .Add("Main.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal("Pass", Kit.Verdicts(p.Audit(), "R2"));
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
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<RibbonButton Label=\"Save\"/>"), "R1"));

    [Theory]
    [InlineData("pack://application:,,,/Fonts/#FontAwesome", "meaningless-name")]
    [InlineData("/Fonts/#FontAwesome", "meaningless-name")]
    [InlineData("pack://application:,,,/Fonts/#Inter", "Pass")]
    public void Icon_fonts_are_recognised_in_pack_URIs(string family, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button FontFamily=\"{family}\" Content=\"a\"/>"), "R1"));

    // ───────── R3 ─────────
    [Fact]
    public void A_focus_visual_drawn_by_a_foreign_attached_property_is_unknown_not_removed()
    {
        // EarTrumpet：<Rectangle Theme:Brush.Stroke="Text" StrokeThickness="2"/>，原本報 8 筆「移除了焦點框」
        const string ns = "xmlns:t=\"clr-namespace:App.Theme\"";
        static string Style(string stroke) => "<Style x:Key=\"f\"><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle " + stroke + " StrokeThickness=\"2\"/></ControlTemplate></Setter.Value></Setter></Style>";
        string Verdict(string stroke) => Kit.Verdicts(Auditor.Run(XamlA11y.Core.Analysis.Project.FromText(
            $"<Window {Kit.Ns} {ns}><Window.Resources>{Style(stroke)}</Window.Resources><StackPanel><Button Content=\"x\" FocusVisualStyle=\"{{StaticResource f}}\"/></StackPanel></Window>")), "R3");
        Assert.Equal("Blocked", Verdict("t:Brush.Stroke=\"Text\""));
        Assert.Equal("focus-not-visible", Verdict(""));
        Assert.Equal("Pass", Verdict("Stroke=\"Red\""));
    }

    [Theory]
    [InlineData("<Rectangle Stroke=\"Red\" Visibility=\"{TemplateBinding IsKeyboardFocused, Converter={StaticResource v}}\"/>", "Pass")]
    [InlineData("<Rectangle Stroke=\"Red\"><Rectangle.Visibility><Binding Path=\"IsKeyboardFocused\" RelativeSource=\"{RelativeSource TemplatedParent}\"/></Rectangle.Visibility></Rectangle>", "Pass")]
    [InlineData("<Rectangle Stroke=\"Red\" Visibility=\"{TemplateBinding IsMouseOver, Converter={StaticResource v}}\"/>", "focus-not-visible")]
    public void Bindings_to_the_focus_state_show_focus(string part, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Grid><ContentPresenter/>{part}</Grid></ControlTemplate></Button.Template></Button>"), "R3"));

    // ───────── R4 ─────────
    [Theory]
    [InlineData("<hc:Shield2 MouseLeftButtonDown=\"H\"/>", "Blocked")]
    [InlineData("<local:SvgViewbox MouseLeftButtonUp=\"H\"/>", "Blocked")]
    [InlineData("<local:FancyButton MouseLeftButtonUp=\"H\"/>", "")]             // R1 已經數了
    [InlineData("<StatusBar><StatusBarItem MouseLeftButtonUp=\"H\"/></StatusBar>", "mouse-only")]
    [InlineData("<MediaElement MouseLeftButtonUp=\"H\"/>", "mouse-only")]
    [InlineData("<TextBlock><Underline MouseLeftButtonUp=\"H\">x</Underline></TextBlock>", "mouse-only")]
    public void Clicks_on_elements_this_tool_does_not_know_are_counted(string body, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit(body), "R4"));

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
        using var p = Project("<Grid><Border MouseLeftButtonDown=\"Down\"/></Grid>", $"class M {{ {method} }}");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R4"));
    }

    [Theory]
    [InlineData("<Button Content=\"x\" Command=\"{Binding Open}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"x\" Command=\"{Binding Other}\"/>", "mouse-only")]
    public void Behaviors_that_invoke_a_command_count_as_the_same_action(string other, string expected)
    {
        const string ns = "xmlns:i=\"http://schemas.microsoft.com/xaml/behaviors\"";
        var r = Auditor.Run(XamlA11y.Core.Analysis.Project.FromText($"<Window {Kit.Ns} {ns}><StackPanel>{other}<Border><i:Interaction.Triggers><i:EventTrigger EventName=\"MouseLeftButtonUp\"><i:InvokeCommandAction Command=\"{{Binding Open}}\"/></i:EventTrigger></i:Interaction.Triggers></Border></StackPanel></Window>"));
        Assert.Equal(expected, Kit.Verdicts(r, "R4"));
    }

    // ───────── R5 ─────────
    [Theory]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"{Binding Open}\"/>", "", "not-in-tab-order")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"{Binding Open}\"/><Grid><Grid.InputBindings><KeyBinding Key=\"F2\" Command=\"{Binding Open}\"/></Grid.InputBindings></Grid>", "", "Pass")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"ApplicationCommands.Copy\"/>", "", "Pass")]
    [InlineData("<Button Content=\"x\" Focusable=\"False\" Command=\"ApplicationCommands.Close\"/>", "", "not-in-tab-order")]
    [InlineData("<Button IsTabStop=\"False\"><AccessText Text=\"_Open\"/></Button>", "", "Pass")]
    [InlineData("<Button IsTabStop=\"False\"><AccessText Text=\"Open\"/></Button>", "", "not-in-tab-order")]
    [InlineData("<Label Content=\"_Name\" Target=\"{Binding ElementName=n}\"/><TextBox x:Name=\"n\" IsTabStop=\"False\"/>", "", "Pass")]
    [InlineData("<Label Content=\"Name\" Target=\"{Binding ElementName=n}\"/><TextBox x:Name=\"n\" IsTabStop=\"False\"/>", "", "not-in-tab-order")]
    [InlineData("<Button Content=\"x\" IsTabStop=\"False\" Click=\"Go\"/><Button Content=\"y\" Click=\"Go\"/>", "", "Pass")]
    [InlineData("", "<DataTemplate x:Key=\"d\"><Button Content=\"x\" IsTabStop=\"False\"/></DataTemplate>", "NotJudged")]
    [InlineData("<ToolBar><Button Content=\"x\"/></ToolBar>", "<Style TargetType=\"Button\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Style>", "")]
    public void Other_ways_in_from_the_keyboard_count(string body, string resources, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit(body, resources), "R5"));

    // ───────── 跑不完 ─────────
    [Fact]
    public void Class_declarations_that_base_each_other_do_not_explode()
    {
        var sb = new System.Text.StringBuilder();
        for (var layer = 0; layer < 7; layer++)
            for (var c = 0; c < 20; c++)
                for (var b = 0; b < 20; b++) sb.Append($"class L{layer}x{c} : L{layer + 1}x{b} {{}}\n");
        using var p = Project("<local:L0x0/>", "").Add("C.cs", sb.ToString());
        var clock = Stopwatch.StartNew();
        p.Audit();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"{clock.Elapsed}");
    }

    [Theory]
    [InlineData("<Border MouseLeftButtonDown=\"H{0}\"/>")]
    [InlineData("<TextBox x:Name=\"n{0}\"/>")]
    [InlineData("<Button/>")]
    public void Many_siblings_are_not_quadratic(string each)
    {
        var xaml = string.Concat(Enumerable.Range(0, 20_000).Select(i => each.Replace("{0}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));
        var clock = Stopwatch.StartNew();
        Kit.Audit(xaml);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"{clock.Elapsed}");
    }

    // ───────── 檔案 ─────────
    [Theory]
    [InlineData("windows-1252")]
    [InlineData("gb2312")]
    public void Legacy_encodings_that_the_file_declares_are_read(string encoding)
    {
        using var p = new Folder();
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(p.At("Main.xaml"), System.Text.Encoding.GetEncoding(encoding).GetBytes($"<?xml version=\"1.0\" encoding=\"{encoding}\"?>" + Kit.Window("<Button/>")));
        var r = p.Audit();
        Assert.Empty(r.ParseErrors);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void A_file_that_cannot_be_parsed_says_why()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>")).Add("Bad.xaml", $"<Window {Kit.Ns} Title=\"a\" Title=\"b\"/>");
        Assert.Contains("'Title'", p.Run("audit", p.Root).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Linked_files_are_not_read()
    {
        using var outside = new Folder().Add("Outside.xaml", Kit.Window("<Button/>"));
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>"));
        try { File.CreateSymbolicLink(p.At("Link.xaml"), outside.At("Outside.xaml")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }      // Windows 沒有開發人員模式就不能建
        var r = p.Audit();
        Assert.Equal(1, r.Files);
        Assert.Empty(r.Findings);
    }

    // ───────── 退出碼與報告 ─────────
    [Theory]
    [InlineData("<Button Content=\"{Binding A}\"/><Button Content=\"{Binding B}\"/>", "60", 0)]      // 名稱都是資料：專案改不了，也不是工具漏看
    [InlineData("<local:WhatButton/>", "60", 3)]                                                     // 全部卡住
    [InlineData("<local:WhatButton/>", "0", 0)]
    public void Nothing_judged_is_only_untrustworthy_when_something_was_blocked(string body, string coverage, int exit)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window(body));
        Assert.Equal(exit, p.Run("audit", p.Root, "--min-coverage", coverage).Exit);
    }

    [Fact]
    public void Turning_the_only_rule_off_is_not_an_error()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button/>")).Add("xamla11y.config.json", "{\"rules\":{\"missing-name\":\"off\"}}");
        Assert.Equal(0, p.Run("audit", p.Root).Exit);
    }

    [Fact]
    public void A_baseline_is_not_written_for_a_project_nothing_could_be_judged_in()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<local:WhatButton/>"));
        Assert.Equal(3, p.Run("audit", p.Root, "--write-baseline", p.At("b.json")).Exit);
        Assert.False(File.Exists(p.At("b.json")));
    }

    [Fact]
    public void A_newer_baseline_asks_to_upgrade_even_with_new_fields()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button/>")).Add("b.json", "{\"keyVersion\":2,\"somethingNew\":1,\"known\":[]}");
        var (exit, _, err) = p.Run("audit", p.Root, "--baseline", p.At("b.json"));
        Assert.Equal(2, exit);
        Assert.Contains("upgrade xamla11y", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_works_after_the_command()
    {
        using var p = new Folder();
        Assert.Equal(0, p.Run("audit", "--help").Exit);
    }

    [Fact]
    public void Bidirectional_controls_are_visible_everywhere()
    {
        var bidi = ((char)0x202E).ToString();
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button><StackPanel><TextBlock Text=\"ab{bidi}cd\"/></StackPanel></Button>"));
        p.Run("audit", p.Root, "--json", p.At("r.json"), "--md", p.At("r.md"), "--sarif", p.At("r.sarif"));
        foreach (var report in (string[])["r.json", "r.md", "r.sarif"])
            Assert.False(File.ReadAllText(p.At(report)).Contains((char)0x202E), report);
        Assert.Equal("👩" + (char)0x200D + "💻", Text.Visible("👩" + (char)0x200D + "💻"));
    }
}
