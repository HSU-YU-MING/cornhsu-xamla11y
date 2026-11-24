using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>data/findings.json 與文件：每一列都填滿、每一種都抓得到、每一種都有修法範例而且範例真的能用。</summary>
public sealed class FindingsTableTests
{
    /// <summary>文件上「原本」的寫法真的會被報，「改成」的真的不會。</summary>
    [Theory]
    [InlineData("docs/fixes.md", "missing-name")]
    [InlineData("docs/fixes.md", "text-not-in-name")]
    [InlineData("docs/fixes.md", "meaningless-name")]
    [InlineData("docs/fixes.md", "symbol-name")]
    [InlineData("docs/fixes.md", "input-no-name")]
    [InlineData("docs/fixes.md", "mouse-only")]
    [InlineData("docs/fixes.md", "list-no-name")]
    [InlineData("docs/fixes.md", "item-type-name")]
    [InlineData("docs/fixes.md", "silent-tab-stop")]
    [InlineData("docs/fixes.md", "same-name")]
    [InlineData("docs/fixes.md", "name-differs")]
    [InlineData("docs/修法.md", "name-differs")]
    [InlineData("docs/修法.md", "missing-name")]
    [InlineData("docs/修法.md", "text-not-in-name")]
    [InlineData("docs/修法.md", "meaningless-name")]
    [InlineData("docs/修法.md", "symbol-name")]
    [InlineData("docs/修法.md", "input-no-name")]
    [InlineData("docs/修法.md", "mouse-only")]
    [InlineData("docs/修法.md", "list-no-name")]
    [InlineData("docs/修法.md", "item-type-name")]
    [InlineData("docs/修法.md", "silent-tab-stop")]
    [InlineData("docs/修法.md", "same-name")]
    public void The_examples_do_what_they_say(string file, string slug)
    {
        var section = Regex.Split(File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)).Replace("\r\n", "\n", StringComparison.Ordinal), "^## ", RegexOptions.Multiline).Single(s => s.StartsWith(slug + "\n", StringComparison.Ordinal));
        var xml = Regex.Match(section, "```xml\n(?<x>.*?)```", RegexOptions.Singleline).Groups["x"].Value;
        var after = Regex.Match(xml, "<!-- (?:after|改成)[^>]*-->");
        Assert.True(after.Success);
        const string icon = "<Geometry x:Key=\"TrashIcon\">M0,0 L9,9 Z</Geometry>";
        // 頁籤要放在 TabControl 裡才是完整的畫面，判定不受影響
        string[] Run(string x) => [.. Kit.AuditBody(x, Kit.Note + " class F { public List<string> Files { get; } }", icon).Findings.Select(f => f.Kind.Slug).Distinct()];
        Assert.Contains(slug, Run(xml[..after.Index]));
        var fixedOnes = Run(xml[(after.Index + after.Length)..]);
        Assert.DoesNotContain(slug, fixedOnes);
        Assert.Empty(fixedOnes.Except(slug == "item-type-name" ? ["list-no-name"] : []));
    }

    [Theory]
    [InlineData("docs/fixes.md")]
    [InlineData("docs/修法.md")]
    public void Every_finding_has_a_section(string file)
    {
        var text = File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        foreach (var k in Kinds.All) Assert.Contains("\n## " + k.Slug, text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(Kinds.All.Count, Regex.Count(text, "^## ", RegexOptions.Multiline));
    }

    [Fact]
    public void The_reports_link_to_the_section()
    {
        var r = Kit.Audit($"<Button>{Kit.Icon}</Button>");
        Assert.Contains("/docs/fixes.md#missing-name\"", SarifReport.Create(r.Findings, "1.0"), StringComparison.Ordinal);
        Assert.Contains("/docs/fixes.md#missing-name\"", JsonReport.Create(r, r.Findings, 0, "1.0", 1), StringComparison.Ordinal);
        Assert.Contains("href=\"https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/fixes.md#missing-name\"", HtmlReport.Create(r, r.Findings, 1, 0, null, "1.0"), StringComparison.Ordinal);
        Assert.Contains("docs/%E4%BF%AE%E6%B3%95.md#missing-name", HtmlReport.Create(r, r.Findings, 1, 0, null, "1.0", Lang.ZhHant), StringComparison.Ordinal);
        Assert.Equal("修法", Uri.UnescapeDataString("%E4%BF%AE%E6%B3%95"));
    }

    [Fact]
    public void Every_kind_is_found_by_default() => Assert.Equal(Kit.KindsInEverything.Order(StringComparer.Ordinal), Kit.Slugs(Kit.AuditEverything()));

    /// <summary>data/findings.json：每一列都填滿了、分組與檢查對得上、同一組排在一起而且先嚴重的。</summary>
    [Fact]
    public void The_findings_table_is_complete()
    {
        Assert.All(Kinds.All, k => Assert.All(new[] { k.En, k.Zh }, w => Assert.True(w.Title.Length > 0 && w.Why.Length > 0 && w.How.Length > 0, k.Slug)));
        Assert.All(Kinds.All, k => Assert.True(k.Zh.Title.Any(c => c > 0x2E80) && !k.En.Help.Any(c => c > 0x2E80), k.Slug));
        Assert.Equal(Kinds.All.Count, Kinds.All.Select(k => k.Slug).Distinct().Count());
        Assert.Equal(["names", "keyboard", "focus", "size"], Kinds.Groups.Select(g => g.Id));
        Assert.Equal(Kinds.Groups.Select(g => g.Id), Kinds.All.Select(k => k.Group).Distinct());
        Assert.Equal(Kinds.All.OrderBy(k => Kinds.Groups.ToList().FindIndex(g => g.Id == k.Group)).ThenByDescending(k => k.Default), Kinds.All);
        // 程式裡的代號與表一個不差
        var constants = typeof(Checks).GetFields().Select(f => (string)f.GetRawConstantValue()!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(constants, Kinds.Checks.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Equal(constants, Kinds.All.Select(k => k.Check).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal([Checks.ControlNames, Checks.InputNames, Checks.Focus, Checks.Mouse], Kinds.Checks.Where(c => c.Gate).Select(c => c.Id).OrderBy(c => c == Checks.Mouse).ThenBy(c => c == Checks.Focus));
        Assert.Equal(["missing-name", "text-not-in-name", "meaningless-name", "input-no-name", "mouse-only", "focus-not-visible"], Kinds.All.Where(k => k.Default == Severity.Fail).Select(k => k.Slug));
        Assert.Equal(["symbol-name", "same-name"], Kinds.All.Where(k => k.Default == Severity.Info).Select(k => k.Slug));
        Assert.Equal(["missing-name", "text-not-in-name", "input-no-name", "list-no-name", "widget-no-name"], Kinds.All.Where(k => k.NameIsEmpty).Select(k => k.Slug));
    }

    /// <summary>文件裡手寫的數字：幾種、幾條準則。</summary>
    [Fact]
    public void The_numbers_in_the_documents_are_right()
    {
        static string Doc(string file) => File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));
        var kinds = Kinds.All.Count;
        var criteria = Kinds.All.SelectMany(k => k.Criteria).Distinct().Count();
        var total = Wcag.Total("2.2", "AA");
        string[] words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"];
        Assert.Contains($"{words[kinds]} kinds of finding", Doc("CHANGELOG.md"), StringComparison.Ordinal);
        Assert.Contains($"，{kinds} 種）", Doc("CLAUDE.md"), StringComparison.Ordinal);
        Assert.Contains($"{words[criteria]} of its {total} criteria", Doc("README.md"), StringComparison.Ordinal);
        Assert.Contains($"{total} 條裡的 {criteria} 條", Doc("README.zh-Hant.md"), StringComparison.Ordinal);
        Assert.Contains($"{words[criteria]} of its {total} success criteria", Doc("docs/standards.md"), StringComparison.Ordinal);
        Assert.Contains($"the other {total - criteria} criteria are not checked", Doc("docs/standards.md").Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' '), StringComparison.Ordinal);
        Assert.Contains($"找其中 {criteria} 條", Doc("docs/標準對照.md"), StringComparison.Ordinal);
        Assert.Contains($"另外 {total - criteria} 條沒查，這 {criteria} 條", Doc("docs/標準對照.md"), StringComparison.Ordinal);
        foreach (var k in Kinds.All) Assert.Contains($"        - {k.Slug}\n", Doc(".github/ISSUE_TEMPLATE/1-false-alarm.yml").Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        foreach (var k in Kinds.All) Assert.Contains($"\"{k.Slug}\":", Doc("scripts/verify-demo.sh"), StringComparison.Ordinal);
    }

    /// <summary>README.md 跟著 NuGet 套件走，nuget.org 不解析相對連結；其他文件的相對連結要找得到檔案。</summary>
    [Fact]
    public void Links_lead_somewhere()
    {
        var root = ActionContractTests.RepoRoot();
        const string blob = "https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/";
        foreach (var file in Directory.EnumerateFiles(root, "*.md").Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)))
        {
            var text = Regex.Replace(File.ReadAllText(file), "```.*?```", "", RegexOptions.Singleline);
            foreach (var link in Regex.Matches(text, @"\]\((?<u>[^)\s]+)\)").Select(m => m.Groups["u"].Value))
            {
                var local = link.StartsWith(blob, StringComparison.Ordinal) ? Path.Combine(root, link[blob.Length..]) : link.Contains("://", StringComparison.Ordinal) ? null : Path.Combine(Path.GetDirectoryName(file)!, link);
                if (Path.GetFileName(file) == "README.md") Assert.True(link.Contains("://", StringComparison.Ordinal) || link[0] == '#', $"README.md: {link} is relative");
                if (local is null || link[0] == '#') continue;
                local = Uri.UnescapeDataString(local.Split('#')[0]);
                Assert.True(File.Exists(local) || Directory.Exists(local), $"{Path.GetFileName(file)}: {link}");
            }
        }
    }
}
