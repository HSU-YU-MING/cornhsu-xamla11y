using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>樣式與資源找不找得到：隱含樣式、BasedOn、合併的字典、同名的鍵、型別的命名空間。</summary>
public sealed class StyleAndResourceTests
{
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

    [Fact]
    public void A_style_for_a_third_party_type_does_not_apply_to_the_WPF_one()
    {
        var r = Kit.AuditNs("xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\"", $"<Button>{Kit.Icon}</Button><ui:Button>{Kit.Icon}</ui:Button>",
            "<Style TargetType=\"{x:Type ui:Button}\"><Setter Property=\"AutomationProperties.Name\" Value=\"Go\"/></Style>");
        Assert.Equal("Button", Assert.Single(r.Findings).Element);
        Assert.Equal(1, r.Checks.First(s => s.Check == "control-names").Pass);
    }

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

    [Fact]
    public void An_empty_string_resource_is_not_a_name() =>
        Assert.Equal("missing-name", Kit.Verdicts(Kit.Audit($"<Button AutomationProperties.Name=\"{{StaticResource N}}\">{Kit.Icon}</Button>", "<sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"N\"></sys:String>"), "control-names"));

    [Fact]
    public void Two_different_resources_with_one_key_are_unknown()
    {
        string Dict(string content) => $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"I\" TargetType=\"Button\"><Setter Property=\"Content\"><Setter.Value>{content}</Setter.Value></Setter></Style></ResourceDictionary>";
        Assert.Equal("Blocked", Kit.Verdicts(Kit.Audit("<Button Style=\"{StaticResource I}\"/>", more: [("A.xaml", Dict("<TextBlock Text=\"Save\"/>")), ("B.xaml", Dict(Kit.Icon))]), "control-names"));
    }

    [Fact]
    public void A_dictionary_merged_only_by_Generic_xaml_is_visible()
    {
        using var p = new Folder()
            .Add("App.xaml", $"<Application {Kit.Ns}/>")
            .Add("Themes/Generic.xaml", $"<ResourceDictionary {Kit.Ns}><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"/App;component/Themes/CloseButton.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary>")
            .Add("Themes/CloseButton.xaml", $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"local:CloseButton\"><Setter Property=\"AutomationProperties.Name\" Value=\"Close\"/></Style></ResourceDictionary>")
            .Add("Main.xaml", Kit.Window("<local:CloseButton/>")).Add("C.cs", "class CloseButton : Button { }");
        Assert.Equal("Pass", Kit.Verdicts(p.Audit(), "control-names"));
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
        Assert.Equal("Pass", Kit.Verdicts(p.Audit(), "input-names"));
    }

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
        Assert.Equal("not-in-tab-order", Kit.Verdicts(Kit.Audit("<Button Content=\"x\"/>", $"<Style TargetType=\"Button\"><Setter Property=\"{owner}.Focusable\" Value=\"False\"/></Style>"), "tab-order"));

    [Theory]
    [InlineData("hc:IconElement.Focusable")]
    [InlineData("local:Own.Focusable")]
    public void A_foreign_owner_is_another_property(string property) =>
        Assert.Equal("", Kit.Verdicts(Kit.Audit("<Button Content=\"x\"/>", $"<Style TargetType=\"Button\"><Setter Property=\"{property}\" Value=\"False\"/></Style>"), "tab-order"));

    [Fact]
    public void Resources_of_one_key_that_differ_are_unknown()
    {
        static string Verdict(string a, string b) => Kit.Verdicts(Auditor.Run(Project.FromText(Kit.Window("<Button Content=\"{StaticResource k}\"/>"), null,
            ("A.xaml", $"<ResourceDictionary {Kit.Ns}>{a}</ResourceDictionary>"), ("B.xaml", $"<ResourceDictionary {Kit.Ns}>{b}</ResourceDictionary>"))), "control-names");
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
        static string Verdict(string a, string b) => Kit.Verdicts(Auditor.Run(Project.FromText(Kit.Window($"<Button Style=\"{{StaticResource k}}\">{Kit.Icon}</Button>"), null, ("A.xaml", Style(a)), ("B.xaml", Style(b)))), "control-names");
        const string named = "<Setter Property=\"AutomationProperties.Name\" Value=\"Save\"/>";
        Assert.Equal("Pass", Verdict(named, named));
        Assert.Equal("Blocked", Verdict(named, ""));
        Assert.Equal("missing-name", Verdict("", ""));
    }

    [Theory]
    [InlineData("Control,FrameworkElement,FrameworkContentElement,UIElement,ContentElement,ContentControl,HeaderedContentControl,ItemsControl,HeaderedItemsControl,ButtonBase,Button,ToggleButton,RepeatButton,CheckBox,RadioButton")]
    [InlineData("TextBlock,TextElement,TextBoxBase,TextBox,PasswordBox,RichTextBox,ComboBox,ComboBoxItem,MenuItem,ListBox,ListBoxItem,ListView,ListViewItem,TreeViewItem,TabItem,Expander,Slider,RangeBase,Selector")]
    [InlineData("Label,Shape,Image,Border,Hyperlink,Window,Popup,DatePicker,UserControl,Rectangle,Ellipse,Path")]
    public void Any_base_may_be_named_as_the_owner(string owners)
    {
        foreach (var owner in owners.Split(','))
            Assert.True(Kit.Verdict("tab-order", "<Button Content=\"x\"/>", $"<Style TargetType=\"Button\"><Setter Property=\"{owner}.Focusable\" Value=\"False\"/></Style>") == "not-in-tab-order", owner);
    }

    [Fact]
    public void A_file_prefers_its_own_resource()
    {
        static string Dictionary(string name) => $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"k\" TargetType=\"Button\">{name}</Style></ResourceDictionary>";
        var main = $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"k\" TargetType=\"Button\"><Setter Property=\"AutomationProperties.Name\" Value=\"n\"/></Style><DataTemplate x:Key=\"d\"><Button Style=\"{{StaticResource k}}\">{Kit.Icon}</Button></DataTemplate></ResourceDictionary>";
        Assert.Equal("Pass", Kit.Verdicts(Auditor.Run(Project.FromText(main, null, ("Other.xaml", Dictionary("")))), "control-names"));
    }

    [Theory]
    [InlineData("Lib/Themes/Generic.xaml", true)]
    [InlineData("Themes/Generic.xaml", true)]
    [InlineData("MyThemes/Generic.xaml", false)]
    public void Generic_xaml_is_where_WPF_looks(string file, bool visible)
    {
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}/>").Add("Main.xaml", Kit.Window("<TextBox/>"))
            .Add(file, $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"TextBox\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style></ResourceDictionary>");
        Assert.Equal(visible ? "Pass" : "input-no-name", Kit.Verdicts(p.Audit(), "input-names"));
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
        Assert.Equal("Pass", Kit.Verdicts(p.Audit(), "input-names"));
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
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Fact]
    public void An_implicit_style_based_on_its_own_type_builds_on_the_outer_one() =>
        Assert.Equal("Pass", Kit.Verdict("control-names", $"<StackPanel><StackPanel.Resources><Style TargetType=\"Button\" BasedOn=\"{{StaticResource {{x:Type Button}}}}\"/></StackPanel.Resources><Button>{Kit.Icon}</Button></StackPanel>",
            "<Style TargetType=\"Button\"><Setter Property=\"AutomationProperties.Name\" Value=\"x\"/></Style>"));

    [Fact]
    public void Global_styles_of_one_type_that_base_themselves_are_not_bases_of_each_other()
    {
        static string Dictionary(string setter) => $"<ResourceDictionary {Kit.Ns}><Style TargetType=\"Button\" BasedOn=\"{{StaticResource {{x:Type Button}}}}\">{setter}</Style></ResourceDictionary>";
        using var p = new Folder().Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"A.xaml\"/><ResourceDictionary Source=\"B.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
            .Add("A.xaml", Dictionary("<Setter Property=\"AutomationProperties.Name\" Value=\"x\"/>")).Add("B.xaml", Dictionary("")).Add("Main.xaml", Kit.Window($"<Button>{Kit.Icon}</Button>"));
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "control-names"));
    }
}
