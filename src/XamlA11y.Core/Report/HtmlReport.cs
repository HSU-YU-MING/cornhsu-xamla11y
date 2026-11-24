using System.Globalization;
using System.Net;
using System.Text;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>
/// HTML（<c>--html</c>）：一個檔、沒有指令碼、不連外，直接傳給人看。
/// ⚠ 路徑、名字、文字都來自 repo：一律走 <see cref="H"/>。CSP 另外擋掉指令碼與外部資源（第二道）。
/// 報告自己也要合格：標題分級、表格有 caption 與 scope、窄視窗換行不橫捲、顏色不是唯一的區別（tests 與 axe 看過）。
/// </summary>
public static class HtmlReport
{
    private const int RowsPerKind = 500;
    private const int ListMax = 50;

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int exitCode, int baselined, string? why, string toolVersion, Lang? lang = null, int stale = 0)
    {
        lang ??= Lang.En;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int Count(Severity s) => reported.Count(f => f.Severity == s);

        var title = lang.T("XamlA11y accessibility audit", "XamlA11y 無障礙稽核");
        sb.Append($"""
            <!DOCTYPE html>
            <html lang="{lang.Id}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <title>{H(title)}</title>
            <style>{Style}</style>
            </head>
            <body>
            <main>
            <h1>{H(title)}</h1>

            """);

        var verdict = exitCode switch
        {
            0 => lang.T("PASS", "通過"),
            1 => lang.T("FAIL", "沒通過"),
            3 => lang.T("NOT TRUSTWORTHY (exit 3)", "結果不可信（exit 3）"),
            _ => "exit " + exitCode.ToString(inv),
        };
        sb.Append($"<p class=\"verdict v{exitCode.ToString(inv)}\">{H(verdict)}</p>\n<dl>\n");
        Term(lang.T("Standard", "標準"), r.Scope.Title);
        Term(lang.T("Findings", "問題"), string.Create(inv, $"{Count(Severity.Fail)} fail, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info"));
        Term(lang.T("Resolution", "可解析率"), lang.IsZh
            ? string.Create(inv, $"{r.Resolution:F1}%（判得出來 {r.Judged}、判不出來 {r.Blocked}、不判 {r.NotJudged}）")
            : string.Create(inv, $"{r.Resolution:F1}% ({r.Judged} judged, {r.Blocked} blocked, {r.NotJudged} not judged)"));
        Term(lang.T("XAML files", "XAML 檔"), r.Files.ToString(inv));
        if (baselined > 0) Term(lang.T("In the baseline", "在 baseline 裡"), baselined.ToString(inv));
        if (stale > 0) Term(lang.T("In the baseline, not found this time", "在 baseline 裡、這次沒有的"), stale.ToString(inv));
        if (r.Suppressed > 0) Term(lang.T("Suppressed", "壓掉的"), r.Suppressed.ToString(inv));
        if (r.Gaps.Excluded > 0) Term(lang.T("Excluded files", "排除的檔案"), r.Gaps.Excluded.ToString(inv));
        if (r.Foreign.Count > 0) Term(lang.T("XAML that is not WPF", "不是 WPF 的 XAML"), r.Foreign.Count.ToString(inv));
        sb.Append("</dl>\n");
        if (exitCode == 3) sb.Append($"<p class=\"note\"><strong>{H(lang.T("The result can't be trusted.", "結果不可信。"))}</strong> {H(why ?? "")}</p>\n");
        Notes(lang.T("Files that could not be parsed — they are missing from the audit:", "讀不懂的檔案，沒有稽核到："), [.. r.ParseErrors.Select(f => r.ParseWhy.TryGetValue(f, out var p) ? $"{f}: {p}" : f)]);
        Notes(lang.T("Folders that could not be read:", "讀不到的資料夾："), r.Gaps.Unreadable);
        Notes(lang.T("xamla11y-ignore comments that do nothing:", "沒作用的 xamla11y-ignore："), r.InvalidIgnores);
        Notes(lang.T("Controls it does not know (declare them in treatAs):", "不認得的控制項（用 treatAs 宣告）："), [.. r.UnknownControls.Select(u => string.Create(inv, $"{u.Element} ×{u.Count}"))]);

        // ── 每條規則 ──
        var rules = r.Rules.Where(s => s.Candidates > 0).ToList();
        if (rules.Count > 0)
        {
            Open(lang.T("By rule", "依規則"), lang.T("Rule", "規則"), lang.T("Findings", "問題"), lang.T("OK", "沒問題"), lang.T("Could not be judged", "判不出來"), lang.T("Not judged", "不判"));
            foreach (var s in rules)
                sb.Append(string.Create(inv, $"<tr><th scope=\"row\">{s.Rule} {H(lang.RuleTitle(s.Rule))}{(s.Off ? H(lang.T(" (off)", "（關）")) : "")}</th><td>{s.Findings}</td><td>{s.Pass}</td><td>{s.Blocked}</td><td>{s.NotJudged}</td></tr>\n"));
            Close();
        }

        // ── finding ──
        sb.Append($"<h2>{H(lang.T("Findings", "問題"))}</h2>\n");
        if (reported.Count == 0) sb.Append($"<p>{H(lang.T("None.", "沒有。"))}</p>\n");
        foreach (var group in reported.GroupBy(f => (f.Severity, f.Kind, f.NameIsEmpty)).OrderByDescending(g => g.Key.Severity).ThenBy(g => Kinds.All.ToList().IndexOf(g.Key.Kind)).ThenByDescending(g => g.Key.NameIsEmpty))
        {
            var (severity, kind, empty) = group.Key;
            var list = group.ToList();
            sb.Append($"<h3><span class=\"badge {Text.Slug(severity)}\">{Text.Slug(severity)}</span> {H(lang.Title(kind))} <small>{kind.Slug} · {list.Count.ToString(inv)}</small></h3>\n");
            sb.Append($"<p>{H(lang.Help(kind))}</p>\n");
            var refs = new List<string>();
            foreach (var c in r.Scope.CriteriaOf(kind).Select(c => Wcag.Criteria[c]))
                refs.Add($"WCAG {c.Number} {H(lang.Criterion(c))} ({c.Level})");
            foreach (var s in r.Scope.Standard.HasClauses ? [r.Scope.Standard] : Standard.All.Where(s => s.HasClauses))
                if (s.ClausesOf(new Scope(s, r.Scope.Level).CriteriaOf(kind).Where(r.Scope.Has), empty) is { Count: > 0 } clauses)
                    refs.Add($"{s.Title}: {string.Join(", ", clauses)}");
            sb.Append(refs.Count > 0
                ? $"<ul class=\"refs\">\n{string.Concat(refs.Select(x => $"<li>{x}</li>\n"))}</ul>\n"
                : $"<p class=\"refs\">{H(lang.T($"Not in {r.Scope.Title}; the config file turns it on.", $"{r.Scope.Title} 沒有這一條，是設定檔打開的。"))}</p>\n");

            // 清單不是表格：路徑與說明都長，窄視窗裡擠成一欄一個字
            sb.Append("<ol class=\"items\">\n");
            foreach (var f in list.Take(RowsPerKind))
            {
                sb.Append($"<li><code>{Path(f.Site)}</code><br/>{H(lang.Message(f.Message))}");
                if (Nvda.Says(f, lang) is { } says) sb.Append($"<br/><span class=\"says\">{H(lang.T("NVDA says: ", "NVDA 會念："))}<q>{H(says)}</q></span>");
                sb.Append("</li>\n");
            }
            sb.Append("</ol>\n");
            if (list.Count > RowsPerKind)
                sb.Append($"<p>{H(lang.T($"… and {(list.Count - RowsPerKind).ToString(inv)} more — the JSON report has all of them.", $"… 還有 {(list.Count - RowsPerKind).ToString(inv)} 筆，JSON 報告裡有全部。"))}</p>\n");
        }

        // ── 看不到的 ──
        var blocked = r.On.SelectMany(s => s.BlockedReasons.Select(b => (s.Rule, b.Key, b.Value))).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).ToList();
        if (blocked.Count > 0)
        {
            sb.Append($"<h2>{H(lang.T("Could not be judged", "判不出來的"))}</h2>\n");
            Open(lang.T("Why, and how often", "原因與次數"), lang.T("Rule", "規則"), lang.T("Reason", "原因"), lang.T("Places", "幾處"));
            foreach (var (rule, reason, n) in blocked.Take(100)) sb.Append($"<tr><td>{rule}</td><td>{H(lang.Reason(reason))}</td><td>{n.ToString(inv)}</td></tr>\n");
            Close();
            if (blocked.Count > 100) sb.Append($"<p>{H(lang.T($"… and {(blocked.Count - 100).ToString(inv)} more reasons — the JSON report has all of them.", $"… 還有 {(blocked.Count - 100).ToString(inv)} 種原因，JSON 報告裡有全部。"))}</p>\n");
        }

        // ── 人工檢查 ──
        sb.Append($"<h2>{H(lang.T("Checks a person has to do", "要人檢查的"))}</h2>\n");
        sb.Append($"<p>{H(Summary.Manual(lang))}</p>\n");
        Open(lang.T("Manual checks", "人工檢查"), "WCAG", lang.T("Check it like this", "怎麼檢查"));
        foreach (var (check, criteria) in ManualCheck.In(r.Scope))
            sb.Append($"<tr><th scope=\"row\">{H(string.Join(lang.T(", ", "、"), criteria.Select(c => $"{c.Number} {lang.Criterion(c)} ({c.Level})")))}</th><td>{H(lang.T(check.En, check.Zh))}</td></tr>\n");
        Close();

        sb.Append($"""
            </main>
            <footer><p>{H(lang.Scope)}</p><p>XamlA11y {H(toolVersion)}</p></footer>
            </body>
            </html>

            """);
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

        // 看不到的要數得出來：列前幾個，後面說還有幾個
        void Notes(string head, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.Append($"<div class=\"note\"><p><strong>{H(head)}</strong> {items.Count.ToString(inv)}</p>\n<ul>\n");
            foreach (var i in items.Take(ListMax)) sb.Append($"<li><code>{Path(i)}</code></li>\n");
            if (items.Count > ListMax) sb.Append($"<li>{H(lang.T($"… and {(items.Count - ListMax).ToString(inv)} more", $"… 還有 {(items.Count - ListMax).ToString(inv)} 個"))}</li>\n");
            sb.Append("</ul></div>\n");
        }

        void Term(string name, string value) => sb.Append($"<div><dt>{H(name)}</dt><dd>{H(value)}</dd></div>\n");

        void Open(string caption, params ReadOnlySpan<string> heads)
        {
            sb.Append($"<table>\n<caption>{H(caption)}</caption>\n<thead><tr>");
            foreach (var h in heads) sb.Append($"<th scope=\"col\">{H(h)}</th>");
            sb.Append("</tr></thead>\n<tbody>\n");
        }

        void Close() => sb.Append("</tbody>\n</table>\n");
    }

    /// <summary>跳脫。雙向控制字元寫成看得見的（會把後面的字的順序改掉），其他控制字元也是（HTML 不收）。</summary>
    private static string H(string s)
    {
        var visible = Text.Visible(s);
        if (visible.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
            visible = string.Concat(visible.Select(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r') ? $"\\u{(int)c:x4}" : c.ToString()));
        return WebUtility.HtmlEncode(visible);
    }

    /// <summary>路徑在斜線後面可以斷行。</summary>
    private static string Path(string s) => H(s).Replace("/", "/<wbr/>", StringComparison.Ordinal);

    private const string Style = """

        :root { color-scheme: light dark; --bg: #fff; --fg: #1b1b1b; --muted: #555; --line: #c8c8c8; --soft: #f3f3f3; --fail: #a4141c; --warn: #7a4a00; --info: #1c4f8a; --ok: #17603a; }
        @media (prefers-color-scheme: dark) { :root { --bg: #1b1b1b; --fg: #f0f0f0; --muted: #b8b8b8; --line: #555; --soft: #262626; --fail: #ff9a9a; --warn: #ffc877; --info: #9cc7ff; --ok: #8fe0b0; } }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--bg); color: var(--fg); font: 1rem/1.6 system-ui, "Segoe UI", "Microsoft JhengHei UI", sans-serif; }
        main, footer { max-width: 72rem; margin: 0 auto; padding: 1.5rem 1rem; }
        h1 { margin: 0 0 .5rem; font-size: 1.6rem; }
        h2 { margin: 2.5rem 0 .5rem; font-size: 1.3rem; border-bottom: 1px solid var(--line); }
        h3 { margin: 1.75rem 0 .25rem; font-size: 1.1rem; }
        small { font-weight: normal; color: var(--muted); }
        .verdict { display: inline-block; margin: 0; padding: .25rem .9rem; font-weight: 700; border: 2px solid currentColor; border-radius: .4rem; }
        .v0 { color: var(--ok); } .v1 { color: var(--fail); } .v2, .v3 { color: var(--warn); }
        dl { display: grid; gap: .25rem 2rem; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); margin: 1rem 0; }
        dt { color: var(--muted); } dd { margin: 0; font-weight: 600; }
        .badge { display: inline-block; padding: 0 .5rem; border: 2px solid currentColor; border-radius: .3rem; font-size: .85rem; }
        .fail { color: var(--fail); } .warn { color: var(--warn); } .info { color: var(--info); }
        .note { padding: .5rem .75rem; border-left: 4px solid var(--warn); background: var(--soft); }
        .note p { margin: 0; } .note ul { margin: .25rem 0 0; padding-left: 1.2rem; }
        .items li { margin: 0 0 .6rem; overflow-wrap: anywhere; } .says { color: var(--muted); }
        .refs { margin: .25rem 0 .75rem; padding-left: 1.2rem; color: var(--muted); }
        table { border-collapse: collapse; width: 100%; margin: .5rem 0 1rem; }
        caption { text-align: left; color: var(--muted); padding: .25rem 0; }
        th, td { text-align: left; vertical-align: top; padding: .4rem .6rem; border: 1px solid var(--line); overflow-wrap: anywhere; }
        thead th { background: var(--soft); }
        tbody th { font-weight: 600; }
        code { font-family: Consolas, "Cascadia Mono", monospace; font-size: .9em; }
        footer { color: var(--muted); border-top: 1px solid var(--line); }

        """;
}
