using System.Globalization;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;

namespace XamlA11y.Core.Report;

/// <summary>主控台：先總結、再清單、最後是看不到的。每一項檢查的數字與怎麼修在 HTML。</summary>
public static class ConsoleReport
{
    private const int Reasons = 5;

    /// <param name="reported">baseline 蓋住的不在裡面。</param>
    /// <param name="stale">baseline 裡這次沒有的。</param>
    public static void Print(TextWriter o, AuditResult r, IReadOnlyList<Finding> reported, int stale, Lang lang)
    {
        var inv = CultureInfo.InvariantCulture;
        int Count(Severity s) => reported.Count(f => f.Severity == s);

        // ── 總結 ──
        o.WriteLine(lang.T($"xamla11y audit — {r.Files} XAML file(s), {r.Scope.Title}", $"xamla11y 無障礙稽核：{r.Files} 個 XAML 檔，{r.Scope.Title}"));
        var known = r.Findings.Count - reported.Count;
        o.WriteLine(lang.T($"  {Count(Severity.Fail)} fail, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info", $"  {Count(Severity.Fail)} fail、{Count(Severity.Warn)} warn、{Count(Severity.Info)} info")
            + (known > 0 ? lang.T($" (new; {known} more in the baseline)", $"（新的；baseline 裡另外有 {known} 筆）") : ""));
        foreach (var (title, n, kinds) in Summary.Groups(reported, lang)) o.WriteLine(lang.T($"    {title}: {n} ({kinds})", $"    {title}：{n}（{kinds}）"));
        o.WriteLine("  " + Summary.Resolution(r, lang));
        o.WriteLine("  " + Summary.Places(r, lang));
        if (stale > 0 && known == 0 && reported.Count > 0)
            o.WriteLine(lang.T($"  !! none of the {stale} entries of the baseline match: was it written for another root folder?", $"  !! baseline 的 {stale} 筆一筆都對不上：是不是在別的資料夾寫的？"));
        else if (stale > 0)
            o.WriteLine(lang.T($"  stale baseline: {stale} (not found this time: fixed, ignored, or not in {r.Scope.Title}; --prune-baseline drops them and accepts nothing new)", $"  baseline 裡這次沒有的：{stale}（修好了、放過了，或不在 {r.Scope.Title} 裡；--prune-baseline 只拿掉它們，不會把新的記進去）"));
        if (r.Suppressed > 0) o.WriteLine(lang.T($"  accepted with xamla11y-ignore: {r.Suppressed}", $"  用 xamla11y-ignore 放過的：{r.Suppressed}"));
        if (r.UnusedIgnores.Count > 0)
            o.WriteLine(lang.T("  xamla11y-ignore with nothing to accept (remove it): ", "  沒有東西要放過的 xamla11y-ignore（可以拿掉）：") + string.Join(lang.T(", ", "、"), r.UnusedIgnores.Take(3).Select(Text.Safe)) + (r.UnusedIgnores.Count > 3 ? lang.T($" and {r.UnusedIgnores.Count - 3} more", $" 等 {r.UnusedIgnores.Count} 處") : ""));
        if (r.CodeBuilt.Any) o.WriteLine("  " + Summary.CodeBuilt(r.CodeBuilt, lang));
        if (r.Checks.Where(c => c.Off).ToList() is { Count: > 0 } off) o.WriteLine(lang.T("  turned off: ", "  關掉的：") + string.Join(lang.T(", ", "、"), off.Select(c => lang.CheckTitle(c.Check))));
        if (r.Gaps.Excluded > 0) o.WriteLine(lang.T($"  excluded: {r.Gaps.Excluded} XAML file(s)", $"  排除 {r.Gaps.Excluded} 個 XAML 檔"));
        foreach (var g in r.Foreign.GroupBy(f => f.Dialect)) o.WriteLine(lang.T($"  not checked: {g.Count()} {g.Key} file(s) (WPF only)", $"  沒有檢查：{g.Count()} 個 {g.Key} 檔（只看 WPF）"));
        foreach (var f in r.ParseErrors) o.WriteLine(lang.T("  !! could not parse ", "  !! 讀不懂 ") + Text.Safe(f) + (r.ParseWhy.TryGetValue(f, out var why) ? lang.T(": ", "：") + Text.Safe(why) : ""));
        foreach (var d in r.Gaps.Unreadable) o.WriteLine(lang.T("  !! could not read the folder ", "  !! 資料夾讀不到 ") + Text.Safe(d));
        foreach (var f in r.InvalidIgnores) o.WriteLine($"  !! {Text.Safe(lang.Ignore(f))}");

        Findings(o, reported, lang);

        // ── 看不到的 ──
        var blocked = r.On.SelectMany(c => c.BlockedReasons.Select(b => (c.Check, b.Key, b.Value))).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).ToList();
        if (blocked.Count > 0)
        {
            o.WriteLine();
            o.WriteLine(lang.T($"  unknown: {r.Blocked}", $"  判不出來：{r.Blocked}"));
            foreach (var (check, reason, n) in blocked.Take(Reasons))
                o.WriteLine(lang.T($"    {n} × {lang.CheckTitle(check)}: ", $"    {n} × {lang.CheckTitle(check)}：") + Trunc(Text.Safe(lang.Reason(reason)), 110));
            if (blocked.Count > Reasons) o.WriteLine(lang.T($"    … and {blocked.Count - Reasons} more reasons", $"    … 還有 {blocked.Count - Reasons} 種原因"));
            if (r.UnknownControls.Count > 0)
                o.WriteLine(lang.T("    controls it does not know: ", "    不認得的控制項：") + string.Join(lang.T(", ", "、"), r.UnknownControls.Take(8).Select(u => $"{Text.Safe(u.Element)} ×{u.Count}"))
                    + lang.T("  (if you know what they are, say so in xamla11y.config.json: ", "（知道是什麼的話寫在 xamla11y.config.json：") + $"\"treatAs\": {{ \"{Text.Safe(r.UnknownControls[0].Element)}\": \"Button\" }}" + lang.T(")", "）"));
        }
        o.WriteLine();
        var manual = ManualCheck.In(r.Scope).Count;
        o.WriteLine(lang.T($"  {manual} more checks need a person; --html and --md list them (the common ones, not the rest of WCAG).", $"  另外有 {manual} 項要人檢查，--html 與 --md 有清單（最常出問題的，不是 WCAG 剩下的全部）。"));
        o.WriteLine("  " + lang.Scope);
    }

    private const int ListAll = 40;
    private const int PerKind = 4;

    /// <summary>少的時候全部列；多的時候（第一次跑常見）每一種列幾筆，加上哪幾個檔案最多 —— 六十筆同一種的清單看不出該從哪裡開始。</summary>
    private static void Findings(TextWriter o, IReadOnlyList<Finding> reported, Lang lang)
    {
        if (reported.Count == 0) return;
        o.WriteLine();
        if (reported.Count <= ListAll)
        {
            foreach (var f in reported.OrderByDescending(f => f.Severity)) One(f, "  ");
            return;
        }
        foreach (var group in reported.GroupBy(f => (f.Severity, f.Kind)).OrderByDescending(g => g.Key.Severity).ThenByDescending(g => g.Count()))
        {
            var (severity, kind) = group.Key;
            o.WriteLine($"  {Text.Slug(severity)} {kind.Slug} ×{group.Count()}  {lang.Title(kind)}");
            foreach (var f in group.Take(PerKind)) One(f, "    ");
            if (group.Count() > PerKind) o.WriteLine(lang.T($"      … and {group.Count() - PerKind} more", $"      … 還有 {group.Count() - PerKind} 筆"));
        }
        var files = reported.Where(f => f.Severity >= Severity.Warn).GroupBy(f => f.File).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(5).ToList();
        if (files.Count > 1) o.WriteLine(lang.T("  most in: ", "  最多的檔案：") + string.Join(lang.T(", ", "、"), files.Select(g => $"{Text.Safe(g.Key)} ×{g.Count()}")));
        o.WriteLine(lang.T("  all of them: --html <file> (to read) or --json <file>", "  全部：--html <檔>（給人看）或 --json <檔>"));

        void One(Finding f, string indent)
        {
            var says = Nvda.Says(f, lang) is { } x ? lang.T("   NVDA: ", "　NVDA：") + Text.Safe(x) : "";
            o.WriteLine(indent.Length > 2 ? $"{indent}{Text.Safe(f.Site)}{says}" : $"{indent}{Text.Slug(f.Severity),-4} {f.Kind.Slug,-18} {Text.Safe(f.Site)}{says}");
            o.WriteLine($"{indent}     {Text.Safe(lang.Message(f.Message))}");
        }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
