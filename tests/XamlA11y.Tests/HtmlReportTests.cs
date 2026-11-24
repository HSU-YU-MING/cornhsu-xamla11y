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
        Assert.DoesNotContain(" tabindex", html, StringComparison.Ordinal);
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

    /// <summary>交給別人看的：哪個專案、哪一天、哪個 commit。</summary>
    [Fact]
    public void The_html_says_whose_and_when()
    {
        using var f = Kit.Bad();
        var root = Path.Combine(f.Root, "MyApp", "src");
        f.Add("MyApp/src/M.xaml", Kit.Window("<TextBox/>")).Add(".git/HEAD", "ref: refs/heads/main").Add(".git/refs/heads/main", "0123456789abcdef0123456789abcdef01234567\n");
        f.Run("audit", root, "--html", f.At("r.html"), "--json", f.At("r.json"));
        var html = File.ReadAllText(f.At("r.html"));
        Assert.Contains("<h1>MyApp/src: accessibility audit</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<dt>commit</dt><dd>0123456</dd>", html, StringComparison.Ordinal);
        Assert.Matches("<dt>Date</dt><dd>20[0-9]{2}-[0-9]{2}-[0-9]{2}</dd>", html);
        Assert.DoesNotContain(f.Root, html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"project\": \"MyApp/src\"", File.ReadAllText(f.At("r.json")), StringComparison.Ordinal);
        f.Run("audit", root, "--html", f.At("r.html"), "--title", "Client <X>");
        Assert.Contains("<h1>Client &lt;X&gt;: accessibility audit</h1>", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0123456789abcdef", "0123456")]
    [InlineData("ref: refs/heads/gone", null)]
    [InlineData("not a commit", null)]
    public void The_commit(string head, string? expected)
    {
        using var f = new Folder().Add(".git/HEAD", head);
        Assert.Equal(expected, ReportInfo.For(f.Root, null).Commit);
    }

    [Fact]
    public void The_commit_of_a_worktree_and_of_packed_refs()
    {
        using var f = new Folder().Add("repo/.git/packed-refs", "# pack-refs\nabcdef0123456789 refs/heads/x\n").Add("repo/.git/worktrees/w/HEAD", "ref: refs/heads/x").Add("repo/.git/worktrees/w/commondir", "../..\n").Add("w/.git", "gitdir: ../repo/.git/worktrees/w\n");
        Assert.Equal("abcdef0", ReportInfo.For(f.At("w"), null).Commit);
    }

    /// <summary>一處都沒有的檢查也列：分得出「沒有東西可查」跟「沒查」。</summary>
    [Fact]
    public void Checks_with_nothing_to_check_are_listed()
    {
        var html = Kit.Html(Kit.Audit("<Button Content=\"a\"/>"));
        foreach (var c in Kinds.Checks) Assert.Contains($"<th scope=\"row\">{c.En}", html, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"row\">visible focus (nothing to check)</th>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void What_NVDA_reads_is_explained_and_not_quoted()
    {
        var html = Kit.Html(Kit.Audit("<TextBox/>"));
        Assert.Contains("NVDA reads: edit</span>", html, StringComparison.Ordinal);
        Assert.Contains("run with NVDA 2026.2", html, StringComparison.Ordinal);
        Assert.Contains("states (checked, collapsed, blank) and the group around it are left out", html, StringComparison.Ordinal);
    }

    /// <summary>別人給的資料夾：不連網路、不讀 repo 外面的檔案、不讀大檔。</summary>
    [Theory]
    [InlineData("gitdir: \\\\192.0.2.1\\share\\x", null)]
    [InlineData("gitdir: //192.0.2.1/share/x", null)]
    [InlineData("gitdir:", null)]
    public void A_git_file_that_points_at_the_network(string git, string? expected)
    {
        using var f = new Folder().Add(".git", git);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(expected, ReportInfo.For(f.Root, null).Commit);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
    }

    [Theory]
    [InlineData("ref: ../../secret.txt")]
    [InlineData("ref: refs/../../secret.txt")]
    [InlineData("ref: C:/secret.txt")]
    [InlineData("ref: /dev/zero")]
    public void A_ref_outside_the_repository(string head)
    {
        using var f = new Folder().Add("secret.txt", "deadbeefdeadbeef").Add("a/b/.git/HEAD", head);
        Assert.Null(ReportInfo.For(f.At("a/b"), null).Commit);
    }

    /// <summary>沒有要寫報告就不讀 .git；「src」這種看不出是誰的名字帶上一層。</summary>
    [Theory]
    [InlineData("Shop", "Shop")]
    [InlineData("Shop/app", "Shop/app")]
    [InlineData("Shop/Source", "Shop/Source")]
    [InlineData("Shop/Views", "Views")]
    public void The_name(string folder, string expected)
    {
        using var f = new Folder().Add(folder + "/x.txt", "");
        Assert.Equal(expected, ReportInfo.For(f.At(folder), null).Name);
        Assert.Equal("T", ReportInfo.For(f.At(folder), " T ").Name);
        Assert.Equal(expected, ReportInfo.For(f.At(folder), " ").Name);
    }

    /// <summary>連結一律不跟：.git、HEAD、refs、refs 的上一層、packed-refs、commondir、gitdir 指到的路上。</summary>
    [Theory]
    [InlineData(".git")]
    [InlineData(".git/HEAD")]
    [InlineData(".git/refs/heads/main")]
    [InlineData(".git/refs/heads")]
    [InlineData(".git/refs")]
    public void Links_in_the_git_folder_are_not_followed(string link)
    {
        using var f = new Folder().Add("real/HEAD", "ref: refs/heads/main").Add("real/refs/heads/main", "0123456789abcdef").Add("repo/x.txt", "");
        // 其他的照放，只有這一段換成連結
        foreach (var (from, to) in (ReadOnlySpan<(string, string)>)[("real/HEAD", "repo/.git/HEAD"), ("real/refs/heads/main", "repo/.git/refs/heads/main")])
            if (to != "repo/" + link && !to.StartsWith("repo/" + link + "/", StringComparison.Ordinal)) f.Add(to, File.ReadAllText(f.At(from)));
        var target = f.At("real" + link[".git".Length..]);
        var at = f.At("repo/" + link);
        Directory.CreateDirectory(Path.GetDirectoryName(at)!);
        try
        {
            if (Directory.Exists(target)) Directory.CreateSymbolicLink(at, target); else File.CreateSymbolicLink(at, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }      // Windows 沒開開發人員模式不能建連結；Linux 的 CI 會跑
        Assert.Null(ReportInfo.For(f.At("repo"), null).Commit);
    }

    [Fact]
    public void A_gitdir_behind_a_link_is_not_followed()
    {
        using var f = new Folder().Add("real/git/HEAD", "0123456789abcdef").Add("repo/.git", "gitdir: ../way/git").Add("plain/.git", "gitdir: ../real/git");
        Assert.Equal("0123456", ReportInfo.For(f.At("plain"), null).Commit);
        try { Directory.CreateSymbolicLink(f.At("way"), f.At("real")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        Assert.Null(ReportInfo.For(f.At("repo"), null).Commit);
    }
}
