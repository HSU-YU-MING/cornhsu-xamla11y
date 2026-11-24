using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>HTML 報告：報告自己也要合格。</summary>
public sealed class HtmlReportTests
{
    /// <summary>除了 DOCTYPE 與 void 元素，寫成 XML 也通：標籤都有關好。</summary>
    private static XElement Parse(string html) =>
        XElement.Parse(Regex.Replace(html, @"<!DOCTYPE html>|<meta[^>]*>", ""), LoadOptions.PreserveWhitespace);

    /// <summary>通過但 baseline 蓋住一堆：報告要說。</summary>
    [Fact]
    public void A_pass_with_known_problems_says_so()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<TextBox/><ListBox/>"));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        Assert.Equal(0, f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--html", f.At("r.html"), "--md", f.At("r.md")).Exit);
        foreach (var file in (string[])["r.html", "r.md"])
            Assert.Contains("Pass means no new findings. 2 known problem(s) in the baseline are not fixed: 1 fail, 1 warn, 0 info.", File.ReadAllText(f.At(file)), StringComparison.Ordinal);
        Assert.Contains("PASS (no new findings)", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
        Assert.DoesNotContain("known problem(s)", HtmlReport.Create(f.Audit(), f.Audit().Findings, 1, 0, null, "1"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wcag22", "AA", "8 of the 55")]
    [InlineData("wcag22", "A", "5 of the 31")]
    [InlineData("wcag21", "AA", "7 of the 50")]
    [InlineData("wcag21", "A", "5 of the 30")]
    [InlineData("section508", "AA", "6 of the 38")]
    [InlineData("section508", "A", "4 of the 25")]
    public void The_html_says_how_much_is_covered(string id, string level, string expected) =>
        Assert.Contains($"This checks {expected} success criteria", Summary.Coverage(new Scope(Standard.ById(id)!, level), Lang.En), StringComparison.Ordinal);

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hant")]
    public void The_html_is_well_formed_and_accessible(string lang)
    {
        var html = Kit.Html(Kit.AuditEverything(), lang: Lang.ById(lang));
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
        foreach (var slug in Kit.KindsInEverything) Assert.Contains(slug, html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_html_says_what_where_why_and_which_clause()
    {
        var en = Kit.Html(Kit.AuditEverything());
        Assert.Contains("button, Delete", en, StringComparison.Ordinal);
        Assert.Contains("WCAG 4.1.2 Name, Role, Value (A)", en, StringComparison.Ordinal);
        Assert.Contains("EN 301 549 V4.1.1: 11.4.1.2, 11.1.1.1, 11.5.2.5", en, StringComparison.Ordinal);
        Assert.Contains("Section 508: E207.2, 502.3.1", en, StringComparison.Ordinal);
        Assert.Contains("<code>" + Kit.AuditEverything().Findings[0].Site + "</code>", en, StringComparison.Ordinal);
        var only = Kit.Html(Kit.AuditEverything(new Scope(Standard.Section508, "AA")), lang: Lang.ZhHant);
        Assert.Contains("Section 508: E207.2, 502.3.1", only, StringComparison.Ordinal);
        Assert.DoesNotContain("11.4.1.2", only, StringComparison.Ordinal);
        Assert.Contains("按鈕, Delete", only, StringComparison.Ordinal);
        Assert.Contains("WCAG 4.1.2 名稱、角色和值 (A)", only, StringComparison.Ordinal);
    }

    [Fact]
    public void The_html_escapes_everything_from_the_repository()
    {
        const string evil = "&lt;script&gt;alert(1)&lt;/script&gt;&quot; onload=&quot;x &#x202E;abc";
        var r = Kit.Audit($"<Button ToolTip=\"{evil}\">{Kit.Icon}</Button><Button x:Name=\"a\"><StackPanel><TextBlock Text=\"{evil}\"/></StackPanel></Button><Border MouseDown=\"{evil}\"/><TextBox><TextBox.ToolTip>{evil}</TextBox.ToolTip></TextBox>");
        foreach (var lang in (Lang[])[Lang.En, Lang.ZhHant])
        {
            var html = Kit.Html(r, exit: 3, lang: lang, why: "<img src=x onerror=alert(1)>");
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
        var html = Kit.Html(r, exit: 0);
        Assert.Contains("PASS", html, StringComparison.Ordinal);
        Assert.Contains("Checks a person has to do", html, StringComparison.Ordinal);
        Assert.Contains("4.1.3 Status Messages (AA)", html, StringComparison.Ordinal);
        Assert.Contains("18 checks a person has to do", MarkdownReport.Create(r, r.Findings, 0, 0, null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_cli_writes_the_html()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Kit.Everything, Kit.EverythingTemplate));
        Assert.Equal(1, f.Run("audit", f.Root, "--html", f.At("r.html"), "--lang", "zh-Hant").Exit);
        Assert.Contains("<html lang=\"zh-Hant\">", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
        Assert.Equal(2, f.Run("audit", f.Root, "--html", f.At("nowhere/r.html")).Exit);
        Assert.Equal(2, f.Run("audit", f.Root, "--html", f.At("r.html"), "--write-baseline", f.At("b.json")).Exit);
    }
}
