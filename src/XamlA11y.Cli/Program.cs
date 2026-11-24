using System.Globalization;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;
using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

// 退出碼契約（三件套同一套）：0 通過 ／ 1 找到違規 ／ 2 用法、設定或輸入檔錯誤 ／ 3 結果不可信
public static class Program
{
    public static int Main(string[] args)
    {
        // 報告裡印的是使用者自己的介面文字；.NET 被導向時用系統字碼頁（繁中 Windows 是 Big5）會變亂碼
        try { Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
        catch (IOException) { }
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0) { Usage.Print(stdout); return 2; }
        switch (args[0])
        {
            case "-h" or "--help" or "help": Usage.Print(stdout); return 0;
            case "--version": stdout.WriteLine(Version()); return 0;
            case "audit" when args.Contains("--help") || args.Contains("-h"): Usage.Print(stdout); return 0;
            case "audit": return AuditCommand.Run(args[1..], stdout, stderr);
            case "rules": return Usage.Rules(args[1..], stdout, stderr);
            default:
                stderr.WriteLine($"unknown command: {Text.Safe(args[0])}");
                stderr.WriteLine(Directory.Exists(args[0]) ? "that is a folder: did you mean 'xamla11y audit <folder>'?" : "run 'xamla11y --help' for usage");
                return 2;
        }
    }

    public static string Version() =>
        typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion is { } v
            ? (v.IndexOf('+') > 0 ? v[..v.IndexOf('+')] : v)
            : "0.0.0-dev";
}

internal sealed class Options
{
    public string? Root, Json, Sarif, Md, Html, OpenAcr, Baseline, WriteBaseline, Config, Standard, Level, Language;
    public Severity FailOn = Severity.Fail;
    public double MinCoverage = 60;
    public bool MsBuild;
    public readonly List<string> Exclude = [];
    public readonly HashSet<string> Given = new(StringComparer.Ordinal);

    public static (Options Options, string? Error) Parse(string[] args)
    {
        var o = new Options();
        var paths = false;      // 「--」之後的都是路徑：叫 --version 的資料夾也不會被當成旗標
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!paths && a == "--") { paths = true; continue; }
            if (paths || !a.StartsWith('-'))
            {
                if (a.Length == 0) return (o, "the root directory is an empty string");
                if (o.Root is not null) return (o, $"unexpected extra argument: {a}");
                o.Root = a;
                continue;
            }

            if (!o.Given.Add(a) && a is not "--exclude") return (o, $"{a} was given twice");
            string? value = null;
            if (a is "--json" or "--sarif" or "--md" or "--html" or "--openacr" or "--standard" or "--level" or "--lang" or "--baseline" or "--write-baseline" or "--config" or "--fail-on" or "--min-coverage" or "--exclude")
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || args[i + 1].Length == 0) return (o, $"{a} needs a value");
                value = args[++i];
            }

            switch (a)
            {
                case "--json": o.Json = value; break;
                case "--sarif": o.Sarif = value; break;
                case "--md": o.Md = value; break;
                case "--html": o.Html = value; break;
                case "--openacr": o.OpenAcr = value; break;
                case "--standard":
                    if (Core.Analysis.Standard.ById(value!) is null) return (o, "--standard takes one of: " + A11yConfig.StandardIds);
                    o.Standard = value;
                    break;
                case "--level":
                    if (value is not ("A" or "AA")) return (o, "--level takes 'A' or 'AA'");
                    o.Level = value;
                    break;
                case "--lang":
                    if (Lang.ById(value!) is null) return (o, "--lang takes one of: " + Lang.Ids);
                    o.Language = value;
                    break;
                case "--baseline": o.Baseline = value; break;
                case "--write-baseline": o.WriteBaseline = value; break;
                case "--config": o.Config = value; break;
                case "--exclude": o.Exclude.Add(value!); break;
                case "--msbuild": o.MsBuild = true; break;
                case "--fail-on":
                    if (value is not ("fail" or "warn")) return (o, "--fail-on takes 'fail' or 'warn'");
                    o.FailOn = value == "warn" ? Severity.Warn : Severity.Fail;
                    break;
                case "--min-coverage":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mc) || !double.IsFinite(mc) || mc < 0 || mc > 100)
                        return (o, "--min-coverage takes a number between 0 and 100");
                    o.MinCoverage = mc;
                    break;
                default: return (o, $"unknown argument: {a}");
            }
        }
        return (o, null);
    }
}

internal static class AuditCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var (opts, error) = Options.Parse(args);
        if (error is not null) { stderr.WriteLine(Text.Safe(error)); return 2; }
        if (opts.Root is null) { stderr.WriteLine("audit needs a root directory"); return 2; }
        if (!Directory.Exists(opts.Root)) { stderr.WriteLine($"not a directory: {Text.Safe(opts.Root)}"); return 2; }
        if (opts.Baseline is not null && opts.WriteBaseline is not null) { stderr.WriteLine("--baseline and --write-baseline cannot be combined"); return 2; }
        if (opts.WriteBaseline is not null && (opts.Json ?? opts.Sarif ?? opts.Md ?? opts.Html ?? opts.OpenAcr) is not null || opts is { WriteBaseline: not null, MsBuild: true })
        { stderr.WriteLine("--write-baseline only writes the baseline; run again for the reports"); return 2; }
        if (opts.Baseline is not null && !File.Exists(opts.Baseline)) { stderr.WriteLine($"baseline not found: {Text.Safe(opts.Baseline)}"); return 2; }

        // ── 設定檔（只補旗標沒給的值）──
        var config = new A11yConfig();
        var configPath = opts.Config ?? Path.Combine(opts.Root, A11yConfig.DefaultFileName);
        if (opts.Config is not null && !File.Exists(opts.Config)) { stderr.WriteLine($"config file not found: {Text.Safe(opts.Config)}"); return 2; }
        if (File.Exists(configPath))
        {
            var (loaded, problems) = A11yConfig.Load(configPath);
            if (problems.Count > 0)
            {
                stderr.WriteLine($"{Text.Safe(configPath)} has {problems.Count} problem(s):");
                foreach (var p in problems) stderr.WriteLine("  " + Text.Safe(p.ToString()));
                return 2;
            }
            config = loaded!;
            stderr.WriteLine($"config: {Text.Safe(configPath)}");
        }
        if (!opts.Given.Contains("--min-coverage") && config.MinCoverage is { } mc) opts.MinCoverage = mc;
        // 旗標 > 設定檔 > 預設
        var scope = new Scope(Core.Analysis.Standard.ById(opts.Standard ?? config.Standard ?? "") ?? Scope.Default.Standard, opts.Level ?? config.Level ?? Scope.Default.Level);
        var lang = Lang.ById(opts.Language ?? config.Language ?? "") ?? Lang.En;

        var project = Project.Load(opts.Root, [.. opts.Exclude, .. config.Exclude ?? []], config.TreatAs);
        var result = Auditor.Run(project, new AuditOptions { Severities = config.Severities, Scope = scope, ThemeShowsFocus = config.ThemeShowsFocus ?? false });

        // ── baseline ──
        if (opts.WriteBaseline is not null)
        {
            // 什麼都沒看到的時候寫出一份空的 baseline，等於替「沒檢查」蓋章
            if (Untrustworthy(result, double.Epsilon) is { } empty) { stderr.WriteLine(empty); stderr.WriteLine("baseline not written"); return 3; }
            if (!TryWrite(opts.WriteBaseline, Baseline.From(result.Findings, DateOnly.FromDateTime(DateTime.Now)).ToJson(), stderr)) return 2;
            stdout.WriteLine(lang.T($"wrote baseline with {result.Findings.Count} known finding(s) → {Text.Safe(opts.WriteBaseline)}", $"寫好 baseline，記下 {result.Findings.Count} 筆已知的 → {Text.Safe(opts.WriteBaseline)}"));
            return 0;
        }

        IReadOnlyList<Finding> reported = result.Findings;
        var baselined = 0;
        var stale = 0;
        if (opts.Baseline is not null)
        {
            try { (reported, baselined, stale) = Baseline.Parse(File.ReadAllText(opts.Baseline)).Split(result.Findings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                stderr.WriteLine($"cannot read baseline: {Text.Safe(ex.Message)}");
                return 2;
            }
        }

        // ── 結論 ──
        // ⚠ 「結果不可信」排在「有沒有 finding」前面：只看到三成就說「沒問題」是謊報健康
        var why = Untrustworthy(result, opts.MinCoverage);
        var blocking = reported.Count(f => f.Severity >= opts.FailOn);
        var exit = why is not null ? 3 : blocking > 0 ? 1 : 0;

        Print(result, reported, baselined, stale, opts, lang, stdout);
        if (why is not null) stderr.WriteLine("\n" + Text.Safe(why));
        else if (blocking > 0)
        {
            stderr.WriteLine("\n" + lang.T($"{blocking} finding(s) at or above '{Text.Slug(opts.FailOn)}'", $"{blocking} 筆在 '{Text.Slug(opts.FailOn)}' 以上"));
            // 第一次跑常是幾十筆：一上來就紅的閘門會被關掉
            if (opts.Baseline is null && blocking >= 10)
                stderr.WriteLine(lang.T("first run on this project? record these as known with --write-baseline <file>, then run with --baseline <file> to fail only on new ones", "第一次在這個專案跑？用 --write-baseline <檔> 記成已知，之後用 --baseline <檔> 只擋新增的"));
        }

        if (opts.Json is not null && !TryWrite(opts.Json, JsonReport.Create(result, reported, baselined, Program.Version(), exit), stderr)) return 2;
        if (opts.Sarif is not null && !TryWrite(opts.Sarif, SarifReport.Create(reported, Program.Version(), scope), stderr)) return 2;
        if (opts.Md is not null && !TryWrite(opts.Md, MarkdownReport.Create(result, reported, exit, baselined, why, lang, Known(result, reported)), stderr)) return 2;
        if (opts.Html is not null && !TryWrite(opts.Html, HtmlReport.Create(result, reported, exit, baselined, why, Program.Version(), lang, stale, Known(result, reported)), stderr)) return 2;
        // 已知債（baseline）也是問題：合規報告的草稿寫全部的 finding
        if (opts.OpenAcr is not null && !TryWrite(opts.OpenAcr, OpenAcrReport.Create(result, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(opts.Root))), Program.Version(), DateOnly.FromDateTime(DateTime.Now)), stderr)) return 2;
        foreach (var path in new[] { opts.Json, opts.Sarif, opts.Md, opts.Html, opts.OpenAcr })
            if (path is not null) stdout.WriteLine(lang.T("wrote ", "寫好 ") + Text.Safe(path));
        if (opts.MsBuild) stdout.Write(MsBuildReport.Create(reported, opts.Root, opts.FailOn, exit));
        return exit;
    }

    /// <summary>baseline 蓋住的：還沒修的問題。</summary>
    private static IReadOnlyList<Finding> Known(AuditResult r, IReadOnlyList<Finding> reported) => [.. r.Findings.Except(reported)];

    private static string? Untrustworthy(AuditResult r, double minCoverage)
    {
        if (r.Files == 0) return "no .xaml files found — result is not trustworthy";
        if (r.Foreign.Count > 0 && r.Foreign.Count + r.ParseErrors.Count == r.Files) return $"none of the {r.Files} XAML file(s) is WPF — result is not trustworthy";
        if (r.ParseErrors.Count == r.Files) return "no XAML file could be parsed — result is not trustworthy";
        var wpf = r.Files - r.Foreign.Count;
        if (minCoverage > 0 && 100.0 * (wpf - r.ParseErrors.Count) / wpf < minCoverage)
            return string.Create(CultureInfo.InvariantCulture, $"{r.ParseErrors.Count} of {wpf} XAML file(s) could not be parsed, above what --min-coverage {minCoverage:0.#}% allows — result is not trustworthy");
        // 全部關掉：沒有東西被檢查，不是通過
        if (!r.On.Any()) return "every rule is turned off — nothing was checked, result is not trustworthy";
        if (r.Candidates == 0) return "no interactive elements found — result is not trustworthy";
        // 全部卡住才是「沒看到」；全部都是執行期的資料（名稱綁定）不是專案改得了的，也不是工具漏看
        if (minCoverage > 0 && r.Judged == 0 && r.Blocked > 0) return "not one element could be judged — result is not trustworthy";
        if (r.Resolution < minCoverage)
            return string.Create(CultureInfo.InvariantCulture, $"resolution {r.Resolution:F1}% is below --min-coverage {minCoverage:0.#}% — result is not trustworthy");
        return null;
    }

    private static void Print(AuditResult r, IReadOnlyList<Finding> reported, int baselined, int stale, Options opts, Lang lang, TextWriter o)
    {
        var inv = CultureInfo.InvariantCulture;
        if (lang.IsZh) { PrintZh(r, reported, baselined, stale, lang, o); return; }
        o.WriteLine($"xamla11y audit — {r.Files} XAML file(s), {r.Candidates} check(s)");
        o.WriteLine($"  standard         {r.Scope.Title}");
        o.WriteLine(string.Create(inv, $"  resolution       {r.Resolution:F1}%  ({r.Judged} judged, {r.Blocked} unknown; {r.NotJudged} left out)"));
        if (r.Suppressed > 0) o.WriteLine($"  suppressed       {r.Suppressed}");
        if (baselined > 0) o.WriteLine($"  in baseline      {baselined}");
        if (stale > 0) o.WriteLine($"  stale baseline   {stale}  (fixed since, or not in {r.Scope.Title}; write the baseline again to drop them)");
        foreach (var s in r.Rules.Where(s => s.Candidates > 0))
            o.WriteLine(string.Create(inv, $"    {s.Rule} {Kinds.RuleTitle(s.Rule),-20} {s.Findings,4} finding(s), {s.Pass,4} ok, {s.Blocked,4} unknown, {s.NotJudged,4} left out{(s.Off ? "  (off)" : "")}"));

        foreach (var f in r.ParseErrors) o.WriteLine($"  !! could not parse {Text.Safe(f)}{(r.ParseWhy.TryGetValue(f, out var why) ? ": " + Text.Safe(why) : "")}");
        if (r.Gaps.Excluded > 0) o.WriteLine($"  {r.Gaps.Excluded} XAML file(s) excluded");
        foreach (var d in r.Gaps.Unreadable) o.WriteLine($"  !! could not read the folder {Text.Safe(d)}");
        foreach (var f in r.InvalidIgnores) o.WriteLine($"  !! {Text.Safe(f)}");
        foreach (var g in r.Foreign.GroupBy(f => f.Dialect)) o.WriteLine($"  {g.Count()} {g.Key} file(s) not checked (WPF only)");
        if (r.UnknownControls.Count > 0)
            o.WriteLine($"  unknown controls {string.Join(", ", r.UnknownControls.Take(8).Select(u => $"{Text.Safe(u.Element)} ×{u.Count}"))}  (if you know what they are, say so in xamla11y.config.json: \"treatAs\": {{ \"{Text.Safe(r.UnknownControls[0].Element)}\": \"Button\" }})");
        var blocked = r.On.SelectMany(s => s.BlockedReasons.Select(b => (s.Rule, b.Key, b.Value))).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).Take(6).ToList();
        foreach (var (rule, reason, n) in blocked) o.WriteLine($"  unknown {n,4}  {rule}: {Trunc(Text.Safe(reason), 110)}");

        foreach (var kind in Kinds.All)
        {
            var n = r.Findings.Count(f => f.Kind == kind);
            if (n == 0) continue;
            var fresh = reported.Count(f => f.Kind == kind);
            o.WriteLine(baselined > 0 ? $"    {kind.Slug,-18} {n}  ({fresh} new, {n - fresh} already in the baseline)" : $"    {kind.Slug,-18} {n}");
        }

        PrintFindings(reported, lang, o);
        o.WriteLine();
        o.WriteLine($"  {ManualCheck.In(r.Scope).Count} more checks need a person; --html and --md list them (the common ones, not the rest of WCAG).");
        o.WriteLine("  " + Summary.Scope);
    }

    /// <summary>主控台的中文版。欄位用不了對齊（全形字兩格寬），寫成句子。</summary>
    private static void PrintZh(AuditResult r, IReadOnlyList<Finding> reported, int baselined, int stale, Lang lang, TextWriter o)
    {
        var inv = CultureInfo.InvariantCulture;
        o.WriteLine($"xamla11y 無障礙稽核：{r.Files} 個 XAML 檔，{r.Candidates} 處");
        o.WriteLine($"  標準：{r.Scope.Title}");
        o.WriteLine(string.Create(inv, $"  可解析率：{r.Resolution:F1}%（判得出來 {r.Judged}、判不出來 {r.Blocked}、不判 {r.NotJudged}）"));
        if (r.Suppressed > 0) o.WriteLine($"  壓掉的：{r.Suppressed}");
        if (baselined > 0) o.WriteLine($"  在 baseline 裡：{baselined}");
        if (stale > 0) o.WriteLine($"  baseline 裡這次沒有的：{stale}（修好了，或不在 {r.Scope.Title} 裡；重寫一次 baseline 就會拿掉）");
        foreach (var s in r.Rules.Where(s => s.Candidates > 0))
            o.WriteLine($"    {s.Rule} {lang.RuleTitle(s.Rule)}：問題 {s.Findings}、沒問題 {s.Pass}、判不出來 {s.Blocked}、不判 {s.NotJudged}{(s.Off ? "（關）" : "")}");

        foreach (var f in r.ParseErrors) o.WriteLine($"  !! 讀不懂 {Text.Safe(f)}{(r.ParseWhy.TryGetValue(f, out var why) ? "：" + Text.Safe(why) : "")}");
        if (r.Gaps.Excluded > 0) o.WriteLine($"  排除 {r.Gaps.Excluded} 個 XAML 檔");
        foreach (var d in r.Gaps.Unreadable) o.WriteLine($"  !! 資料夾讀不到 {Text.Safe(d)}");
        foreach (var f in r.InvalidIgnores) o.WriteLine($"  !! {Text.Safe(lang.Ignore(f))}");
        foreach (var g in r.Foreign.GroupBy(f => f.Dialect)) o.WriteLine($"  {g.Count()} 個 {g.Key} 檔沒有檢查（只看 WPF）");
        if (r.UnknownControls.Count > 0)
            o.WriteLine($"  不認得的控制項：{string.Join("、", r.UnknownControls.Take(8).Select(u => $"{Text.Safe(u.Element)} ×{u.Count}"))}（知道是什麼的話寫在 xamla11y.config.json：\"treatAs\": {{ \"{Text.Safe(r.UnknownControls[0].Element)}\": \"Button\" }}）");
        foreach (var (rule, reason, n) in r.On.SelectMany(s => s.BlockedReasons.Select(b => (s.Rule, b.Key, b.Value))).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).Take(6))
            o.WriteLine($"  判不出來 {n} 處，{rule}：{Trunc(Text.Safe(lang.Reason(reason)), 110)}");

        foreach (var kind in Kinds.All)
        {
            var n = r.Findings.Count(f => f.Kind == kind);
            if (n == 0) continue;
            var fresh = reported.Count(f => f.Kind == kind);
            o.WriteLine($"    {kind.Slug}（{lang.Title(kind)}）：{n}{(baselined > 0 ? $"（新的 {fresh}，baseline 裡 {n - fresh}）" : "")}");
        }

        PrintFindings(reported, lang, o);
        o.WriteLine();
        o.WriteLine($"  另外有 {ManualCheck.In(r.Scope).Count} 項要人檢查，--html 與 --md 有清單（最常出問題的，不是 WCAG 剩下的全部）。");
        o.WriteLine("  " + lang.Scope);
    }

    private const int ListAll = 40;
    private const int PerKind = 4;

    /// <summary>少的時候全部列；多的時候（第一次跑常見）每一種列幾筆，加上哪幾個檔案最多 —— 六十筆同一種的清單看不出該從哪裡開始。</summary>
    private static void PrintFindings(IReadOnlyList<Finding> reported, Lang lang, TextWriter o)
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
            var says = Nvda.Says(f, lang) is { } x ? lang.T($"   NVDA: \"{Text.Safe(x)}\"", $"　NVDA 會念「{Text.Safe(x)}」") : "";
            o.WriteLine(indent.Length > 2 ? $"{indent}{Text.Safe(f.Site)}{says}" : $"{indent}{Text.Slug(f.Severity),-4} {f.Kind.Slug,-18} {Text.Safe(f.Site)}{says}");
            o.WriteLine($"{indent}     {Text.Safe(lang.Message(f.Message))}");
        }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private static bool TryWrite(string path, string content, TextWriter stderr)
    {
        try
        {
            // 不替使用者建目錄：Git Bash 下的 /tmp/x 會在目前磁碟的根目錄建出目錄樹（XamlFit 踩坑 46）
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"cannot write {Text.Safe(path)}: {Text.Safe(ex.Message)}");
            return false;
        }
    }
}

internal static class Usage
{
    public static void Print(TextWriter o) => o.WriteLine("""
        xamla11y — static accessibility audit for WPF XAML

        usage:
          xamla11y audit <root> [options]
          xamla11y rules [--lang zh-Hant]    list the rules, their WCAG criteria and default severity
          xamla11y --version

        options:
          --json <file>            full report
          --sarif <file>           SARIF 2.1.0 for code scanning
          --md <file>              Markdown summary (for a PR comment)
          --html <file>            one self-contained page for people: why, how to fix, what NVDA says, manual checks
          --msbuild                also print MSBuild diagnostics (XA0001 gated, XA0002 not gated, XA0003 not trustworthy)
          --baseline <file>        only report findings that are not in this file
          --write-baseline <file>  record today's findings as known and exit
          --openacr <file>         draft accessibility conformance report (OpenACR YAML): only the criteria with fail findings, for a person to finish
          --standard <id>          wcag22 (default), wcag21, wcag20, en301549, en301549-v4, section508
          --level A|AA             AA (default) includes A
          --lang en|zh-Hant        language of the console, Markdown and HTML reports (default: en)
          --fail-on fail|warn      lowest severity that fails the run (default: fail)
          --min-coverage <0-100>   exit 3 when less than this share could be judged (default: 60)
          --config <file>          default: xamla11y.config.json in <root>
          --exclude <text>         skip files whose path contains this (repeatable)

        exit codes:
          0 pass   1 findings at or above --fail-on   2 usage, config or input error   3 result not trustworthy

        Only WPF XAML is checked. Suppress one element with a comment right before it (the reason is required):
          <!-- xamla11y-ignore: decorative, the row itself is the control -->
        """);

    public static int Rules(string[] args, TextWriter o, TextWriter stderr)
    {
        var lang = args switch { [] => Lang.En, ["--lang", var id] => Lang.ById(id), _ => null };
        if (lang is null) { stderr.WriteLine($"usage: xamla11y rules [--lang {Lang.Ids.Replace(", ", "|", StringComparison.Ordinal)}]"); return 2; }
        PrintRules(o, lang);
        return 0;
    }

    public static void PrintRules(TextWriter o, Lang lang)
    {
        if (lang.IsZh) o.WriteLine($"WCAG {Wcag.Version} 的成功準則，括號是等級。通過不等於符合規範：https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/%E6%A8%99%E6%BA%96%E5%B0%8D%E7%85%A7.md");
        else o.WriteLine($"WCAG {Wcag.Version} success criteria, level in brackets. Passing is not a claim of conformance: https://github.com/HSU-YU-MING/cornhsu-xamla11y/blob/main/docs/standards.md");
        o.WriteLine();
        foreach (var k in Kinds.All)
        {
            o.WriteLine($"{k.Slug,-18} {k.Rule}  {Text.Slug(k.Default),-4}  WCAG {k.WcagWithLevels}");
            o.WriteLine(lang.IsZh ? $"    {lang.Title(k)}。{lang.Help(k)}" : $"    {k.Title}. {k.Help}");
            o.WriteLine(lang.T("    Examples: ", "    寫法範例：") + k.Uri(lang.IsZh));
            o.WriteLine($"    {string.Join("; ", Core.Analysis.Standard.All.Where(s => s.HasClauses).Select(s => (s.Title, Clauses: s.ClausesOf(k.Criteria.Where(s.Has), k.NameIsEmpty))).Where(x => x.Clauses.Count > 0).Select(x => $"{x.Title}: {string.Join(", ", x.Clauses)}"))}");
        }
    }
}
