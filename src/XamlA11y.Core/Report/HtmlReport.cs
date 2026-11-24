using System.Globalization;
using System.Net;
using System.Text;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

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

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int exitCode, int baselined, string? why, string toolVersion, Lang? lang = null, int stale = 0, IReadOnlyList<Finding>? known = null, ReportInfo? info = null)
    {
        lang ??= Lang.En;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int Count(Severity s) => reported.Count(f => f.Severity == s);

        var title = info is null ? lang.T("XamlA11y accessibility audit", "XamlA11y 無障礙稽核") : lang.T($"{info.Name}: accessibility audit", $"{info.Name}：無障礙稽核");
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
            0 when known is { Count: > 0 } => lang.T("PASS (no new findings)", "通過（沒有新的問題）"),
            0 => lang.T("PASS", "通過"),
            1 => lang.T("FAIL", "沒通過"),
            3 => lang.T("NOT TRUSTWORTHY (exit 3)", "結果不可信（exit 3）"),
            _ => "exit " + exitCode.ToString(inv),
        };
        sb.Append($"<p class=\"verdict v{exitCode.ToString(inv)}\">{H(verdict)}</p>\n<dl>\n");
        Term(lang.T("Standard", "標準"), r.Scope.Title);
        Term(lang.T("Findings", "問題"), string.Create(inv, $"{Count(Severity.Fail)} fail, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info"));
        if (info is not null) Term(lang.T("Date", "日期"), info.DateText);
        if (info?.Commit is { } commit) Term("commit", commit);
        Term(lang.T("Resolution", "可解析率"), string.Create(inv, $"{r.Resolution:F1}%{lang.T($" ({r.GateJudged} of {r.GateJudged + r.GateBlocked})", $"（{r.GateJudged + r.GateBlocked} 處判得出 {r.GateJudged} 處）")}"));
        Term(lang.T("All checks", "全部的檢查"), lang.T($"{r.Judged} judged, {r.Blocked} unknown, {r.NotJudged} left out", $"判得出來 {r.Judged}、判不出來 {r.Blocked}、不判 {r.NotJudged}"));
        Term(lang.T("XAML files", "XAML 檔"), r.Files.ToString(inv));
        if (baselined > 0) Term(lang.T("In the baseline", "在 baseline 裡"), baselined.ToString(inv));
        if (stale > 0) Term(lang.T("In the baseline, not found this time", "在 baseline 裡、這次沒有的"), stale.ToString(inv));
        if (r.Suppressed > 0) Term(lang.T("Accepted with xamla11y-ignore", "用 xamla11y-ignore 放過的"), r.Suppressed.ToString(inv));
        if (r.Gaps.Excluded > 0) Term(lang.T("Excluded files", "排除的檔案"), r.Gaps.Excluded.ToString(inv));
        if (r.Foreign.Count > 0) Term(lang.T("XAML that is not WPF", "不是 WPF 的 XAML"), r.Foreign.Count.ToString(inv));
        sb.Append("</dl>\n");
        if (Summary.KnownDebt(known, exitCode, lang) is { } debt) sb.Append($"<p class=\"note\"><strong>{H(debt)}</strong></p>\n");
        sb.Append($"<p>{H(Summary.Coverage(r.Scope, lang))}</p>\n");
        if (exitCode == 3) sb.Append($"<p class=\"note\"><strong>{H(lang.T("The result can't be trusted.", "結果不可信。"))}</strong> {H(why ?? "")}</p>\n");
        Notes(lang.T("Files that could not be parsed — they are missing from the audit:", "讀不懂的檔案，沒有稽核到："), [.. r.ParseErrors.Select(f => r.ParseWhy.TryGetValue(f, out var p) ? $"{f}: {p}" : f)]);
        Notes(lang.T("Folders that could not be read:", "讀不到的資料夾："), r.Gaps.Unreadable);
        Notes(lang.T("xamla11y-ignore comments that do nothing:", "沒作用的 xamla11y-ignore："), [.. r.InvalidIgnores.Select(lang.Ignore)]);
        Notes(lang.T("xamla11y-ignore comments with nothing to accept (remove them, or a later problem stays hidden):", "沒有東西要放過的 xamla11y-ignore（拿掉，不然之後再壞也看不到）："), r.UnusedIgnores);
        if (r.CodeBuilt.Any) sb.Append($"<p class=\"note\"><strong>{H(Summary.CodeBuilt(r.CodeBuilt, lang))}</strong></p>\n");
        Notes(lang.T("Controls it does not know (say what they are with treatAs in xamla11y.config.json):", "不認得的控制項（在 xamla11y.config.json 用 treatAs 說明它是什麼）："), [.. r.UnknownControls.Select(u => string.Create(inv, $"{u.Element} ×{u.Count}"))]);

        // ── 每一項檢查 ──
        // 一處都沒有的也列：看的人要分得出「沒有東西可查」跟「沒查」
        var checks = r.Checks;
        if (checks.Count > 0)
        {
            Open(lang.T("What was checked", "查了什麼"), lang.T("Check", "檢查"), lang.T("Findings", "問題"), lang.T("OK", "沒問題"), lang.T("Unknown", "判不出來"), lang.T("Left out", "不判"));
            foreach (var s in checks)
                sb.Append(string.Create(inv, $"<tr><th scope=\"row\">{H(lang.CheckTitle(s.Check))}{(s.Off ? H(lang.T(" (off)", "（關）")) : s.Candidates == 0 ? H(lang.T(" (nothing to check)", "（沒有東西可查）")) : "")}</th><td>{s.Findings}</td><td>{s.Pass}</td><td>{s.Blocked}</td><td>{s.NotJudged}</td></tr>\n"));
            Close();
        }

        // ── 檔案 ──：該從哪裡開始
        var files = reported.GroupBy(f => f.File).Select(g => (File: g.Key, Fail: g.Count(f => f.Severity == Severity.Fail), Warn: g.Count(f => f.Severity == Severity.Warn), Info: g.Count(f => f.Severity == Severity.Info)))
            .OrderByDescending(x => x.Fail).ThenByDescending(x => x.Warn).ThenBy(x => x.File, StringComparer.Ordinal).ToList();
        if (files.Count > 1)
        {
            Open(lang.T("Files with the most findings", "問題最多的檔案"), lang.T("File", "檔案"), "fail", "warn", "info");
            foreach (var x in files.Take(15)) sb.Append(string.Create(inv, $"<tr><th scope=\"row\">{Path(x.File)}</th><td>{x.Fail}</td><td>{x.Warn}</td><td>{x.Info}</td></tr>\n"));
            Close();
            if (files.Count > 15) sb.Append($"<p>{H(lang.T($"… and {(files.Count - 15).ToString(inv)} more files", $"… 還有 {(files.Count - 15).ToString(inv)} 個檔案"))}</p>\n");
        }

        // ── finding ──：先分組，組裡面先嚴重的
        sb.Append($"<h2>{H(lang.T("Findings", "問題"))}</h2>\n");
        if (reported.Count == 0) sb.Append($"<p>{H(known is { Count: > 0 } ? lang.T("No new ones.", "沒有新的。") : lang.T("None.", "沒有。"))}</p>\n");
        foreach (var ((severity, kind, empty), list, part) in Kinds.Groups.SelectMany(part => reported.Where(f => f.Kind.Group == part.Id).GroupBy(f => (f.Severity, f.Kind, f.NameIsEmpty))
            .OrderByDescending(g => g.Key.Severity).ThenBy(g => Kinds.All.ToList().IndexOf(g.Key.Kind)).ThenByDescending(g => g.Key.NameIsEmpty).Select((g, i) => (g.Key, g.ToList(), i == 0 ? part : null))))
        {
            if (part is not null) sb.Append($"<h3>{H(lang.GroupTitle(part))} <small>{reported.Count(f => f.Kind.Group == part.Id).ToString(inv)}</small></h3>\n");
            sb.Append($"<h4><span class=\"badge {Text.Slug(severity)}\">{Text.Slug(severity)}</span> {H(lang.Title(kind))} <small>{kind.Slug} · {list.Count.ToString(inv)}</small></h4>\n");
            sb.Append($"<p>{H(lang.Help(kind))} <a href=\"{H(kind.Uri(lang.IsZh))}\">{H(lang.T($"Examples for {kind.Slug}", $"{kind.Slug} 的寫法範例"))}</a></p>\n");
            var refs = new List<string>();
            foreach (var c in r.Scope.CriteriaOf(kind).Select(c => Wcag.Criteria[c]))
                refs.Add($"WCAG {c.Number} {H(lang.Criterion(c))} ({c.Level})");
            foreach (var s in r.Scope.Standard.HasClauses ? [r.Scope.Standard] : Standard.All.Where(s => s.HasClauses))
                if (s.ClausesOf(new Scope(s, r.Scope.Level).CriteriaOf(kind).Where(r.Scope.Has), empty) is { Count: > 0 } clauses)
                    refs.Add($"{s.Title}: {string.Join(", ", clauses)}");
            // 清單不是表格：路徑與說明都長，窄視窗裡擠成一欄一個字
            sb.Append("<ol class=\"items\">\n");
            foreach (var f in list.Take(RowsPerKind))
            {
                sb.Append($"<li><code>{Path(f.Site)}</code><br/>{H(lang.Message(f.Message))}");
                if (Nvda.Says(f, lang) is { } says) sb.Append($"<br/><span class=\"says\">{H(lang.T("NVDA reads: ", "NVDA 會念："))}{H(says)}</span>");
                sb.Append("</li>\n");
            }
            sb.Append("</ol>\n");
            // 條文放在後面：先看問題在哪
            sb.Append(refs.Count > 0
                ? $"<ul class=\"refs\">\n{string.Concat(refs.Select(x => $"<li>{x}</li>\n"))}</ul>\n"
                : $"<p class=\"refs\">{H(lang.T($"Not in {r.Scope.Title}; the config file turns it on.", $"{r.Scope.Title} 沒有這一條，是設定檔打開的。"))}</p>\n");
            if (list.Count > RowsPerKind)
                sb.Append($"<p>{H(lang.T($"… and {(list.Count - RowsPerKind).ToString(inv)} more — the JSON report has all of them.", $"… 還有 {(list.Count - RowsPerKind).ToString(inv)} 筆，JSON 報告裡有全部。"))}</p>\n");
        }

        // ── 放過的 ──：誰、為什麼
        if (r.Suppressions.Count > 0)
        {
            sb.Append($"<h2>{H(lang.T("Accepted with xamla11y-ignore", "用 xamla11y-ignore 放過的"))}</h2>\n");
            Open(lang.T("Accepted findings and why", "放過的問題與理由"), lang.T("Finding", "問題"), lang.T("Where", "在哪"), lang.T("Reason", "理由"));
            foreach (var s in r.Suppressions.Take(RowsPerKind))
                sb.Append($"<tr><td>{H(lang.Title(s.Kind))}</td><td><code>{Path($"{s.File}:{s.Line.ToString(inv)}")}</code></td><td>{H(s.Reason)}</td></tr>\n");
            Close();
            if (r.Suppressions.Count > RowsPerKind) sb.Append($"<p>{H(lang.T($"… and {(r.Suppressions.Count - RowsPerKind).ToString(inv)} more — the JSON report has all of them.", $"… 還有 {(r.Suppressions.Count - RowsPerKind).ToString(inv)} 筆，JSON 報告裡有全部。"))}</p>\n");
        }

        // ── 看不到的 ──
        var blocked = r.On.SelectMany(s => s.BlockedReasons.Select(b => (s.Check, b.Key, b.Value))).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).ToList();
        if (blocked.Count > 0)
        {
            sb.Append($"<h2>{H(lang.T("Unknown", "判不出來的"))}</h2>\n");
            var where = r.Judgements.Where(j => j.Verdict == Verdict.Blocked).ToLookup(j => (j.Check, Context.CutMiddle(j.Reason ?? "", 200)), j => $"{j.File}:{j.Line.ToString(inv)}");
            Open(lang.T("Why, how often and where", "原因、次數、在哪"), lang.T("Check", "檢查"), lang.T("Reason", "原因"), lang.T("Places", "幾處"), lang.T("Where", "在哪"));
            foreach (var (check, reason, n) in blocked.Take(100))
                sb.Append($"<tr><td>{H(lang.CheckTitle(check))}</td><td>{H(lang.Reason(reason))}</td><td>{n.ToString(inv)}</td><td>{string.Join(", ", where[(check, reason)].Take(3).Select(x => $"<code>{Path(x)}</code>"))}{(n > 3 ? ", …" : "")}</td></tr>\n");
            Close();
            if (blocked.Count > 100) sb.Append($"<p>{H(lang.T($"… and {(blocked.Count - 100).ToString(inv)} more reasons — the JSON report has all of them.", $"… 還有 {(blocked.Count - 100).ToString(inv)} 種原因，JSON 報告裡有全部。"))}</p>\n");
        }

        // ── 用語 ──
        sb.Append($"<h2>{H(lang.T("What the words mean", "用語"))}</h2>\n<dl class=\"words\">\n");
        foreach (var (word, meaning) in Words(lang)) Term(word, meaning);
        sb.Append("</dl>\n");

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

    private static IEnumerable<(string, string)> Words(Lang lang) =>
    [
        ("fail", lang.T("The XAML decides it and the criterion leaves no room. Stops the build by default.", "XAML 就決定了，準則沒有留餘地。預設會擋建置。")),
        ("warn", lang.T("Very likely a problem; something the XAML does not show may make it fine.", "很可能是問題；XAML 看不到的東西可能讓它沒問題。")),
        ("info", lang.T("Worth a look.", "值得看一眼。")),
        (lang.T("Resolution", "可解析率"), lang.T("Of the names, inputs, focus and mouse-only checks (the ones that can stop a build), the share that could be judged: judged ÷ (judged + unknown). Places that are left out are not in it.", "名稱、輸入框、焦點、只能用滑鼠這四項（會擋建置的）裡，判得出來的比例：判得出來 ÷（判得出來 + 判不出來）。不判的不算在內。")),
        (lang.T("Unknown", "判不出來"), lang.T("This tool should have been able to tell and could not. Not the same as fine.", "應該判得出來卻判不出來。不等於沒問題。")),
        (lang.T("Left out", "不判"), lang.T("Only known when the program runs, or not the job of this check.", "執行時才知道，或不歸這項檢查管。")),
        (lang.T("Accessible name", "名稱"), lang.T("What a screen reader says for a control. Windows hands it out through UI Automation.", "螢幕閱讀器替一個控制項念的字。Windows 透過 UI Automation 提供。")),
        ("AutomationProperties.Name, LabeledBy", lang.T("The XAML properties that set the name, directly or from another element.", "在 XAML 裡設名稱的屬性：直接寫，或拿別的元素的文字。")),
        ("AutomationPeer", lang.T("The class that describes a WPF control to UI Automation. Without one a screen reader does not see the control.", "替 WPF 控制項向 UI Automation 說明自己的類別。沒有它，螢幕閱讀器看不到這個控制項。")),
        ("NVDA", lang.T("A free screen reader for Windows. What it reads was run with NVDA 2026.2 in English and Traditional Chinese. Shown are the type, the name and the description; states (checked, collapsed, blank) and the group around it are left out.", "Windows 上免費的螢幕閱讀器。念法是用 NVDA 2026.2 的英文與繁體中文跑過的，列的是型別、名稱與描述；狀態（核取、折疊、空白）與外面的群組沒有列。")),
    ];

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
        h3 { margin: 1.75rem 0 .25rem; font-size: 1.15rem; }
        h4 { margin: 1.5rem 0 .25rem; font-size: 1.05rem; }
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
        a { color: var(--info); }
        .words { grid-template-columns: 1fr; } .words dt { color: var(--fg); font-weight: 600; } .words dd { font-weight: normal; }
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
