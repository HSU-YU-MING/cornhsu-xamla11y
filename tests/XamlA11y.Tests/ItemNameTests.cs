using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>資料項目念成型別名：集合怎麼宣告、型別有沒有 ToString、樣式有沒有給名稱。</summary>
public sealed class ItemNameTests
{
    private const string NamedList = "<ListBox ItemsSource=\"{Binding Notes}\" AutomationProperties.Name=\"a\"/>";

    private static string[] TypeNamed(string code, string xaml = NamedList, string codeBehind = "", string resources = "")
    {
        using var f = new Folder().Add("Main.xaml", Kit.Window(xaml, resources)).Add("Code.cs", code);
        if (codeBehind.Length > 0) f.Add("Main.xaml.cs", codeBehind);
        return [.. f.Audit().Findings.Where(x => x.Kind == Kinds.ItemTypeName).Select(x => x.Kind.Slug)];
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
    public void Every_collection_is_followed(string type) => Assert.Single(TypeNamed($"class Note {{ }} class V {{ public {type}<Note> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("string")] [InlineData("String")] [InlineData("char")] [InlineData("int")] [InlineData("uint")] [InlineData("long")] [InlineData("ulong")]
    [InlineData("short")] [InlineData("ushort")] [InlineData("byte")] [InlineData("sbyte")] [InlineData("float")] [InlineData("double")] [InlineData("decimal")]
    [InlineData("bool")] [InlineData("Int32")] [InlineData("Int64")] [InlineData("Double")] [InlineData("Boolean")] [InlineData("DateTime")] [InlineData("DateTimeOffset")]
    [InlineData("DateOnly")] [InlineData("TimeOnly")] [InlineData("TimeSpan")] [InlineData("Guid")] [InlineData("Uri")] [InlineData("Version")] [InlineData("CultureInfo")]
    [InlineData("FileInfo")] [InlineData("DirectoryInfo")] [InlineData("IPAddress")] [InlineData("BigInteger")]
    public void Every_readable_type_passes(string type) => Assert.Equal("Pass", Kit.VerdictWithCode("item-names", NamedList, $"class V {{ public List<{type}> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("object")] [InlineData("Object")] [InlineData("ObservableObject")] [InlineData("ObservableRecipient")] [InlineData("ObservableValidator")]
    [InlineData("BindableBase")] [InlineData("PropertyChangedBase")] [InlineData("ReactiveObject")] [InlineData("DependencyObject")]
    [InlineData("MarshalByRefObject")] [InlineData("NotifyPropertyChangedBase")] [InlineData("INotifyPropertyChanged")]
    public void Bases_without_a_ToString_of_their_own(string type) => Assert.Single(TypeNamed($"class Note : {type} {{ }} class V {{ public List<Note> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("ItemContainerStyle")]
    [InlineData("RowStyle")]
    [InlineData("ItemContainerStyleSelector")]
    public void A_style_set_in_code_may_name_the_items(string member) => Assert.Empty(TypeNamed("class Note { } class V { public List<Note> Notes { get; } }",
        "<ListBox x:Name=\"list\" ItemsSource=\"{Binding Notes}\"/>", $"partial class Main {{ void Load() {{ list.{member} = x; }} }}"));

    /// <summary>名字一樣、種類不一樣的型別：不知道（後面那一個的內容也不能算進前面的）。</summary>
    [Theory]
    [InlineData("class Note { } namespace B { struct Note { } }")]
    [InlineData("struct Note { } namespace B { class Note { } }")]
    [InlineData("interface Note { } namespace B { class Note { } }")]
    public void The_same_name_for_two_kinds_is_unknown(string types) => Assert.Empty(TypeNamed(types + " class V { public List<Note> Notes { get; } }"));

    /// <summary>清單的樣式在專案外：合併了量過的佈景就知道它不會設名稱，沒有就不知道。</summary>
    [Fact]
    public void A_list_style_from_a_measured_theme_is_known()
    {
        const string list = "<ListBox ItemsSource=\"{Binding Notes}\" AutomationProperties.Name=\"a\" Style=\"{StaticResource MaterialDesignListBox}\"/>";
        const string code = "class Note { } class V { public List<Note> Notes { get; } }";
        Assert.Equal("NotJudged", Kit.VerdictWithCode("item-names", list, code));
        Assert.Equal("Finding", Kit.VerdictWithCode("item-names", list, code, ("App.xaml", "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")));
    }

    [Theory]
    [InlineData("ListBox")] [InlineData("ListView")] [InlineData("ComboBox")] [InlineData("TreeView")] [InlineData("TabControl")]
    [InlineData("Menu")] [InlineData("ContextMenu")] [InlineData("MenuItem")] [InlineData("DataGrid")]
    public void Every_list_is_checked(string element) => Assert.Single(TypeNamed("class Note { } class V { public List<Note> Notes { get; } }", $"<{element} ItemsSource=\"{{Binding Notes}}\"/>"));

    [Theory]
    [InlineData("XPath=Notes")]
    [InlineData("Notes, Source={x:Static local:X.Y}")]
    [InlineData("Notes[0]")]
    [InlineData("(local:X.Notes)")]
    public void Bindings_that_are_not_a_property_of_the_view_model(string binding) =>
        Assert.Equal("NotJudged", Kit.VerdictWithCode("item-names", $"<ListBox ItemsSource=\"{{Binding {binding}}}\" AutomationProperties.Name=\"a\"/>", "class Note { } class V { public List<Note> Notes { get; } }"));

    [Theory]
    [InlineData("ItemContainerStyleSelector")]
    [InlineData("RowStyleSelector")]
    public void A_style_selector_may_name_the_items(string property) =>
        Assert.Equal("NotJudged", Kit.VerdictWithCode("item-names", $"<DataGrid ItemsSource=\"{{Binding Notes}}\" {property}=\"{{StaticResource x}}\" AutomationProperties.Name=\"a\"/>", "class Note { } class V { public List<Note> Notes { get; } }"));

    [Theory]
    [InlineData("AutomationProperties.Name", "NotJudged")]
    [InlineData("Background", "Finding")]
    public void Only_a_trigger_that_sets_the_name_makes_it_unknown(string property, string expected) =>
        Assert.Equal(expected, Kit.VerdictWithCode("item-names", $"<ListBox ItemsSource=\"{{Binding Notes}}\" AutomationProperties.Name=\"a\"><ListBox.ItemContainerStyle><Style TargetType=\"ListBoxItem\"><Style.Triggers><Trigger Property=\"IsSelected\" Value=\"True\"><Setter Property=\"{property}\" Value=\"x\"/></Trigger></Style.Triggers></Style></ListBox.ItemContainerStyle></ListBox>", "class Note { } class V { public List<Note> Notes { get; } }"));

    /// <summary>寫在裡面的 WPF 元素、函式庫的控制項、自己的控制項不是資料物件。</summary>
    [Theory]
    [InlineData("<TextBlock Text=\"a\"/>")]
    [InlineData("<ListBoxItem Content=\"a\"/>")]
    [InlineData("<hc:Card/>")]
    [InlineData("<local:MyItem/>")]
    public void Elements_inside_a_list_are_not_data(string item) =>
        Assert.Equal("", Kit.VerdictWithCode("item-names", $"<ListBox AutomationProperties.Name=\"a\">{item}</ListBox>", "class MyItem : ListBoxItem { }"));

    [Theory]
    [InlineData("interface INote { } class Note : INote { }")]
    [InlineData("interface Marker { } class Note : Marker { }")]                                  // 名字不像介面的介面
    [InlineData("class Base { } class Note : Base, System.IDisposable { }")]
    [InlineData("class Note<T> { } class Note2 : Note<int> { }")]
    public void Interfaces_and_plain_bases_do_not_change_the_answer(string types) =>
        Assert.Single(TypeNamed(types + $" class V {{ public List<{(types.Contains("Note2", StringComparison.Ordinal) ? "Note2" : "Note")}> Notes {{ get; }} }}"));

    [Theory]
    [InlineData("public List<Note> Notes { get; } public class W { public System.Collections.Generic.IList<Note> Notes { get; } }", 1)]      // 同一個答案
    [InlineData("public List<Note> Notes { get; } public class W { public List<Known> Notes { get; } }", 0)]                               // 一個念得出來、一個念不出來
    public void Properties_with_the_same_name(string members, int expected) =>
        Assert.Equal(expected, TypeNamed($"class Note {{ }} class Known {{ public override string ToString() => \"x\"; }} class V {{ {members} }}").Length);

    /// <summary>括號對不上的檔案（寫到一半的程式）：照讀，不能當掉。</summary>
    [Theory]
    [InlineData("}}} class Note { } class V { public List<Note> Notes { get; } }")]
    [InlineData("class Note { } class V { public List<Note> Notes { get; } {{{")]
    public void Unbalanced_braces_are_read(string code) => Assert.Single(TypeNamed(code));

    [Fact]
    public void A_list_of_plain_objects_is_read_as_type_names()
    {
        var f = Assert.Single(Kit.AuditBody(Kit.NoteList, Kit.Note).Findings, x => x.Kind == Kinds.ItemTypeName);
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
    public void Readable_or_unknown_items_are_not_reported(string code) => Assert.Empty(Kit.SlugsOf(Kit.AuditBody(Kit.NoteList, code), "item-names"));

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
    public void Plain_objects_are_found_however_they_are_declared(string code) => Assert.Equal(["item-type-name"], Kit.SlugsOf(Kit.AuditBody(Kit.NoteList, code), "item-names"));

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
    public void Nothing_names_these(string list, string resources) => Assert.Equal(["item-type-name"], Kit.SlugsOf(Kit.AuditBody(list, Kit.Note, resources), "item-names"));

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
    public void These_are_named_or_unknown(string list, string resources) => Assert.Empty(Kit.SlugsOf(Kit.AuditBody(list, Kit.Note + " public class NoteList : ListBox { }", resources), "item-names"));

    /// <summary>樣式在專案外時每個屬性都讀不出來：不能說它設了 DisplayMemberPath（CLAUDE.md 第 9 條）。</summary>
    [Fact]
    public void DisplayMemberPath_is_only_mentioned_when_it_is_there()
    {
        var f = Assert.Single(Kit.AuditBody("<ListBox ItemsSource=\"{Binding Notes}\"/>", Kit.Note).Findings, x => x.Kind == Kinds.ItemTypeName);
        Assert.DoesNotContain("DisplayMemberPath", f.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip", string.Concat(Kit.AuditBody($"<Button Style=\"{{StaticResource nowhere}}\">{Kit.Icon}</Button><TextBox Style=\"{{StaticResource nowhere}}\"/>").Findings.Select(x => x.Message)), StringComparison.Ordinal);
    }

    /// <summary>範本寫明了型別：就算綁定的屬性看不出來。</summary>
    [Fact]
    public void The_data_type_of_the_template_is_used()
    {
        const string list = "<ListBox ItemsSource=\"{Binding Somewhere}\"><ListBox.ItemTemplate><DataTemplate DataType=\"{x:Type local:Note}\"><TextBlock Text=\"{Binding Title}\"/></DataTemplate></ListBox.ItemTemplate></ListBox>";
        Assert.Contains("the ItemTemplate only changes what is shown", Assert.Single(Kit.AuditBody(list, Kit.Note).Findings, f => f.Kind == Kinds.ItemTypeName).Message, StringComparison.Ordinal);
    }
}
