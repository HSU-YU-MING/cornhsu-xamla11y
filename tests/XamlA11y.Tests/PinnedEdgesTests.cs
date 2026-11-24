using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>變異測試第三批：條件的另一邊沒有人走過的地方。</summary>
public class PinnedEdgesTests
{
    private static string Audit(string rule, string body, string resources = "") => Kit.Verdicts(Kit.Audit(body, resources), rule);

    private static string WithNs(string ns, string rule, string body) =>
        Kit.Verdicts(Auditor.Run(Project.FromText($"<Window {Kit.Ns} {ns}><StackPanel>{body}</StackPanel></Window>")), rule);

    private static string Csproj(params string[] packages) =>
        "<Project><ItemGroup>" + string.Concat(packages.Select(p => $"<PackageReference Include=\"{p.Split(' ')[0]}\" Version=\"{p.Split(' ')[1]}\"/>")) + "</ItemGroup></Project>";

    // ───────── 函式庫與命名空間 ─────────
    [Theory]
    [InlineData("clr-namespace:HandyControl.Controls;assembly=HandyControl", "input-no-name")]
    [InlineData("clr-namespace:HandyControl.Controls;assembly=Other", "Blocked")]
    [InlineData("clr-namespace:HandyControl.Controls", "Blocked")]
    public void A_library_is_recognised_by_its_assembly(string ns, string expected) => Assert.Equal(expected, WithNs($"xmlns:h=\"{ns}\"", "R2", "<h:TextBox/>"));

    [Theory]
    [InlineData("<MediaElement x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "Pass")]
    [InlineData("<Image x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "Pass")]
    [InlineData("<Viewport3D x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "Pass")]
    [InlineData("<Border x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "input-no-name")]
    [InlineData("<StatusBarItem x:Name=\"l\" AutomationProperties.Name=\"x\"/>", "input-no-name")]
    public void Only_elements_with_a_peer_can_label(string label, string expected) =>
        Assert.Equal(expected, Audit("R2", label + "<TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"/>"));

    [Theory]
    [InlineData("Segoe MDL2 Assets,Segoe Fluent Icons,Marlett,Webdings,Wingdings 2,FontAwesome,Material Design Icons,Material Icons,Material Symbols Outlined,iconfont,IcoFont,IcoMoon,Ionicons,/Fonts/#iconfont")]
    public void Icon_fonts(string families)
    {
        foreach (var family in families.Split(','))
            Assert.True(Audit("R1", $"<Button FontFamily=\"{family}\" Content=\"a\"/>") == "meaningless-name", family);
        Assert.Equal("Pass", Audit("R1", "<Button FontFamily=\"Segoe UI, Segoe MDL2 Assets\" Content=\"a\"/>"));
        Assert.Equal("Pass", Audit("R1", "<Button FontFamily=\"\" Content=\"a\"/>"));
    }

    [Theory]
    [InlineData("FooIcon,FooGlyph,FontAwesome,SvgViewbox,SymbolIcon,CachedImage,ImageEx", "missing-name")]
    [InlineData("FooChart,Spinner", "Blocked")]
    public void Unknown_content_that_looks_like_an_icon(string types, string expected)
    {
        foreach (var type in types.Split(','))
            Assert.True(Audit("R1", $"<Button><local:{type}/></Button>").Split(',')[0] == expected, type);
    }

    [Fact]
    public void Unknown_content_is_listed() =>
        Assert.Equal("FooChart", Assert.Single(Kit.Audit("<Button><local:FooChart/></Button>").UnknownControls).Element);

    // ───────── 名稱 ─────────
    [Theory]
    [InlineData("Copy,Cut,Paste,Undo,Redo,Delete,Close,New,Open,Save,SaveAs,Print,PrintPreview,Find,Replace,SelectAll,Help,Properties,Stop")]
    [InlineData("ApplicationCommands.Copy,EditingCommands.ToggleBold,NavigationCommands.BrowseBack,MediaCommands.Play,ComponentCommands.MoveDown")]
    public void Commands_that_give_a_name(string commands)
    {
        foreach (var command in commands.Split(','))
            Assert.True(Audit("R1", $"<Button Command=\"{command}\">{Kit.Icon}</Button>") == "Pass", command);
    }

    [Theory]
    [InlineData("xmlns:p=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"", "p:ApplicationCommands.Copy", "Pass")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "p:ApplicationCommands.Copy", "Blocked")]
    [InlineData("xmlns:p=\"clr-namespace:Microsoft.Windows.Shell;assembly=Microsoft.Windows.Shell\"", "p:SystemCommands.CloseWindowCommand", "missing-name")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "p:SystemCommands.CloseWindowCommand", "Blocked")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "q:SystemCommands.CloseWindowCommand", "Blocked")]
    [InlineData("xmlns:p=\"clr-namespace:App\"", "SystemCommands.CloseWindowCommand", "missing-name")]
    public void Commands_are_told_apart_by_their_namespace(string ns, string command, string expected) =>
        Assert.Equal(expected, WithNs(ns, "R1", $"<Button Command=\"{{x:Static {command}}}\">{Kit.Icon}</Button>"));

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
    [InlineData("<Button><TextBlock><InlineUIContainer><TextBlock Text=\"Save\"/></InlineUIContainer></TextBlock></Button>", "meaningless-name")]
    [InlineData("<Button><TextBlock><Run><Run.Text>Save</Run.Text></Run></TextBlock></Button>", "Pass")]
    [InlineData("<Button><TextBlock><Bold>Save</Bold></TextBlock></Button>", "Pass")]
    [InlineData("<Button><TextBlock><Span><Italic><Underline>Save</Underline></Italic></Span></TextBlock></Button>", "Pass")]
    public void Content(string xaml, string expected) =>
        Assert.Equal(expected, Audit("R1", xaml, "<s:String xmlns:s=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"k\">Save</s:String><Style x:Key=\"empty\" TargetType=\"Button\"/><Style x:Key=\"outside\" TargetType=\"Button\" BasedOn=\"{StaticResource far}\"/>"));

    [Fact]
    public void A_trigger_in_a_base_style_may_set_the_name() =>
        Assert.Equal("Blocked", Audit("R1", $"<Button Style=\"{{StaticResource d}}\">{Kit.Icon}</Button>",
            "<Style x:Key=\"b\" TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsEnabled\" Value=\"True\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Trigger></Style.Triggers></Style><Style x:Key=\"d\" TargetType=\"Button\" BasedOn=\"{StaticResource b}\"/>"));

    [Theory]
    [InlineData("", "version was not found")]
    [InlineData("MaterialDesignThemes 4.9.0", "the project uses 4.9.0")]
    [InlineData("MaterialDesignThemes 5.3.2", null)]
    public void A_hint_names_only_in_the_version_that_was_measured(string package, string? why)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><md:BundledTheme/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("Main.xaml", Kit.Window("<TextBox md:HintAssist.Hint=\"Name\"/>")).Add("App.csproj", package.Length > 0 ? Csproj(package) : "<Project/>");
        var j = p.Audit().Judgements.Single(x => x.Rule == "R2");
        if (why is null) Assert.Equal(Verdict.Pass, j.Verdict);
        else Assert.Contains(why, j.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("class MyButton : System.Windows.Controls.Button { }", "missing-name")]
    [InlineData("class MyButton : global::System.Windows.Controls.Button { }", "missing-name")]
    [InlineData("class MyButton : Button, IDisposable { }", "missing-name")]
    [InlineData("class MyButton<T> : Button { }", "missing-name")]
    [InlineData("class MyButton(int x) : Button(x) { }", "missing-name")]
    public void Bases_are_read_however_they_are_written(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<local:MyButton/>")).Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    // ───────── 樣式 ─────────
    [Theory]
    [InlineData("Lib/Themes/Generic.xaml", true)]
    [InlineData("Themes/Generic.xaml", true)]
    [InlineData("MyThemes/Generic.xaml", false)]
    public void Generic_xaml_is_where_WPF_looks(string file, bool visible)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}/>").Add("Main.xaml", Kit.Window("<TextBox/>"))
            .Add(file, $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"TextBox\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ResourceDictionary>");
        Assert.Equal(visible ? "Pass" : "input-no-name", Kit.Verdicts(p.Audit(), "R2"));
    }

    [Theory]
    [InlineData("./D.xaml")]
    [InlineData("Sub/../D.xaml")]
    [InlineData("/D.xaml")]
    [InlineData("Sub\\..\\D.xaml")]
    [InlineData("/App;component/D.xaml")]
    [InlineData("pack://application:,,,/D.xaml")]
    public void A_merged_dictionary_is_found(string source)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary Source=\"{source}\"/></Application.Resources></Application>")
            .Add("Main.xaml", Kit.Window("<TextBox/>")).Add("D.xaml", $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"TextBox\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ResourceDictionary>");
        Assert.Equal("Pass", Kit.Verdicts(p.Audit(), "R2"));
    }

    [Theory]
    [InlineData("TargetType=\"local:MyButton\"", "local:MyButton", "Pass")]
    [InlineData("TargetType=\"{x:Type local:MyButton}\"", "local:MyButton", "Pass")]
    [InlineData("TargetType=\"{x:Type TypeName=local:MyButton}\"", "local:MyButton", "Pass")]
    [InlineData("TargetType=\"{x:Type Button}\"", "Button", "Pass")]
    [InlineData("TargetType=\"{x:Type Button}\"", "local:MyButton", "missing-name")]
    [InlineData("x:Key=\"{x:Type Button}\" TargetType=\"Button\"", "Button", "Pass")]
    [InlineData("x:Key=\"\" TargetType=\"Button\"", "Button", "missing-name")]
    public void Implicit_styles_apply_to_their_type(string target, string element, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<{element}>{Kit.Icon}</{element}>", $"<Style {target}><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style>")).Add("C.cs", "class MyButton : Button { }");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R1"));
    }

    [Fact]
    public void An_implicit_style_based_on_its_own_type_builds_on_the_outer_one() =>
        Assert.Equal("Pass", Audit("R1", $"<StackPanel><StackPanel.Resources><Style TargetType=\"Button\" BasedOn=\"{{StaticResource {{x:Type Button}}}}\"/></StackPanel.Resources><Button>{Kit.Icon}</Button></StackPanel>",
            "<Style TargetType=\"Button\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style>"));

    [Fact]
    public void Global_styles_of_one_type_that_base_themselves_are_not_bases_of_each_other()
    {
        static string Dictionary(string setter) => $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"Button\" BasedOn=\"{{StaticResource {{x:Type Button}}}}\">{setter}</Style></ResourceDictionary>";
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"A.xaml\"/><ResourceDictionary Source=\"B.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("A.xaml", Dictionary("<Setter Property=\"AutomationProperties.Name\" Value=\"x\"/>")).Add("B.xaml", Dictionary("")).Add("Main.xaml", Kit.Window($"<Button>{Kit.Icon}</Button>"));
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "R1"));
    }

    [Theory]
    [InlineData("<TreeView><TreeView.ItemContainerStyle><Style TargetType=\"TreeViewItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></TreeView.ItemContainerStyle><TreeViewItem/></TreeView>", "Pass")]
    [InlineData("<TreeView><TreeViewItem Header=\"a\"><TreeViewItem.ItemContainerStyle><Style TargetType=\"TreeViewItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></TreeViewItem.ItemContainerStyle><TreeViewItem/></TreeViewItem></TreeView>", "Pass,Pass")]
    [InlineData("<ContextMenu x:Key=\"c\"><ContextMenu.ItemContainerStyle><Style TargetType=\"MenuItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ContextMenu.ItemContainerStyle><MenuItem/></ContextMenu>", "Pass")]
    [InlineData("<Menu><MenuItem Header=\"a\"><MenuItem.ItemContainerStyle><Style TargetType=\"MenuItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></MenuItem.ItemContainerStyle><MenuItem/></MenuItem></Menu>", "Pass,Pass")]
    [InlineData("<ComboBox AutomationProperties.Name=\"n\"><ComboBox.ItemContainerStyle><Style TargetType=\"ComboBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ComboBox.ItemContainerStyle><ComboBoxItem/></ComboBox>", "Pass")]
    [InlineData("<ListView><ListView.ItemContainerStyle><Style TargetType=\"ListViewItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ListView.ItemContainerStyle><ListViewItem/></ListView>", "Pass")]
    [InlineData("<ListBox ItemContainerStyle=\"{Binding S}\"><ListBoxItem>x</ListBoxItem></ListBox>", "Pass")]
    public void Item_container_styles(string xaml, string expected) => Assert.Equal(expected, Audit("R1", xaml));

    [Fact]
    public void The_target_of_a_template_may_come_from_its_style() =>
        Assert.Equal("NotJudged", Audit("R1", "", "<Style x:Key=\"s\" TargetType=\"ComboBox\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate><ToggleButton/></ControlTemplate></Setter.Value></Setter></Style>"));

    // ───────── 焦點 ─────────
    [Theory]
    [InlineData("Focusable=\"False\"", "NotJudged")]
    [InlineData("IsTabStop=\"False\"", "NotJudged")]
    [InlineData("Focusable=\"True\"", "focus-not-visible")]
    public void What_cannot_take_focus_needs_no_visual(string attribute, string expected)
    {
        Assert.Equal(expected, Audit("R3", $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\" {attribute}/>"));
        Assert.Equal(expected, Audit("R3", $"<Button Content=\"x\" {attribute}/>", "<Style TargetType=\"Button\"><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/></Style>"));
    }

    [Theory]
    [InlineData("local:FancyButton")]
    [InlineData("local:FancyTextBox")]
    public void A_style_for_an_unknown_control_lists_it(string type)
    {
        var r = Kit.Audit($"<{type} Style=\"{{StaticResource s}}\"/>", $"<Style x:Key=\"s\" TargetType=\"{type}\"><Setter Property=\"FocusVisualStyle\" Value=\"{{x:Null}}\"/></Style>");
        Assert.Equal("Blocked", Kit.Verdicts(r, "R3"));
        Assert.Contains(r.UnknownControls, u => u.Element == type[6..] && u.Count == 2);
    }

    [Theory]
    [InlineData("<Setter Property=\"Control.Template\" Value=\"{x:Null}\"/>")]
    [InlineData("<Setter Property=\"Control.Template\"><Setter.Value><ControlTemplate/></Setter.Value></Setter>")]
    [InlineData("")]
    public void An_empty_focus_visual_is_none(string setter) =>
        Assert.Equal("focus-not-visible", Audit("R3", "<Button Content=\"x\" FocusVisualStyle=\"{StaticResource f}\"/>", $"<Style x:Key=\"f\">{setter}</Style>"));

    [Theory]
    [InlineData("<Setter Property=\"BorderBrush\" Value=\"Red\"/>", "{TemplateBinding BorderBrush}", "Pass")]
    [InlineData("<Setter Property=\"Border.BorderBrush\" Value=\"Red\"/>", "{TemplateBinding BorderBrush}", "Pass")]
    [InlineData("<Setter Property=\"Background\" Value=\"Red\"/>", "{TemplateBinding BorderBrush}", "Blocked")]
    [InlineData("<Setter Property=\"FocusVisualStyle\" Value=\"{StaticResource other}\"/>", "Red", "Blocked")]
    public void A_style_trigger_needs_a_template_that_draws_it(string setter, string brush, string expected) =>
        Assert.Equal(expected, Audit("R3", "<Button Content=\"x\" Style=\"{StaticResource s}\"/>",
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
        Assert.Equal(expected, Audit("R3", $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border/><ControlTemplate.Triggers>{trigger}</ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>"));

    [Theory]
    [InlineData("MaterialDesign3.Defaults.xaml", "MaterialDesignThemes 5.3.0", "Pass")]
    [InlineData("MaterialDesign2.Defaults.xaml", "MaterialDesignThemes 5.3.0", "Blocked")]
    [InlineData("MaterialDesign3.Defaults.xaml", "MaterialDesignThemes 4.9.0", "Blocked")]
    public void A_theme_shows_focus_in_the_version_that_was_measured(string dictionary, string package, string expected)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/{dictionary}\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("Main.xaml", Kit.Window("<Button Content=\"x\" FocusVisualStyle=\"{x:Null}\"/>")).Add("App.csproj", Csproj(package));
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "R3"));
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

    // ───────── 滑鼠 ─────────
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
    public void Where_the_click_is(string xaml, string expected) => Assert.Equal(expected, Audit("R4", xaml));

    // ───────── 大小、Tab ─────────
    [Theory]
    [InlineData("<Slider AutomationProperties.Name=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<RichTextBox AutomationProperties.Name=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<PasswordBox AutomationProperties.Name=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<RadioButton Content=\"n\" IsTabStop=\"False\"/>", "not-in-tab-order")]
    [InlineData("<TextBlock><Hyperlink IsTabStop=\"False\">n</Hyperlink></TextBlock>", "not-in-tab-order")]
    [InlineData("<TextBox AutomationProperties.Name=\"n\" IsTabStop=\"False\" IsReadOnly=\"True\"/>", "NotJudged")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"False\" IsDefault=\"True\"/>", "Pass")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"False\" IsCancel=\"True\"/>", "Pass")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"{Binding T}\"/>", "NotJudged")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"{StaticResource nowhere}\"/>", "Blocked")]
    [InlineData("<Button Content=\"n\" IsTabStop=\"True\"/>", "Pass")]
    public void Tab_order(string xaml, string expected) => Assert.Equal(expected, Audit("R5", xaml));

    [Theory]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "small-target,small-target")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><ComboBox Width=\"16\" Height=\"30\" AutomationProperties.Name=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "small-target,small-target")]
    [InlineData("<ToolBar><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></ToolBar>", "small-target,small-target")]
    [InlineData("<ToolBarTray Orientation=\"Vertical\"><ToolBar><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></ToolBar></ToolBarTray>", "NotJudged,NotJudged")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\" Visibility=\"Collapsed\"/></StackPanel>", "NotJudged,small-target")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><StackPanel.LayoutTransform><ScaleTransform/></StackPanel.LayoutTransform><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "NotJudged,NotJudged")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><StackPanel.RenderTransform><ScaleTransform/></StackPanel.RenderTransform><Button Width=\"16\" Height=\"30\" Content=\"a\"/><Button Width=\"16\" Height=\"30\" Content=\"b\"/></StackPanel>", "NotJudged,NotJudged")]
    [InlineData("<StackPanel Orientation=\"Horizontal\"><Button Width=\"30\" Height=\"30\" Content=\"a\"/><Button Width=\"30\" Height=\"30\" Content=\"b\"/></StackPanel>", "Pass,Pass")]
    public void Target_size(string xaml, string expected) => Assert.Equal(expected, Audit("R6", xaml).Replace("Passed", "Pass", StringComparison.Ordinal));

    // ───────── 檔案 ─────────
    [Theory]
    [InlineData("obj")]
    [InlineData("OBJ")]
    [InlineData("bin")]
    [InlineData("node_modules")]
    [InlineData(".git")]
    public void Build_output_is_not_scanned(string directory)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>")).Add(directory + "/Copy.xaml", Kit.Window("<Button/>"));
        Assert.Equal(1, p.Audit().Files);
    }

    [Fact]
    public void A_file_without_marks_is_what_its_neighbours_are()
    {
        const string wpf = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:l=\"clr-namespace:A\"><Button/></Page>";
        const string winui = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:l=\"using:A\"><Button/></Page>";
        const string plain = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Button/></Page>";
        using var p = new Folder().Add("W/A.xaml", wpf).Add("W/B.xaml", plain).Add("U/A.xaml", winui).Add("U/B.xaml", plain).Add("U/Deep/C.xaml", plain);
        var r = p.Audit();
        Assert.Equal(["U/A.xaml", "U/B.xaml", "U/Deep/C.xaml"], r.Foreign.Select(f => f.File).Order(StringComparer.Ordinal));
        Assert.Equal(2, r.Findings.Count);
    }

    [Fact]
    public void The_reports_count_each_severity_on_its_own()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button Content=\"x\" IsTabStop=\"False\"/><Button Content=\"y\" IsTabStop=\"False\"/><Button Content=\"×\"/>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 1));
        var text = json.RootElement.GetProperty("summary").GetRawText();
        Assert.Contains("\"fail\": 4", text, StringComparison.Ordinal);
        Assert.Contains("\"warn\": 2", text, StringComparison.Ordinal);
        Assert.Contains("\"info\": 1", text, StringComparison.Ordinal);
        Assert.Contains("**4 fail**, 2 warn, 1 info", MarkdownReport.Create(r, r.Findings, 1, 0, null), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<TextBlock x:Name=\"a,b\" Text=\"x\"/><TextBox AutomationProperties.LabeledBy=\"{Binding Path='X,Y', ElementName=l}\"/><TextBlock x:Name=\"l\" Text=\"Name\"/>")]
    public void Quotes_inside_a_binding_do_not_split_it(string xaml) => Assert.Equal("Pass", Audit("R2", xaml));
}
