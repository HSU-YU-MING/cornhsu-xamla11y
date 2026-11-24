using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>設定檔：錯誤說得清楚，旗標優先。</summary>
public sealed class ConfigTests
{
    [Fact]
    public void Severities_can_be_changed_or_turned_off()
    {
        var off = Kit.Audit("<TextBox/>", options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Off } });
        Assert.Empty(off.Findings);
        Assert.Equal(1, off.NotJudged);
        Assert.Equal(Severity.Info, Assert.Single(Kit.Audit("<TextBox/>", options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Info } }).Findings).Severity);
    }

    [Theory]
    [InlineData("\"a:B\":\"Button\"")]
    [InlineData("\"B\":\"a:Button\"")]
    [InlineData("\" \":\"Button\"")]
    [InlineData("\"B\":\"\"")]
    [InlineData("\"B\":\"Nothing\"")]
    public void A_wrong_treatAs_is_an_error(string entry) => Assert.NotEmpty(A11yConfig.Parse($"{{\"treatAs\":{{{entry}}}}}").Problems);

    [Theory]
    [InlineData("-1", false)]
    [InlineData("-0.5", false)]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("100.5", false)]
    [InlineData("\"NaN\"", false)]
    public void The_coverage_setting_is_a_percentage(string value, bool valid) => Assert.Equal(valid, A11yConfig.Parse($"{{\"minCoverage\":{value}}}").Problems.Count == 0);

    [Theory]
    [InlineData("{ \"standrad\": \"wcag22\" }", "standrad: is not a field; the fields are")]
    [InlineData("{ \"rules\": { \"list-names\": \"off\" } }", "is a group; name its findings instead: list-no-name, widget-no-name")]
    [InlineData("{ \"rules\": { \"keyboard\": \"off\" } }", "is a group; name its findings instead: mouse-only, not-in-tab-order")]
    [InlineData("{ \"rules\": { \"missing\": \"off\" } }", "is not a finding; they are missing-name")]
    public void Config_mistakes_in_plain_words(string json, string expected)
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<Button/>")).Add("xamla11y.config.json", json);
        var r = f.Run("audit", f.Root);
        Assert.Equal(2, r.Exit);
        Assert.Contains(expected, r.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(".NET", r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_line_wins_over_the_config_file()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Kit.Everything, Kit.EverythingTemplate)).Add("xamla11y.config.json", "{ \"standard\": \"section508\", \"level\": \"A\", \"language\": \"zh-Hant\" }");
        var fromFile = f.Run("audit", f.Root);
        Assert.Contains("個 XAML 檔，Section 508 (WCAG 2.0 A)", fromFile.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("small-target", fromFile.Out, StringComparison.Ordinal);
        var flags = f.Run("audit", f.Root, "--standard", "wcag22", "--level", "AA", "--lang", "en");
        Assert.Contains("XAML file(s), WCAG 2.2 A + AA", flags.Out, StringComparison.Ordinal);
        Assert.Contains("small-target", flags.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("standard", "WCAG22")]
    [InlineData("standard", "")]
    [InlineData("level", "AAA")]
    [InlineData("language", "zh-TW")]
    public void Wrong_config_values_are_refused(string field, string value)
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<Button/>")).Add("xamla11y.config.json", $"{{ \"{field}\": \"{value}\" }}");
        var r = f.Run("audit", f.Root);
        Assert.Equal(2, r.Exit);
        Assert.Contains(field + ": must be", r.Err, StringComparison.Ordinal);
    }

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
        using var p = Kit.Good().Add("xamla11y.config.json", json);
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
}
