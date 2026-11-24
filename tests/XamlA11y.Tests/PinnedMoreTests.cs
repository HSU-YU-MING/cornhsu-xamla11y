using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>變異測試活著的：清單少一項沒人發現。一項一個案例。</summary>
public class PinnedMoreTests
{
    private const string List = "<ListBox ItemsSource=\"{Binding Notes}\" AutomationProperties.Name=\"a\"/>";

    private static string[] R8(string code, string xaml = List, string codeBehind = "", string resources = "")
    {
        using var f = new Folder().Add("Main.xaml", Kit.Window(xaml, resources)).Add("Code.cs", code);
        if (codeBehind.Length > 0) f.Add("Main.xaml.cs", codeBehind);
        return [.. f.Audit().Findings.Where(x => x.Kind == Kinds.ItemTypeName).Select(x => x.Kind.Slug)];
    }

    private static string Verdict(string rule, string xaml, string code = "", params (string File, string Text)[] more)
    {
        using var f = new Folder().Add("Main.xaml", Kit.Window(xaml)).Add("Code.cs", code);
        foreach (var (file, text) in more) f.Add(file, text);
        return string.Join(",", f.Audit().Judgements.Where(j => j.Rule == rule).Select(j => j.Verdict));
    }

    [Theory]
    [InlineData("ObservableCollection")]
    [InlineData("ReadOnlyObservableCollection")]
    [InlineData("Collection")]
    [InlineData("ReadOnlyCollection")]
    [InlineData("List")]
    [InlineData("IList")]
    [InlineData("IEnumerable")]
    [InlineData("ICollection")]
    [InlineData("IReadOnlyList")]
    [InlineData("IReadOnlyCollection")]
    [InlineData("BindingList")]
    [InlineData("ObservableRangeCollection")]
    [InlineData("BindableCollection")]
    [InlineData("IObservableCollection")]
    [InlineData("HashSet")]
    [InlineData("LinkedList")]
    [InlineData("ImmutableArray")]
    [InlineData("ImmutableList")]
    public void Every_collection_is_followed(string type) => Assert.Single(R8($"class Note {{ }} class V {{ public {type}<Note> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("string")] [InlineData("String")] [InlineData("char")] [InlineData("int")] [InlineData("uint")] [InlineData("long")] [InlineData("ulong")]
    [InlineData("short")] [InlineData("ushort")] [InlineData("byte")] [InlineData("sbyte")] [InlineData("float")] [InlineData("double")] [InlineData("decimal")]
    [InlineData("bool")] [InlineData("Int32")] [InlineData("Int64")] [InlineData("Double")] [InlineData("Boolean")] [InlineData("DateTime")] [InlineData("DateTimeOffset")]
    [InlineData("DateOnly")] [InlineData("TimeOnly")] [InlineData("TimeSpan")] [InlineData("Guid")] [InlineData("Uri")] [InlineData("Version")] [InlineData("CultureInfo")]
    [InlineData("FileInfo")] [InlineData("DirectoryInfo")] [InlineData("IPAddress")] [InlineData("BigInteger")]
    public void Every_readable_type_passes(string type) => Assert.Equal("Pass", Verdict("R8", List, $"class V {{ public List<{type}> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("object")] [InlineData("Object")] [InlineData("ObservableObject")] [InlineData("ObservableRecipient")] [InlineData("ObservableValidator")]
    [InlineData("BindableBase")] [InlineData("PropertyChangedBase")] [InlineData("ReactiveObject")] [InlineData("DependencyObject")]
    [InlineData("MarshalByRefObject")] [InlineData("NotifyPropertyChangedBase")] [InlineData("INotifyPropertyChanged")]
    public void Bases_without_a_ToString_of_their_own(string type) => Assert.Single(R8($"class Note : {type} {{ }} class V {{ public List<Note> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("ItemContainerStyle")]
    [InlineData("RowStyle")]
    [InlineData("ItemContainerStyleSelector")]
    public void A_style_set_in_code_may_name_the_items(string member) => Assert.Empty(R8("class Note { } class V { public List<Note> Notes { get; } }",
        "<ListBox x:Name=\"list\" ItemsSource=\"{Binding Notes}\"/>", $"partial class Main {{ void Load() {{ list.{member} = x; }} }}"));

    /// <summary>名字一樣、種類不一樣的型別：不知道（後面那一個的內容也不能算進前面的）。</summary>
    [Theory]
    [InlineData("class Note { } namespace B { struct Note { } }")]
    [InlineData("struct Note { } namespace B { class Note { } }")]
    [InlineData("interface Note { } namespace B { class Note { } }")]
    public void The_same_name_for_two_kinds_is_unknown(string types) => Assert.Empty(R8(types + " class V { public List<Note> Notes { get; } }"));

    /// <summary>清單的樣式在專案外：合併了量過的佈景就知道它不會設名稱，沒有就不知道。</summary>
    [Fact]
    public void A_list_style_from_a_measured_theme_is_known()
    {
        const string list = "<ListBox ItemsSource=\"{Binding Notes}\" AutomationProperties.Name=\"a\" Style=\"{StaticResource MaterialDesignListBox}\"/>";
        const string code = "class Note { } class V { public List<Note> Notes { get; } }";
        Assert.Equal("NotJudged", Verdict("R8", list, code));
        Assert.Equal("Finding", Verdict("R8", list, code, ("App.xaml", "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")));
    }

    [Theory]
    [InlineData("ListBoxItem")] [InlineData("TabItem")] [InlineData("TreeViewItem")] [InlineData("ComboBoxItem")] [InlineData("ListViewItem")] [InlineData("MenuItem")]
    [InlineData("ScrollBar")] [InlineData("ScrollViewer")] [InlineData("Slider")] [InlineData("ComboBox")] [InlineData("ToolTip")] [InlineData("DataGridCell")]
    [InlineData("DataGridRow")] [InlineData("GridViewColumnHeader")] [InlineData("DataGridColumnHeader")] [InlineData("DataGridRowHeader")] [InlineData("DataGridRowsPresenter")]
    public void Template_parts_that_Tab_may_not_reach_are_not_judged(string target) =>
        Assert.Equal("NotJudged", Verdict("R9", $"<Grid><Grid.Resources><ControlTemplate x:Key=\"t\" TargetType=\"{target}\"><ContentControl/></ControlTemplate></Grid.Resources></Grid>"));

    [Theory]
    [InlineData("Button")]
    [InlineData("GroupBox")]
    [InlineData("Window")]
    public void Other_template_parts_are(string target) =>
        Assert.Equal("Finding", Verdict("R9", $"<Grid><Grid.Resources><ControlTemplate x:Key=\"t\" TargetType=\"{target}\"><ContentControl/></ControlTemplate></Grid.Resources></Grid>"));

    /// <summary>AvalonDock 的拖曳指示視窗：ShowActivated=false，Tab 到不了。</summary>
    [Fact]
    public void A_window_that_never_activates() =>
        Assert.Equal("NotJudged", Verdict("R9", "<Grid><Grid.Resources><ControlTemplate x:Key=\"t\" TargetType=\"{x:Type local:Overlay}\"><ContentControl/></ControlTemplate></Grid.Resources></Grid>", "class Overlay : Window { Overlay() { ShowActivated = false; } }"));

    [Fact]
    public void OpenACR_chapters_only_where_there_is_something()
    {
        var yaml = OpenAcrReport.Create(Kit.Audit("<Border MouseDown=\"x\"/>", options: new() { Scope = new Scope(Standard.En301549, "AA") }), "a", "1", new DateOnly(2026, 11, 24));
        Assert.DoesNotContain("software:\n", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("success_criteria_level_aa:", yaml, StringComparison.Ordinal);
        Assert.Contains("success_criteria_level_a:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenACR_lists_ten_places_and_says_there_are_more()
    {
        string Notes(int n) => OpenAcrReport.Create(Kit.Audit(string.Concat(Enumerable.Range(0, n).Select(i => $"<Button x:Name=\"b{i}\">{Kit.Icon}</Button>"))), "a", "1", new DateOnly(2026, 11, 24));
        Assert.DoesNotContain(", …", Notes(10), StringComparison.Ordinal);
        Assert.Contains("11 place(s)", Notes(11), StringComparison.Ordinal);
        Assert.Contains(", ….", Notes(11), StringComparison.Ordinal);
    }

    /// <summary>主控台：多的時候依種類分組、列檔案；忘了打 audit 會提示。</summary>
    [Fact]
    public void Many_findings_are_grouped()
    {
        using var f = new Folder().Add("A.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<TextBox/>", 30)))).Add("B.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<TextBox/>", 20))));
        var o = f.Run("audit", f.Root).Out;
        Assert.Contains("fail input-no-name ×50", o, StringComparison.Ordinal);
        Assert.Contains("… and 46 more", o, StringComparison.Ordinal);
        Assert.Contains("most in: A.xaml ×30, B.xaml ×20", o, StringComparison.Ordinal);
        var forgot = f.Run(f.Root);
        Assert.Equal(2, forgot.Exit);
        Assert.Contains("did you mean 'xamla11y audit <folder>'?", forgot.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("did you mean", f.Run("audti").Err, StringComparison.Ordinal);
    }

    /// <summary>通過但 baseline 蓋住一堆：報告要說。</summary>
    [Fact]
    public void A_pass_with_known_problems_says_so()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<TextBox/><ListBox/>"));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        Assert.Equal(0, f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--html", f.At("r.html"), "--md", f.At("r.md")).Exit);
        foreach (var file in (string[])["r.html", "r.md"])
            Assert.Contains("Pass means no new findings. 2 known problem(s) in the baseline are not fixed: 1 fail, 1 warn, 0 info.", File.ReadAllText(f.At(file)), StringComparison.Ordinal);
        Assert.Contains("PASS (no new findings)", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
        Assert.DoesNotContain("known problem(s)", HtmlReport.Create(f.Audit(), f.Audit().Findings, 1, 0, null, "1"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wcag22", "AA", "8 of the 55")]
    [InlineData("wcag22", "A", "5 of the 31")]
    [InlineData("wcag21", "AA", "7 of the 50")]
    [InlineData("wcag21", "A", "5 of the 30")]
    [InlineData("section508", "AA", "6 of the 38")]
    [InlineData("section508", "A", "4 of the 25")]
    public void The_html_says_how_much_is_covered(string id, string level, string expected) =>
        Assert.Contains($"This checks {expected} success criteria", Summary.Coverage(new Scope(Standard.ById(id)!, level), Lang.En), StringComparison.Ordinal);

    [Theory]
    [InlineData("<Button Focusable=\"False\">[ICON]</Button>", "; a warning because it cannot take focus")]
    [InlineData("<Button IsEnabled=\"False\">[ICON]</Button>", "; a warning because it is disabled")]
    [InlineData("<Button Visibility=\"Hidden\">[ICON]</Button>", "; a warning because it is hidden")]
    [InlineData("<TextBox Visibility=\"Collapsed\"/>", "; a warning because it is hidden")]
    public void The_message_says_why_it_is_only_a_warning(string xaml, string note)
    {
        var f = Assert.Single(Kit.Audit(xaml.Replace("[ICON]", Kit.Icon, StringComparison.Ordinal)).Findings, x => x.Kind.Rule is "R1" or "R2");
        Assert.Equal(Severity.Warn, f.Severity);
        Assert.EndsWith(note, f.Message, StringComparison.Ordinal);
        Assert.True(Zh.TryMessage(f.Message, out _));
    }

    [Theory]
    [InlineData("<StackPanel><TextBlock Text=\"No data!\"/><ListBox/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"🔍\"/><TextBox/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"Right-click an entry to delete it from the list\"/><TextBox/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"Name\" Visibility=\"Collapsed\"/><TextBox/></StackPanel>")]
    [InlineData("<DockPanel><TextBlock Text=\"Name\" DockPanel.Dock=\"Bottom\"/><TextBox/></DockPanel>")]
    [InlineData("<Grid><TextBlock Text=\"Name\" Grid.Row=\"0\"/><TextBox Grid.Row=\"3\"/></Grid>")]
    public void Text_that_is_not_a_label_is_not_suggested(string xaml) =>
        Assert.DoesNotContain("LabeledBy", Kit.Audit(xaml).Findings.First(x => x.Kind.Rule is "R2" or "R7").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("<Grid><TextBlock Text=\"Name\"/><TextBox Grid.Column=\"1\"/></Grid>")]
    [InlineData("<Grid><TextBlock Text=\"Name\"/><TextBox Grid.Row=\"1\"/></Grid>")]
    public void A_label_in_the_same_row_or_right_above_is(string xaml) =>
        Assert.Contains("is its label, use AutomationProperties.LabeledBy", Kit.Audit(xaml).Findings.Single(x => x.Kind.Rule == "R2").Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("{ \"standrad\": \"wcag22\" }", "standrad: is not a field; the fields are")]
    [InlineData("{ \"rules\": { \"R7\": \"off\" } }", "is a group; name its findings instead: list-no-name, widget-no-name")]
    [InlineData("{ \"rules\": { \"missing\": \"off\" } }", "is not a finding; they are missing-name")]
    public void Config_mistakes_in_plain_words(string json, string expected)
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<Button/>")).Add("xamla11y.config.json", json);
        var r = f.Run("audit", f.Root);
        Assert.Equal(2, r.Exit);
        Assert.Contains(expected, r.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(".NET", r.Err, StringComparison.Ordinal);
    }

    // ───────────────────────── R8 ─────────────────────────

    [Theory]
    [InlineData("ListBox")] [InlineData("ListView")] [InlineData("ComboBox")] [InlineData("TreeView")] [InlineData("TabControl")]
    [InlineData("Menu")] [InlineData("ContextMenu")] [InlineData("MenuItem")] [InlineData("DataGrid")]
    public void Every_list_is_checked(string element) => Assert.Single(R8("class Note { } class V { public List<Note> Notes { get; } }", $"<{element} ItemsSource=\"{{Binding Notes}}\"/>"));

    [Theory]
    [InlineData("XPath=Notes")]
    [InlineData("Notes, Source={x:Static local:X.Y}")]
    [InlineData("Notes[0]")]
    [InlineData("(local:X.Notes)")]
    public void Bindings_that_are_not_a_property_of_the_view_model(string binding) =>
        Assert.Equal("NotJudged", Verdict("R8", $"<ListBox ItemsSource=\"{{Binding {binding}}}\" AutomationProperties.Name=\"a\"/>", "class Note { } class V { public List<Note> Notes { get; } }"));

    [Theory]
    [InlineData("ItemContainerStyleSelector")]
    [InlineData("RowStyleSelector")]
    public void A_style_selector_may_name_the_items(string property) =>
        Assert.Equal("NotJudged", Verdict("R8", $"<DataGrid ItemsSource=\"{{Binding Notes}}\" {property}=\"{{StaticResource x}}\" AutomationProperties.Name=\"a\"/>", "class Note { } class V { public List<Note> Notes { get; } }"));

    [Theory]
    [InlineData("AutomationProperties.Name", "NotJudged")]
    [InlineData("Background", "Finding")]
    public void Only_a_trigger_that_sets_the_name_makes_it_unknown(string property, string expected) =>
        Assert.Equal(expected, Verdict("R8", $"<ListBox ItemsSource=\"{{Binding Notes}}\" AutomationProperties.Name=\"a\"><ListBox.ItemContainerStyle><Style TargetType=\"ListBoxItem\"><Style.Triggers><Trigger Property=\"IsSelected\" Value=\"True\"><Setter Property=\"{property}\" Value=\"x\"/></Trigger></Style.Triggers></Style></ListBox.ItemContainerStyle></ListBox>", "class Note { } class V { public List<Note> Notes { get; } }"));

    /// <summary>寫在裡面的 WPF 元素、函式庫的控制項、自己的控制項不是資料物件。</summary>
    [Theory]
    [InlineData("<TextBlock Text=\"a\"/>")]
    [InlineData("<ListBoxItem Content=\"a\"/>")]
    [InlineData("<hc:Card/>")]
    [InlineData("<local:MyItem/>")]
    public void Elements_inside_a_list_are_not_data(string item) =>
        Assert.Equal("", Verdict("R8", $"<ListBox AutomationProperties.Name=\"a\">{item}</ListBox>", "class MyItem : ListBoxItem { }"));

    [Theory]
    [InlineData("interface INote { } class Note : INote { }")]
    [InlineData("interface Marker { } class Note : Marker { }")]                                  // 名字不像介面的介面
    [InlineData("class Base { } class Note : Base, System.IDisposable { }")]
    [InlineData("class Note<T> { } class Note2 : Note<int> { }")]
    public void Interfaces_and_plain_bases_do_not_change_the_answer(string types) =>
        Assert.Single(R8(types + $" class V {{ public List<{(types.Contains("Note2", StringComparison.Ordinal) ? "Note2" : "Note")}> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("public List<Note> Notes { get; } public class W { public System.Collections.Generic.IList<Note> Notes { get; } }", 1)]      // 同一個答案
    [InlineData("public List<Note> Notes { get; } public class W { public List<Known> Notes { get; } }", 0)]                               // 一個念得出來、一個念不出來
    public void Properties_with_the_same_name(string members, int expected) =>
        Assert.Equal(expected, R8($"class Note {{ }} class Known {{ public override string ToString() => \"x\"; }} class V {{ {members} }}").Length);

    // ───────────────────────── R9 ─────────────────────────

    [Theory]
    [InlineData("Focusable")]
    [InlineData("IsTabStop")]
    public void Set_in_the_code_behind(string member) =>
        Assert.Equal("NotJudged", Verdict("R9", "<ContentControl x:Name=\"host\"/>", "", ("Main.xaml.cs", $"partial class Main {{ void Load() {{ host.{member} = false; }} }}")));

    [Theory]
    [InlineData("Focusable", "NotJudged")]
    [InlineData("IsTabStop", "NotJudged")]
    [InlineData("Background", "Finding")]
    public void Changed_by_a_trigger(string property, string expected) =>
        Assert.Equal(expected, Verdict("R9", $"<ContentControl><ContentControl.Style><Style TargetType=\"ContentControl\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"{property}\" Value=\"False\"/></Trigger></Style.Triggers></Style></ContentControl.Style></ContentControl>"));

    [Theory]
    [InlineData("Collapsed")]
    [InlineData("Hidden")]
    public void Hidden_elements_are_not_stops(string visibility) => Assert.Equal("", Verdict("R9", $"<ContentControl Visibility=\"{visibility}\"/>"));

    /// <summary>有鍵的範本裡的元素是畫面上的；有鍵的元素自己是資源。</summary>
    [Theory]
    [InlineData("<DataTemplate x:Key=\"k\"><ContentControl/></DataTemplate>", "NotJudged")]
    [InlineData("<HierarchicalDataTemplate x:Key=\"k\"><ContentControl/></HierarchicalDataTemplate>", "NotJudged")]
    [InlineData("<ControlTemplate x:Key=\"k\" TargetType=\"Button\"><ContentControl/></ControlTemplate>", "Finding")]
    [InlineData("<Style x:Key=\"k\" TargetType=\"Button\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Button\"><ContentControl/></ControlTemplate></Setter.Value></Setter></Style>", "Finding")]
    [InlineData("<StackPanel x:Key=\"k\"><ContentControl/></StackPanel>", "")]
    public void In_resources(string resource, string expected) => Assert.Equal(expected, Verdict("R9", $"<Grid><Grid.Resources>{resource}</Grid.Resources></Grid>"));

    [Theory]
    [InlineData("GotFocus")] [InlineData("GotKeyboardFocus")] [InlineData("PreviewGotKeyboardFocus")]
    public void A_focus_handler_may_pass_it_on(string handler) => Assert.Equal("NotJudged", Verdict("R9", $"<ContentControl {handler}=\"x\"/>"));

    // ───────────────────────── R10 ─────────────────────────

    [Theory]
    [InlineData("CheckBox", "IsChecked")] [InlineData("ComboBox", "SelectedItem")] [InlineData("ComboBox", "SelectedValue")] [InlineData("ComboBox", "SelectedIndex")]
    [InlineData("ComboBox", "Text")] [InlineData("Slider", "Value")] [InlineData("DatePicker", "SelectedDate")] [InlineData("TextBox", "Text")]
    public void What_an_input_is_for(string element, string property) =>
        Assert.Equal("Finding", Verdict("R10", $"<{element} AutomationProperties.Name=\"X\" {property}=\"{{Binding A}}\"/><{element} AutomationProperties.Name=\"X\" {property}=\"{{Binding B}}\"/>"));

    [Theory]
    [InlineData("Visibility")] [InlineData("IsEnabled")] [InlineData("Content")] [InlineData("AutomationProperties.Name")]
    public void A_button_that_changes_is_left_out(string property) =>
        Assert.Equal("", Verdict("R10", $"<Button Content=\"Go\" Click=\"A\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"{property}\" Value=\"False\"/></Trigger></Style.Triggers></Style></Button.Style></Button><Button Content=\"Go\" Click=\"B\"/>"));

    [Fact]
    public void A_button_that_only_changes_its_color_is_not() =>
        Assert.Equal("Finding", Verdict("R10", "<Button Content=\"Go\" Click=\"A\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"Background\" Value=\"Red\"/></Trigger></Style.Triggers></Style></Button.Style></Button><Button Content=\"Go\" Click=\"B\"/>"));

    [Fact]
    public void Items_of_a_list_may_sound_alike() =>
        Assert.Equal("", Verdict("R10", "<ListBox AutomationProperties.Name=\"a\"><ListBoxItem Content=\"x\" Tag=\"1\"/><ListBoxItem Content=\"x\" Tag=\"2\"/></ListBox><TabControl><TabItem Header=\"x\" Tag=\"1\"/><TabItem Header=\"x\" Tag=\"2\"/></TabControl>"));

    [Fact]
    public void A_name_from_the_style_counts() =>
        Assert.Equal("Finding", Verdict("R10", "<StackPanel><StackPanel.Resources><Style TargetType=\"Button\"><Setter Property=\"Content\" Value=\"…\"/></Style></StackPanel.Resources><Button Click=\"A\"/><Button Click=\"B\"/></StackPanel>"));

    // ───────────────────────── NVDA 的念法 ─────────────────────────

    /// <summary>會念描述的是這幾種，一個不多一個不少（清單從表上拿掉一項，別的測試看不出來）。</summary>
    [Fact]
    public void The_controls_whose_description_was_heard() => Assert.Equal(
        ["Button", "CheckBox", "ComboBox", "ListBox", "PasswordBox", "RadioButton", "RepeatButton", "RichTextBox", "Slider", "TextBox", "ToggleButton", "TreeView"],
        Nvda.WithDescription.Order(StringComparer.Ordinal));

    [Fact]
    public void The_controls_that_were_heard() => Assert.Equal(
        ["Button", "CheckBox", "ComboBox", "DataGrid", "DatePicker", "ListBox", "ListView", "MenuItem", "PasswordBox", "RadioButton", "RepeatButton", "RichTextBox", "Slider", "TabItem", "TextBox", "ToggleButton", "TreeView"],
        Nvda.Controls.Order(StringComparer.Ordinal));

    /// <summary>括號對不上的檔案（寫到一半的程式）：照讀，不能當掉。</summary>
    [Theory]
    [InlineData("}}} class Note { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class Note { } class V { public List<Note> Notes { get; } {{{")]
    public void Unbalanced_braces_are_read(string code) => Assert.Single(R8(code));

    [Fact]
    public void Rules_in_chinese()
    {
        using var f = new Folder();
        Assert.Contains("控制項沒有名稱", f.Run("rules", "--lang", "zh-Hant").Out, StringComparison.Ordinal);
        Assert.Contains("Control has no accessible name", f.Run("rules").Out, StringComparison.Ordinal);
        Assert.Equal(2, f.Run("rules", "--lang", "zh").Exit);
        Assert.Equal(2, f.Run("rules", "extra").Exit);
    }
}
