using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>選標準與等級、條號、中文、HTML、人工檢查清單、NVDA 的念法。</summary>
public class ReportTests
{
    private static readonly string Icon = Kit.Icon;

    private const string Everything = """
        <Button ToolTip="Delete"><Path Data="M0,0 L9,9 Z"/></Button>
        <Button><StackPanel><TextBlock Text="Save"/></StackPanel></Button>
        <Button FontFamily="Segoe MDL2 Assets" Content="&#xE74D;"/>
        <Button Content="×"/>
        <Label Target="{Binding ElementName=n}" Content="Name"/><TextBox x:Name="n"/>
        <Border MouseLeftButtonUp="OnOpen"/>
        <Button Content="a" IsTabStop="False"/>
        <Button Content="b" FocusVisualStyle="{x:Null}" Template="{StaticResource T}"/>
        <GroupBox Header="Files"><ListBox/></GroupBox>
        <ProgressBar/><ContentControl/>
        <StackPanel><Button Content="Edit" Click="A"/><Button Content="Edit" Click="B"/></StackPanel>
        <StackPanel Orientation="Horizontal"><Button Content="c" Width="18" Height="30"/><Button Content="d" Width="18" Height="30"/></StackPanel>
        """;

    private const string T = "<ControlTemplate x:Key=\"T\" TargetType=\"Button\"><Border><ContentPresenter/></Border></ControlTemplate>";

    private static AuditResult All(Scope? scope = null, Dictionary<string, Severity>? rules = null) =>
        Kit.Audit(Everything, T, options: new AuditOptions { Scope = scope ?? Scope.Default, Severities = rules ?? [] });

    /// <summary>上面那一組有的種類（item-type-name 要有 C#）。</summary>
    private static IEnumerable<string> Here => Kinds.All.Select(k => k.Slug).Where(s => s != "item-type-name");

    private static string[] Slugs(AuditResult r) => [.. r.Findings.Select(f => f.Kind.Slug).Distinct().Order(StringComparer.Ordinal)];

    // ───────────────────────── 標準與等級 ─────────────────────────

    [Fact]
    public void Every_kind_is_found_by_default() => Assert.Equal(Here.Order(StringComparer.Ordinal), Slugs(All()));

    [Theory]
    [InlineData("wcag22", "AA", "")]
    [InlineData("wcag21", "AA", "small-target")]                     // 2.5.8 是 2.2 才有
    [InlineData("wcag20", "AA", "small-target")]
    [InlineData("en301549", "AA", "small-target")]                   // V3.2.1 用 WCAG 2.1
    [InlineData("en301549-v4", "AA", "")]
    [InlineData("section508", "AA", "small-target")]
    [InlineData("wcag22", "A", "focus-not-visible,same-name,small-target")]    // 2.4.7、2.4.6、2.5.8 是 AA
    [InlineData("section508", "A", "focus-not-visible,same-name,small-target")]
    public void A_standard_decides_what_is_reported(string id, string level, string missing)
    {
        var found = Slugs(All(new Scope(Standard.ById(id)!, level)));
        Assert.Equal(missing, string.Join(",", Here.Except(found).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Criteria_outside_the_standard_are_not_quoted()
    {
        Assert.Equal("4.1.2 (A), 2.5.3 (A)", Scope.Default.WcagOf(Kinds.TextNotName));
        Assert.Equal("4.1.2 (A)", new Scope(Standard.Section508, "AA").WcagOf(Kinds.TextNotName));      // 2.5.3 是 2.1 才有
        Assert.Equal("–", new Scope(Standard.Wcag21, "AA").WcagOf(Kinds.SmallTarget));
    }

    [Fact]
    public void A_severity_in_the_config_wins_over_the_standard()
    {
        var r = All(new Scope(Standard.Wcag20, "A"), new Dictionary<string, Severity> { ["small-target"] = Severity.Fail, ["missing-name"] = Severity.Off });
        Assert.Contains("small-target", Slugs(r));
        Assert.DoesNotContain("missing-name", Slugs(r));
        Assert.Contains(r.Rules.Single(x => x.Rule == "R1").NotJudgedReasons.Keys, k => k == "missing-name is turned off");
        Assert.Contains(r.Rules.Single(x => x.Rule == "R3").NotJudgedReasons.Keys, k => k == "focus-not-visible is not in WCAG 2.0 A");
    }

    /// <summary>不看的規則看不到東西，不是「結果不可信」。</summary>
    [Fact]
    public void A_rule_that_is_off_does_not_lower_the_resolution()
    {
        const string body = "<Button Content=\"a\"/><Button Content=\"b\" FocusVisualStyle=\"{StaticResource nowhere}\"/>";
        Assert.True(Kit.Audit(body).Resolution < 100);
        var a = Kit.Audit(body, options: new AuditOptions { Scope = new Scope(Standard.Wcag22, "A") });
        Assert.Equal(100, a.Resolution);
        Assert.True(a.Rules.Single(x => x.Rule == "R3").Off);
        Assert.False(a.Rules.Single(x => x.Rule == "R1").Off);
        Assert.Equal(100, Kit.Audit(body, options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["focus-not-visible"] = Severity.Off } }).Resolution);
    }

    /// <summary>條號對過 EN 301 549 兩版的 PDF 與 Section 508 的原文（2026-11-24）。</summary>
    [Theory]
    [InlineData("missing-name", "en301549", "11.4.1.2.1,11.1.1.1.1,11.5.2.5")]
    [InlineData("text-not-in-name", "en301549", "11.4.1.2.1,11.2.5.3.1,11.5.2.5")]
    [InlineData("meaningless-name", "en301549", "11.4.1.2.1,11.1.1.1.1")]          // 名稱不是空的：沒有 11.5.2.5
    [InlineData("symbol-name", "en301549", "11.1.1.1.1")]
    [InlineData("input-no-name", "en301549", "11.4.1.2.1,11.1.3.1.1,11.5.2.5")]
    [InlineData("focus-not-visible", "en301549", "11.2.4.7")]
    [InlineData("mouse-only", "en301549", "11.2.1.1.1")]
    [InlineData("not-in-tab-order", "en301549", "11.2.1.1.1")]
    [InlineData("small-target", "en301549", "")]
    [InlineData("list-no-name", "en301549", "11.4.1.2.1,11.1.3.1.1,11.5.2.5")]
    [InlineData("missing-name", "en301549-v4", "11.4.1.2,11.1.1.1,11.5.2.5")]
    [InlineData("text-not-in-name", "en301549-v4", "11.4.1.2,11.2.5.3,11.5.2.5")]
    [InlineData("input-no-name", "en301549-v4", "11.4.1.2,11.1.3.1,11.5.2.5")]
    [InlineData("focus-not-visible", "en301549-v4", "11.2.4.7")]
    [InlineData("mouse-only", "en301549-v4", "11.2.1.1")]
    [InlineData("small-target", "en301549-v4", "11.2.5.8")]
    [InlineData("same-name", "en301549", "11.2.4.6")]
    [InlineData("same-name", "section508", "E207.2")]
    [InlineData("widget-no-name", "en301549-v4", "11.4.1.2,11.5.2.5")]
    [InlineData("item-type-name", "en301549-v4", "11.4.1.2")]
    [InlineData("silent-tab-stop", "section508", "E207.2")]
    [InlineData("missing-name", "section508", "E207.2,502.3.1")]
    [InlineData("text-not-in-name", "section508", "E207.2,502.3.1")]
    [InlineData("meaningless-name", "section508", "E207.2")]
    [InlineData("focus-not-visible", "section508", "E207.2")]
    [InlineData("mouse-only", "section508", "E207.2")]
    [InlineData("small-target", "section508", "")]
    [InlineData("missing-name", "wcag22", "")]
    public void Clauses(string kind, string standard, string expected) =>
        Assert.Equal(expected, string.Join(",", new Scope(Standard.ById(standard)!, "AA").ClausesOf(Kinds.BySlug(kind)!)));

    [Theory]
    [InlineData("wcag22", "AA", "WCAG 2.2 A + AA")]
    [InlineData("wcag21", "A", "WCAG 2.1 A")]
    [InlineData("en301549", "AA", "EN 301 549 V3.2.1 (WCAG 2.1 A + AA)")]
    [InlineData("en301549-v4", "AA", "EN 301 549 V4.1.1 (WCAG 2.2 A + AA)")]
    [InlineData("section508", "AA", "Section 508 (WCAG 2.0 A + AA)")]
    public void The_scope_says_what_it_is(string id, string level, string title) => Assert.Equal(title, new Scope(Standard.ById(id)!, level).Title);

    [Fact]
    public void The_command_line_wins_over_the_config_file()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Everything, T)).Add("xamla11y.config.json", "{ \"standard\": \"section508\", \"level\": \"A\", \"language\": \"zh-Hant\" }");
        var fromFile = f.Run("audit", f.Root);
        Assert.Contains("標準：Section 508 (WCAG 2.0 A)", fromFile.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("small-target（", fromFile.Out, StringComparison.Ordinal);
        var flags = f.Run("audit", f.Root, "--standard", "wcag22", "--level", "AA", "--lang", "en");
        Assert.Contains("standard         WCAG 2.2 A + AA", flags.Out, StringComparison.Ordinal);
        Assert.Contains("small-target", flags.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--standard", "wcag30", "--standard takes one of: wcag22, wcag21, wcag20, en301549, en301549-v4, section508")]
    [InlineData("--level", "AAA", "--level takes 'A' or 'AA'")]
    [InlineData("--level", "a", "--level takes 'A' or 'AA'")]
    [InlineData("--lang", "zh", "--lang takes one of: en, zh-Hant")]
    [InlineData("--lang", "ZH-HANT", "--lang takes one of: en, zh-Hant")]
    public void Wrong_values_are_refused(string flag, string value, string message)
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<Button/>"));
        var r = f.Run("audit", f.Root, flag, value);
        Assert.Equal(2, r.Exit);
        Assert.Contains(message, r.Err, StringComparison.Ordinal);
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

    [Fact]
    public void The_json_has_the_scope_and_every_standards_clauses()
    {
        var r = All(new Scope(Standard.Section508, "AA"));
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 1));
        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal("section508", summary.GetProperty("standard").GetString());
        Assert.Equal("AA", summary.GetProperty("level").GetString());
        Assert.Equal("2.0", summary.GetProperty("wcagVersion").GetString());
        var text = json.RootElement.GetProperty("findings").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "text-not-in-name");
        Assert.Equal("4.1.2", text.GetProperty("wcag").GetString());
        Assert.Equal("11.2.5.3", text.GetProperty("clauses").GetProperty("en301549-v4")[1].GetString());
        Assert.Equal("502.3.1", text.GetProperty("clauses").GetProperty("section508")[1].GetString());
        Assert.Equal(ManualCheck.In(r.Scope).Count, json.RootElement.GetProperty("manualChecks").GetArrayLength());
        Assert.Contains("\"502.3.1\"", SarifReport.Create(r.Findings, "1.0"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_baseline_key_does_not_depend_on_the_standard_or_the_language()
    {
        var a = All();
        var b = All(new Scope(Standard.Section508, "AA"));
        Assert.Subset(a.Findings.Select(f => f.BaselineKey).ToHashSet(), b.Findings.Select(f => f.BaselineKey).ToHashSet());
    }

    // ───────────────────────── 關掉的規則不能看起來像通過（獨立審查） ─────────────────────────

    private const string Unknown = "<local:FancyButton/><local:FancyTextBox/><local:FooButton/><Button Content=\"a\"/>";

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

    /// <summary>亂碼名稱、只有符號的輸入框也算 R2：只關 input-no-name 不是關掉 R2。</summary>
    [Fact]
    public void One_kind_off_does_not_hide_what_is_blocked()
    {
        const string body = "<local:FancyTextBox/><local:BTextBox/><Button Content=\"a\"/><TextBox AutomationProperties.Name=\"&#xE74D;\"/>";
        AuditResult Run(params string[] off) => Kit.Audit(body, options: new AuditOptions { Severities = off.ToDictionary(x => x, _ => Severity.Off) });
        var r2 = Run("input-no-name").Rules.Single(x => x.Rule == "R2");
        Assert.False(r2.Off);
        Assert.Equal(1, r2.Findings);
        Assert.Equal(Run().Resolution, Run("input-no-name").Resolution);
        Assert.True(Run("input-no-name", "meaningless-name", "symbol-name").Rules.Single(x => x.Rule == "R2").Off);
    }

    [Fact]
    public void The_totals_leave_out_the_rules_that_are_off()
    {
        var all = Kit.Audit(Unknown);
        var r = Kit.Audit(Unknown, options: new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Off, ["meaningless-name"] = Severity.Off, ["symbol-name"] = Severity.Off } });
        Assert.Equal(all.Blocked - 1, r.Blocked);
        Assert.Equal(all.Candidates - 1, r.Candidates);
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 3));
        Assert.Equal(["R2"], json.RootElement.GetProperty("summary").GetProperty("rules").EnumerateArray().Where(x => x.GetProperty("off").GetBoolean()).Select(x => x.GetProperty("rule").GetString()));
    }

    /// <summary>名稱存在、念不出意思的清單：不是「名稱要能由程式判定」那一條。</summary>
    [Fact]
    public void An_unreadable_list_name_is_not_an_empty_one()
    {
        var r = Kit.Audit("<ListBox AutomationProperties.Name=\"&#xE74D;\"/><TreeView/>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 0));
        string[] Clauses(int i) => [.. json.RootElement.GetProperty("findings")[i].GetProperty("clauses").GetProperty("section508").EnumerateArray().Select(x => x.GetString()!)];
        var list = r.Findings[0].Element == "ListBox" ? 0 : 1;
        Assert.Equal(["E207.2"], Clauses(list));
        Assert.Equal(["E207.2", "502.3.1"], Clauses(1 - list));
        Assert.Equal(1, Regex.Count(Html(r), "502\\.3\\.1"));
    }

    [Fact]
    public void Sarif_follows_the_chosen_standard()
    {
        var r = All(new Scope(Standard.Section508, "AA"));
        var sarif = SarifReport.Create(r.Findings, "1.0", r.Scope);
        Assert.DoesNotContain("\"WCAG 2.5.", sarif, StringComparison.Ordinal);
        Assert.Contains("Section 508 (WCAG 2.0 A + AA): 4.1.2 (A)", sarif, StringComparison.Ordinal);
        Assert.Contains("\"WCAG 2.5.8\"", SarifReport.Create(r.Findings, "1.0"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_baseline_does_not_claim_that_things_were_fixed()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Everything, T));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        Assert.Contains("fixed since, or not in WCAG 2.2 A;", f.Run("audit", f.Root, "--level", "A", "--baseline", f.At("b.json")).Out, StringComparison.Ordinal);
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

    [Fact]
    public void A_kind_turned_on_from_outside_the_standard_says_so() =>
        Assert.Contains("Not in WCAG 2.1 A + AA; the config file turns it on.", Html(All(new Scope(Standard.Wcag21, "AA"), new Dictionary<string, Severity> { ["small-target"] = Severity.Warn })), StringComparison.Ordinal);

    /// <summary>很長的鍵：句子的頭尾要留著，中文才翻得到。</summary>
    [Fact]
    public void Long_keys_are_cut_in_the_middle()
    {
        var r = Kit.Audit($"<Button Content=\"{{StaticResource {new string('K', 300)}}}\"/><Button Content=\"a\" Style=\"{{StaticResource {new string('S', 300)}}}\" FocusVisualStyle=\"{{StaticResource {new string('F', 300)}}}\"/>");
        var reasons = r.Rules.SelectMany(x => x.BlockedReasons.Keys).ToList();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, x => { Assert.True(x.Length <= 200); Assert.True(Zh.TryReason(x, out _), x); });
        Assert.Equal(200, Context.CutMiddle(string.Concat(Enumerable.Repeat("😀", 400)), 200).EnumerateRunes().Sum(x => x.Utf16SequenceLength) + (200 - Context.CutMiddle(string.Concat(Enumerable.Repeat("😀", 400)), 200).Length));
        Assert.DoesNotContain("\uFFFD", string.Concat(Context.CutMiddle(string.Concat(Enumerable.Repeat("😀", 400)), 200).EnumerateRunes()), StringComparison.Ordinal);
    }

    /// <summary>看不見的字：BMP 外的標籤字元、行分隔、諺文填充也算。</summary>
    [Theory]
    [InlineData(0xE0041, "\\U000e0041")]
    [InlineData(0x3164, "\\u3164")]
    [InlineData(0x2028, "\\u2028")]
    [InlineData(0x85, "\\u0085")]
    [InlineData(0x202E, "\\u202e")]
    [InlineData(0xAD, "\\u00ad")]
    public void Invisible_characters_are_made_visible(int code, string shown)
    {
        var text = "a" + char.ConvertFromUtf32(code) + "b";
        Assert.Equal("a" + shown + "b", Text.Visible(text));
        Assert.Equal("a" + shown + "b", Text.Safe(text));
        Assert.Equal("👩‍💻 é 中", Text.Visible("👩‍💻 é 中"));
    }

    // ───────────────────────── 人工檢查 ─────────────────────────

    [Fact]
    public void The_manual_checks_follow_the_standard()
    {
        Assert.Equal(18, ManualCheck.In(Scope.Default).Count);
        var old = ManualCheck.In(new Scope(Standard.Wcag20, "AA")).SelectMany(m => m.Criteria).ToList();
        Assert.All(old, c => Assert.Equal("2.0", c.Since));
        Assert.DoesNotContain(old, c => c.Number is "2.5.7" or "2.4.11" or "3.3.7" or "1.4.11");
        Assert.All(ManualCheck.In(new Scope(Standard.Wcag22, "A")).SelectMany(m => m.Criteria), c => Assert.Equal("A", c.Level));
        // 一條在、一條不在的：只列在的
        Assert.Equal(["3.3.1"], ManualCheck.In(new Scope(Standard.Wcag22, "A")).Single(m => m.Check.Criteria.Contains("3.3.1")).Criteria.Select(c => c.Number));
    }

    [Theory]
    [InlineData("docs/standards.md")]
    [InlineData("docs/標準對照.md")]
    public void The_documents_list_the_same_manual_checks_as_the_reports(string file)
    {
        var zh = file.Contains("標準", StringComparison.Ordinal);
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        var rows = Regex.Matches(text[text.LastIndexOf("| WCAG 2.2 |", StringComparison.Ordinal)..], @"^\| (?<c>\d\.\d[^|]+) \| (?<t>[^|]+) \|", RegexOptions.Multiline);
        Assert.Equal(ManualCheck.All.Count, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var check = ManualCheck.All[i];
            Assert.Equal(check.Criteria, Regex.Matches(rows[i].Groups["c"].Value, @"\d\.\d\.\d+").Select(m => m.Value));
            Assert.Equal(zh ? check.Zh : check.En, rows[i].Groups["t"].Value.Replace("`", "", StringComparison.Ordinal));
            foreach (var c in check.Criteria)
                Assert.Contains((zh ? Wcag.Criteria[c].ZhName.Replace("(", "（", StringComparison.Ordinal).Replace(")", "）", StringComparison.Ordinal) : Wcag.Criteria[c].Name), rows[i].Groups["c"].Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>兩份標準文件的條號表就是程式裡的。</summary>
    [Theory]
    [InlineData("docs/standards.md", "the name is empty")]
    [InlineData("docs/標準對照.md", "名稱是空的")]
    public void The_documents_quote_the_clauses_the_program_uses(string file, string last)
    {
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        Standard[] columns = [Standard.En301549, Standard.En301549V4, Standard.Section508];
        foreach (var c in Kinds.All.SelectMany(k => k.Criteria).Distinct())
            Assert.Contains($"| {c} | {string.Join(" | ", columns.Select(s => s.Clauses.GetValueOrDefault(c, "–")))} |", text, StringComparison.Ordinal);
        Assert.Contains($"| {last} | {string.Join(" | ", columns.Select(s => s.EmptyName))} |", text, StringComparison.Ordinal);
        foreach (var s in Standard.All) Assert.Contains($"`{s.Id}`", text.Replace("--standard ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    // ───────────────────────── 中文 ─────────────────────────

    [Fact]
    public void Every_kind_and_rule_has_chinese()
    {
        Assert.Equal(Kinds.All.Select(k => k.Slug).Order(StringComparer.Ordinal), Zh.Kinds.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Kinds.Rules, Zh.Rules.Keys);
        Assert.All(Zh.Kinds.Values, v => Assert.True(v.Title.Length > 0 && v.Why.Length > 0 && v.How.Length > 0));
    }

    /// <summary>規則真的會寫出來的訊息，每一筆都要翻得到（所有錄過的 WPF 案例、示範專案、上面那一組）。</summary>
    [Fact]
    public void Every_message_the_rules_write_has_chinese()
    {
        var facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json"))).RootElement;
        var xaml = facts.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).SelectMany(p => p.Value.EnumerateArray())
            .Where(x => x.TryGetProperty("xaml", out _)).Select(x => x.GetProperty("xaml").GetString()!).ToList();
        Assert.True(xaml.Count > 300);
        var results = xaml.Select(x => Auditor.Run(Project.FromText(x))).Append(All()).ToList();
        results.Add(Auditor.Run(Project.Load(Path.Combine(ActionContractTests.RepoRoot(), "samples", "demo"), [])));
        var missing = results.SelectMany(r => r.Findings).Select(f => f.Message).Where(m => !Zh.TryMessage(m, out _))
            .Concat(results.SelectMany(r => r.Rules).SelectMany(s => s.BlockedReasons.Keys.Concat(s.NotJudgedReasons.Keys)).Where(r => !Zh.TryReason(r, out _)))
            .Distinct().ToList();
        Assert.Empty(missing);
    }

    /// <summary>程式裡寫的原因字串，一個一個都要翻得到：有些要很特別的 XAML 才走得到，上面那一條抓不到。</summary>
    [Theory]
    [InlineData("the labelling chain is too deep")]
    [InlineData("its content is too complex to follow")]
    [InlineData("the code-behind sets its accessible name")]
    [InlineData("<Foo> is not a control this tool knows")]
    [InlineData("a trigger sets its accessible name")]
    [InlineData("its accessible name is resource 'a' is defined differently in 2 places")]
    [InlineData("the name is bound to Tag, which is an object")]
    [InlineData("the name is bound to Tag")]
    [InlineData("the name comes from Binding")]
    [InlineData("its style is outside the project, so whether MaterialDesignThemes still names it from HintAssist.Hint is unknown")]
    [InlineData("MaterialDesignThemes names it from HintAssist.Hint only where its theme is merged, and that was not found in the XAML")]
    [InlineData("MaterialDesignThemes 5.3 names it from HintAssist.Hint; the project's MaterialDesignThemes version was not found")]
    [InlineData("MaterialDesignThemes 5.3 names it from HintAssist.Hint; the project uses 4.9, 5.1, which was not measured")]
    [InlineData("LabeledBy is set to an element this tool does not know")]
    [InlineData("LabeledBy is set to something this tool cannot follow")]
    [InlineData("LabeledBy points at 'a', which is not in this file")]
    [InlineData("LabeledBy points at <Foo>, which this tool does not know")]
    [InlineData("it has no Content until a trigger sets one")]
    [InlineData("a trigger changes its Content; some values name it and some do not")]
    [InlineData("the code-behind sets its Content")]
    [InlineData("the root of A.xaml is not a control this tool knows")]
    [InlineData("its class sets its Content in code")]
    [InlineData("its style is outside the project and may set the content")]
    [InlineData("its Command is a resource this tool cannot read, which may be a RoutedUICommand")]
    [InlineData("its Command may be a RoutedUICommand, whose text would be the name")]
    [InlineData("its content is {x:Static a:Icons.B}, which looks like an icon")]
    [InlineData("its content is {DynamicResource a}, which this tool cannot resolve")]
    [InlineData("its content is an icon ({md:PackIcon})")]
    [InlineData("its content comes from the markup extension {a:B …}")]
    [InlineData("its content is nested or aliased too deeply")]
    [InlineData("its content is a resource this tool cannot read")]
    [InlineData("resource 'a' is not defined in the project")]
    [InlineData("its content is a LinearGradientBrush, so its name is the type name, a color code or a file path")]
    [InlineData("its content is only <Icon>, which looks like an icon")]
    [InlineData("its content is <Foo>, and this tool does not know what text that gives")]
    [InlineData("its content is a TextBox, whose text becomes the name")]
    [InlineData("its content is a RichTextBox, whose text becomes the name")]
    [InlineData("its content is <Path>, which gives no text; its name becomes the type name")]
    [InlineData("its Text is set by a style this tool cannot read")]
    [InlineData("a style may set the text of its Run")]
    [InlineData("its class sets its text in code")]
    [InlineData("its text is drawn as an icon by the font 'FontAwesome'; a screen reader reads the raw characters")]
    [InlineData("the template comes from the theme 'X', which was not measured for Button")]
    [InlineData("the theme 'X' was only measured with MaterialDesign3.Defaults.xaml")]
    [InlineData("the template comes from the theme 'X'; measured with MahApps.Metro 2.4, the project uses an unknown version")]
    [InlineData("the template comes from the theme 'X'; measured with MahApps.Metro 2.4, the project uses 1.6")]
    [InlineData("the Button template of the theme 'X' shows focus only through FocusVisualStyle (measured)")]
    [InlineData("several themes apply and they differ")]
    [InlineData("its Slider template may show focus on its parts")]
    [InlineData("a ToolBar gives it a style of its own")]
    [InlineData("<CheckBox> has a Style this tool cannot read")]
    [InlineData("style 'a' is not defined in the project")]
    [InlineData("based on 'a', which is not defined in the project")]
    [InlineData("more than 64 style definitions could apply")]
    [InlineData("the BasedOn chain is too deep")]
    [InlineData("a style in the chain has a BasedOn this tool cannot read")]
    [InlineData("the ItemContainerStyle of its list is something this tool cannot read")]
    [InlineData("not a control the keyboard goes to, or a base type ((headered toggle))")]
    [InlineData("a Setter without a Value")]
    [InlineData("a resource key this tool cannot read")]
    [InlineData("a resource with a type key")]
    [InlineData("defined differently in 3 styles with the same key or target")]
    [InlineData("the Template is not a ControlTemplate this tool can read")]
    [InlineData("small-target is not in WCAG 2.1 A + AA")]
    [InlineData("small-target is turned off")]
    public void Each_reason_has_chinese(string reason)
    {
        Assert.True(Zh.TryReason(reason, out var zh), reason);
        Assert.NotEqual(reason, zh);
    }

    [Theory]
    [InlineData("<Button> has no accessible name: its content is <Path>, which gives no text; its ToolTip is the help text, not the name", "<Button> 沒有名稱：內容是 <Path>，給不出文字；ToolTip 是說明文字，不是名稱")]
    [InlineData("<ExtendedButton> has no accessible name: it has no Content and no AutomationProperties.Name; its Text property belongs to the class and is not the name", "<ExtendedButton> 沒有名稱：沒有 Content，也沒有 AutomationProperties.Name；Text 是它自己的屬性，不是名稱")]
    [InlineData("<TextBox> has no accessible name", "<TextBox> 沒有名稱")]
    [InlineData("<TextBox> has no accessible name; if the Label \"a\" at line 3 is its label, use AutomationProperties.LabeledBy", "<TextBox> 沒有名稱；第 3 行的 Label「a」如果是它的標籤，用 AutomationProperties.LabeledBy 接起來")]
    [InlineData("<Border> reacts to a LeftClick MouseBinding ({Binding Open}) but the keyboard cannot reach it", "<Border> 處理 LeftClick 的 MouseBinding ({Binding Open})，鍵盤到不了")]
    [InlineData("<Button> is 18.5 DIP high and 2 DIP away from <Button> 'b'; targets under 24 need more space around them", "<Button> 高 18.5 DIP，離 <Button> 'b' 只有 2 DIP；小於 24 的要留更多空間")]
    [InlineData("<Button> has Focusable=\"False\" (from the style at a b.xaml:3), so Tab never reaches it", "<Button> 設了 Focusable=\"False\"（樣式在 a b.xaml:3），Tab 到不了")]
    [InlineData("The style 'A' removes FocusVisualStyle and nothing else shows keyboard focus (as used by the style for Button): its template has no focus trigger and no Focused state", "樣式「A」拿掉 FocusVisualStyle，又沒有別的東西表示焦點（樣式（Button 的）用到它）：範本沒有焦點觸發器，也沒有 Focused 狀態")]
    public void Messages_in_chinese(string en, string zh) => Assert.Equal(zh, Lang.ZhHant.Message(en));

    /// <summary>認不得的照英文印，不會掉字；訊息裡 repo 的字不能讓翻譯跑不完。</summary>
    [Fact]
    public void An_unknown_message_stays_as_it_is()
    {
        Assert.Equal("something new", Lang.ZhHant.Message("something new"));
        Assert.Equal("<Button> has no accessible name: something new", Lang.ZhHant.Message("<Button> has no accessible name: something new"));
        var evil = "<Button> has no accessible name: " + string.Concat(Enumerable.Repeat("; its ToolTip is the help text, not the name", 2000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Lang.ZhHant.Message(evil);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
    }

    [Fact]
    public void The_chinese_console_and_markdown_have_no_english_sentences_left()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Everything + "<FancyButton/>", T));
        var md = f.At("r.md");
        var r = f.Run("audit", f.Root, "--lang", "zh-Hant", "--md", md);
        foreach (var text in (string[])[r.Out, File.ReadAllText(md)])
            foreach (var word in (string[])["has no accessible", "which gives", "could not", "finding(s)", "blocked", "judged", "does not", "Check it"])
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
        Assert.Contains("標準：WCAG 2.2 A + AA", r.Out, StringComparison.Ordinal);
        Assert.Contains("要人檢查的 18 項", File.ReadAllText(md), StringComparison.Ordinal);
    }

    /// <summary>給程式讀的不隨語言變。</summary>
    [Fact]
    public void Json_sarif_and_msbuild_stay_english()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Everything, T));
        string[] Run(string lang)
        {
            var r = f.Run("audit", f.Root, "--lang", lang, "--json", f.At("r.json"), "--sarif", f.At("r.sarif"), "--msbuild");
            return [File.ReadAllText(f.At("r.json")), File.ReadAllText(f.At("r.sarif")), string.Join('\n', r.Out.Split('\n').Where(l => l.Contains("XA000", StringComparison.Ordinal)))];
        }
        var en = Run("en");
        Assert.Contains("error XA0001", en[2], StringComparison.Ordinal);
        Assert.Equal(en, Run("zh-Hant"));
    }

    // ───────────────────────── HTML ─────────────────────────

    private static string Html(AuditResult r, int exit = 1, Lang? lang = null, string? why = null) => HtmlReport.Create(r, r.Findings, exit, 0, why, "1.0", lang);

    /// <summary>除了 DOCTYPE 與 void 元素，寫成 XML 也通：標籤都有關好。</summary>
    private static XElement Parse(string html) =>
        XElement.Parse(Regex.Replace(html, @"<!DOCTYPE html>|<meta[^>]*>", ""), LoadOptions.PreserveWhitespace);

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hant")]
    public void The_html_is_well_formed_and_accessible(string lang)
    {
        var html = Html(All(), lang: Lang.ById(lang));
        var root = Parse(html);
        Assert.Equal(lang, (string?)root.Attribute("lang"));
        Assert.Single(root.Descendants("h1"));
        Assert.Single(root.Descendants("main"));
        var levels = root.Descendants().Where(e => Regex.IsMatch(e.Name.LocalName, "^h[1-6]$")).Select(e => e.Name.LocalName[1] - '0').ToList();
        for (var i = 1; i < levels.Count; i++) Assert.True(levels[i] <= levels[i - 1] + 1, string.Join(",", levels));
        foreach (var table in root.Descendants("table"))
        {
            Assert.NotEqual("", table.Element("caption")!.Value);
            Assert.All(table.Descendants("th"), th => Assert.NotNull(th.Attribute("scope")));
            var columns = table.Descendants("thead").Single().Descendants("th").Count();
            Assert.All(table.Element("tbody")!.Elements("tr"), tr => Assert.Equal(columns, tr.Elements().Count()));
        }
        Assert.DoesNotContain("tabindex", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        // 連結只有「寫法範例」；沒有任何東西會自己載入
        Assert.DoesNotContain("https://", Regex.Replace(html, "<a href=\"https://github\\.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/[^\"]+\">", ""), StringComparison.Ordinal);
        Assert.DoesNotContain(root.Descendants(), e => e.Attribute("src") is not null || e.Name.LocalName is "link" or "img" or "iframe");
        foreach (var slug in Here) Assert.Contains(slug, html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_html_says_what_where_why_and_which_clause()
    {
        var en = Html(All());
        Assert.Contains("button, Delete", en, StringComparison.Ordinal);
        Assert.Contains("WCAG 4.1.2 Name, Role, Value (A)", en, StringComparison.Ordinal);
        Assert.Contains("EN 301 549 V4.1.1: 11.4.1.2, 11.1.1.1, 11.5.2.5", en, StringComparison.Ordinal);
        Assert.Contains("Section 508: E207.2, 502.3.1", en, StringComparison.Ordinal);
        Assert.Contains("<code>" + All().Findings[0].Site + "</code>", en, StringComparison.Ordinal);
        var only = Html(All(new Scope(Standard.Section508, "AA")), lang: Lang.ZhHant);
        Assert.Contains("Section 508: E207.2, 502.3.1", only, StringComparison.Ordinal);
        Assert.DoesNotContain("11.4.1.2", only, StringComparison.Ordinal);
        Assert.Contains("按鈕, Delete", only, StringComparison.Ordinal);
        Assert.Contains("WCAG 4.1.2 名稱、角色和值 (A)", only, StringComparison.Ordinal);
    }

    [Fact]
    public void The_html_escapes_everything_from_the_repository()
    {
        const string evil = "&lt;script&gt;alert(1)&lt;/script&gt;&quot; onload=&quot;x &#x202E;abc";
        var r = Kit.Audit($"<Button ToolTip=\"{evil}\">{Icon}</Button><Button x:Name=\"a\"><StackPanel><TextBlock Text=\"{evil}\"/></StackPanel></Button><Border MouseDown=\"{evil}\"/><TextBox><TextBox.ToolTip>{evil}</TextBox.ToolTip></TextBox>");
        foreach (var lang in (Lang[])[Lang.En, Lang.ZhHant])
        {
            var html = Html(r, exit: 3, lang: lang, why: "<img src=x onerror=alert(1)>");
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(((char)0x202E).ToString(), html, StringComparison.Ordinal);
            Parse(html);
            var md = MarkdownReport.Create(r, r.Findings, 1, 0, null, lang);
            Assert.All(md.Split('\n').Where(l => l.Contains("script", StringComparison.Ordinal)), l => Assert.Contains("`", l, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_clean_run_still_lists_the_manual_checks()
    {
        var r = Kit.Audit("<Button Content=\"Save\"/>");
        var html = Html(r, exit: 0);
        Assert.Contains("PASS", html, StringComparison.Ordinal);
        Assert.Contains("Checks a person has to do", html, StringComparison.Ordinal);
        Assert.Contains("4.1.3 Status Messages (AA)", html, StringComparison.Ordinal);
        Assert.Contains("18 checks a person has to do", MarkdownReport.Create(r, r.Findings, 0, 0, null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_cli_writes_the_html()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Everything, T));
        Assert.Equal(1, f.Run("audit", f.Root, "--html", f.At("r.html"), "--lang", "zh-Hant").Exit);
        Assert.Contains("<html lang=\"zh-Hant\">", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
        Assert.Equal(2, f.Run("audit", f.Root, "--html", f.At("nowhere/r.html")).Exit);
        Assert.Equal(2, f.Run("audit", f.Root, "--html", f.At("r.html"), "--write-baseline", f.At("b.json")).Exit);
    }

    // ───────────────────────── NVDA 的念法 ─────────────────────────

    /// <summary>自己的類別、treatAs 宣告的：UIA 的型別可以是別的，不猜。</summary>
    [Fact]
    public void NVDA_is_not_guessed_for_other_classes()
    {
        var f = Assert.Single(Kit.Audit("<local:MyToggle/>", treatAs: new Dictionary<string, string> { ["MyToggle"] = "CheckBox" }).Findings);
        Assert.Null(Nvda.Says(f, Lang.En));
    }

    [Theory]
    [InlineData("<Button>[ICON]</Button>", "button", "按鈕")]
    [InlineData("<Button ToolTip=\"Delete\">[ICON]</Button>", "button, Delete", "按鈕, Delete")]
    [InlineData("<Button ToolTip=\"{Binding T}\">[ICON]</Button>", null, null)]          // 執行時才知道
    [InlineData("<Button ToolTip=\"a\" AutomationProperties.HelpText=\"b\">[ICON]</Button>", "button, b", "按鈕, b")]
    [InlineData("<TextBox/>", "edit", "編輯區")]
    [InlineData("<ListBox/>", "list", "清單")]
    [InlineData("<ListView><ListView.View><GridView/></ListView.View></ListView>", "data grid", "資料格")]
    [InlineData("<TabControl><TabItem><TabItem.Header>[ICON]</TabItem.Header></TabItem></TabControl>", "System dot Windows dot Controls dot Tab Item…, tab", "System dot Windows dot Controls dot Tab Item…, 索引標籤")]
    [InlineData("<Button Content=\"×\"/>", null, null)]                                  // 怎麼念看 NVDA 的符號字典
    [InlineData("<Button FontFamily=\"Segoe MDL2 Assets\" Content=\"&#xE74D;\"/>", null, null)]
    [InlineData("<TabControl><TabItem Header=\"&#xE74D;\"/></TabControl>", null, null)]   // 名稱是字元，不是型別名
    [InlineData("<Border MouseDown=\"x\"/>", null, null)]
    [InlineData("<RichTextBox/>", "document", "文件")]
    [InlineData("<RepeatButton>[ICON]</RepeatButton>", "button", "按鈕")]
    [InlineData("<ListView/>", "list", "清單")]
    [InlineData("<ListView View=\"{x:Null}\"/>", "list", "清單")]
    [InlineData("<ListView><ListView.View><local:Tiles/></ListView.View></ListView>", null, null)]       // 別的 View 沒跑過
    [InlineData("<Button><AccessText Text=\"_Open\"/></Button>", null, null)]                              // NVDA 還會念快速鍵
    [InlineData("<Button AutomationProperties.AccessKey=\"Alt+D\">[ICON]</Button>", null, null)]
    [InlineData("<Button IsEnabled=\"False\" Width=\"9\">[ICON]</Button>", null, null)]                   // Tab 到不了
    [InlineData("<Button ToolTip=\"Save&#xE74D;\">[ICON]</Button>", null, null)]                           // 符號怎麼念看字典
    [InlineData("<Button ToolTip=\"×\">[ICON]</Button>", null, null)]
    [InlineData("<Button ToolTip=\"Find, then replace.\">[ICON]</Button>", "button, Find, then replace.", "按鈕, Find, then replace.")]
    [InlineData("<Button ToolTip=\"Tip\" AutomationProperties.HelpText=\" \">[ICON]</Button>", null, null)] // 空白的 HelpText 就是它
    [InlineData("<Button ToolTip=\"Tip\" AutomationProperties.HelpText=\"\">[ICON]</Button>", "button, Tip", "按鈕, Tip")]
    [InlineData("<Button><Button.ToolTip><ToolTip/></Button.ToolTip>[ICON]</Button>", null, null)]
    [InlineData("<TabControl><TabItem ToolTip=\"Home\"><TabItem.Header>[ICON]</TabItem.Header></TabItem></TabControl>", null, null)]
    [InlineData("<Expander/>", null, null)]                                              // 念的是裡面的切換按鈕
    public void What_NVDA_says(string xaml, string? en, string? zh)
    {
        var f = Assert.Single(Kit.Audit(xaml.Replace("[ICON]", Icon, StringComparison.Ordinal)).Findings, x => x.Kind.Rule is "R1" or "R2" or "R4" or "R7");
        Assert.Equal(en, Nvda.Says(f, Lang.En));
        Assert.Equal(zh, Nvda.Says(f, Lang.ZhHant));
    }
}
