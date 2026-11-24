namespace XamlA11y.Tests;

/// <summary>code-behind 與類別的程式碼（C#、VB）：寫了什麼會讓 XAML 的答案變成不知道。</summary>
public sealed class CodeBehindTests
{
    [Fact]
    public void Code_behind_that_sets_the_content_or_the_name_makes_it_unknown()
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window("<Button x:Name=\"a\"/><Button x:Name=\"b\"/><Button x:Name=\"c\"/>"))
            .Add("Main.xaml.cs", "void M() { a.Content = \"x\"; AutomationProperties.SetName(b, \"y\"); }");
        Assert.Equal("Blocked,Blocked,missing-name", Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Fact]
    public void A_class_that_names_itself_is_blocked_for_names_and_still_judged_for_the_keyboard()
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window($"<local:Tile Focusable=\"False\">{Kit.Icon}</local:Tile><local:Plain>{Kit.Icon}</local:Plain>"))
            .Add("Tile.cs", "public class Tile : Button { void N() { AutomationProperties.SetName(this, \"x\"); } } public class Plain : Button { }");
        var r = p.Audit();
        Assert.Equal("Blocked,missing-name", Kit.Verdicts(r, "control-names"));
        Assert.Equal("not-in-tab-order", Kit.Verdicts(r, "tab-order"));
        Assert.Equal("Tile", Assert.Single(r.UnknownControls).Element);
    }

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
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("void Down(object s, MouseButtonEventArgs e) { Open(); /* not DragMove here */ }", "mouse-only")]
    [InlineData("void M() { AutomationProperties.SetName(card, \"x\"); }", "mouse-only")]
    [InlineData("void M() { card.KeyDown += K; }", "Blocked")]
    public void Naming_an_element_is_not_handling_keys(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Border x:Name=\"card\" MouseLeftButtonDown=\"Down\"/>")).Add("Main.xaml.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "mouse"));
    }

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
        using var p = Kit.Files("<Button x:Name=\"b\"/>", $"class M {{ void F() {{ {statement} }} }}");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("var s = b.Content;")]
    [InlineData("if (b.Content == x) { }")]
    [InlineData("b.SetBinding(Button.ToolTipProperty, z);")]
    [InlineData("other.SetBinding(Button.ContentProperty, z);")]
    [InlineData("list.Where(x => b.Content => 1);")]
    public void Code_that_does_not_set_them_changes_nothing(string statement)
    {
        using var p = Kit.Files("<Button x:Name=\"b\"/>", $"class M {{ void F() {{ {statement} }} }}");
        Assert.Equal("missing-name", Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("<Button><TextBlock><Run x:Name=\"v\"/></TextBlock></Button>", "control-names")]
    [InlineData("<TextBlock><Hyperlink><Run x:Name=\"v\"/></Hyperlink></TextBlock>", "control-names")]
    [InlineData("<TextBlock x:Name=\"l\"><Run x:Name=\"v\"/></TextBlock><TextBox AutomationProperties.LabeledBy=\"{Binding ElementName=l}\"/>", "input-names")]
    public void A_run_filled_by_code_is_unknown(string body, string rule)
    {
        using var p = Kit.Files(body, "class M { void F() { v.Text = \"1.0\"; } }");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), rule));
    }

    [Theory]
    [InlineData("play.Content = \"x\"", "Blocked")]
    [InlineData("AutomationProperties.SetName(Me.play, \"x\")", "Blocked")]
    [InlineData("Dim s = other.Content", "missing-name")]
    public void Visual_Basic_is_case_insensitive(string statement, string expected)
    {
        using var p = Kit.Files("<Button x:Name=\"Play\"/>", $"Class M\nSub F()\n{statement}\nEnd Sub\nEnd Class", ext: ".vb");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Fact]
    public void Visual_Basic_window_drag_is_seen()
    {
        using var p = Kit.Files("<Grid><Border MouseLeftButtonDown=\"Bar_Down\"/></Grid>", "Class M\nPrivate Sub Bar_Down(sender As Object, e As MouseButtonEventArgs)\n  Me.DragMove()\nEnd Sub\nEnd Class", ext: ".vb");
        Assert.Equal("NotJudged", Kit.Verdicts(p.Audit(), "mouse"));
    }

    [Fact]
    public void Visual_Basic_AddHandler_is_a_key_handler()
    {
        using var p = Kit.Files("<Grid><Border x:Name=\"Card\" MouseLeftButtonDown=\"Go\"/></Grid>", "Class M\nSub New()\nAddHandler card.KeyDown, AddressOf K\nEnd Sub\nEnd Class", ext: ".vb");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "mouse"));
    }

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
        using var p = Kit.Files("<local:CloseButton/>", "").Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Fact]
    public void A_text_block_class_that_fills_its_own_text_is_unknown()
    {
        using var p = Kit.Files("<Button><local:LocText/></Button>", "").Add("C.cs", "class LocText : TextBlock { public LocText() { Text = \"x\"; } }");
        Assert.Equal("Blocked", Kit.Verdicts(p.Audit(), "control-names"));
    }

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
        using var p = Kit.Files($"<Button Command=\"{{x:Static local:Cmds.Go}}\">{Kit.Icon}</Button>", "").Add("C.cs", code);
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("var c = '\"'; b.Content = 1; var d = '\"';", "Blocked")]                  // 字元常數裡的引號不是字串的開頭
    [InlineData("var c = '\\''; b.Content = 1;", "Blocked")]
    [InlineData("var s = \"b.Content = 1\";", "missing-name")]
    [InlineData("var s = @\"a\\\"; b.Content = 1;", "Blocked")]                             // 逐字字串裡的反斜線不是跳脫
    [InlineData("var s = $@\"a\\\"; b.Content = 1;", "Blocked")]
    [InlineData("var s = \"\"\" \" b.Content = 1 \"\"\";", "missing-name")]
    [InlineData("/* b.Content = 1 */", "missing-name")]
    public void Strings_and_comments_are_not_code(string statement, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button x:Name=\"b\"/>")).Add("Main.xaml.cs", $"class M {{ void F() {{ {statement} }} }}");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }

    [Theory]
    [InlineData("\"a\"\"b\"\nplay.Content = 1", "Blocked")]        // VB 的引號跳脫
    [InlineData("\"play.Content = 1\"", "missing-name")]
    [InlineData("' play.Content = 1", "missing-name")]
    [InlineData("\"a ' b\"\nplay.Content = 1", "Blocked")]
    public void Visual_Basic_strings_and_comments(string code, string expected)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button x:Name=\"play\"/>")).Add("Main.xaml.vb", "Class M\nSub F()\nDim s = " + code + "\nEnd Sub\nEnd Class");
        Assert.Equal(expected, Kit.Verdicts(p.Audit(), "control-names"));
    }
}
