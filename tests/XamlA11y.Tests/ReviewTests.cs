using System.Diagnostics;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>2026-11-24 獨立審查實跑重現的問題，一條一個。多數是**假通過** —— 工具說沒問題、WPF 說有。</summary>
public class ReviewTests
{
    private const string FocusTemplate =
        "<ControlTemplate x:Key=\"T\" TargetType=\"Button\"><Border x:Name=\"b\"/><ControlTemplate.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"b\" Property=\"Background\" Value=\"Red\"/></Trigger></ControlTemplate.Triggers></ControlTemplate>";

    private static string Doc(string extraNs, string body, string resources = "") =>
        $"<Window {Kit.Ns} {extraNs} x:Class=\"App.Main\"><Window.Resources>{resources}</Window.Resources><StackPanel>{body}</StackPanel></Window>";

    private static AuditResult Audit(string extraNs, string body, string resources = "") => Auditor.Run(Project.FromText(Doc(extraNs, body, resources)));

    // ───────── 1 設計期 ─────────
    [Fact]
    public void Design_time_attributes_do_not_exist_at_run_time()
    {
        const string d = "xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"d\"";
        var r = Audit(d, $"<Button d:Content=\"Save\">{Kit.Icon}</Button><TextBox d:AutomationProperties.Name=\"x\"/><Button Content=\"x\" d:IsTabStop=\"False\"/>");
        Assert.Equal(["input-no-name", "missing-name"], r.Findings.Select(f => f.Kind.Slug).Order());
    }

    // ───────── 2 LabeledBy ─────────
    [Theory]
    [InlineData("<Border x:Name=\"b\" AutomationProperties.Name=\"X\"/><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=b}\"/>", "input-no-name")]
    [InlineData("<ListBox AutomationProperties.Name=\"l\"><ListBox.ItemTemplate><DataTemplate><TextBlock x:Name=\"b\" Text=\"X\"/></DataTemplate></ListBox.ItemTemplate></ListBox><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=b}\"/>", "Blocked")]
    [InlineData("<TextBlock x:Name=\"b\" Text=\"X\"/><ContentControl><ContentControl.ContentTemplate><DataTemplate><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=b}\"/></DataTemplate></ContentControl.ContentTemplate></ContentControl>", "Pass")]
    public void LabeledBy_needs_a_peer_in_reach(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "R2"));

    // ───────── 3 同名的第三方型別 ─────────
    [Fact]
    public void A_style_for_a_third_party_type_does_not_apply_to_the_WPF_one()
    {
        var r = Audit("xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\"", $"<Button>{Kit.Icon}</Button><ui:Button>{Kit.Icon}</ui:Button>",
            "<Style TargetType=\"{x:Type ui:Button}\"><Setter Property=\"AutomationProperties.Name\" Value=\"Go\"/></Style>");
        Assert.Equal("Button", Assert.Single(r.Findings).Element);
        Assert.Equal(1, r.Rules.First(s => s.Rule == "R1").Pass);
    }

    // ───────── 4 沒合併的字典 ─────────
    [Theory]
    [InlineData("", "", true)]
    [InlineData("<ResourceDictionary Source=\"Other/Dict.xaml\"/>", "", false)]
    [InlineData("<ResourceDictionary Source=\"pack://application:,,,/App;component/Other/Dict.xaml\"/>", "", false)]
    [InlineData("", "void M() { Load(\"Other/Dict.xaml\"); }", false)]
    public void An_implicit_style_in_a_dictionary_nobody_merges_does_not_apply(string merged, string code, bool reported)
    {
        using var p = new Folder()
            .Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("Other/Dict.xaml", $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"TextBox\"><Setter Property=\"AutomationProperties.Name\" Value=\"Search\"/></Style></ResourceDictionary>")
            .Add("Main.xaml", Kit.Window("<TextBox/>"))
            .Add("X.cs", code);
        Assert.Equal(reported, p.Audit().Findings.Count == 1);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Without_an_App_something_has_to_merge_the_dictionary(bool merged, bool reported)
    {
        using var p = new Folder()
            .Add("Other/Dict.xaml", $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"TextBox\"><Setter Property=\"AutomationProperties.Name\" Value=\"Search\"/></Style></ResourceDictionary>")
            .Add("Main.xaml", Kit.Window("<TextBox/>"));
        if (merged) p.Add("All.xaml", $"<ResourceDictionary {Kit.Ns}><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"Other/Dict.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary>");
        Assert.Equal(reported, p.Audit().Findings.Count == 1);
    }

    // ───────── 5 只有觸發器設內容 ─────────
    [Fact]
    public void Content_that_only_a_trigger_sets_is_unknown() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button><Button.Style><Style TargetType=\"Button\"><Style.Triggers><DataTrigger Binding=\"{Binding P}\" Value=\"True\"><Setter Property=\"Content\" Value=\"Pause\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button>"), "R1"));

    // ───────── 6 專案自己的 ApplicationCommands ─────────
    [Theory]
    [InlineData("{x:Static local:ApplicationCommands.SaveCommand}", "Blocked")]
    [InlineData("{x:Static ApplicationCommands.Save}", "Pass")]
    [InlineData("ApplicationCommands.Save", "Pass")]
    public void A_project_class_called_ApplicationCommands_is_not_the_WPF_one(string command, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit($"<Button Command=\"{command}\">{Kit.Icon}</Button>"), "R1"));

    // ───────── 7 R5 ─────────
    [Theory]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource Ext}\" KeyboardNavigation.IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<Button Content=\"x\" Focusable=\"{Binding F}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource Ext}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"x\" KeyboardNavigation.IsTabStop=\"True\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Style></Button.Style></Button>", "Pass")]
    [InlineData("<Button Content=\"x\"/>", "")]
    public void Tab_order_only_counts_what_it_read(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "R5"));

    // ───────── 8 R3 看用到樣式的元素 ─────────
    [Theory]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource S}\" Template=\"{StaticResource T}\"/>", "<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>", "Pass")]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource S}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border/></ControlTemplate></Button.Template></Button>", "<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/><Setter Property=\"Template\" Value=\"{StaticResource T}\"/>", "focus-not-visible")]
    [InlineData("<Button Content=\"x\" Style=\"{StaticResource S}\" FocusVisualStyle=\"{StaticResource V}\"/><Button Content=\"y\" Style=\"{StaticResource S}\" Focusable=\"False\"/>", "<Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>", "NotJudged,Pass")]
    public void Focus_is_judged_where_the_style_is_used(string body, string setters, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit(body, FocusTemplate +
            "<Style x:Key=\"V\"><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle Stroke=\"Black\"/></ControlTemplate></Setter.Value></Setter></Style>" +
            $"<Style x:Key=\"S\" TargetType=\"Button\">{setters}</Style>"), "R3"));

    // ───────── 9 焦點框被拿掉的其他寫法 ─────────
    [Fact]
    public void A_style_without_a_target_type_can_remove_focus() =>
        Assert.Equal("focus-not-visible", Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource B}\"/>", "<Style x:Key=\"B\"><Setter Property=\"Control.FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"), "R3"));

    [Fact]
    public void The_xaml_namespace_may_have_another_prefix() =>
        Assert.Equal("focus-not-visible", Kit.Verdicts(Audit("xmlns:xaml=\"http://schemas.microsoft.com/winfx/2006/xaml\"", "<Button Content=\"x\"/>",
            "<Style TargetType=\"{xaml:Type Button}\"><Setter Property=\"FocusVisualStyle\" Value=\"{xaml:Null}\"/></Style>"), "R3"));

    [Fact]
    public void A_replaced_menu_item_template_may_not_show_focus() =>
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Menu><MenuItem Header=\"x\"/></Menu>",
            "<Style TargetType=\"MenuItem\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"MenuItem\"><Border/></ControlTemplate></Setter.Value></Setter></Style>"), "R3"));

    [Theory]
    [InlineData("Transparent", "focus-not-visible")]
    [InlineData("#00FFFFFF", "focus-not-visible")]
    [InlineData("#0FFF", "focus-not-visible")]
    [InlineData("#80FFFFFF", "Pass")]
    [InlineData("#000", "Pass")]
    public void A_transparent_focus_rectangle_is_no_focus_rectangle(string stroke, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource S}\"/>",
            $"<Style x:Key=\"S\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\"><Setter.Value><Style><Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate><Rectangle Stroke=\"{stroke}\"/></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter></Style>"), "R3"));

    [Theory]
    [InlineData("<Border/>", "Blocked")]
    [InlineData("<Border BorderBrush=\"{TemplateBinding BorderBrush}\"/>", "Pass")]
    public void A_style_trigger_counts_only_if_the_template_draws_what_it_sets(string template, string expected) =>
        Assert.Equal(expected, Kit.Verdicts(Kit.Audit("<Button Content=\"x\" Style=\"{StaticResource S}\"/>",
            $"<Style x:Key=\"S\" TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\">{template}</ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter Property=\"BorderBrush\" Value=\"Red\"/></Trigger></Style.Triggers></Style>"), "R3"));

    // ───────── 10 字型 ─────────
    [Theory]
    [InlineData("Segoe UI, Segoe MDL2 Assets", "")]
    [InlineData("Segoe MDL2 Assets, Segoe UI", "meaningless-name")]
    public void Letters_are_drawn_by_the_first_family(string family, string kinds) =>
        Assert.Equal(kinds, string.Join(",", Kit.Kinds($"<Button Content=\"Save\" FontFamily=\"{family}\"/>")));

    // ───────── 11 卡死與當機 ─────────
    [Fact]
    public void Branching_cycles_finish()
    {
        var trigger = "<DataTrigger Binding=\"{Binding X}\" Value=\"1\"><Setter Property=\"Content\" Value=\"{StaticResource a}\"/></DataTrigger>";
        var watch = Stopwatch.StartNew();
        Kit.Audit("<Button Content=\"{StaticResource a}\"/>", $"<Button x:Key=\"a\" Content=\"{{StaticResource a}}\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers>{trigger}{trigger}</Style.Triggers></Style></Button.Style></Button>");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Theory]
    [InlineData("{:X}")]
    [InlineData("{X:}")]
    [InlineData("{:}")]
    public void A_markup_extension_with_an_empty_prefix_does_not_crash(string value) => Kit.Audit($"<Button Content=\"{value}\" Command=\"{value}\"/>");

    [Fact]
    public void Long_runs_of_white_space_do_not_hang()
    {
        var spaces = new string(' ', 150_000);
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window($"<!-- xamla11y-ignore: a{spaces}b --><TextBox/><local:Foo x:Name=\"n\"/>"))
            .Add("Main.xaml.cs", $"class X{spaces}; n{spaces}.Content{spaces}= 1; void Down({spaces}")
            .Add("Foo.cs", "class Foo" + spaces + ": Button {}");
        var watch = Stopwatch.StartNew();
        p.Audit();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), watch.Elapsed.ToString());
    }

    // ───────── 12 baseline 檔 ─────────
    [Theory]
    [InlineData("{\"keyVersion\":1,\"known\":null}")]
    [InlineData("{\"keyVersion\":1,\"known\":[null]}")]
    [InlineData("{\"keyVersion\":-5,\"known\":[]}")]
    [InlineData("{\"Known\":[]}")]
    public void A_malformed_baseline_exits_2(string json)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox/>")).Add("b.json", json);
        Assert.Equal(2, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    // ───────── 13 自己設名稱的類別 ─────────
    [Fact]
    public void A_class_that_names_itself_is_blocked_for_names_and_still_judged_for_the_keyboard()
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window($"<local:Tile Focusable=\"False\">{Kit.Icon}</local:Tile><local:Plain>{Kit.Icon}</local:Plain>"))
            .Add("Tile.cs", "public class Tile : Button { void N() { AutomationProperties.SetName(this, \"x\"); } } public class Plain : Button { }");
        var r = p.Audit();
        Assert.Equal("Blocked,missing-name", Kit.Verdicts(r, "R1"));
        Assert.Equal("not-in-tab-order", Kit.Verdicts(r, "R5"));
        Assert.Equal("Tile", Assert.Single(r.UnknownControls).Element);
    }

    // ───────── 14 R4 ─────────
    [Theory]
    [InlineData("<Border MouseLeftButtonUp=\"O\"><Border.ContextMenu><ContextMenu><MenuItem Header=\"Copy\"/></ContextMenu></Border.ContextMenu><TextBlock Text=\"Open\"/></Border>", "mouse-only")]
    [InlineData("<Border Drop=\"D\" MouseLeftButtonUp=\"Browse\"/>", "mouse-only")]
    [InlineData("<UserControl MouseLeftButtonUp=\"O\"/>", "mouse-only")]
    [InlineData("<GroupBox Header=\"x\" MouseDown=\"O\"/>", "mouse-only")]
    [InlineData("<Border UIElement.MouseLeftButtonUp=\"O\"/>", "mouse-only")]
    [InlineData("<Border Mouse.MouseDown=\"O\"/>", "mouse-only")]
    [InlineData("<Button AutomationProperties.Name=\"t\"><Grid><Image MouseLeftButtonUp=\"CloseTab\"/></Grid></Button>", "NotJudged")]
    [InlineData("<UserControl MouseLeftButtonUp=\"O\"><Button Content=\"x\"/></UserControl>", "NotJudged")]
    public void Mouse_rule_cases(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "R4"));

    // ───────── 15 code-behind ─────────
    [Theory]
    [InlineData("// save.Content = \"x\"", "missing-name")]
    [InlineData("/* save.Content = 1; */", "missing-name")]
    [InlineData("var s = \"save.Content = 1\";", "missing-name")]
    [InlineData("if (save.Content == null) { }", "missing-name")]
    [InlineData("var c = save.Content;", "missing-name")]
    [InlineData("save.Content = \"x\";", "Blocked")]
    [InlineData("this.save . Content = x;", "Blocked")]
    [InlineData("save.SetValue(AutomationProperties.NameProperty, \"x\");", "Blocked")]
    [InlineData("char c = '\"'; save.Content = \"x\";", "Blocked")]
    [InlineData("var p = @\"c:\\\"; save.Content = \"x\";", "Blocked")]
    public void Only_real_assignments_in_the_code_behind_count(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button x:Name=\"save\"/>")).Add("Main.xaml.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    [Theory]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Open(); /* not DragMove here */ }", "mouse-only")]
    [InlineData("void M() { AutomationProperties.SetName(card, \"x\"); }", "mouse-only")]
    [InlineData("void M() { card.KeyDown += K; }", "Blocked")]
    public void Naming_an_element_is_not_handling_keys(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Border x:Name=\"card\" MouseLeftButtonDown=\"Down\"/>")).Add("Main.xaml.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R4"));
    }

    // ───────── 16 x:Class 的根 ─────────
    [Fact]
    public void Attributes_on_the_root_of_a_class_are_its_local_values()
    {
        using var p = new Folder()
            .Add("IconButton.xaml", $"<Button {Kit.Ns} x:Class=\"App.IconButton\" AutomationProperties.Name=\"Delete\" IsTabStop=\"False\"/>")
            .Add("IconButton.xaml.cs", "namespace App { public partial class IconButton { } }")
            .Add("Main.xaml", Kit.Window($"<local:IconButton>{Kit.Icon}</local:IconButton><local:IconButton IsTabStop=\"True\">{Kit.Icon}</local:IconButton>"));
        var r = p.Audit();
        Assert.Equal("Pass,Pass", Kit.Verdicts(r, "R1"));
        Assert.Equal("not-in-tab-order,Pass", Kit.Verdicts(r, "R5"));
    }

    // ───────── 17 資源 ─────────
    [Fact]
    public void An_empty_string_resource_is_not_a_name() =>
        Assert.Equal("missing-name", Kit.Verdicts(Kit.Audit($"<Button AutomationProperties.Name=\"{{StaticResource N}}\">{Kit.Icon}</Button>", "<sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"N\"></sys:String>"), "R1"));

    [Fact]
    public void Two_different_resources_with_one_key_are_unknown()
    {
        string Dict(string content) => $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"I\" TargetType=\"Button\"><Setter Property=\"Content\"><Setter.Value>{content}</Setter.Value></Setter></Style></ResourceDictionary>";
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button Style=\"{StaticResource I}\"/>", more: [("A.xaml", Dict("<TextBlock Text=\"Save\"/>")), ("B.xaml", Dict(Kit.Icon))]), "R1"));
    }

    // ───────── 18 可解析率 ─────────
    [Fact]
    public void The_resolution_gate_is_about_the_rules_that_fail()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"{StaticResource A}\"/><Button Content=\"{StaticResource B}\"/><Button Content=\"{StaticResource C}\"/><Button Content=\"ok\"/>"));
        Assert.Equal(25, p.Audit().Resolution);
        Assert.Equal(3, p.Run("audit", p.Root).Exit);
    }

    [Fact]
    public void Mostly_unparseable_projects_are_not_trustworthy()
    {
        using var p = new Folder().Add("Good.xaml", Kit.Window("<Button Content=\"ok\"/>"));
        for (var i = 0; i < 5; i++) p.Add($"Bad{i}.xaml", "<Window");
        Assert.Equal(3, p.Run("audit", p.Root).Exit);
    }

    [Fact]
    public void A_source_folder_called_Packages_is_scanned_and_a_NuGet_one_is_not()
    {
        using var p = new Folder()
            .Add("Views/Packages/A.xaml", Kit.Window("<TextBox/>"))
            .Add("packages/Some.Package.1.0.0/Some.Package.1.0.0.nupkg", "")
            .Add("packages/Some.Package.1.0.0/B.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal("Views/Packages/A.xaml", Assert.Single(p.Audit().Findings).File);
    }

    [Theory]
    [InlineData("Views\\Old")]
    [InlineData("./Views/Old")]
    [InlineData("/views/old/")]
    public void Excluded_files_are_counted(string exclude)
    {
        using var p = new Folder().Add("Views/Old/A.xaml", Kit.Window("<TextBox/>")).Add("Main.xaml", Kit.Window("<Button Content=\"ok\"/>"));
        var (exit, output, _) = p.Run("audit", p.Root, "--exclude", exclude);
        Assert.Equal(0, exit);
        Assert.Contains("1 XAML file(s) excluded", output);
    }

    // ───────── 19 baseline 語意 ─────────
    [Fact]
    public void A_finding_that_gets_worse_is_new()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button Content=\"ok\"/><Button x:Name=\"q\" Focusable=\"False\">{Kit.Icon}</Button>"));
        p.Run("audit", p.Root, "--write-baseline", p.At("b.json"));
        p.Add("Main.xaml", Kit.Window($"<Button Content=\"ok\"/><Button x:Name=\"q\">{Kit.Icon}</Button>"));
        Assert.Equal(1, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void A_key_is_never_cut_inside_a_surrogate_pair()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<TextBox ToolTip=\"{new string('a', 71)}😀bbb\"/><TextBox ToolTip=\"{new string('a', 72)}😀bbb\"/><TextBox ToolTip=\"{new string('a', 79)}😀bbb\"/>"));
        p.Run("audit", p.Root, "--write-baseline", p.At("b.json"));
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void Keys_cannot_collide_through_the_separator()
    {
        var a = new Finding(Kinds.MissingName, Severity.Fail, "f", 1, "Button", "") { Identity = "a|#2" };
        var b = new Finding(Kinds.MissingName, Severity.Fail, "f", 1, "Button", "") { Identity = "a", Ordinal = 2 };
        Assert.NotEqual(a.BaselineKey, b.BaselineKey);
    }

    [Fact]
    public void Write_baseline_does_not_silently_drop_other_outputs()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal(2, p.Run("audit", p.Root, "--write-baseline", p.At("b.json"), "--json", p.At("r.json")).Exit);
        Assert.False(File.Exists(p.At("b.json")));
    }

    // ───────── 20 其餘 ─────────
    [Theory]
    [InlineData(" xamla11y-ignore: decorative ", "decorative")]
    [InlineData("xamlfit-ignore: a; xamla11y-ignore: b", "b")]
    [InlineData("xamla11y-ignore: b; xamlfit-ignore: a", "b")]
    [InlineData("xamla11y-ignore: see xamlcontrast-ignore", "see")]
    [InlineData("xamla11y-ignore", "")]
    [InlineData("xamla11y-ignore:", "")]
    [InlineData("xamla11y-ignore::", "")]
    [InlineData("xamla11y-ignore - because", "")]
    [InlineData("xamla11y-ignored: no", null)]
    [InlineData("TODO: remove every xamla11y-ignore in this file", null)]
    [InlineData("xamlfit-ignore: a", null)]
    [InlineData("", null)]
    public void Ignore_comments_are_read_strictly(string comment, string? reason) => Assert.Equal(reason, Context.ParseIgnore(comment));

    [Theory]
    [InlineData("<WrapPanel ItemWidth=\"60\"><Button Content=\"a\" Width=\"16\"/><Button Content=\"b\" Width=\"16\"/></WrapPanel>", "NotJudged,NotJudged")]
    [InlineData("<Button Content=\"a\" Width=\"40\" Height=\"{DynamicResource Missing}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"a\" MaxWidth=\"300\" MaxHeight=\"300\"/>", "NotJudged")]
    [InlineData("<Button Content=\"a\" Width=\"40\" MinHeight=\"30\"/>", "Pass")]
    public void Sizes_that_are_only_partly_known(string xaml, string expected) => Assert.Equal(expected, Kit.Verdicts(Kit.Audit(xaml), "R6"));

    [Fact]
    public void Sarif_messages_cannot_contain_links()
    {
        var f = new Finding(Kinds.MissingName, Severity.Fail, "f", 1, "Button", "shows [fix](https://evil.example)");
        Assert.Contains(@"\\[fix\\](https", SarifReport.Create([f], "0"));
    }

    [Fact]
    public void One_huge_resource_key_does_not_blow_up_the_report()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button Content=\"{{StaticResource {new string('k', 200_000)}}}\"/><Button Content=\"ok\"/>"));
        p.Run("audit", p.Root, "--md", p.At("r.md"), "--min-coverage", "0");
        Assert.True(new FileInfo(p.At("r.md")).Length < 10_000);
    }

    [Fact]
    public void A_percent_sign_in_a_path_is_just_a_percent_sign()
    {
        using var p = new Folder().Add("rt%41/A%41.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal("rt%41/A%41.xaml", Assert.Single(p.Audit().Findings).File);
    }

    [Fact]
    public void TreatAs_does_not_override_WPF_itself() =>
        Assert.Equal("missing-name", Kit.Verdicts(Kit.Audit($"<Button>{Kit.Icon}</Button>", treatAs: new Dictionary<string, string> { ["Button"] = "Border" }), "R1"));

    [Theory]
    [InlineData("##vso[task.x]")]
    [InlineData("##VSO[task.x]")]
    [InlineData("#\u00AD#vso[task.x]")]
    [InlineData("##[error]")]
    public void Ci_commands_are_defused(string s)
    {
        var safe = Text.Safe(s);
        Assert.DoesNotContain("##", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00AD", safe, StringComparison.Ordinal);
    }
}
