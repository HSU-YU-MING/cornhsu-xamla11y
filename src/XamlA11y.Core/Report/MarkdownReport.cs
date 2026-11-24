using System.Globalization;
using System.Text;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>
/// Markdown（<c>--md</c>）：給 PR 留言用。「看不到的東西」放最前面。
/// ⚠ 路徑、名字、文字都來自 repo（不可信的輸入）：不是工具自己寫的字串一律走 <see cref="Code"/> ——
///   GitHub 只有在 code span 裡不處理連結、圖片、@ 提及與 www. 自動連結。訊息裡夾了使用者的文字，所以整句也進 code span。
/// </summary>
public static class MarkdownReport
{
    private const int RowsPerSection = 50;
    private const int ListMax = 20;

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int exitCode, int baselined, string? why, Lang? lang = null, IReadOnlyList<Finding>? known = null)
    {
        lang ??= Lang.En;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int Count(Severity s) => reported.Count(f => f.Severity == s);

        sb.AppendLine(lang.T("## XamlA11y — accessibility audit", "## XamlA11y 無障礙稽核")).AppendLine();
        var verdict = exitCode switch { 0 => lang.T("✅ **PASS**", "✅ **通過**"), 1 => lang.T("❌ **FAIL**", "❌ **沒通過**"), 3 => lang.T("⚠️ **NOT TRUSTWORTHY** (exit 3)", "⚠️ **結果不可信**（exit 3）"), _ => $"exit {exitCode.ToString(inv)}" };
        sb.AppendLine(lang.IsZh
            ? string.Create(inv, $"{verdict} · **{Count(Severity.Fail)} fail**、{Count(Severity.Warn)} warn、{Count(Severity.Info)} info · {r.Files} 個 XAML 檔、判了 {r.Judged} 處 · 可解析率 {r.Resolution:F1}% · {r.Scope.Title}")
            : string.Create(inv, $"{verdict} · **{Count(Severity.Fail)} fail**, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info · {r.Judged} check(s) in {r.Files} XAML file(s) · resolution {r.Resolution:F1}% · {r.Scope.Title}")).AppendLine();

        if (reported.Count > 0) sb.AppendLine(string.Join(" · ", Summary.Groups(reported, lang).Select(g => string.Create(inv, $"{g.Title} **{g.Count}**")))).AppendLine();
        if (Summary.KnownDebt(known, exitCode, lang) is { } debt) sb.AppendLine($"> {debt}").AppendLine();
        if (exitCode == 3)
            sb.AppendLine($"> ⚠️ **{lang.T("The result can't be trusted.", "結果不可信。")}** {Code(why ?? lang.T("see the console output of the run", "原因看這次執行的輸出"), table: false)}").AppendLine();
        foreach (var f in r.ParseErrors.Take(ListMax))
            sb.AppendLine($"> ⚠️ **{lang.T("Parse failed:", "讀不懂：")}** {Code(f, table: false)} — {lang.T("this file is missing from the audit.", "這個檔案沒有稽核到。")}").AppendLine();
        if (r.ParseErrors.Count > ListMax) sb.AppendLine(string.Create(inv, $"> ⚠️ {lang.T($"… and {r.ParseErrors.Count - ListMax} more file(s) that could not be parsed.", $"… 還有 {r.ParseErrors.Count - ListMax} 個檔案讀不懂。")}")).AppendLine();
        foreach (var i in r.InvalidIgnores.Take(ListMax)) sb.AppendLine($"> ⚠️ {Code(lang.Ignore(i), table: false)}").AppendLine();
        if (r.InvalidIgnores.Count > ListMax) sb.AppendLine(string.Create(inv, $"> ⚠️ {lang.T($"… and {r.InvalidIgnores.Count - ListMax} more xamla11y-ignore that do nothing.", $"… 還有 {r.InvalidIgnores.Count - ListMax} 個沒作用的 xamla11y-ignore。")}")).AppendLine();
        if (r.Blocked > 0)
        {
            sb.AppendLine(string.Create(inv, $"> ⚠️ **{r.Blocked} {lang.T("place(s) could not be judged:", "處判不出來：")}**"));
            var why5 = r.On.SelectMany(x => x.BlockedReasons).GroupBy(p => p.Key).Select(g => (g.Key, N: g.Sum(p => p.Value))).OrderByDescending(x => x.N).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            foreach (var (reason, n) in why5.Take(5))
                sb.AppendLine(string.Create(inv, $"> - {n} × {Code(lang.Reason(reason), table: false)}"));
            if (why5.Count > 5) sb.AppendLine(string.Create(inv, $"> - … {lang.T($"and {why5.Count - 5} more reason(s)", $"還有 {why5.Count - 5} 種原因")}"));
            if (r.UnknownControls.Count > 0)
                sb.AppendLine($"> - {lang.T("controls it does not know (declare them in `treatAs`):", "不認得的控制項（用 `treatAs` 宣告）：")} {string.Join(", ", r.UnknownControls.Take(8).Select(u => Code(string.Create(inv, $"{u.Element} ×{u.Count}"), table: false)))}{(r.UnknownControls.Count > 8 ? " …" : "")}");
            sb.AppendLine();
        }

        foreach (var severity in (ReadOnlySpan<Severity>)[Severity.Fail, Severity.Warn])
        {
            var group = reported.Where(f => f.Severity == severity).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine(string.Create(inv, $"### {(severity == Severity.Fail ? "Fail" : "Warn")} ({group.Count})")).AppendLine();
            Table(sb, group, r.Scope, lang);
        }
        var info = reported.Where(f => f.Severity == Severity.Info).ToList();
        if (info.Count > 0)
        {
            sb.AppendLine(string.Create(inv, $"<details><summary>{info.Count} {lang.T("info finding(s) (do not fail the build)", "筆 info（不會擋）")}</summary>")).AppendLine();
            Table(sb, info, r.Scope, lang);
            sb.AppendLine("</details>").AppendLine();
        }

        var notes = new List<string>();
        if (baselined > 0) notes.Add(string.Create(inv, $"{baselined} {lang.T("in the baseline", "筆在 baseline 裡")}"));
        if (r.NotJudged > 0) notes.Add(string.Create(inv, $"{r.NotJudged} {lang.T("not judged (data-bound names, template parts, list items…)", "處不判（名稱來自資料、範本零件、清單項目…）")}"));
        if (r.Suppressed > 0) notes.Add(string.Create(inv, $"{r.Suppressed} {lang.T("suppressed via xamla11y-ignore", "筆用 xamla11y-ignore 壓掉")}"));
        if (r.Foreign.Count > 0) notes.Add(string.Create(inv, $"{r.Foreign.Count} {lang.T("non-WPF XAML file(s)", "個不是 WPF 的 XAML")}"));
        if (r.Gaps.Excluded > 0) notes.Add(string.Create(inv, $"{r.Gaps.Excluded} {lang.T("file(s) excluded", "個檔案排除")}"));
        if (r.Gaps.Unreadable.Count > 0) notes.Add(string.Create(inv, $"{r.Gaps.Unreadable.Count} {lang.T("unreadable folder(s)", "個資料夾讀不到")}"));
        if (notes.Count > 0) sb.AppendLine($"**{lang.T("Not in the list above:", "上面沒列的：")}** {string.Join(" · ", notes)}").AppendLine();

        // 工具查不到的要跟結果放在一起，不然「通過」看起來像全部查過了
        var manual = ManualCheck.In(r.Scope);
        sb.AppendLine(string.Create(inv, $"<details><summary>{lang.T($"{manual.Count} checks a person has to do (the common ones)", $"要人檢查的 {manual.Count} 項（最常出問題的）")}</summary>")).AppendLine().AppendLine(Summary.Manual(lang)).AppendLine();
        sb.AppendLine($"| WCAG | {lang.T("Check it like this", "怎麼檢查")} |").AppendLine("|---|---|");
        foreach (var (check, criteria) in manual)
            sb.AppendLine($"| {string.Join(lang.T(", ", "、"), criteria.Select(c => $"{c.Number} {lang.Criterion(c)} ({c.Level})"))} | {lang.T(check.En, check.Zh)} |");
        sb.AppendLine().AppendLine("</details>").AppendLine();

        sb.AppendLine($"<sub>{lang.T("Generated by XamlA11y", "XamlA11y 產生")} · {lang.Scope}</sub>");
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void Table(StringBuilder sb, List<Finding> findings, Scope scope, Lang lang)
    {
        sb.AppendLine(lang.T("| Location | Kind | WCAG | Detail |", "| 位置 | 種類 | WCAG | 說明 |")).AppendLine("|---|---|---|---|");
        foreach (var f in findings.Take(RowsPerSection))
        {
            var clauses = scope.ClausesOf(f);
            var says = Nvda.Says(f, lang) is { } s ? $"<br>NVDA: {Code(s)}" : "";
            sb.AppendLine($"| {Code(f.Site)} | {(lang.IsZh ? $"{f.Kind.Slug}<br>{lang.Title(f.Kind)}" : f.Kind.Slug)} | {scope.WcagOf(f.Kind)}{(clauses.Count > 0 ? $"<br>{scope.Standard.Title}: {string.Join(", ", clauses)}" : "")} | {Code(Trunc(lang.Message(f.Message), 220))}{says} |");
        }
        sb.AppendLine();
        if (findings.Count > RowsPerSection)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<sub>{lang.T($"… and {findings.Count - RowsPerSection} more — see the JSON report artifact.", $"… 還有 {findings.Count - RowsPerSection} 筆，看 JSON 報告。")}</sub>")).AppendLine();
    }

    private static string Trunc(string s, int n)
    {
        if (s.Length <= n) return s;
        var cut = n - 1;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut] + "…";
    }

    /// <summary>code span：圍欄比內容裡最長的反引號串多一個，換行收成空白，表格裡的 | 跳脫。</summary>
    internal static string Code(string s, bool table = true)
    {
        var body = Text.Visible((table ? s.Replace("|", "\\|") : s).Replace("\r", " ").Replace("\n", " "));
        if (body.Trim().Length == 0) body = "(empty)";
        int longest = 0, run = 0;
        foreach (var c in body)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', longest + 1);
        var pad = longest > 0 ? " " : "";
        return fence + pad + body + pad + fence;
    }
}
