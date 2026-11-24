using System.Text.Json;
using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>條文對應：等級、名稱跟 W3C 的資料一致；README 與標準文件寫的跟程式一致（手寫的表會漂）。</summary>
public class StandardsTests
{
    private static readonly Dictionary<string, (string Level, string Name)> W3C =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wcag22.json"))).RootElement.GetProperty("criteria").EnumerateArray()
            .ToDictionary(c => c.GetProperty("num").GetString()!, c => (c.GetProperty("level").GetString()!, c.GetProperty("name").GetString()!), StringComparer.Ordinal);

    /// <summary>從輸出資料夾往上找。⚠ 不要用 CallerFilePath：CI 的建置會把原始碼路徑換成 /_/。</summary>
    private static string Repo() => ActionContractTests.RepoRoot();

    [Fact]
    public void The_levels_and_names_are_the_ones_W3C_publishes()
    {
        Assert.Equal(55, W3C.Count);
        foreach (var (number, (level, name)) in Wcag.Criteria)
        {
            Assert.Equal(W3C[number].Level, level);
            Assert.Equal(W3C[number].Name, name);
        }
        // 沒用到的不要留著，用到的都要有
        Assert.Equal(Wcag.Criteria.Keys.Order(StringComparer.Ordinal), Kinds.All.SelectMany(k => k.Criteria).Distinct().Order(StringComparer.Ordinal));
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
    public void Each_finding_names_its_criteria(string slug, string expected) => Assert.Equal(expected, Kinds.BySlug(slug)!.WcagWithLevels);

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.zh-Hant.md")]
    [InlineData("docs/standards.md")]
    [InlineData("docs/標準對照.md")]
    public void The_documents_say_what_the_program_does(string file)
    {
        var text = File.ReadAllText(Path.Combine(Repo(), file));
        foreach (var k in Kinds.All)
        {
            var row = Regex.Match(text, $@"^\| `{Regex.Escape(k.Slug)}` \| (?<wcag>[^|]+) \| (?<default>\w+) \|", RegexOptions.Multiline);
            Assert.True(row.Success, k.Slug);
            Assert.Equal(k.WcagWithLevels, row.Groups["wcag"].Value);
            Assert.Equal(Text.Slug(k.Default), row.Groups["default"].Value);
        }
    }

    [Theory]
    [InlineData("docs/standards.md")]
    [InlineData("docs/標準對照.md")]
    public void The_levels_in_the_manual_checks_are_right(string file)
    {
        var text = File.ReadAllText(Path.Combine(Repo(), file));
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
        static string[] Numbers(string file) => [.. Regex.Matches(File.ReadAllText(Path.Combine(Repo(), file))[File.ReadAllText(Path.Combine(Repo(), file)).LastIndexOf("| WCAG 2.2 |", StringComparison.Ordinal)..], @"^\| (?<n>\d\.\d\.\d+)", RegexOptions.Multiline).Select(m => m.Groups["n"].Value)];
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
