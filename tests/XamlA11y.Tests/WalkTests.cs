using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Walk;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Tests;

/// <summary>xamla11y walk：Tab 依序停在哪裡。WPF 的順序在 uia-facts.json 的 walk（真的視窗走出來的）。</summary>
public sealed class WalkTests
{
    private static readonly JsonElement Facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json"))).RootElement;

    public static TheoryData<string> Titles => [.. Facts.GetProperty("walk").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];

    private static string Name(WalkStop s) => s.Control is "Expander" or "DatePicker" or "Calendar" || s.Control == "ComboBox" && (string?)s.Element.Attribute("IsEditable") == "True" ? "*" : Markup.NameOf(s.Element) ?? s.Control;

    [Theory, MemberData(nameof(Titles))]
    public void The_order_is_the_one_WPF_has(string title)
    {
        var c = Facts.GetProperty("walk").EnumerateArray().Single(x => x.GetProperty("title").GetString() == title);
        var project = Project.FromText(c.GetProperty("xaml").GetString()!);
        var real = c.GetProperty("stops").EnumerateArray().Select(x => x.GetString()!).Select(s => s is "HeaderSite" or "PART_TextBox" or "PART_EditableTextBox" or "CalendarDayButton" ? "*" : s);
        var walk = TabWalk.Of(project, project.Docs[0]);
        // 「不確定」的可能停、可能不停：確定的那些，順序要在真的裡面
        if (walk.Stops.All(s => s.Kind == WalkKind.Stop)) { Assert.Equal(real, walk.Stops.Select(Name)); return; }
        Assert.Contains(title, (string[])["a trigger of the style hides it", "a trigger of the style shows it", "a trigger of the style disables it", "bound IsEnabled"]);
        var sure = walk.Stops.Where(s => s.Kind == WalkKind.Stop).Select(Name).ToList();
        Assert.Equal(sure, real.Where(sure.Contains));
        Assert.Subset(walk.Stops.Select(Name).ToHashSet(), real.ToHashSet());
    }

    private static string Walk(string body, string code = "", params (string File, string Text)[] more)
    {
        using var f = Kit.Files(body, code);
        foreach (var (file, text) in more) f.Add(file, text);
        var project = Project.Load(f.Root, []);
        return string.Join(" ", TabWalk.Of(project, project.Docs.Single(d => d.File == "Main.xaml")).Stops.Select(s => (s.Kind switch { WalkKind.Stop => "", WalkKind.Maybe => "?", _ => "??" }) + (Markup.NameOf(s.Element) ?? s.Element.Name.LocalName)));
    }

    /// <summary>要看執行時的標成不確定，不認得的說不認得 —— 不能當成沒有。</summary>
    [Theory]
    [InlineData("<Button x:Name=\"a\" Visibility=\"{Binding V}\"/>", "", "?a")]
    [InlineData("<StackPanel Visibility=\"{Binding V}\"><Button x:Name=\"a\"/></StackPanel>", "", "?a")]
    [InlineData("<Button x:Name=\"a\" Focusable=\"{Binding V}\"/>", "", "?a")]
    [InlineData("<Button x:Name=\"a\" Visibility=\"Collapsed\"/>", "", "")]
    [InlineData("<Button x:Name=\"a\" Visibility=\"Collapsed\"/>", "class M { void L() { a.Visibility = 0; } }", "?a")]
    [InlineData("<Button x:Name=\"a\" IsEnabled=\"False\"/>", "class M { void L() { a.IsEnabled = true; } }", "?a")]
    [InlineData("<ListBox x:Name=\"a\" ItemsSource=\"{Binding N}\"/>", "", "?a")]
    [InlineData("<ListBox x:Name=\"a\"/>", "", "")]
    [InlineData("<ListBox x:Name=\"a\"/>", "class M { void L() { a.ItemsSource = x; } }", "?a")]
    [InlineData("<DataGrid x:Name=\"a\" ItemsSource=\"{Binding N}\"/>", "", "?a")]
    [InlineData("<TabControl ItemsSource=\"{Binding N}\" x:Name=\"a\"/>", "", "?a")]
    [InlineData("<Menu ItemsSource=\"{Binding N}\" x:Name=\"a\"/>", "", "?a")]
    [InlineData("<ItemsControl x:Name=\"a\" Focusable=\"False\" ItemsSource=\"{Binding N}\"/>", "", "??a")]
    [InlineData("<ContentControl x:Name=\"a\" Content=\"{Binding N}\"/>", "", "a ??a")]
    [InlineData("<Frame x:Name=\"a\" Source=\"P.xaml\"/>", "", "a ??a")]
    [InlineData("<local:Fancy x:Name=\"a\"/><Button x:Name=\"b\"/>", "", "??a b")]
    [InlineData("<local:Fancy x:Name=\"a\"><Button x:Name=\"b\"/></local:Fancy>", "", "??a b")]
    [InlineData("<Expander x:Name=\"a\" IsExpanded=\"{Binding V}\"><Button x:Name=\"b\"/></Expander>", "", "a ?b")]
    [InlineData("<Popup><Button x:Name=\"a\"/></Popup>", "", "")]
    [InlineData("<Button x:Name=\"a\" IsEnabled=\"{Binding C}\"/>", "", "?a")]
    [InlineData("<Button x:Name=\"a\"><Button.IsEnabled><Binding Path=\"C\"/></Button.IsEnabled></Button>", "", "?a")]
    [InlineData("<Button x:Name=\"a\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><DataTrigger Binding=\"{Binding V}\" Value=\"1\"><Setter Property=\"IsEnabled\" Value=\"False\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button>", "", "?a")]
    [InlineData("<Button x:Name=\"a\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/><Style.Triggers><DataTrigger Binding=\"{Binding V}\" Value=\"1\"><Setter Property=\"Visibility\" Value=\"Visible\"/></DataTrigger></Style.Triggers></Style></Button.Style></Button>", "", "?a")]
    [InlineData("<Menu><MenuItem x:Name=\"a\" Header=\"a\" IsEnabled=\"{Binding C}\"/><MenuItem x:Name=\"b\" Header=\"b\" Visibility=\"Collapsed\"/></Menu>", "", "?a")]
    [InlineData("<TabControl SelectedIndex=\"{Binding I}\"><TabItem x:Name=\"a\" Header=\"a\"><Button x:Name=\"b\"/></TabItem></TabControl>", "", "?a ?b")]
    [InlineData("<TabControl x:Name=\"t\"><TabItem x:Name=\"a\" Header=\"a\"/></TabControl>", "class M { void L() { t.SelectedIndex = 1; } }", "?a")]
    [InlineData("<ListBox SelectedItem=\"{Binding I}\"><ListBoxItem x:Name=\"a\"/><ListBoxItem x:Name=\"b\"/></ListBox>", "", "?a")]
    [InlineData("<Menu><MenuItem x:Name=\"a\" Header=\"a\" Focusable=\"False\"/><MenuItem x:Name=\"b\" Header=\"b\"/></Menu>", "", "b")]
    [InlineData("<TreeView x:Name=\"a\"><TreeViewItem x:Name=\"b\"><TreeViewItem.Header><Button x:Name=\"c\"/></TreeViewItem.Header></TreeViewItem></TreeView>", "", "a")]
    [InlineData("<TextBox x:Name=\"a\"><TextBox.Text>x</TextBox.Text></TextBox><ComboBox x:Name=\"b\"><Button x:Name=\"c\"/></ComboBox>", "", "a b")]
    [InlineData("<Button x:Name=\"a\"><Button.Style><Style TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"Focusable\" Value=\"False\"/></Trigger></Style.Triggers></Style></Button.Style></Button>", "", "?a")]
    [InlineData("<StackPanel KeyboardNavigation.TabNavigation=\"Local\"><Button x:Name=\"a\"/><StackPanel KeyboardNavigation.TabNavigation=\"Cycle\"><Button x:Name=\"b\"/></StackPanel><Button x:Name=\"c\"/></StackPanel><Button x:Name=\"d\"/>", "", "a b")]
    [InlineData("<ListView><ListViewItem x:Name=\"a\"><Button x:Name=\"b\"/></ListViewItem></ListView>", "", "a b")]
    [InlineData("<ListBox><Button x:Name=\"b\"/></ListBox>", "", "b b")]
    [InlineData("<ListBox><ListBoxItem x:Name=\"a\" Focusable=\"False\"/><ListBoxItem x:Name=\"b\"/></ListBox>", "", "b")]
    [InlineData("<ListBox><ListBoxItem x:Name=\"a\" IsTabStop=\"False\"/><ListBoxItem x:Name=\"b\"/></ListBox>", "", "b")]
    [InlineData("<WebBrowser x:Name=\"a\"/><FlowDocumentReader x:Name=\"b\"/><RibbonButton x:Name=\"c\"/>", "", "??a ??b ??c")]
    [InlineData("<DatePicker x:Name=\"a\" Focusable=\"False\"/><ComboBox x:Name=\"b\" IsEditable=\"True\" Focusable=\"False\"/><ComboBox x:Name=\"c\" Focusable=\"False\"/>", "", "a b")]
    [InlineData("<HeaderedContentControl x:Name=\"a\" Style=\"{StaticResource NotHere}\"/><Control x:Name=\"b\" Style=\"{StaticResource NotHere}\"/><Button x:Name=\"c\" Style=\"{StaticResource NotHere}\"/>", "", "?a ?b c")]
    [InlineData("<Button x:Name=\"a\"><Button.ToolTip><StackPanel><Button x:Name=\"b\"/></StackPanel></Button.ToolTip><Button.Resources><Button x:Key=\"c\" x:Name=\"c\"/></Button.Resources></Button>", "", "a")]
    public void What_depends_on_the_run(string body, string code, string expected) => Assert.Equal(expected, Walk(body, code));

    [Fact]
    public void A_user_control_of_the_project_is_walked_into() =>
        Assert.Equal("a in b", Walk("<Button x:Name=\"a\"/><local:Box/><Button x:Name=\"b\"/>", "", ("Box.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Box\"><StackPanel><TextBox x:Name=\"in\"/></StackPanel></UserControl>")));

    [Fact]
    public void Controls_that_hold_themselves_end() =>
        Assert.StartsWith("in", Walk("<local:Box/>", "", ("Box.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Box\"><StackPanel><TextBox x:Name=\"in\"/><local:Box/></StackPanel></UserControl>")), StringComparison.Ordinal);

    /// <summary>自己包自己 40 次：不能跑不完。</summary>
    [Fact]
    public void Many_copies_of_itself_end()
    {
        using var f = new Folder().Add("Main.xaml", Kit.Window("<local:Loop/>")).Add("Loop.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Loop\"><StackPanel><Button/>{string.Concat(Enumerable.Repeat("<local:Loop/>", 40))}</StackPanel></UserControl>")
            .Add("Two.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Two\"><StackPanel>{string.Concat(Enumerable.Repeat("<local:One/>", 100))}</StackPanel></UserControl>")
            .Add("One.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.One\"><StackPanel>{string.Concat(Enumerable.Repeat("<Button/>", 100))}</StackPanel></UserControl>")
            .Add("Big.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<local:Two/>", 100))));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Contains("1 Tab stop(s)", f.Run("walk", f.At("Main.xaml")).Out, StringComparison.Ordinal);
        Assert.Equal(0, f.Run("walk", f.At("Big.xaml")).Exit);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
    }

    /// <summary>整個視窗的根設 Cycle：後面沒有站，不用說。</summary>
    [Fact]
    public void A_ring_around_everything_is_not_a_trap()
    {
        using var f = new Folder().Add("Main.xaml", $"<Window {Kit.Ns}><Grid KeyboardNavigation.TabNavigation=\"Cycle\"><Button Content=\"a\"/></Grid></Window>");
        Assert.DoesNotContain("Tab stays inside", f.Run("walk", f.At("Main.xaml")).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_prints_the_walk()
    {
        using var f = new Folder().Add("App.csproj", "<Project/>").Add("Views/Main.xaml", Kit.Window("<Button Content=\"Save\"/><TextBox/><Button ToolTip=\"Delete\"><Image/></Button><ToolBar><Button Content=\"Bold\"/></ToolBar><Button Content=\"after\"/><Popup><Button Content=\"p\"/></Popup><Button Content=\"h\" Visibility=\"Collapsed\"/>"));
        var r = f.Run("walk", f.At("Views/Main.xaml"));
        Assert.Equal((0, ""), (r.Exit, r.Err));
        Assert.Contains("xamla11y walk — Views/Main.xaml: 4 Tab stop(s), 0 not sure", r.Out, StringComparison.Ordinal);
        Assert.Contains("    1  1      <Button> \"Save\"  NVDA: Save, button", r.Out, StringComparison.Ordinal);
        Assert.Contains("    2  1      <TextBox> (no name)  NVDA: edit  !! input-no-name", r.Out, StringComparison.Ordinal);
        Assert.Contains("    3  1      <Button> (no name)  NVDA: button, Delete  !! missing-name", r.Out, StringComparison.Ordinal);
        Assert.Contains("Tab stays inside <ToolBar> (line 1): 1 stop(s) after it need Ctrl+Tab", r.Out, StringComparison.Ordinal);
        Assert.Contains("not listed: 1 in popups", r.Out, StringComparison.Ordinal);
        // 面板型的不算進「藏起來的」數字
        f.Add("Views/P.xaml", Kit.Window("<StackPanel Visibility=\"Collapsed\"><Button/><ContentControl/><ItemsControl/><TextBox/></StackPanel>"));
        Assert.Contains("not listed: 2 hidden or disabled", f.Run("walk", f.At("Views/P.xaml")).Out, StringComparison.Ordinal);
        Assert.Contains("not listed: 1 hidden or disabled", r.Out, StringComparison.Ordinal);
        var zh = f.Run("walk", f.At("Views/Main.xaml"), "--lang", "zh-Hant").Out;
        Assert.Contains("Tab 停 4 站", zh, StringComparison.Ordinal);
        Assert.Contains("<TextBox>（沒有名稱）　NVDA：編輯區  !! input-no-name", zh, StringComparison.Ordinal);
        Assert.Contains("（第 1 行）就在裡面繞：後面 1 站", zh, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_note_has_chinese()
    {
        using var f = new Folder().Add("Main.xaml", Kit.Window("<Button Visibility=\"{Binding V}\"/><ListBox ItemsSource=\"{Binding N}\"/><local:Fancy/><ContentControl Content=\"{Binding N}\"/><Expander IsExpanded=\"{Binding V}\"><Button/></Expander><Button Focusable=\"{Binding V}\"/><ContentControl Style=\"{StaticResource NotHere}\"/><Button x:Name=\"a\" Visibility=\"Collapsed\"/><WebBrowser/><Button IsEnabled=\"{Binding V}\"/><TabControl SelectedIndex=\"{Binding V}\"><TabItem/></TabControl><Button><Button.Style><Style TargetType=\"Button\"><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"IsTabStop\" Value=\"False\"/></Trigger></Style.Triggers></Style></Button.Style></Button>")).Add("Main.xaml.cs", "class M { void L() { a.Visibility = 0; } }");
        var project = Project.Load(f.Root, []);
        var notes = TabWalk.Of(project, project.Docs[0]).Stops.Select(s => s.Note).OfType<string>().ToHashSet();
        Assert.Equal(TabWalk.Notes.Keys.Order(StringComparer.Ordinal), notes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Wrong_targets()
    {
        using var f = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>")).Add("Bad.xaml", "<Window").Add("A.axaml", "<x/>").Add("W.xaml", "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:u=\"using:X\"/>");
        Assert.Contains("needs one XAML file", f.Run("walk", f.Root).Err, StringComparison.Ordinal);
        Assert.Contains("could not parse Bad.xaml", f.Run("walk", f.At("Bad.xaml")).Err, StringComparison.Ordinal);
        Assert.Contains("not WPF: W.xaml", f.Run("walk", f.At("W.xaml")).Err, StringComparison.Ordinal);
        Assert.Contains("not a folder, project or XAML file", f.Run("walk", f.At("A.axaml")).Err, StringComparison.Ordinal);
        Assert.Equal(2, f.Run("walk").Exit);
        Assert.Equal(2, f.Run("walk", f.At("Main.xaml"), "--json", "x").Exit);
        Assert.Equal(0, f.Run("walk", "--help").Exit);
    }
}
