using System.Diagnostics;
using System.Text;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>怪的、惡意的輸入：循環、很深、很長、很多，不能當掉也不能跑不完。</summary>
public sealed class RobustnessTests
{
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

    [Fact]
    public void One_huge_resource_key_does_not_blow_up_the_report()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button Content=\"{{StaticResource {new string('k', 200_000)}}}\"/><Button Content=\"ok\"/>"));
        p.Run("audit", p.Root, "--md", p.At("r.md"), "--min-coverage", "0");
        Assert.True(new FileInfo(p.At("r.md")).Length < 10_000);
    }

    [Fact]
    public void Class_declarations_that_base_each_other_do_not_explode()
    {
        var sb = new System.Text.StringBuilder();
        for (var layer = 0; layer < 7; layer++)
            for (var c = 0; c < 20; c++)
                for (var b = 0; b < 20; b++) sb.Append($"class L{layer}x{c} : L{layer + 1}x{b} {{}}\n");
        using var p = Kit.Files("<local:L0x0/>", "").Add("C.cs", sb.ToString());
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

    [Fact]
    public void A_hostile_source_file_does_not_hang()
    {
        var nested = string.Concat(Enumerable.Range(0, 4000).Select(i => $"class C{i} : C{i + 1} {{ ")) + string.Concat(Enumerable.Repeat("}", 4000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Kit.AuditBody(Kit.NoteList, nested + " class V { public List<C0> Notes { get; } }" + new string('<', 20000) + "public " + new string('a', 50000));
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
        foreach (var code in (string[])[bases, nested, overrides]) Kit.AuditBody(lists, code);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), clock.Elapsed.ToString());
    }

    [Fact]
    public void Cycles_do_not_hang()
    {
        var styles = string.Concat(Enumerable.Range(0, 14).Select(i => $"<Style TargetType=\"Button\" BasedOn=\"{{StaticResource {{x:Type Button}}}}\"><Setter Property=\"Margin\" Value=\"{i}\"/></Style>"));
        using var p = new Folder()
            .Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary>{styles}<Style x:Key=\"a\" TargetType=\"Button\" BasedOn=\"{{StaticResource b}}\"/><Style x:Key=\"b\" TargetType=\"Button\" BasedOn=\"{{StaticResource a}}\"/><StaticResource x:Key=\"r1\" ResourceKey=\"r2\"/><StaticResource x:Key=\"r2\" ResourceKey=\"r1\"/></ResourceDictionary></Application.Resources></Application>")
            .Add("Main.xaml", Kit.Window("<Button x:Name=\"p\" AutomationProperties.LabeledBy=\"{Binding ElementName=q}\"/><Button x:Name=\"q\" AutomationProperties.LabeledBy=\"{Binding ElementName=p}\"/><Button Style=\"{StaticResource a}\" Content=\"{StaticResource r1}\"/><Button Content=\"ok\"/>"));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        p.Run("audit", p.Root, "--min-coverage", "0");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), watch.Elapsed.ToString());
    }

    [Fact]
    public void Deep_nesting_is_a_parse_error_not_a_stack_overflow()
    {
        using var p = Kit.Good().Add("Deep.xaml", string.Concat(Enumerable.Repeat("<a>", 5000)) + string.Concat(Enumerable.Repeat("</a>", 5000)));
        Assert.Contains("could not parse Deep.xaml", p.Run("audit", p.Root).Out);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{{{{")]
    [InlineData("{Binding")]
    [InlineData("{Binding Path={Binding Path={x:Static }")]
    [InlineData("{StaticResource}")]
    [InlineData("{StaticResource {x:Type }}")]
    [InlineData("{x:Static}")]
    [InlineData("}")]
    [InlineData("_")]
    [InlineData("{Binding 'a,b', ElementName='}'}")]
    public void Odd_markup_never_crashes(string value)
    {
        var v = System.Security.SecurityElement.Escape(value);
        Kit.Audit($"<Button Content=\"{v}\" AutomationProperties.Name=\"{v}\" AutomationProperties.LabeledBy=\"{v}\" Style=\"{v}\" FocusVisualStyle=\"{v}\" Width=\"{v}\" Margin=\"{v}\" Command=\"{v}\" IsTabStop=\"{v}\"/><TextBlock Text=\"{v}\" MouseDown=\"{v}\"/>",
            $"<Style x:Key=\"{v}\" TargetType=\"{v}\" BasedOn=\"{v}\"><Setter Property=\"{v}\" Value=\"{v}\"/></Style>");
    }
}
