using System.Text;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Fix;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Tests;

/// <summary>xamla11y fix：接上已經在那裡的名稱；只加屬性，其他一個字都不動。</summary>
public sealed class FixTests
{
    private static string Fixed(string body, string code = "", string resources = "", params string[] flags)
    {
        using var f = Kit.Files(body, code, resources);
        var before = File.ReadAllText(f.At("Main.xaml"));
        var r = f.Run(["fix", f.Root, "--write", .. flags]);
        Assert.Equal((0, ""), (r.Exit, r.Err));
        var after = File.ReadAllText(f.At("Main.xaml"));
        // 拿掉加進去的，就是原本的
        Assert.Equal(before, System.Text.RegularExpressions.Regex.Replace(after, @" (AutomationProperties\.(Name|LabeledBy)=""[^""]*""(?<!=""KEEP"")| ?x:Name=""\w+Label\d*"")", ""));
        var start = after.IndexOf("<StackPanel>", StringComparison.Ordinal) + "<StackPanel>".Length;
        return after[start..after.LastIndexOf("</StackPanel></Window>", StringComparison.Ordinal)];
    }

    [Fact]
    public void A_label_that_points_at_the_field() => Assert.Equal(
        "<Label Content=\"_User name\" Target=\"{Binding ElementName=u}\" x:Name=\"UserNameLabel\"/><TextBox x:Name=\"u\" AutomationProperties.LabeledBy=\"{Binding ElementName=UserNameLabel}\"/>",
        Fixed("<Label Content=\"_User name\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/>"));

    [Fact]
    public void A_label_with_a_name_keeps_it() => Assert.Equal(
        "<Label Name=\"L\" Content=\"a b\" Target=\"{Binding ElementName=u}\"/><TextBox Name=\"u\" AutomationProperties.LabeledBy=\"{Binding ElementName=L}\"/>",
        Fixed("<Label Name=\"L\" Content=\"a b\" Target=\"{Binding ElementName=u}\"/><TextBox Name=\"u\"/>"));

    [Theory]
    [InlineData("<Button><StackPanel><Image/><TextBlock Text=\"Save\"/></StackPanel></Button>", "AutomationProperties.Name=\"Save\"")]
    [InlineData("<Button><StackPanel><Image/><TextBlock>Save</TextBlock></StackPanel></Button>", "AutomationProperties.Name=\"Save\"")]
    [InlineData("<Button><Border><AccessText Text=\"_Save\"/></Border></Button>", "AutomationProperties.Name=\"Save\"")]
    [InlineData("<Button><StackPanel><Image/><TextBlock Text=\"{x:Static local:R.Save}\"/></StackPanel></Button>", "AutomationProperties.Name=\"{x:Static local:R.Save}\"")]
    [InlineData("<TabItem><TabItem.Header><StackPanel><TextBlock Text=\"Home\"/></StackPanel></TabItem.Header></TabItem>", "AutomationProperties.Name=\"Home\"")]
    [InlineData("<Button ToolTip=\"Delete\"><Image/></Button>", "AutomationProperties.Name=\"Delete\"")]
    [InlineData("<Button ToolTip=\"Cut &amp; paste &quot;x&quot;\"><Image/></Button>", "AutomationProperties.Name=\"Cut &amp; paste &quot;x&quot;\"")]
    [InlineData("<Button ToolTip=\"{}{Delete}\"><Image/></Button>", "AutomationProperties.Name=\"{}{Delete}\"")]
    [InlineData("<Button ToolTip=\"{x:Static local:Resources.Delete}\"><Image/></Button>", "AutomationProperties.Name=\"{x:Static local:Resources.Delete}\"")]
    [InlineData("<Button FontFamily=\"Segoe MDL2 Assets\" Content=\"&#xE74D;\" ToolTip=\"Delete\"/>", "AutomationProperties.Name=\"Delete\"")]
    [InlineData("<Button Content=\"×\" ToolTip=\"Close\"/>", "AutomationProperties.Name=\"Close\"")]
    [InlineData("<TextBox ToolTip=\"Search\"/>", "AutomationProperties.Name=\"Search\"")]
    [InlineData("<ItemsControl><Button ToolTip=\"Delete\"><Image/></Button></ItemsControl>", "AutomationProperties.Name=\"Delete\"")]
    [InlineData("<ItemsControl><Button><StackPanel><Image/><TextBlock Text=\"Save\"/></StackPanel></Button></ItemsControl>", "AutomationProperties.Name=\"Save\"")]
    [InlineData("<ItemsControl><Label x:Name=\"l\" Content=\"Mail\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/></ItemsControl>", "AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"")]
    [InlineData("<ListBox ToolTip=\"Files\"/>", "AutomationProperties.Name=\"Files\"")]
    [InlineData("<ProgressBar ToolTip=\"Progress\"/>", "AutomationProperties.Name=\"Progress\"")]
    [InlineData("<Button ToolTip=\"{StaticResource s}\"><Image/></Button>", "AutomationProperties.Name=\"{StaticResource s}\"")]
    [InlineData("<Button ToolTip=\"{DynamicResource s}\"><Image/></Button>", "AutomationProperties.Name=\"{DynamicResource s}\"")]
    public void What_is_already_there(string xaml, string added)
    {
        var after = Fixed(xaml, resources: "<x:String x:Key=\"s\">Delete</x:String>");
        Assert.Contains(added, after, StringComparison.Ordinal);
    }

    /// <summary>不確定的不改：好幾段字、整句的說明、指到別處的綁定、不是字的資源、猜的標籤。</summary>
    [Theory]
    [InlineData("<Button><StackPanel><TextBlock Text=\"Save\"/><TextBlock Text=\"Ctrl+S\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel><TextBlock Visibility=\"Collapsed\" Text=\"Save\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel DataContext=\"{Binding X}\"><TextBlock Text=\"{Binding Title}\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel><TextBlock Text=\"{Binding Text, ElementName=o}\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel><TextBlock Text=\"{Binding Tag, RelativeSource={RelativeSource AncestorType=Window}}\"/></StackPanel></Button>")]
    [InlineData("<Button ToolTip=\"Removes the file. This cannot be undone.\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"刪除這個檔案。\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"a tool tip that is much too long to be the name of a button\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"×\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{StaticResource t}\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{StaticResource nothere}\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{DynamicResource t}\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{x:Null}\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{TemplateBinding ToolTip}\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{x:Type Button}\"><Image/></Button>")]
    [InlineData("<TextBlock Text=\"Name\"/><TextBox/>")]
    [InlineData("<Button><StackPanel><Image/><TextBlock Text=\"{Binding Title}\"/></StackPanel></Button>")]                              // 可能是徽章
    [InlineData("<Button ToolTip=\"{Binding Tip}\"><Image/></Button>")]
    [InlineData("<Button><StackPanel><Image/><TextBlock Text=\"3\"/></StackPanel></Button>")]
    [InlineData("<Button ToolTip=\"Ctrl+S\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"Ctrl + Shift + N\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"F5\"><Image/></Button>")]
    [InlineData("<Button ToolTip=\"{x:Static local:Tips.Save}\"><Image/></Button>")]                                                 // 可能是 ToolTip 物件
    [InlineData("<Button><StackPanel><TextBlock Opacity=\"0\" Text=\"Save\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel Visibility=\"Hidden\"><TextBlock Text=\"Save\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel><TextBlock Text=\"Busy\" Style=\"{StaticResource gone}\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel><TextBlock Text=\"Busy\" Style=\"{StaticResource blink}\"/></StackPanel></Button>")]
    [InlineData("<ToggleButton><StackPanel><TextBlock Text=\"On\"/></StackPanel><ToggleButton.CheckedElement><TextBlock Text=\"Off\"/></ToggleButton.CheckedElement></ToggleButton>")]
    [InlineData("<Button><StackPanel><StackPanel.Resources><x:String x:Key=\"t\">Save</x:String></StackPanel.Resources><TextBlock Text=\"{StaticResource t}\"/></StackPanel></Button>")]
    [InlineData("<Button><Button.Resources><x:String x:Key=\"t\">Save</x:String></Button.Resources><StackPanel><TextBlock Text=\"{StaticResource t}\"/></StackPanel></Button>")]
    [InlineData("<Button><StackPanel><TextBlock Text=\"{StaticResource nothere}\"/></StackPanel></Button>")]
    [InlineData("<Button ToolTip=\"Close\"><AutomationProperties.Name>×</AutomationProperties.Name></Button>")]
    [InlineData("<Button xmlns:a=\"clr-namespace:System.Windows.Automation;assembly=PresentationCore\" a:AutomationProperties.Name=\"×\" ToolTip=\"Close\"/>")]
    [InlineData("<Label Target=\"{Binding ElementName=u}\"><Label.Name>zz</Label.Name>Mail</Label><TextBox x:Name=\"u\"/>")]
    [InlineData("<Label Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/>")]
    [InlineData("<Border MouseLeftButtonUp=\"O\" ToolTip=\"Open\"/>")]
    public void What_is_left_for_a_person(string xaml)
    {
        Assert.DoesNotContain("AutomationProperties.", Fixed(xaml, resources: "<ToolTip x:Key=\"t\">x</ToolTip><Style x:Key=\"gone\" TargetType=\"TextBlock\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/></Style><Style x:Key=\"blink\" TargetType=\"TextBlock\"><Style.Triggers><Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Visibility\" Value=\"Collapsed\"/></Trigger></Style.Triggers></Style>").Replace("a:AutomationProperties.", "", StringComparison.Ordinal).Replace("<AutomationProperties.", "", StringComparison.Ordinal).Replace("</AutomationProperties.", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Guessing_has_to_be_asked_for()
    {
        Assert.Equal("<TextBlock Text=\"Name\" x:Name=\"NameLabel\"/><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=NameLabel}\"/>", Fixed("<TextBlock Text=\"Name\"/><TextBox/>", flags: "--guess"));
        Assert.Equal("<TextBlock Text=\"Nothing here yet!\"/><TextBox/>", Fixed("<TextBlock Text=\"Nothing here yet!\"/><TextBox/>", flags: "--guess"));
    }

    /// <summary>x:Name 會變成欄位：不跟 XAML、code-behind 的名字撞。</summary>
    [Fact]
    public void A_fresh_name_does_not_collide()
    {
        var after = Fixed("<Label Content=\"Name\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/><Button x:Name=\"NAMELABEL2\" Content=\"x\"/>", "partial class Main { string NameLabel; }");
        Assert.Contains("x:Name=\"NameLabel3\"", after, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UserFieldLabel\"", Fixed("<Label Content=\"名稱\" Target=\"{Binding ElementName=userField}\"/><TextBox x:Name=\"userField\"/>"), StringComparison.Ordinal);
        var two = Fixed("<Label Content=\"Name\" Target=\"{Binding ElementName=a}\"/><TextBox x:Name=\"a\"/><Label Content=\"Name\" Target=\"{Binding ElementName=b}\"/><TextBox x:Name=\"b\"/>");
        Assert.Contains("x:Name=\"NameLabel\"", two, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NameLabel2\"", two, StringComparison.Ordinal);
    }

    /// <summary>放在專案自己的控制項裡的元素不能取名字（MC3093）；範本裡外是兩個名稱範圍。</summary>
    [Fact]
    public void No_name_where_WPF_does_not_allow_one()
    {
        using var f = new Folder().Add("Box.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.Box\"/>").Add("Main.xaml", Kit.Window("<local:Box><StackPanel><Label Content=\"Name\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/></StackPanel></local:Box>"));
        Assert.Contains("0 change(s)", f.Run("fix", f.Root).Out, StringComparison.Ordinal);
        foreach (var template in (string[])["HierarchicalDataTemplate", "ControlTemplate"])
            Assert.DoesNotContain("LabeledBy", Fixed("<Label Content=\"Name\" Target=\"{Binding ElementName=u}\"/>", resources: $"<{template} x:Key=\"k\"><TextBox x:Name=\"u\"/></{template}>"), StringComparison.Ordinal);
        Assert.DoesNotContain("LabeledBy", Fixed("<Label Content=\"Name\" Target=\"{Binding ElementName=u}\"/><ListBox AutomationProperties.Name=\"KEEP\"><ListBox.ItemTemplate><DataTemplate><TextBox x:Name=\"u\"/></DataTemplate></ListBox.ItemTemplate></ListBox>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_written_without_write()
    {
        using var f = Kit.Files("<Button ToolTip=\"Delete\"><Image/></Button>");
        var before = File.ReadAllBytes(f.At("Main.xaml"));
        var r = f.Run("fix", f.Root);
        Assert.Equal(0, r.Exit);
        Assert.Contains("1 change(s) in 1 file(s)", r.Out, StringComparison.Ordinal);
        Assert.Contains("Main.xaml:1 <Button> AutomationProperties.Name=\"Delete\"  (its ToolTip)", r.Out, StringComparison.Ordinal);
        Assert.Contains("nothing was written; add --write", r.Out, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(f.At("Main.xaml")));
        Assert.Contains("1 處可以改", f.Run("fix", f.Root, "--lang", "zh-Hant").Out, StringComparison.Ordinal);
    }

    /// <summary>降成關掉的、放過的、別的檔案不動。</summary>
    [Fact]
    public void What_is_off_accepted_or_elsewhere_stays()
    {
        const string button = "<Button ToolTip=\"Delete\"><Image/></Button>";
        using var off = Kit.Files(button).Add("xamla11y.config.json", "{ \"rules\": { \"missing-name\": \"off\" } }");
        Assert.Contains("0 change(s)", off.Run("fix", off.Root).Out, StringComparison.Ordinal);
        using var accepted = Kit.Files("<!-- xamla11y-ignore: a test -->" + button);
        Assert.Contains("0 change(s)", accepted.Run("fix", accepted.Root).Out, StringComparison.Ordinal);
        using var two = new Folder().Add("A.xaml", Kit.Window(button)).Add("B.xaml", Kit.Window(button));
        two.Run("fix", two.Root, "--write", "--only", two.At("A.xaml"));
        Assert.Contains("AutomationProperties.Name", File.ReadAllText(two.At("A.xaml")), StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name", File.ReadAllText(two.At("B.xaml")), StringComparison.Ordinal);
    }

    /// <summary>另一個標準（沒有這一條）也不動；重複跑第二次沒有東西可改。</summary>
    [Fact]
    public void A_second_run_has_nothing_to_do()
    {
        using var f = Kit.Files("<Button ToolTip=\"Delete\"><Image/></Button><TextBox ToolTip=\"Search\"/>");
        f.Run("fix", f.Root, "--write");
        Assert.Contains("0 change(s)", f.Run("fix", f.Root).Out, StringComparison.Ordinal);
        Assert.Equal(0, f.Run("audit", f.Root).Exit);
    }

    [Theory]
    [InlineData("--json", "x.json")]
    [InlineData("--baseline", "x.json")]
    public void Flags_of_audit_are_refused(string flag, string value)
    {
        using var f = Kit.Good();
        var r = f.Run("fix", f.Root, flag, value);
        Assert.Equal(2, r.Exit);
        Assert.Contains("belongs to 'xamla11y audit'", r.Err, StringComparison.Ordinal);
        Assert.Equal(2, f.Run("audit", f.Root, "--write").Exit);
        Assert.Equal(2, f.Run("fix").Exit);
        Assert.Equal(2, f.Run("fix", f.At("nothere")).Exit);
    }

    // ───────────────────────── 原文 ─────────────────────────

    [Theory]
    [InlineData("<a:B\r\n    X=\"1\"\r\n    Y=\"2\"/>", "<a:B\r\n    X=\"1\"\r\n    Y=\"2\"\r\n    N=\"v\"/>")]
    [InlineData("<a:B\n\tX=\"1\">", "<a:B\n\tX=\"1\"\n\tN=\"v\">")]
    [InlineData("<a:B X=\"1\"\n     Y=\"2\" >", "<a:B X=\"1\"\n     Y=\"2\"\n     N=\"v\" >")]
    [InlineData("<a:B X=\"1\" Y=\"2\"\n     Z=\"3\" W=\"4\"/>", "<a:B X=\"1\" Y=\"2\"\n     Z=\"3\" W=\"4\"\n     N=\"v\"/>")]
    [InlineData("<a:B/>", "<a:B N=\"v\"/>")]
    [InlineData("<a:B />", "<a:B N=\"v\" />")]
    [InlineData("<a:B>", "<a:B N=\"v\">")]
    [InlineData("<a:B X='a > b'/>", "<a:B X='a > b' N=\"v\"/>")]
    [InlineData("<a:B X = \"1\" />", "<a:B X = \"1\" N=\"v\" />")]
    [InlineData("<a:B X=\"a/b\"/>", "<a:B X=\"a/b\" N=\"v\"/>")]
    [InlineData("<a:B X=\"1\"  Y='2'\t/>", "<a:B X=\"1\"  Y='2' N=\"v\"\t/>")]
    [InlineData("<a:B X=\"1>2\" Y=\"/>\">", "<a:B X=\"1>2\" Y=\"/>\" N=\"v\">")]
    [InlineData("<a:B X =\n  \"1\"/>", "<a:B X =\n  \"1\" N=\"v\"/>")]
    public void An_attribute_goes_after_the_last_one(string tag, string expected)
    {
        var text = "<r xmlns:a=\"u\">\r\n  <!-- x -->" + tag + (tag.EndsWith("/>", StringComparison.Ordinal) ? "" : "</a:B>") + "\r\n</r>";
        var source = XamlSource.Read(Encoding.UTF8.GetBytes(text))!;
        Assert.True(source.Insert(SafeXml.Parse(text).Root!.Elements().Single(), "N", "v"));
        Assert.Equal(text.Replace(tag, expected, StringComparison.Ordinal), source.Result());
    }

    [Fact]
    public void Encodings_and_the_BOM_stay()
    {
        var text = Kit.Window("<Button ToolTip=\"刪除\"><Image/></Button>");
        foreach (var encoding in (Encoding[])[new UTF8Encoding(true), new UTF8Encoding(false), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true)])
        {
            using var f = new Folder().Add("Main.xaml", "x");
            File.WriteAllBytes(f.At("Main.xaml"), [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
            f.Run("fix", f.Root, "--write");
            var expected = text.Replace("ToolTip=\"刪除\"", "ToolTip=\"刪除\" AutomationProperties.Name=\"刪除\"", StringComparison.Ordinal);
            Assert.Equal([.. encoding.GetPreamble(), .. encoding.GetBytes(expected)], File.ReadAllBytes(f.At("Main.xaml")));
        }
    }

    [Fact]
    public void A_file_that_would_not_come_back_the_same_is_not_touched()
    {
        Assert.Null(XamlSource.Read([0xEF, 0xBB, 0xBF, (byte)'<', 0xFF, 0xFE]));
        Assert.NotNull(XamlSource.Read("<a/>"u8.ToArray()));
    }

    /// <summary>預設命名空間不是 WPF：不能寫不帶前綴的附加屬性。</summary>
    [Fact]
    public void Another_default_namespace_is_left_alone()
    {
        using var f = new Folder().Add("Main.xaml", "<p:Window xmlns:p=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns=\"clr-namespace:App\"><p:Button ToolTip=\"Delete\"><p:Image/></p:Button></p:Window>");
        Assert.Contains("0 change(s)", f.Run("fix", f.Root).Out, StringComparison.Ordinal);
    }

    /// <summary>程式碼會換的字與提示：寫死目前的那一個，以後就是錯的（modernflyouts 的 Pin／Unpin）。</summary>
    [Theory]
    [InlineData("<Button x:Name=\"b\" ToolTip=\"Show password\"><Image/></Button>", "b.ToolTip = \"Hide password\";")]
    [InlineData("<Button x:Name=\"b\" ToolTip=\"Show password\"><Image/></Button>", "ToolTipService.SetToolTip(b, \"Hide password\");")]
    [InlineData("<Button><StackPanel><TextBlock x:Name=\"t\" Text=\"v1.0\"/></StackPanel></Button>", "t.Text = Version;")]
    [InlineData("<Button><StackPanel><TextBlock x:Name=\"t\" Text=\"Save\"/></StackPanel></Button>", "t.Visibility = Visibility.Collapsed;")]
    [InlineData("<Button><StackPanel x:Name=\"p\"><TextBlock Text=\"Save\"/></StackPanel></Button>", "p.Children.Add(x);")]
    public void What_the_code_changes_is_left(string xaml, string code) =>
        Assert.DoesNotContain("AutomationProperties.", Fixed(xaml, $"partial class Main {{ void L() {{ {code} }} }}"), StringComparison.Ordinal);

    [Fact]
    public void A_tool_tip_a_trigger_changes_is_left() =>
        Assert.DoesNotContain("AutomationProperties.", Fixed("<ToggleButton ToolTip=\"Pin\"><Image/><ToggleButton.Style><Style TargetType=\"ToggleButton\"><Style.Triggers><Trigger Property=\"IsChecked\" Value=\"True\"><Setter Property=\"ToolTip\" Value=\"Unpin\"/></Trigger></Style.Triggers></Style></ToggleButton.Style></ToggleButton>"), StringComparison.Ordinal);

    /// <summary>元素裡的文字不是標記延伸：寫進屬性要跳脫（不然 MC3074）。</summary>
    [Theory]
    [InlineData("{0} items", "{}{0} items")]
    [InlineData("{Binding Title}", "{}{Binding Title}")]
    public void Braces_in_text_are_escaped(string text, string expected) =>
        Assert.Contains($"AutomationProperties.Name=\"{expected}\"", Fixed($"<Button><StackPanel><Image/><TextBlock>{text}</TextBlock></StackPanel></Button>"), StringComparison.Ordinal);

    /// <summary>執行時才知道的要 --guess，而且說要開起來看。跟自己的值同一個路徑的不接。</summary>
    [Theory]
    [InlineData("<Button><StackPanel><Image/><TextBlock Text=\"{Binding Title}\"/></StackPanel></Button>", "AutomationProperties.Name=\"{Binding Title}\"")]
    [InlineData("<Button ToolTip=\"{Binding Tip}\"><Image/></Button>", "AutomationProperties.Name=\"{Binding Tip}\"")]
    [InlineData("<TextBlock><Hyperlink ToolTip=\"{Binding Tip}\"><InlineUIContainer><Image/></InlineUIContainer></Hyperlink></TextBlock>", "AutomationProperties.Name=\"{Binding Tip}\"")]
    [InlineData("<Button Tag=\"{Binding Tip}\" ToolTip=\"{Binding Tip}\"><Image/></Button>", null)]
    [InlineData("<Slider Value=\"{Binding Volume}\" ToolTip=\"{Binding Volume}\"/>", null)]
    [InlineData("<TextBox ToolTip=\"{Binding Error}\"/>", null)]
    [InlineData("<Button ToolTip=\"{Binding Text, ElementName=o}\"><Image/></Button>", null)]
    [InlineData("<Button ToolTip=\"{Binding Tag, RelativeSource={RelativeSource Self}}\"><Image/></Button>", null)]
    [InlineData("<Button ToolTip=\"{Binding}\"><Image/></Button>", null)]
    [InlineData("<Button><StackPanel><StackPanel.DataContext><Binding Path=\"X\"/></StackPanel.DataContext><TextBlock Text=\"{Binding Title}\"/></StackPanel></Button>", null)]
    public void Bound_texts_are_a_guess(string xaml, string? added)
    {
        var after = Fixed(xaml, flags: "--guess");
        if (added is null) Assert.DoesNotContain("AutomationProperties.", after, StringComparison.Ordinal);
        else Assert.Contains(added, after, StringComparison.Ordinal);
        using var f = Kit.Files(xaml);
        var r = f.Run("fix", f.Root, "--guess").Out;
        Assert.Equal(added is not null, r.Contains("1 of them get a name that is only known at run time", StringComparison.Ordinal));
        Assert.Equal(added is not null, r.Contains("(GUESS: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_label_with_bound_text_is_connected_and_said()
    {
        using var f = Kit.Files("<Label Content=\"{Binding Caption}\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/>");
        var r = f.Run("fix", f.Root).Out;
        Assert.Contains("1 change(s)", r, StringComparison.Ordinal);
        Assert.Contains("1 of them get a name that is only known at run time", r, StringComparison.Ordinal);
    }

    /// <summary>宣告的編碼裝不下的字會變成「?」：那個檔案不動。BOM 與宣告不一樣的也不動。</summary>
    [Fact]
    public void A_text_the_encoding_cannot_hold_is_not_written()
    {
        var text = "<?xml version=\"1.0\" encoding=\"us-ascii\"?>" + Kit.Window("<Button ToolTip=\"&#x5132;&#x5B58;\"><Image/></Button>");
        using var f = new Folder().Add("Main.xaml", text).Add("Other.xaml", Kit.Window("<Button ToolTip=\"Save\"><Image/></Button>"));
        var r = f.Run("fix", f.Root, "--write");
        Assert.Equal(text, File.ReadAllText(f.At("Main.xaml")));
        Assert.Contains("not touched: Main.xaml", r.Out, StringComparison.Ordinal);
        Assert.Contains("1 change(s) in 1 file(s)", r.Out, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Save\"", File.ReadAllText(f.At("Other.xaml")), StringComparison.Ordinal);
        Assert.Null(XamlSource.Read([0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"windows-1252\"?><a/>")]));
        Assert.NotNull(XamlSource.Read([0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>")]));
    }

    [Theory]
    [InlineData("<a:B\r   X=\"1\"/>", "<a:B\r   X=\"1\"\r   N=\"v\"/>")]
    [InlineData("<a:B\r\r   X=\"1\"/>", "<a:B\r\r   X=\"1\"\r   N=\"v\"/>")]
    [InlineData("<a:B\n\n   X=\"1\"/>", "<a:B\n\n   X=\"1\"\n   N=\"v\"/>")]
    public void The_line_end_of_the_tag_is_used(string tag, string expected)
    {
        var text = "<r xmlns:a=\"u\">" + tag + "</r>";
        var source = XamlSource.Read(Encoding.UTF8.GetBytes(text))!;
        Assert.True(source.Insert(SafeXml.Parse(text).Root!.Elements().Single(), "N", "v"));
        Assert.Equal(text.Replace(tag, expected, StringComparison.Ordinal), source.Result());
    }

    /// <summary>XAML 的前綴不叫 x：讀進來的時候改寫過，照抄會不認得。</summary>
    [Fact]
    public void Another_prefix_for_the_XAML_namespace()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:xaml=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:App\"";
        using var f = new Folder().Add("Main.xaml", $"<Window {ns}><StackPanel><Button ToolTip=\"{{xaml:Static local:Resources.Save}}\"><Image/></Button><Label Content=\"Mail\" Target=\"{{Binding ElementName=u}}\"/><TextBox xaml:Name=\"u\"/></StackPanel></Window>");
        f.Run("fix", f.Root, "--write");
        var after = File.ReadAllText(f.At("Main.xaml"));
        Assert.DoesNotContain("{x:Static", after, StringComparison.Ordinal);
        Assert.DoesNotContain(" x:Name", after, StringComparison.Ordinal);
    }

    /// <summary>名字不跟別的檔案的 partial、基底類別的撞（CS0102、CS0108）。</summary>
    [Fact]
    public void A_fresh_name_does_not_collide_with_other_files()
    {
        using var f = Kit.Files("<Label Content=\"Mail\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/>").Add("Main.Helpers.cs", "partial class Main { object mailLabel; } class Base { object MailLabel2; }");
        f.Run("fix", f.Root, "--write");
        Assert.Contains("x:Name=\"MailLabel3\"", File.ReadAllText(f.At("Main.xaml")), StringComparison.Ordinal);
    }

    /// <summary>繼承來的 UserControl 裡面也不能取名字（MC3093）。</summary>
    [Fact]
    public void No_name_inside_a_class_derived_from_a_user_control()
    {
        using var f = new Folder().Add("U.xaml", $"<UserControl {Kit.Ns} x:Class=\"App.U\"/>").Add("Fancy.cs", "namespace App; public class Fancy : U { }")
            .Add("Main.xaml", Kit.Window("<local:Fancy><StackPanel><TextBlock Text=\"Mail\"/><TextBox/></StackPanel></local:Fancy>"));
        Assert.Contains("0 change(s)", f.Run("fix", f.Root, "--guess").Out, StringComparison.Ordinal);
    }

    /// <summary>標籤是接對的，前面那段字是猜的：要印得出來。</summary>
    [Fact]
    public void Only_the_text_before_is_a_guess()
    {
        using var f = Kit.Files("<Label Content=\"Mail\" Target=\"{Binding ElementName=u}\"/><TextBox x:Name=\"u\"/><TextBlock Text=\"Name\"/><TextBox/>");
        var plan = Fixer.Plan(r => Project.Load(f.Root, [], null, r), x => File.ReadAllBytes(f.At(x)), new AuditOptions(), guess: true);
        Assert.Equal([(FixSource.Label, false), (FixSource.TextBefore, true)], plan.Edits.Select(e => (e.Source, e.Guess)));
    }

    /// <summary>有一處改了沒有用：那一處不改，同一個檔案的別處照改。</summary>
    [Fact]
    public void The_rest_of_the_file_is_still_changed()
    {
        using var f = Kit.Files("<Button ToolTip=\"Delete\"><Image/></Button><TextBox ToolTip=\"Search\"/>");
        // 「Delete」寫進去之後看起來還是沒名稱
        Project Load(IReadOnlyDictionary<string, string> replaced) =>
            Project.Load(f.Root, [], null, replaced.ToDictionary(p => p.Key, p => p.Value.Replace("AutomationProperties.Name=\"Delete\"", "", StringComparison.Ordinal)));
        var plan = Fixer.Plan(Load, x => File.ReadAllBytes(f.At(x)), new AuditOptions(), guess: false);
        Assert.Equal(["Search"], plan.Edits.Select(e => e.Value));
        Assert.Equal(1, plan.Left);
        Assert.Empty(plan.Untouched);
    }

    /// <summary>改完沒有用、弄出別的問題的那一處不改，別的照改。</summary>
    [Fact]
    public void A_change_that_does_not_help_is_dropped()
    {
        using var f = Kit.Files("<Button ToolTip=\"Delete\"><Image/></Button><Button ToolTip=\"Copy\"><Image/></Button>");
        var calls = 0;
        // 第二次讀專案（改過的內容）時，假裝「Delete」那一處弄壞了檔案
        Project Load(IReadOnlyDictionary<string, string> replaced) =>
            Project.Load(f.Root, [], null, replaced.Count > 0 && replaced.Values.Any(t => t.Contains("Name=\"Delete\"", StringComparison.Ordinal)) && calls++ >= 0
                ? replaced.ToDictionary(p => p.Key, p => "<Window") : replaced);
        var plan = Fixer.Plan(Load, x => File.ReadAllBytes(f.At(x)), new AuditOptions(), guess: false);
        Assert.Empty(plan.Edits);
        Assert.Empty(plan.Files);
        Assert.Equal(2, plan.Left);
    }

    [Fact]
    public void Check_says_which_changes_do_not_help()
    {
        using var f = Kit.Files("<Button ToolTip=\"Delete\"><Image/></Button><TextBox ToolTip=\"Search\"/>");
        Project Load(IReadOnlyDictionary<string, string> replaced) => Project.Load(f.Root, [], null, replaced);
        var before = Auditor.Run(Load(new Dictionary<string, string>()));
        FixEdit Edit(Kind kind, string element) => new("Main.xaml", 1, element, kind, FixSource.ToolTip, "AutomationProperties.Name", "x", 0, false, false);
        FixEdit[] both = [Edit(Kinds.MissingName, "Button"), Edit(Kinds.InputNoName, "TextBox")];
        List<FixEdit> Check(string text) => Fixer.Check(before, Load, new Dictionary<string, string> { ["Main.xaml"] = text }, new AuditOptions(), [.. both]);
        Assert.Empty(Check(Kit.Window("<Button AutomationProperties.Name=\"a b\"><Image/></Button><TextBox AutomationProperties.Name=\"c\"/>")));
        Assert.Equal([both[1]], Check(Kit.Window("<Button AutomationProperties.Name=\"a b\"><Image/></Button><TextBox/>")));
        Assert.Equal(both, Check("<Window"));
        Assert.Equal(both, Check(Kit.Window("<Button AutomationProperties.Name=\"a b\"><Image/></Button><TextBox AutomationProperties.Name=\"c\"/><CheckBox/>")));
        Assert.DoesNotContain(Check(Kit.Window("<Button Click=\"A\" AutomationProperties.Name=\"a b\"><Image/></Button><TextBox AutomationProperties.Name=\"c\"/><Button Click=\"B\" AutomationProperties.Name=\"a b\"><Image/></Button>")), e => e.Kind == Kinds.SameName);
    }

    /// <summary>一萬兩千個元素：標籤與名字各建一次表，不是每次掃整份。</summary>
    [Fact]
    public void Many_fields_are_not_quadratic()
    {
        using var f = Kit.Files(string.Concat(Enumerable.Range(0, 4000).Select(i => $"<Label Content=\"L\" Target=\"{{Binding ElementName=t{i}}}\"/><TextBox x:Name=\"t{i}\"/>")));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Contains("4000 change(s)", f.Run("fix", f.Root).Out, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), clock.Elapsed.ToString());
    }
}
