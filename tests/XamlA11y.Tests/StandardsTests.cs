using System.Text.Json;
using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>標準與等級：條文對應跟 W3C、EN 301 549、508 一致，README 與標準文件寫的跟程式一致（手寫的表會漂）。</summary>
public sealed class StandardsTests
{
    private static readonly Dictionary<string, (string Level, string Name, string Since)> W3C =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wcag22.json"))).RootElement.GetProperty("criteria").EnumerateArray()
            .ToDictionary(c => c.GetProperty("num").GetString()!, c => (c.GetProperty("level").GetString()!, c.GetProperty("name").GetString()!, c.GetProperty("since").GetString()!), StringComparer.Ordinal);

    [Theory]
    [InlineData("wcag22", "AA", "")]
    [InlineData("wcag21", "AA", "small-target")]                     // 2.5.8 是 2.2 才有
    [InlineData("wcag20", "AA", "name-differs,small-target")]
    [InlineData("en301549", "AA", "small-target")]                   // V3.2.1 用 WCAG 2.1
    [InlineData("en301549-v4", "AA", "")]
    [InlineData("section508", "AA", "name-differs,small-target")]
    [InlineData("wcag22", "A", "focus-not-visible,same-name,small-target")]    // 2.4.7、2.4.6、2.5.8 是 AA
    [InlineData("section508", "A", "focus-not-visible,name-differs,same-name,small-target")]
    public void A_standard_decides_what_is_reported(string id, string level, string missing)
    {
        var found = Kit.Slugs(Kit.AuditEverything(new Scope(Standard.ById(id)!, level)));
        Assert.Equal(missing, string.Join(",", Kit.KindsInEverything.Except(found).Order(StringComparer.Ordinal)));
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
        var r = Kit.AuditEverything(new Scope(Standard.Wcag20, "A"), new Dictionary<string, Severity> { ["small-target"] = Severity.Fail, ["missing-name"] = Severity.Off });
        Assert.Contains("small-target", Kit.Slugs(r));
        Assert.DoesNotContain("missing-name", Kit.Slugs(r));
        Assert.Contains(r.Checks.Single(x => x.Check == "control-names").NotJudgedReasons.Keys, k => k == "missing-name is turned off");
        Assert.Contains(r.Checks.Single(x => x.Check == "focus").NotJudgedReasons.Keys, k => k == "focus-not-visible is not in WCAG 2.0 A");
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
    public void The_json_has_the_scope_and_every_standards_clauses()
    {
        var r = Kit.AuditEverything(new Scope(Standard.Section508, "AA"));
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
    public void Sarif_follows_the_chosen_standard()
    {
        var r = Kit.AuditEverything(new Scope(Standard.Section508, "AA"));
        var sarif = SarifReport.Create(r.Findings, "1.0", r.Scope);
        Assert.DoesNotContain("\"WCAG 2.5.", sarif, StringComparison.Ordinal);
        Assert.Contains("Section 508 (WCAG 2.0 A + AA): 4.1.2 (A)", sarif, StringComparison.Ordinal);
        Assert.Contains("\"WCAG 2.5.8\"", SarifReport.Create(r.Findings, "1.0"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_kind_turned_on_from_outside_the_standard_says_so() =>
        Assert.Contains("Not in WCAG 2.1 A + AA; the config file turns it on.", Kit.Html(Kit.AuditEverything(new Scope(Standard.Wcag21, "AA"), new Dictionary<string, Severity> { ["small-target"] = Severity.Warn })), StringComparison.Ordinal);

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

    [Fact]
    public void The_levels_and_names_are_the_ones_W3C_publishes()
    {
        Assert.Equal(55, W3C.Count);
        foreach (var c in Wcag.Criteria.Values)
        {
            Assert.Equal(W3C[c.Number].Level, c.Level);
            Assert.Equal(W3C[c.Number].Name, c.Name);
            Assert.Equal(W3C[c.Number].Since, c.Since);
        }
        // 沒用到的不要留著，用到的都要有
        Assert.Equal(Wcag.Criteria.Keys.Order(StringComparer.Ordinal), Kinds.All.SelectMany(k => k.Criteria).Concat(ManualCheck.All.SelectMany(m => m.Criteria)).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("missing-name", "4.1.2 (A), 1.1.1 (A)")]
    [InlineData("text-not-in-name", "4.1.2 (A), 2.5.3 (A)")]
    [InlineData("meaningless-name", "4.1.2 (A), 1.1.1 (A)")]
    [InlineData("symbol-name", "1.1.1 (A)")]
    [InlineData("input-no-name", "4.1.2 (A), 1.3.1 (A)")]            // 不是 3.3.2：那條要的是看得到的標籤
    [InlineData("focus-not-visible", "2.4.7 (AA)")]
    [InlineData("mouse-only", "2.1.1 (A)")]
    [InlineData("not-in-tab-order", "2.1.1 (A)")]                    // 不是 2.4.3：那條管的是順序
    [InlineData("small-target", "2.5.8 (AA)")]
    [InlineData("list-no-name", "4.1.2 (A), 1.3.1 (A)")]
    [InlineData("widget-no-name", "4.1.2 (A)")]
    [InlineData("item-type-name", "4.1.2 (A)")]
    [InlineData("silent-tab-stop", "4.1.2 (A)")]
    [InlineData("same-name", "2.4.6 (AA)")]
    [InlineData("name-differs", "2.5.3 (A)")]
    public void Each_finding_names_its_criteria(string slug, string expected) => Assert.Equal(expected, Kinds.BySlug(slug)!.WcagWithLevels);

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.zh-Hant.md")]
    [InlineData("docs/standards.md")]
    [InlineData("docs/標準對照.md")]
    public void The_documents_say_what_the_program_does(string file)
    {
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        foreach (var k in Kinds.All)
        {
            var row = Regex.Match(text, $@"^\| `{Regex.Escape(k.Slug)}` \| (?<wcag>[^|]+) \| (?<default>\w+) \|", RegexOptions.Multiline);
            Assert.True(row.Success, k.Slug);
            Assert.Equal(k.WcagWithLevels, row.Groups["wcag"].Value);
            Assert.Equal(Text.Slug(k.Default), row.Groups["default"].Value);
        }
        // 順序跟 data/findings.json 一樣：同一組的排在一起
        Assert.Equal(Kinds.All.Select(k => k.Slug), Regex.Matches(text, @"^\| `(?<slug>[a-z-]+)` \| \d", RegexOptions.Multiline).Select(m => m.Groups["slug"].Value));
    }

    /// <summary>README 的分組就是報告的分組。</summary>
    [Theory]
    [InlineData("README.md", "## What it finds")]
    [InlineData("README.zh-Hant.md", "## 找什麼")]
    public void The_readmes_have_the_groups_of_the_reports(string file, string heading)
    {
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = text[text.IndexOf(heading, StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 4, StringComparison.Ordinal)];
        var lang = file.Contains("zh", StringComparison.Ordinal) ? Lang.ZhHant : Lang.En;
        var parts = Regex.Split(section, "^### ", RegexOptions.Multiline)[1..];
        Assert.Equal(Kinds.Groups.Select(g => lang.GroupTitle(g)), parts.Select(p => p[..p.IndexOf('\n')]), StringComparer.OrdinalIgnoreCase);
        foreach (var (g, part) in Kinds.Groups.Zip(parts))
            Assert.Equal(Kinds.All.Where(k => k.Group == g.Id).Select(k => k.Slug), Regex.Matches(part, @"^\| `(?<slug>[a-z-]+)`", RegexOptions.Multiline).Select(m => m.Groups["slug"].Value));
    }

    [Theory]
    [InlineData("docs/standards.md")]
    [InlineData("docs/標準對照.md")]
    public void The_levels_in_the_manual_checks_are_right(string file)
    {
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        var manual = text[text.LastIndexOf("| WCAG 2.2 |", StringComparison.Ordinal)..];
        var seen = 0;
        foreach (var row in Regex.Matches(manual, @"^\| (?<cell>\d\.\d[^|]+) \|", RegexOptions.Multiline).Select(m => m.Groups["cell"].Value))
            // 「3.2.1 焦點、3.2.2 輸入 (A)」：一個等級管前面還沒有等級的編號
            foreach (Match part in Regex.Matches(row, @"(?<numbers>[^()（）]*(?:[（(](?!A)[^()（）]*[)）][^()（）]*)*)\((?<level>A{1,2})\)"))
                foreach (Match n in Regex.Matches(part.Groups["numbers"].Value, @"\d\.\d\.\d+")) { seen++; Assert.Equal(W3C[n.Value].Level, part.Groups["level"].Value); }
        Assert.True(seen >= 19, $"{seen}");
    }

    [Fact]
    public void Both_languages_list_the_same_manual_checks()
    {
        static string[] Numbers(string file) => [.. Regex.Matches(File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file))[File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)).LastIndexOf("| WCAG 2.2 |", StringComparison.Ordinal)..], @"^\| (?<n>\d\.\d\.\d+)", RegexOptions.Multiline).Select(m => m.Groups["n"].Value)];
        Assert.Equal(Numbers("docs/standards.md"), Numbers("docs/標準對照.md"));
    }

    [Fact]
    public void The_reports_carry_the_version_and_the_levels()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button>");
        using var json = JsonDocument.Parse(JsonReport.Create(r, r.Findings, 0, "1.0", 1));
        Assert.Equal("2.2", json.RootElement.GetProperty("summary").GetProperty("wcagVersion").GetString());
        Assert.Equal("A", json.RootElement.GetProperty("findings")[0].GetProperty("wcagLevels").GetProperty("4.1.2").GetString());
        Assert.Contains("4.1.2 (A), 1.1.1 (A)", MarkdownReport.Create(r, r.Findings, 1, 0, null), StringComparison.Ordinal);
        Assert.Contains("\"WCAG 1.1.1\"", SarifReport.Create(r.Findings, "1.0"), StringComparison.Ordinal);
    }
}
