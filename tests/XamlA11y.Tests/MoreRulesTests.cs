using System.Text.Json;
using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>R8 資料項目的名稱、R9 沒聲音的停留點、R10 名稱一樣、R7 的進度條等；修法文件的範例；OpenACR。</summary>
public class MoreRulesTests
{
    private static readonly JsonElement Facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json"))).RootElement;

    private static AuditResult With(string xaml, string code)
    {
        using var f = new Folder().Add("Main.xaml", xaml).Add("Code.cs", code);
        return f.Audit();
    }

    private static AuditResult Body(string body, string code = "", string resources = "") => With(Kit.Window(body, resources), code);

    private static string[] Slugs(AuditResult r, string rule) => [.. r.Findings.Where(f => f.Kind.Rule == rule).Select(f => f.Kind.Slug)];

    private static string Verdict(AuditResult r, string rule) => string.Join(",", r.Judgements.Where(j => j.Rule == rule).Select(j => j.Kind?.Slug ?? $"{j.Verdict}: {j.Reason}".TrimEnd(' ', ':')));

    // ───────────────────────── WPF 的真值 ─────────────────────────

    public static TheoryData<string> ItemTitles => [.. Facts.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];
    public static TheoryData<string> SilentTitles => [.. Facts.GetProperty("silent").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];

    private static (AuditResult Result, JsonElement Case) Fact(string group, string title)
    {
        var c = Facts.GetProperty(group).EnumerateArray().Single(x => x.GetProperty("title").GetString() == title);
        return (With(c.GetProperty("xaml").GetString()!, Facts.GetProperty("code").GetString()!), c);
    }

    [Theory, MemberData(nameof(ItemTitles))]
    public void Data_items_are_judged_the_way_WPF_names_them(string title)
    {
        var (r, c) = Fact("items", title);
        var typeName = c.GetProperty("name").GetString()!.StartsWith("UiaOracle.", StringComparison.Ordinal);
        var judged = r.Judgements.Where(j => j.Rule == "R8" && j.Verdict is not (Core.Analysis.Verdict.NotJudged or Core.Analysis.Verdict.Blocked)).ToList();
        Assert.NotEmpty(judged);
        Assert.All(judged, j => Assert.Equal(typeName, j.Verdict == Core.Analysis.Verdict.Finding));
    }

    [Theory, MemberData(nameof(SilentTitles))]
    public void Silent_tab_stops_are_the_ones_WPF_has(string title)
    {
        var (r, c) = Fact("silent", title);
        var silent = c.GetProperty("stop").GetBoolean() && !c.GetProperty("inTree").GetBoolean();
        var judged = r.Judgements.Where(j => j.Rule == "R9" && (string?)j.Element.Attribute(Core.Xaml.Markup.XName_) == "p").ToList();
        if (title.Contains("limits Tab", StringComparison.Ordinal)) { Assert.All(judged, j => Assert.Equal(Core.Analysis.Verdict.NotJudged, j.Verdict)); return; }
        Assert.DoesNotContain(judged, j => j.Verdict is Core.Analysis.Verdict.NotJudged or Core.Analysis.Verdict.Blocked);
        Assert.Equal(silent, judged.Any(j => j.Verdict == Core.Analysis.Verdict.Finding));
    }

    // ───────────────────────── R8 ─────────────────────────

    private const string Note = "namespace A; public class Note { public string Title { get; set; } = \"\"; } public class Vm { public ObservableCollection<Note> Notes { get; } = new(); }";
    private const string List = "<ListBox ItemsSource=\"{Binding Notes}\" DisplayMemberPath=\"Title\"/>";

    [Fact]
    public void A_list_of_plain_objects_is_read_as_type_names()
    {
        var f = Assert.Single(Body(List, Note).Findings, x => x.Kind == Kinds.ItemTypeName);
        Assert.Equal(Severity.Warn, f.Severity);
        Assert.Contains("(Note)", f.Message, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath only changes what is shown", f.Message, StringComparison.Ordinal);
    }

    /// <summary>每一種念得出來的、看不出來的：都不能報。</summary>
    [Theory]
    [InlineData("public class Note { public override string ToString() => \"x\"; } class V { public List<Note> Notes { get; } }")]
    [InlineData("public record Note(string Title); class V { public List<Note> Notes { get; } }")]
    [InlineData("public record struct Note(string Title); class V { public List<Note> Notes { get; } }")]
    [InlineData("public enum Note { A } class V { public List<Note> Notes { get; } }")]
    [InlineData("class V { public List<string> Notes { get; } }")]
    [InlineData("class V { public string[] Notes { get; } }")]
    [InlineData("class V { public List<int> Notes { get; } }")]
    [InlineData("class Base { public override string ToString() => \"x\"; } class Note : Base { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class Note : SomeLibraryBase { } class V { public List<Note> Notes { get; } }")]                       // 基底看不到
    [InlineData("interface Note { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class V { public List<LibraryType> Notes { get; } }")]
    [InlineData("class Note { } class V { public ICollectionView Notes { get; } }")]
    [InlineData("class Note { } class V { public object Notes { get; } }")]
    [InlineData("class Note { } class V { public List<Note> Notes { get; } } class W { public List<string> Notes { get; } }")]      // 同名的屬性兩種
    [InlineData("class Note { } class V { public List<Note> Notes { get; } } class W { public object Notes { get; } }")]
    [InlineData("class Note { } class V { public Dictionary<string, Note> Notes { get; } }")]
    [InlineData("class Note { } class V { public List<List<Note>> Notes { get; } }")]
    [InlineData("class Note { } struct X { } namespace B { interface Note { } } class V { public List<Note> Notes { get; } }")]       // 同名的型別兩種
    [InlineData("partial class Note { } partial class Note { public override string ToString() => \"x\"; } class V { public List<Note> Notes { get; } }")]
    [InlineData("// class Note { } public List<Note> Notes { get; }")]
    [InlineData("class Note { override public string ToString() => \"x\"; } class V { public List<Note> Notes { get; } }")]                 // 修飾詞順序不限
    [InlineData("class Note { public override global::System.String ToString() => \"x\"; } class V { public List<Note> Notes { get; } }")]
    [InlineData("[ToString] class Note { } class V { public List<Note> Notes { get; } }")]                                              // Fody
    [InlineData("abstract class Note { } class A : Note { public override string ToString() => \"a\"; } class V { public List<Note> Notes { get; } }")]   // 裡面放的是子類別
    public void Readable_or_unknown_items_are_not_reported(string code) => Assert.Empty(Slugs(Body(List, code), "R8"));

    [Theory]
    [InlineData("class Note { } class V { public IReadOnlyList<Note>? Notes { get; } }")]
    [InlineData("class Note { } class V { public Note[] Notes { get; } }")]
    [InlineData("class Note { } class V { public System.Collections.ObjectModel.ObservableCollection<Note> Notes { get; } }")]
    [InlineData("class Note { } class V { public ObservableCollection<Note> Notes => _notes; }")]
    [InlineData("class Note : ObservableObject, INotifyDataErrorInfo { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class Base : BindableBase { } class Note : Base { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class Note { } class NoteList : ObservableCollection<Note> { } class V { public NoteList Notes { get; } }")]
    [InlineData("class Note { } class V { [ObservableProperty] private ObservableCollection<Note> _notes = new(); }")]
    [InlineData("class Note { } class V { [ObservableProperty] [NotifyPropertyChangedFor(nameof(X))] private ObservableCollection<Note> _notes = new(); }")]
    [InlineData("struct Note { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class A { class Note { } } class V { public List<A.Note> Notes { get; } }")]
    [InlineData("class Note { class Inner { public override string ToString() => \"x\"; } } class V { public List<Note> Notes { get; } }")]    // 覆寫的是裡面的類別
    [InlineData("class Note { public new string ToString() => \"x\"; } class V { public List<Note> Notes { get; } }")]
    [InlineData("abstract class Note { } class A : Note { } class V { public List<Note> Notes { get; } }")]
    public void Plain_objects_are_found_however_they_are_declared(string code) => Assert.Equal(["item-type-name"], Slugs(Body(List, code), "R8"));

    [Theory]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\"/>", "")]
    [InlineData("<ListView ItemsSource=\"{Binding Path=Vm.Notes}\"/>", "")]
    [InlineData("<ComboBox ItemsSource=\"{Binding Notes}\" AutomationProperties.Name=\"a\"/>", "")]
    [InlineData("<DataGrid ItemsSource=\"{Binding Notes}\"/>", "")]
    [InlineData("<TreeView ItemsSource=\"{Binding Notes}\"/>", "")]
    [InlineData("<TabControl ItemsSource=\"{Binding Notes}\"/>", "")]
    [InlineData("<Menu ItemsSource=\"{Binding Notes}\"/>", "")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"ListBoxItem\"><Setter Property=\"ToolTip\" Value=\"{Binding Title}\"/></Style>")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"ListBoxItem\"/><Style TargetType=\"ListBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\"/></Style>")] // 明寫的整個取代隱含的
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"ListBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"\"/></Style>")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"ListBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding}\"/></Style>")]   // 綁整個物件還是 ToString()
    [InlineData("<ListBox><local:Note Title=\"a\"/></ListBox>", "")]                                                               // 直接寫在裡面
    [InlineData("<ListBox><ListBox.Items><local:Note Title=\"a\"/></ListBox.Items></ListBox>", "")]
    public void Nothing_names_these(string list, string resources) => Assert.Equal(["item-type-name"], Slugs(Body(list, Note, resources), "R8"));

    [Theory]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"ListBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\"/></Style>")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\"/>", "<Style TargetType=\"ListBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\"/></Style>")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"b\" TargetType=\"ListBoxItem\"><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\"/></Style><Style x:Key=\"s\" TargetType=\"ListBoxItem\" BasedOn=\"{StaticResource b}\"/>")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource nowhere}\"/>", "")]             // 樣式在專案外
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" ItemContainerStyleSelector=\"{StaticResource x}\"/>", "")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes, Converter={StaticResource c}}\"/>", "")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes, ElementName=x}\"/>", "")]
    [InlineData("<ListBox ItemsSource=\"{StaticResource Notes}\"/>", "")]
    [InlineData("<ListBox ItemsSource=\"{Binding}\"/>", "")]
    [InlineData("<ListBox/>", "")]
    [InlineData("<ListBox ItemsSource=\"{Binding Notes}\" Style=\"{StaticResource nowhere}\"/>", "")]                                 // 清單的樣式在專案外：可能設了容器樣式
    [InlineData("<ItemsControl ItemsSource=\"{Binding Notes}\"/>", "")]                                                          // 沒有項目容器
    [InlineData("<local:NoteList ItemsSource=\"{Binding Notes}\"/>", "")]
    [InlineData("<DataGrid ItemsSource=\"{Binding Notes}\" RowStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"DataGridRow\"><Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\"/></Style>")]
    [InlineData("<TabControl ItemsSource=\"{Binding Notes}\" ItemContainerStyle=\"{StaticResource s}\"/>", "<Style x:Key=\"s\" TargetType=\"TabItem\"><Setter Property=\"Header\" Value=\"{Binding Title}\"/></Style>")]
    public void These_are_named_or_unknown(string list, string resources) => Assert.Empty(Slugs(Body(list, Note + " public class NoteList : ListBox { }", resources), "R8"));

    /// <summary>樣式在專案外時每個屬性都讀不出來：不能說它設了 DisplayMemberPath（CLAUDE.md 第 9 條）。</summary>
    [Fact]
    public void DisplayMemberPath_is_only_mentioned_when_it_is_there()
    {
        var f = Assert.Single(Body("<ListBox ItemsSource=\"{Binding Notes}\"/>", Note).Findings, x => x.Kind == Kinds.ItemTypeName);
        Assert.DoesNotContain("DisplayMemberPath", f.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip", string.Concat(Body($"<Button Style=\"{{StaticResource nowhere}}\">{Kit.Icon}</Button><TextBox Style=\"{{StaticResource nowhere}}\"/>").Findings.Select(x => x.Message)), StringComparison.Ordinal);
    }

    /// <summary>範本寫明了型別：就算綁定的屬性看不出來。</summary>
    [Fact]
    public void The_data_type_of_the_template_is_used()
    {
        const string list = "<ListBox ItemsSource=\"{Binding Somewhere}\"><ListBox.ItemTemplate><DataTemplate DataType=\"{x:Type local:Note}\"><TextBlock Text=\"{Binding Title}\"/></DataTemplate></ListBox.ItemTemplate></ListBox>";
        Assert.Contains("the ItemTemplate only changes what is shown", Assert.Single(Body(list, Note).Findings, f => f.Kind == Kinds.ItemTypeName).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hostile_source_file_does_not_hang()
    {
        var nested = string.Concat(Enumerable.Range(0, 4000).Select(i => $"class C{i} : C{i + 1} {{ ")) + string.Concat(Enumerable.Repeat("}", 4000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Body(List, nested + " class V { public List<C0> Notes { get; } }" + new string('<', 20000) + "public " + new string('a', 50000));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
    }

    /// <summary>獨立審查：40 KB 跑 95 秒、301 KB 跑不完；1 MB 的巢狀型別 28 秒。</summary>
    [Fact]
    public void Hostile_types_take_linear_time()
    {
        var bases = "class V { public L0 Notes { get; } } " + string.Concat(Enumerable.Range(0, 300).Select(i => i == 0 ? "class L0 : L1 { } " : $"class L{i} : {string.Join(",", Enumerable.Range(0, i).Select(x => "L" + x))} {{ }} "));
        var nested = string.Concat(Enumerable.Range(0, 70000).Select(i => "class A{"));
        var overrides = string.Concat(Enumerable.Range(0, 40000).Select(i => "class A{override string "));
        var lists = string.Concat(Enumerable.Repeat("<ListBox ItemsSource=\"{Binding Notes}\"/>", 30));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var code in (string[])[bases, nested, overrides]) Body(lists, code);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), clock.Elapsed.ToString());
    }

    // ───────────────────────── R9 ─────────────────────────

    [Theory]
    [InlineData("<ContentControl Content=\"{Binding Page}\"/>")]
    [InlineData("<Border Focusable=\"True\"/>")]
    [InlineData("<local:Fancy/>")]
    public void Silent(string xaml) => Assert.Equal(["silent-tab-stop"], Slugs(Body(xaml, "class Fancy : ContentControl { }"), "R9"));

    [Theory]
    [InlineData("<ContentControl Focusable=\"False\"/>")]
    [InlineData("<ContentControl IsTabStop=\"False\"/>")]
    [InlineData("<ContentControl KeyboardNavigation.IsTabStop=\"False\"/>")]
    [InlineData("<ContentControl Visibility=\"Collapsed\"/>")]
    [InlineData("<ContentControl IsEnabled=\"False\"/>")]
    [InlineData("<ContentControl Focusable=\"{Binding X}\"/>")]
    [InlineData("<ContentControl GotKeyboardFocus=\"OnFocus\"/>")]
    [InlineData("<ContentControl Style=\"{StaticResource nowhere}\"/>")]
    [InlineData("<ContentControl><ContentControl.Style><Style TargetType=\"ContentControl\"><Style.Triggers><Trigger Property=\"IsEnabled\" Value=\"True\"><Setter Property=\"Focusable\" Value=\"False\"/></Trigger></Style.Triggers></Style></ContentControl.Style></ContentControl>")]
    [InlineData("<StackPanel KeyboardNavigation.TabNavigation=\"None\"><ContentControl/></StackPanel>")]
    [InlineData("<ListBox AutomationProperties.Name=\"a\"><ListBoxItem><ContentControl/></ListBoxItem></ListBox>")]
    [InlineData("<Border/>")]
    [InlineData("<UserControl Focusable=\"True\" IsTabStop=\"True\"/>")]
    [InlineData("<TextBlock Focusable=\"True\" Text=\"a\"/>")]
    [InlineData("<hc:Card/>")]
    [InlineData("<local:Peered/>")]
    [InlineData("<local:Quiet/>")]
    [InlineData("<local:Quieter/>")]
    [InlineData("<local:IsButton/>")]
    [InlineData("<local:Elsewhere/>")]
    [InlineData("<local:Forwards/>")]
    [InlineData("<Popup Focusable=\"True\"/>")]
    [InlineData("<Button Content=\"a\"><Button.ToolTip><ContentControl/></Button.ToolTip></Button>")]
    [InlineData("<StackPanel IsEnabled=\"False\"><ContentControl/></StackPanel>")]
    [InlineData("<Border><Border.Background><VisualBrush><VisualBrush.Visual><ContentControl/></VisualBrush.Visual></VisualBrush></Border.Background></Border>")]
    [InlineData("<ContentControl><ContentControl.Visibility>Collapsed</ContentControl.Visibility></ContentControl>")]
    [InlineData("<DataGrid AutomationProperties.Name=\"a\"><DataGrid.RowDetailsTemplate><DataTemplate><ContentControl/></DataTemplate></DataGrid.RowDetailsTemplate></DataGrid>")]
    [InlineData("<Button Content=\"a\"><Button.Resources><ContentControl x:Key=\"k\"/></Button.Resources></Button>")]
    public void Not_silent_or_not_known(string xaml) => Assert.Empty(Slugs(Body(xaml,
        "class Peered : Control { protected override AutomationPeer OnCreateAutomationPeer() => null; } class Quiet : Control { Quiet() { Focusable = false; } } class Quieter : Quiet { } class IsButton : Button { } class Forwards : Control { protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { inner.Focus(); } } class Elsewhere : LibraryControl { }"), "R9"));

    /// <summary>自己的類別：修的是類別，報一次；它的樣式不讓 Tab 進來就不報（命名空間怎麼寫都算）。</summary>
    [Fact]
    public void A_class_of_your_own_is_reported_once_at_its_style()
    {
        using var f = new Folder().Add("A.xaml", Kit.Window("<local:Fancy/><local:Fancy/>")).Add("B.xaml", Kit.Window("<local:Fancy/>")).Add("C.cs", "class Fancy : Control { }");
        var once = Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop);
        Assert.Equal("A.xaml", once.File);
        f.Add("Themes/Generic.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:c=\"clr-namespace:Other\"><Style TargetType=\"c:Fancy\"><Setter Property=\"Height\" Value=\"9\"/></Style></ResourceDictionary>");
        Assert.Equal("Themes/Generic.xaml", Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop).File);
        var key = Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop).BaselineKey;
        f.Add("Themes/Generic.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:c=\"clr-namespace:Other\"><Style TargetType=\"c:Fancy\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Style></ResourceDictionary>");
        Assert.DoesNotContain(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop);
        // 函式庫作者的寫法：值在 BasedOn 那一份
        f.Add("Themes/Generic.xaml", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:c=\"clr-namespace:Other\"><Style x:Key=\"b\" TargetType=\"c:Fancy\"><Setter Property=\"Focusable\" Value=\"False\"/></Style><Style TargetType=\"c:Fancy\" BasedOn=\"{StaticResource b}\"/></ResourceDictionary>");
        Assert.DoesNotContain(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop);
        // 多一個用它的檔案、樣式拿掉：baseline 的鍵不變
        File.Delete(f.At("Themes/Generic.xaml"));
        f.Add("0.xaml", Kit.Window("<local:Fancy/>"));
        Assert.Equal(key, Assert.Single(f.Audit().Findings, x => x.Kind == Kinds.SilentTabStop).BaselineKey);
    }

    /// <summary>壓掉的是那一個用法，不是整個類別。</summary>
    [Fact]
    public void Ignoring_one_use_does_not_hide_the_class()
    {
        using var f = new Folder().Add("A.xaml", Kit.Window("<!-- xamla11y-ignore: decorative here --><local:Fancy/>")).Add("B.xaml", Kit.Window("<local:Fancy/>")).Add("C.cs", "class Fancy : Control { }");
        var r = f.Audit();
        Assert.Equal("B.xaml", Assert.Single(r.Findings, x => x.Kind == Kinds.SilentTabStop).File);
        Assert.Equal(1, r.Rules.Single(x => x.Rule == "R9").Suppressed);
    }

    [Fact]
    public void Name_on_something_without_a_peer_is_pointed_out() =>
        Assert.Contains("its AutomationProperties.Name is not used", Assert.Single(Body("<ContentControl AutomationProperties.Name=\"a\"/>").Findings).Message, StringComparison.Ordinal);

    // ───────────────────────── R10 ─────────────────────────

    [Theory]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\"/><Button Content=\"Edit\" Command=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"_Edit\" Click=\"A\"/><Button Content=\"Edit\" Click=\"B\"/>")]
    [InlineData("<Button Content=\"{DynamicResource s}\" Click=\"A\"/><Button Content=\"{DynamicResource s}\" Click=\"B\"/>")]
    [InlineData("<Button Click=\"A\"><TextBlock Text=\"Edit\"/></Button><Button Click=\"B\">Edit</Button>")]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\" CommandParameter=\"1\"/><Button Content=\"Edit\" Command=\"{Binding A}\" CommandParameter=\"2\"/>")]
    [InlineData("<TextBox AutomationProperties.Name=\"X\" Text=\"{Binding A}\"/><TextBox AutomationProperties.Name=\"X\" Text=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"Edit\" Click=\"Go\" Tag=\"in\"/><Button Content=\"Edit\" Click=\"Go\" Tag=\"out\"/>")]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\" CommandTarget=\"{Binding X}\"/><Button Content=\"Edit\" Command=\"{Binding A}\" CommandTarget=\"{Binding Y}\"/>")]
    public void Same_name_different_action(string xaml) => Assert.Equal(["same-name"], Slugs(Body(xaml, resources: "<sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"s\">Edit</sys:String>"), "R10"));

    [Theory]
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\"/><Button Content=\"Edit\" Command=\"{Binding A}\"/>")]           // 同一個動作
    [InlineData("<Button Content=\"Edit\"/><Button Content=\"Edit\"/>")]                                                            // 做什麼看不出來
    [InlineData("<Button Content=\"Edit\" Command=\"{Binding A}\"/><Button Content=\"Delete\" Command=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"Edit\" Click=\"A\" Visibility=\"{Binding V}\"/><Button Content=\"Edit\" Click=\"B\"/>")]         // 輪流出現
    [InlineData("<Button Content=\"Edit\" Click=\"A\"/><CheckBox Content=\"Edit\" IsChecked=\"{Binding B}\"/>")]                     // 型別不同，念起來不一樣
    [InlineData("<StackPanel><Button Content=\"Edit\" Click=\"A\"/></StackPanel><StackPanel><Button Content=\"Edit\" Click=\"B\"/></StackPanel>")]
    [InlineData("<Button Content=\"{Binding N}\" Click=\"A\"/><Button Content=\"{Binding N}\" Click=\"B\"/>")]
    [InlineData("<Button Content=\"{DynamicResource s}\" Click=\"A\"/><Button Content=\"{DynamicResource t}\" Click=\"B\"/>")]       // 不同的鍵剛好同一個字：別的語言可能不同
    [InlineData("<TextBlock x:Name=\"l\" Text=\"In\"/><TextBlock x:Name=\"m\" Text=\"Out\"/><CheckBox Content=\"On\" IsChecked=\"{Binding A}\" AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"/><CheckBox Content=\"On\" IsChecked=\"{Binding B}\" AutomationProperties.LabeledBy=\"{Binding ElementName=m}\"/>")]
    [InlineData("<Button Click=\"A\"><TextBlock>x<Span>y</Span></TextBlock></Button><Button Content=\"x\" Click=\"B\"/>")]
    [InlineData("<Button AutomationProperties.Name=\"a_b\" Content=\"1\" Click=\"A\"/><Button Content=\"ab\" Click=\"B\"/>")]                  // Name 的底線不是存取鍵
    [InlineData("<Button Content=\"Edit\" Click=\"A\"><Button.Visibility><Binding Path=\"V\"/></Button.Visibility></Button><Button Content=\"Edit\" Click=\"B\"/>")]
    [InlineData("<Button Content=\"Go\" Click=\"A\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><DataTrigger Binding=\"{Binding R}\" Value=\"True\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button><Button Content=\"Go\" Click=\"B\"/>")]
    [InlineData("<RadioButton Content=\"Yes\" GroupName=\"1\" IsChecked=\"{Binding A}\"/><RadioButton Content=\"Yes\" GroupName=\"2\" IsChecked=\"{Binding B}\"/>")]
    [InlineData("<Button Content=\"Edit\" Click=\"A\" IsEnabled=\"False\"/><Button Content=\"Edit\" Click=\"B\"/>")]
    public void Not_the_same_name(string xaml) => Assert.Empty(Slugs(Body(xaml, resources: "<sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"s\">Edit</sys:String><sys:String xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" x:Key=\"t\">Edit</sys:String>"), "R10"));

    /// <summary>一組報一次，多加一顆同名的不會變成新的。</summary>
    [Fact]
    public void One_finding_per_group_and_a_stable_key()
    {
        const string two = "<Button Content=\"Edit\" Click=\"A\"/><Button Content=\"Edit\" Click=\"B\"/>";
        var before = Assert.Single(Body(two).Findings, f => f.Kind == Kinds.SameName);
        var after = Assert.Single(Body("<Button Content=\"Edit\" Click=\"C\"/>" + two).Findings, f => f.Kind == Kinds.SameName);
        Assert.Equal(before.BaselineKey, after.BaselineKey);
        Assert.Equal(Severity.Info, before.Severity);
        Assert.Contains("3 <Button> here may sound alike", after.Message, StringComparison.Ordinal);
    }

    // ───────────────────────── R7：進度條等 ─────────────────────────

    [Theory]
    [InlineData("<ProgressBar/>", "widget-no-name:Warn")]
    [InlineData("<Calendar/>", "widget-no-name:Warn")]
    [InlineData("<Menu/>", "widget-no-name:Info")]
    [InlineData("<ToolBar/>", "widget-no-name:Info")]
    [InlineData("<ProgressBar AutomationProperties.Name=\"a\"/>", "")]
    [InlineData("<ToolBar Header=\"a\"/>", "")]
    public void Widgets(string xaml, string expected) => Assert.Equal(expected, string.Join(",", Body(xaml).Findings.Where(f => f.Kind.Rule == "R7").Select(f => $"{f.Kind.Slug}:{f.Severity}")));

    // ───────────────────────── 修法文件 ─────────────────────────

    /// <summary>文件上「原本」的寫法真的會被報，「改成」的真的不會。</summary>
    [Theory]
    [InlineData("docs/fixes.md", "missing-name")]
    [InlineData("docs/fixes.md", "text-not-in-name")]
    [InlineData("docs/fixes.md", "meaningless-name")]
    [InlineData("docs/fixes.md", "symbol-name")]
    [InlineData("docs/fixes.md", "input-no-name")]
    [InlineData("docs/fixes.md", "mouse-only")]
    [InlineData("docs/fixes.md", "list-no-name")]
    [InlineData("docs/fixes.md", "item-type-name")]
    [InlineData("docs/fixes.md", "silent-tab-stop")]
    [InlineData("docs/fixes.md", "same-name")]
    [InlineData("docs/修法.md", "missing-name")]
    [InlineData("docs/修法.md", "text-not-in-name")]
    [InlineData("docs/修法.md", "meaningless-name")]
    [InlineData("docs/修法.md", "symbol-name")]
    [InlineData("docs/修法.md", "input-no-name")]
    [InlineData("docs/修法.md", "mouse-only")]
    [InlineData("docs/修法.md", "list-no-name")]
    [InlineData("docs/修法.md", "item-type-name")]
    [InlineData("docs/修法.md", "silent-tab-stop")]
    [InlineData("docs/修法.md", "same-name")]
    public void The_examples_do_what_they_say(string file, string slug)
    {
        var section = Regex.Split(File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)).Replace("\r\n", "\n", StringComparison.Ordinal), "^## ", RegexOptions.Multiline).Single(s => s.StartsWith(slug + "\n", StringComparison.Ordinal));
        var xml = Regex.Match(section, "```xml\n(?<x>.*?)```", RegexOptions.Singleline).Groups["x"].Value;
        var after = Regex.Match(xml, "<!-- (?:after|改成)[^>]*-->");
        Assert.True(after.Success);
        const string icon = "<Geometry x:Key=\"TrashIcon\">M0,0 L9,9 Z</Geometry>";
        // 頁籤要放在 TabControl 裡才是完整的畫面，判定不受影響
        string[] Run(string x) => [.. Body(x, Note + " class F { public List<string> Files { get; } }", icon).Findings.Select(f => f.Kind.Slug).Distinct()];
        Assert.Contains(slug, Run(xml[..after.Index]));
        var fixedOnes = Run(xml[(after.Index + after.Length)..]);
        Assert.DoesNotContain(slug, fixedOnes);
        Assert.Empty(fixedOnes.Except(slug == "item-type-name" ? ["list-no-name"] : []));
    }

    [Theory]
    [InlineData("docs/fixes.md")]
    [InlineData("docs/修法.md")]
    public void Every_finding_has_a_section(string file)
    {
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        foreach (var k in Kinds.All) Assert.Contains("\n## " + k.Slug, text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(Kinds.All.Count, Regex.Count(text, "^## ", RegexOptions.Multiline));
    }

    [Fact]
    public void The_reports_link_to_the_section()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button>");
        Assert.Contains("/docs/fixes.md#missing-name\"", SarifReport.Create(r.Findings, "1.0"), StringComparison.Ordinal);
        Assert.Contains("/docs/fixes.md#missing-name\"", JsonReport.Create(r, r.Findings, 0, "1.0", 1), StringComparison.Ordinal);
        Assert.Contains("href=\"https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/fixes.md#missing-name\"", HtmlReport.Create(r, r.Findings, 1, 0, null, "1.0"), StringComparison.Ordinal);
        Assert.Contains("docs/%E4%BF%AE%E6%B3%95.md#missing-name", HtmlReport.Create(r, r.Findings, 1, 0, null, "1.0", Lang.ZhHant), StringComparison.Ordinal);
        Assert.Equal("修法", Uri.UnescapeDataString("%E4%BF%AE%E6%B3%95"));
    }

    // ───────────────────────── OpenACR ─────────────────────────

    [Fact]
    public void The_OpenACR_draft_only_says_what_is_known()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><TextBox/><ListBox/>");
        var yaml = OpenAcrReport.Create(r, "My \"App\"", "1.0", new DateOnly(2026, 11, 24));
        Assert.Contains("title: \"My \\\"App\\\" Accessibility Conformance Report (draft)\"", yaml, StringComparison.Ordinal);
        Assert.Contains("catalog: \"2.5-edition-wcag-2.2-en\"", yaml, StringComparison.Ordinal);
        Assert.Equal(["1.1.1", "1.3.1", "4.1.2"], Regex.Matches(yaml, "num: \"(?<n>[^\"]+)\"").Select(m => m.Groups["n"].Value));
        // 沒找到問題不是 supports；警告（清單）不寫
        Assert.DoesNotContain("level: supports", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("list-no-name", yaml, StringComparison.Ordinal);
        Assert.All(Regex.Matches(yaml, "level: (?<l>.+)").Select(m => m.Groups["l"].Value), l => Assert.Equal("partially-supports", l));
        Assert.Contains("chapters: {}", OpenAcrReport.Create(Kit.Audit("<Button Content=\"a\"/>"), "a", "1.0", new DateOnly(2026, 11, 24)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wcag22", "2.5-edition-wcag-2.2-en", false)]
    [InlineData("wcag21", "2.4-edition-wcag-2.1-en", false)]
    [InlineData("section508", "2.5-edition-wcag-2.0-508-en", true)]
    [InlineData("wcag20", "2.5-edition-wcag-2.0-508-en", true)]
    [InlineData("en301549", "2.4-edition-wcag-2.1-508-eu-en", true)]
    [InlineData("en301549-v4", "2.5-edition-wcag-2.2-508-eu-en", true)]
    public void The_OpenACR_catalog_follows_the_standard(string id, string catalog, bool software)
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button>", options: new AuditOptions { Scope = new Scope(Standard.ById(id)!, "AA") });
        var yaml = OpenAcrReport.Create(r, "a", "1.0", new DateOnly(2026, 11, 24));
        Assert.Contains($"catalog: \"{catalog}\"", yaml, StringComparison.Ordinal);
        Assert.Equal(software, yaml.Contains("num: \"502.3.1\"", StringComparison.Ordinal));
        Assert.Equal(id.StartsWith("en", StringComparison.Ordinal), yaml.Contains("num: \"11.5.2.5\"", StringComparison.Ordinal));
        Assert.DoesNotContain("%", yaml, StringComparison.Ordinal);
    }

    /// <summary>baseline 蓋住的還是問題。</summary>
    [Fact]
    public void The_OpenACR_draft_includes_what_is_in_the_baseline()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window($"<Button>{Kit.Icon}</Button>"));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        Assert.Equal(0, f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--openacr", f.At("a.yaml")).Exit);
        Assert.Contains("missing-name", File.ReadAllText(f.At("a.yaml")), StringComparison.Ordinal);
    }
}
