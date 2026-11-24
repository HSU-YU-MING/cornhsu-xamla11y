using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>可解析率與「結果不可信」（exit 3）：沒看到不能當成沒問題。</summary>
public sealed class ResolutionTests
{
    private const string Unknown = "<local:FancyButton/><local:FancyTextBox/><local:FooButton/><Button Content=\"a\"/>";

    [Fact]
    public void The_resolution_gate_is_about_the_rules_that_fail()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"{StaticResource A}\"/><Button Content=\"{StaticResource B}\"/><Button Content=\"{StaticResource C}\"/><Button Content=\"ok\"/>"));
        Assert.Equal(25, p.Audit().Resolution);
        Assert.Equal(3, p.Run("audit", p.Root).Exit);
    }

    [Fact]
    public void Mostly_unparseable_projects_are_not_trustworthy()
    {
        using var p = new Folder().Add("Good.xaml", Kit.Window("<Button Content=\"ok\"/>"));
        for (var i = 0; i < 5; i++) p.Add($"Bad{i}.xaml", "<Window");
        Assert.Equal(3, p.Run("audit", p.Root).Exit);
    }

    [Theory]
    [InlineData("<Button Content=\"{Binding A}\"/><Button Content=\"{Binding B}\"/>", "60", 0)]      // 名稱都是資料：專案改不了，也不是工具漏看
    [InlineData("<local:WhatButton/>", "60", 3)]                                                     // 全部卡住
    [InlineData("<local:WhatButton/>", "0", 0)]
    public void Nothing_judged_is_only_untrustworthy_when_something_was_blocked(string body, string coverage, int exit)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window(body));
        Assert.Equal(exit, p.Run("audit", p.Root, "--min-coverage", coverage).Exit);
    }

    [Fact]
    public void Turning_the_only_rule_off_is_not_an_error()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button/>")).Add("xamla11y.config.json", "{\"rules\":{\"missing-name\":\"off\"}}");
        Assert.Equal(0, p.Run("audit", p.Root).Exit);
    }

    [Fact]
    public void Blocked_reasons_are_counted()
    {
        var r = Kit.Audit("<local:AButton/><local:BButton/><local:CButton/>");
        Assert.Equal(3, Assert.Single(r.Checks.First(s => s.Check == "control-names").BlockedReasons).Value);
    }

    /// <summary>不看的規則看不到東西，不是「結果不可信」。</summary>
    [Fact]
    public void A_rule_that_is_off_does_not_lower_the_resolution()
    {
        const string body = "<Button Content=\"a\"/><Button Content=\"b\" FocusVisualStyle=\"{StaticResource nowhere}\"/>";
        Assert.True(Kit.Audit(body).Resolution < 100);
        var a = Kit.Audit(body, options: new AuditOptions { Scope = new Scope(Standard.Wcag22, "A") });
        Assert.Equal(100, a.Resolution);
        Assert.True(a.Checks.Single(x => x.Check == "focus").Off);
        Assert.False(a.Checks.Single(x => x.Check == "control-names").Off);
        Assert.Equal(100, Kit.Audit(body, options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["focus-not-visible"] = Severity.Off } }).Resolution);
    }

    [Fact]
    public void Everything_off_is_not_a_pass()
    {
        var off = string.Join(", ", Kinds.All.Select(k => $"\"{k.Slug}\": \"off\""));
        using var f = new Folder().Add("M.xaml", Kit.Window(Unknown)).Add("xamla11y.config.json", $"{{ \"rules\": {{ {off} }} }}");
        var r = f.Run("audit", f.Root, "--html", f.At("r.html"));
        Assert.Equal(3, r.Exit);
        Assert.Contains("every rule is turned off", r.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
        Assert.Equal(3, f.Run("audit", f.Root, "--write-baseline", f.At("b.json")).Exit);
        Assert.False(File.Exists(f.At("b.json")));
        Assert.Equal(3, f.Run("audit", f.Root, "--min-coverage", "0").Exit);
    }

    /// <summary>亂碼名稱、只有符號的輸入框也算 輸入框的名稱：只關 input-no-name 不是關掉 輸入框的名稱。</summary>
    [Fact]
    public void One_kind_off_does_not_hide_what_is_blocked()
    {
        const string body = "<local:FancyTextBox/><local:BTextBox/><Button Content=\"a\"/><TextBox AutomationProperties.Name=\"&#xE74D;\"/>";
        AuditResult Run(params string[] off) => Kit.Audit(body, options: new AuditOptions { Severities = off.ToDictionary(x => x, _ => Severity.Off) });
        var r2 = Run("input-no-name").Checks.Single(x => x.Check == "input-names");
        Assert.False(r2.Off);
        Assert.Equal(1, r2.Findings);
        Assert.Equal(Run().Resolution, Run("input-no-name").Resolution);
        Assert.True(Run("input-no-name", "meaningless-name", "symbol-name").Checks.Single(x => x.Check == "input-names").Off);
    }

    [Fact]
    public void The_totals_leave_out_the_rules_that_are_off()
    {
        var all = Kit.Audit(Unknown);
        var r = Kit.Audit(Unknown, options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Off, ["meaningless-name"] = Severity.Off, ["symbol-name"] = Severity.Off } });
        Assert.Equal(all.Blocked - 1, r.Blocked);
        Assert.Equal(all.Candidates - 1, r.Candidates);
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 3));
        Assert.Equal(["input-names"], json.RootElement.GetProperty("summary").GetProperty("checks").EnumerateArray().Where(x => x.GetProperty("off").GetBoolean()).Select(x => x.GetProperty("check").GetString()));
    }
}
