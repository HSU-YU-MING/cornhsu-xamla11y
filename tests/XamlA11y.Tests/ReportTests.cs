using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>JSON、SARIF、MSBuild、Markdown 報告。</summary>
public sealed class ReportTests
{
    [Fact]
    public void The_markdown_report_has_a_heading_per_severity()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><Button Content=\"x\" IsTabStop=\"False\"/>");
        var md = MarkdownReport.Create(r, r.Findings, 1, 0, null);
        Assert.Contains("### Fail (1)", md, StringComparison.Ordinal);
        Assert.Contains("### Warn (1)", md, StringComparison.Ordinal);
    }

    [Fact]
    public void The_json_summary_counts_by_severity()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button Content=\"x\" IsTabStop=\"False\"/><Button Content=\"×\"/>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 1));
        var text = json.RootElement.GetProperty("summary").GetRawText();
        Assert.Contains("\"fail\": 2", text, StringComparison.Ordinal);
        Assert.Contains("\"warn\": 1", text, StringComparison.Ordinal);
        Assert.Contains("\"info\": 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_markdown_only_mentions_what_there_is()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button>");
        var md = MarkdownReport.Create(r, r.Findings, 1, 0, null);
        Assert.DoesNotContain("non-WPF", md, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be parsed", md, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reports_count_each_severity_on_its_own()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button Content=\"x\" IsTabStop=\"False\"/><Button Content=\"y\" IsTabStop=\"False\"/><Button Content=\"×\"/>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 1));
        var text = json.RootElement.GetProperty("summary").GetRawText();
        Assert.Contains("\"fail\": 4", text, StringComparison.Ordinal);
        Assert.Contains("\"warn\": 2", text, StringComparison.Ordinal);
        Assert.Contains("\"info\": 1", text, StringComparison.Ordinal);
        Assert.Contains("**4 fail**, 2 warn, 1 info", MarkdownReport.Create(r, r.Findings, 1, 0, null), StringComparison.Ordinal);
    }

    /// <summary>看不到的要數得出來：列不完要說還有幾個；沒作用的 ignore、不認得的控制項三種報告都要講。</summary>
    [Fact]
    public void Reports_count_what_they_do_not_list()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<!-- xamla11y-ignore --><Button/><local:FancyButton/><Button Content=\"a\"/>"));
        for (var i = 0; i < 60; i++) f.Add($"b{i}.xaml", "<Window");
        var r = f.Run("audit", f.Root, "--html", f.At("r.html"), "--md", f.At("r.md"), "--min-coverage", "0");
        var html = System.Net.WebUtility.HtmlDecode(File.ReadAllText(f.At("r.html")));
        var md = File.ReadAllText(f.At("r.md"));
        Assert.Contains("… and 10 more", html, StringComparison.Ordinal);
        Assert.Contains("… and 40 more file(s) that could not be parsed", md, StringComparison.Ordinal);
        foreach (var text in (string[])[r.Out, html, md])
        {
            Assert.Contains("has no reason", text, StringComparison.Ordinal);
            Assert.Contains("FancyButton ×1", text, StringComparison.Ordinal);
        }
    }

    /// <summary>給程式讀的不隨語言變。</summary>
    [Fact]
    public void Json_sarif_and_msbuild_stay_english()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Kit.Everything, Kit.EverythingTemplate));
        string[] Run(string lang)
        {
            var r = f.Run("audit", f.Root, "--lang", lang, "--json", f.At("r.json"), "--sarif", f.At("r.sarif"), "--msbuild");
            return [File.ReadAllText(f.At("r.json")), File.ReadAllText(f.At("r.sarif")), string.Join('\n', r.Out.Split('\n').Where(l => l.Contains("XA000", StringComparison.Ordinal)))];
        }
        var en = Run("en");
        Assert.Contains("error XA0001", en[2], StringComparison.Ordinal);
        Assert.Equal(en, Run("zh-Hant"));
    }

    [Fact]
    public void Json_has_the_summary_a_machine_needs()
    {
        using var p = Kit.Bad();
        p.Run("audit", p.Root, "--json", p.At("r.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(p.At("r.json")));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("counts").GetProperty("missing-name").GetInt32());
        Assert.Equal(Kinds.Checks.Count, root.GetProperty("summary").GetProperty("checks").GetArrayLength());
        var f = root.GetProperty("findings")[0];
        Assert.Equal("Main.xaml:1", f.GetProperty("site").GetString());
        Assert.Equal("4.1.2, 1.1.1", f.GetProperty("wcag").GetString());
    }

    [Fact]
    public void Sarif_is_valid_enough_for_code_scanning()
    {
        using var p = Kit.Bad();
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
    public void MsBuild_lines()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox/><Button Content=\"x\" IsTabStop=\"False\"/>"));
        var (_, output, _) = p.Run("audit", p.Root, "--msbuild");
        Assert.Contains("Main.xaml(1,1): error XA0001: input-no-name:", output);
        Assert.Contains("Main.xaml(1,1): warning XA0002: not-in-tab-order:", output);
        using var none = new Folder();
        Assert.Contains("error XA0003", none.Run("audit", none.Root, "--msbuild").Out);
    }

    [Fact]
    public void Every_report_says_what_was_not_looked_at()
    {
        using var p = Kit.Bad();
        var (_, output, _) = p.Run("audit", p.Root, "--md", p.At("r.md"));
        Assert.Contains("nothing was run", output);
        Assert.Contains("nothing was run", File.ReadAllText(p.At("r.md")));
        Assert.DoesNotContain("EAA compliant", output);
    }
}
