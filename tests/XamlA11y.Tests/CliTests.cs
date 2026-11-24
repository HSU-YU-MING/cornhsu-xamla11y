using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>退出碼、旗標、設定檔、baseline、報告格式、不可信的輸入。</summary>
public class CliTests
{
    private static Folder Good() => new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"Save\"/>"));
    private static Folder Bad() => new Folder().Add("Main.xaml", Kit.Window($"<Button Content=\"ok\"/><Button>{Kit.Icon}</Button>"));

    [Fact]
    public void Exit_codes()
    {
        using var good = Good();
        using var bad = Bad();
        using var empty = new Folder();
        using var foreign = new Folder().Add("Main.xaml", "<Window xmlns=\"https://github.com/avaloniaui\"/>");
        using var broken = new Folder().Add("Main.xaml", "<Window");
        using var nothing = new Folder().Add("Main.xaml", Kit.Window("<TextBlock Text=\"x\"/>"));
        using var unknown = new Folder().Add("Main.xaml", Kit.Window("<local:FancyButton/><local:FancyButton/><Button Content=\"x\"/>"));
        Assert.Equal(0, good.Run("audit", good.Root).Exit);
        Assert.Equal(1, bad.Run("audit", bad.Root).Exit);
        foreach (var f in new[] { empty, foreign, broken, nothing, unknown }) Assert.Equal(3, f.Run("audit", f.Root).Exit);
        Assert.Equal(0, unknown.Run("audit", unknown.Root, "--min-coverage", "30").Exit);
    }

    [Theory]
    [InlineData()]
    [InlineData("nope")]
    [InlineData("audit")]
    [InlineData("audit", "/definitely/not/here")]
    [InlineData("audit", "ROOT", "--nope")]
    [InlineData("audit", "ROOT", "extra")]
    [InlineData("audit", "ROOT", "--fail-on", "info")]
    [InlineData("audit", "ROOT", "--min-coverage", "NaN")]
    [InlineData("audit", "ROOT", "--min-coverage", "101")]
    [InlineData("audit", "ROOT", "--json")]
    [InlineData("audit", "ROOT", "--json", "--md")]
    [InlineData("audit", "ROOT", "--json", "a", "--json", "b")]
    [InlineData("audit", "ROOT", "--baseline", "/not/here.json")]
    [InlineData("audit", "ROOT", "--config", "/not/here.json")]
    [InlineData("audit", "ROOT", "--json", "/no/such/dir/x/y.json")]
    public void Usage_errors_exit_2(params string[] args)
    {
        using var p = Good();
        Assert.Equal(2, p.Run([.. args.Select(a => a == "ROOT" ? p.Root : a)]).Exit);
    }

    [Fact]
    public void A_folder_named_like_a_flag_can_be_audited()
    {
        using var p = new Folder().Add("--version/Main.xaml", Kit.Window("<Button Content=\"Save\"/>"));
        Assert.Equal(0, p.Run("audit", "--", p.At("--version")).Exit);
    }

    [Fact]
    public void Fail_on_warn_gates_warnings()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\" IsTabStop=\"False\"/>"));
        Assert.Equal(0, p.Run("audit", p.Root).Exit);
        Assert.Equal(1, p.Run("audit", p.Root, "--fail-on", "warn").Exit);
    }

    // ───────── baseline ─────────

    [Fact]
    public void Baseline_hides_known_findings_survives_moves_and_still_catches_new_ones()
    {
        using var p = Bad();
        var b = p.At("b.json");
        Assert.Equal(0, p.Run("audit", p.Root, "--write-baseline", b).Exit);
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", b).Exit);

        // 上面插十行、改縮排：還是已知的
        p.Add("Main.xaml", Kit.Window($"{new string('\n', 10)}<TextBlock Text=\"new\"/>   <Button Content=\"ok\"/><Button>{Kit.Icon}</Button>"));
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", b).Exit);

        // 多一個一模一樣的：是新的
        p.Add("Main.xaml", Kit.Window($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button>"));
        var (exit, output, _) = p.Run("audit", p.Root, "--baseline", b);
        Assert.Equal(1, exit);
        Assert.Contains("(1 new, 1 already in the baseline)", output);

        // 修好了：baseline 有過期的
        p.Add("Main.xaml", Kit.Window("<Button Content=\"ok\"/>"));
        Assert.Contains("stale baseline", p.Run("audit", p.Root, "--baseline", b).Out);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"keyVersion\": 99, \"known\": []}")]
    [InlineData("null")]
    public void A_bad_baseline_exits_2(string content)
    {
        using var p = Bad();
        p.Add("b.json", content);
        Assert.Equal(2, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void An_empty_scan_does_not_write_a_baseline()
    {
        using var p = new Folder();
        Assert.Equal(3, p.Run("audit", p.Root, "--write-baseline", p.At("b.json")).Exit);
        Assert.False(File.Exists(p.At("b.json")));
    }

    // ───────── 設定檔 ─────────

    [Theory]
    [InlineData("{ \"minCoverge\": 50 }", "minCoverge")]
    [InlineData("{ \"minCoverage\": 500 }", "minCoverage")]
    [InlineData("{ \"schemaVersion\": 2 }", "schemaVersion")]
    [InlineData("{ \"treatAs\": { \"a:B\": \"Button\" } }", "treatAs")]
    [InlineData("{ \"treatAs\": { \"B\": \"Nope\" } }", "treatAs")]
    [InlineData("{ \"rules\": { \"nope\": \"off\" } }", "rules")]
    [InlineData("{ \"rules\": { \"mouse-only\": \"loud\" } }", "rules")]
    [InlineData("{ \"exclude\": [\"\"] }", "exclude")]
    [InlineData("{", "")]
    public void Config_mistakes_exit_2_and_name_the_field(string json, string field)
    {
        using var p = Good().Add("xamla11y.config.json", json);
        var (exit, _, err) = p.Run("audit", p.Root);
        Assert.Equal(2, exit);
        Assert.Contains(field, err);
    }

    [Fact]
    public void Config_is_applied_and_the_command_line_wins()
    {
        using var p = new Folder()
            .Add("Main.xaml", Kit.Window("<local:FancyButton/><TextBox/>"))
            .Add("Skip/Other.xaml", Kit.Window("<CheckBox/>"))
            .Add("xamla11y.config.json", "{ // comments are fine\n \"$schema\": \"x\", \"schemaVersion\": 1, \"exclude\": [\"/skip/\"], \"treatAs\": { \"FancyButton\": \"Button\" }, \"rules\": { \"input-no-name\": \"off\", \"missing-name\": \"warn\" }, \"minCoverage\": 99, }");
        Assert.Equal(0, p.Run("audit", p.Root).Exit);
        Assert.Equal(1, p.Run("audit", p.Root, "--fail-on", "warn").Exit);
        Assert.Single(Project.Load(p.Root, ["/skip/"]).Docs);
    }

    // ───────── 報告 ─────────

    [Fact]
    public void Json_has_the_summary_a_machine_needs()
    {
        using var p = Bad();
        p.Run("audit", p.Root, "--json", p.At("r.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(p.At("r.json")));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("counts").GetProperty("missing-name").GetInt32());
        Assert.Equal(6, root.GetProperty("summary").GetProperty("rules").GetArrayLength());
        var f = root.GetProperty("findings")[0];
        Assert.Equal("Main.xaml:1", f.GetProperty("site").GetString());
        Assert.Equal("4.1.2, 1.1.1", f.GetProperty("wcag").GetString());
    }

    [Fact]
    public void Sarif_is_valid_enough_for_code_scanning()
    {
        using var p = Bad();
        p.Run("audit", p.Root, "--sarif", p.At("r.sarif"));
        using var doc = JsonDocument.Parse(File.ReadAllText(p.At("r.sarif")));
        Assert.StartsWith("https://json.schemastore.org/sarif", doc.RootElement.GetProperty("$schema").GetString());
        var run = doc.RootElement.GetProperty("runs")[0];
        Assert.Equal("XamlA11y", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        var ids = run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet();
        var result = run.GetProperty("results")[0];
        Assert.Contains(result.GetProperty("ruleId").GetString(), ids);
        Assert.Equal("error", result.GetProperty("level").GetString());
        Assert.Equal("Main.xaml", result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void Sarif_paths_are_valid_uris()
    {
        using var p = new Folder().Add("我的 視窗/Main #1.xaml", Kit.Window("<TextBox/>"));
        p.Run("audit", p.Root, "--sarif", p.At("r.sarif"));
        using var doc = JsonDocument.Parse(File.ReadAllText(p.At("r.sarif")));
        var uri = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0].GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString()!;
        Assert.True(System.Uri.IsWellFormedUriString(uri, UriKind.Relative), uri);
        Assert.Equal("我的 視窗/Main #1.xaml", System.Uri.UnescapeDataString(uri));
    }

    [Fact]
    public void A_long_first_run_points_at_the_baseline()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<TextBox/>", 12))));
        Assert.Contains("--write-baseline", p.Run("audit", p.Root).Err);
        using var few = Bad();
        Assert.DoesNotContain("--write-baseline", few.Run("audit", few.Root).Err);
    }

    [Fact]
    public void MsBuild_lines()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox/><Button Content=\"x\" IsTabStop=\"False\"/>"));
        var (_, output, _) = p.Run("audit", p.Root, "--msbuild");
        Assert.Contains("Main.xaml(1,1): error XA0001: input-no-name:", output);
        Assert.Contains("Main.xaml(1,1): warning XA0002: not-in-tab-order:", output);
        using var none = new Folder();
        Assert.Contains("error XA0003", none.Run("audit", none.Root, "--msbuild").Out);
    }

    [Theory]
    [InlineData("](http://evil) @everyone ![x](http://evil/x.png)")]
    [InlineData("`code` ``more`` | pipe")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void Markdown_keeps_untrusted_text_inside_code_spans(string hostile)
    {
        var esc = System.Security.SecurityElement.Escape(hostile);
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<TextBlock Text=\"{esc}\"/><TextBox/><Border MouseDown=\"{esc}\"/><Button Content=\"{{StaticResource {esc}}}\"/><Button Content=\"ok\"/>"));
        p.Run("audit", p.Root, "--md", p.At("r.md"), "--min-coverage", "0");
        var md = File.ReadAllText(p.At("r.md"));
        Assert.Contains("evil".Length > 0 && hostile.Contains("evil") ? "evil" : hostile[..3].Trim(), md);
        // 去掉所有 code span 之後，不可信的字一個都不剩
        var outside = System.Text.RegularExpressions.Regex.Replace(md, @"(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)", "");
        Assert.DoesNotContain("evil", outside);
        Assert.DoesNotContain("@everyone", outside);
        Assert.DoesNotContain("<img", outside);
        Assert.DoesNotContain("onerror", outside);
    }

    [Fact]
    public void Console_output_cannot_be_turned_into_ci_commands_or_extra_lines()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBlock Text=\"a&#10;::error::pwned&#10;##vso[task.setvariable variable=x]y\"/><TextBox/><Border MouseDown=\"h&#10;::set-output name=x::y\"/>"));
        var (_, output, err) = p.Run("audit", p.Root, "--msbuild");
        foreach (var line in (output + err).Split('\n'))
        {
            Assert.DoesNotMatch(@"^\s*::", line);
            Assert.DoesNotContain("##vso[", line);
        }
    }

    [Fact]
    public void Every_report_says_what_was_not_looked_at()
    {
        using var p = Bad();
        var (_, output, _) = p.Run("audit", p.Root, "--md", p.At("r.md"));
        Assert.Contains("nothing was run", output);
        Assert.Contains("nothing was run", File.ReadAllText(p.At("r.md")));
        Assert.DoesNotContain("EAA compliant", output);
    }

    [Fact]
    public void Output_is_the_same_whatever_the_culture()
    {
        using var p = Bad();
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var invariant = p.Run("audit", p.Root).Out;
            try { System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE"); }
            catch (System.Globalization.CultureNotFoundException) { return; }
            Assert.Equal(invariant, p.Run("audit", p.Root).Out);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    // ───────── 奇怪的輸入 ─────────

    [Theory]
    [InlineData("<Window")]
    [InlineData("")]
    [InlineData("\0\0\0")]
    [InlineData("<!DOCTYPE x [<!ENTITY a SYSTEM \"file:///etc/passwd\">]><x>&a;</x>")]
    public void Unparseable_files_are_listed(string content)
    {
        using var p = Good().Add("Good2.xaml", Kit.Window("<Button Content=\"ok\"/>")).Add("Broken.xaml", content);
        var (exit, output, _) = p.Run("audit", p.Root);
        Assert.Equal(0, exit);
        Assert.Contains("could not parse Broken.xaml", output);
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
        using var p = Good().Add("Deep.xaml", string.Concat(Enumerable.Repeat("<a>", 5000)) + string.Concat(Enumerable.Repeat("</a>", 5000)));
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

    [Fact]
    public void Help_lists_every_exit_code_and_rules_lists_every_kind()
    {
        using var p = new Folder();
        var help = p.Run("--help");
        Assert.Equal(0, help.Exit);
        Assert.Contains("exit codes", help.Out);
        var rules = p.Run("rules").Out;
        Assert.All(Kinds.All, k => Assert.Contains(k.Slug, rules));
    }
}
