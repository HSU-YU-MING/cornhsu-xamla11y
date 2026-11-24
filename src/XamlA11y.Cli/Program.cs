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
            case "rules": Usage.PrintRules(stdout); return 0;
            default:
                stderr.WriteLine($"unknown command: {Text.Safe(args[0])}");
                stderr.WriteLine("run 'xamla11y --help' for usage");
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
    public string? Root, Json, Sarif, Md, Baseline, WriteBaseline, Config;
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
            if (a is "--json" or "--sarif" or "--md" or "--baseline" or "--write-baseline" or "--config" or "--fail-on" or "--min-coverage" or "--exclude")
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || args[i + 1].Length == 0) return (o, $"{a} needs a value");
                value = args[++i];
            }

            switch (a)
            {
                case "--json": o.Json = value; break;
                case "--sarif": o.Sarif = value; break;
                case "--md": o.Md = value; break;
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
        if (opts.WriteBaseline is not null && (opts.Json ?? opts.Sarif ?? opts.Md) is not null || opts is { WriteBaseline: not null, MsBuild: true })
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

        var project = Project.Load(opts.Root, [.. opts.Exclude, .. config.Exclude ?? []], config.TreatAs);
        var result = Auditor.Run(project, new AuditOptions { Severities = config.Severities, ThemeShowsFocus = config.ThemeShowsFocus ?? false });

        // ── baseline ──
        if (opts.WriteBaseline is not null)
        {
            // 什麼都沒看到的時候寫出一份空的 baseline，等於替「沒檢查」蓋章
            if (Untrustworthy(result, double.Epsilon) is { } empty) { stderr.WriteLine(empty); stderr.WriteLine("baseline not written"); return 3; }
            if (!TryWrite(opts.WriteBaseline, Baseline.From(result.Findings, DateOnly.FromDateTime(DateTime.Now)).ToJson(), stderr)) return 2;
            stdout.WriteLine($"wrote baseline with {result.Findings.Count} known finding(s) → {Text.Safe(opts.WriteBaseline)}");
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

        Print(result, reported, baselined, stale, opts, stdout);
        if (why is not null) stderr.WriteLine("\n" + Text.Safe(why));
        else if (blocking > 0)
        {
            stderr.WriteLine($"\n{blocking} finding(s) at or above '{Text.Slug(opts.FailOn)}'");
            // 第一次跑常是幾十筆：一上來就紅的閘門會被關掉
            if (opts.Baseline is null && blocking >= 10)
                stderr.WriteLine("first run on this project? record these as known with --write-baseline <file>, then run with --baseline <file> to fail only on new ones");
        }

        if (opts.Json is not null && !TryWrite(opts.Json, JsonReport.Create(result, reported, baselined, Program.Version(), exit), stderr)) return 2;
        if (opts.Sarif is not null && !TryWrite(opts.Sarif, SarifReport.Create(reported, Program.Version()), stderr)) return 2;
        if (opts.Md is not null && !TryWrite(opts.Md, MarkdownReport.Create(result, reported, exit, baselined, why), stderr)) return 2;
        foreach (var path in new[] { opts.Json, opts.Sarif, opts.Md })
            if (path is not null) stdout.WriteLine($"wrote {Text.Safe(path)}");
        if (opts.MsBuild) stdout.Write(MsBuildReport.Create(reported, opts.Root, opts.FailOn, exit));
        return exit;
    }

    private static string? Untrustworthy(AuditResult r, double minCoverage)
    {
        if (r.Files == 0) return "no .xaml files found — result is not trustworthy";
        if (r.Foreign.Count > 0 && r.Foreign.Count + r.ParseErrors.Count == r.Files) return $"none of the {r.Files} XAML file(s) is WPF — result is not trustworthy";
        if (r.ParseErrors.Count == r.Files) return "no XAML file could be parsed — result is not trustworthy";
        var wpf = r.Files - r.Foreign.Count;
        if (minCoverage > 0 && 100.0 * (wpf - r.ParseErrors.Count) / wpf < minCoverage)
            return string.Create(CultureInfo.InvariantCulture, $"{r.ParseErrors.Count} of {wpf} XAML file(s) could not be parsed, above what --min-coverage {minCoverage:0.#}% allows — result is not trustworthy");
        if (r.Candidates == 0) return "no interactive elements found — result is not trustworthy";
        // 全部卡住才是「沒看到」；全部都是執行期的資料（名稱綁定）不是專案改得了的，也不是工具漏看
        if (minCoverage > 0 && r.Judged == 0 && r.Blocked > 0) return "not one element could be judged — result is not trustworthy";
        if (r.Resolution < minCoverage)
            return string.Create(CultureInfo.InvariantCulture, $"resolution {r.Resolution:F1}% is below --min-coverage {minCoverage:0.#}% — result is not trustworthy");
        return null;
    }

    private static void Print(AuditResult r, IReadOnlyList<Finding> reported, int baselined, int stale, Options opts, TextWriter o)
    {
        var inv = CultureInfo.InvariantCulture;
        o.WriteLine($"xamla11y audit — {r.Files} XAML file(s), {r.Candidates} check(s)");
        o.WriteLine(string.Create(inv, $"  resolution       {r.Resolution:F1}%  ({r.Judged} judged, {r.Blocked} blocked; {r.NotJudged} not judged)"));
        if (r.Suppressed > 0) o.WriteLine($"  suppressed       {r.Suppressed}");
        if (baselined > 0) o.WriteLine($"  in baseline      {baselined}");
        if (stale > 0) o.WriteLine($"  stale baseline   {stale}  (fixed since; write the baseline again to drop them)");
        foreach (var s in r.Rules.Where(s => s.Candidates > 0))
            o.WriteLine(string.Create(inv, $"    {s.Rule} {Kinds.RuleTitle(s.Rule),-20} {s.Findings,4} finding(s), {s.Pass,4} ok, {s.Blocked,4} blocked, {s.NotJudged,4} not judged"));

        foreach (var f in r.ParseErrors) o.WriteLine($"  !! could not parse {Text.Safe(f)}{(r.ParseWhy.TryGetValue(f, out var why) ? ": " + Text.Safe(why) : "")}");
        if (r.Gaps.Excluded > 0) o.WriteLine($"  {r.Gaps.Excluded} XAML file(s) excluded");
        foreach (var d in r.Gaps.Unreadable) o.WriteLine($"  !! could not read the folder {Text.Safe(d)}");
        foreach (var f in r.InvalidIgnores) o.WriteLine($"  !! {Text.Safe(f)}");
        foreach (var g in r.Foreign.GroupBy(f => f.Dialect)) o.WriteLine($"  {g.Count()} {g.Key} file(s) not checked (WPF only)");
        if (r.UnknownControls.Count > 0)
            o.WriteLine($"  unknown controls {string.Join(", ", r.UnknownControls.Take(8).Select(u => $"{Text.Safe(u.Element)} ×{u.Count}"))}  (declare them in treatAs)");
        var blocked = r.Rules.SelectMany(s => s.BlockedReasons.Select(b => (s.Rule, b.Key, b.Value))).OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal).Take(6).ToList();
        foreach (var (rule, reason, n) in blocked) o.WriteLine($"  blocked {n,4}  {rule}: {Trunc(Text.Safe(reason), 110)}");

        foreach (var kind in Kinds.All)
        {
            var n = r.Findings.Count(f => f.Kind == kind);
            if (n == 0) continue;
            var fresh = reported.Count(f => f.Kind == kind);
            o.WriteLine(baselined > 0 ? $"    {kind.Slug,-18} {n}  ({fresh} new, {n - fresh} already in the baseline)" : $"    {kind.Slug,-18} {n}");
        }

        if (reported.Count > 0)
        {
            o.WriteLine();
            foreach (var f in reported.OrderByDescending(f => f.Severity).Take(60))
            {
                o.WriteLine($"  {Text.Slug(f.Severity),-4} {f.Kind.Slug,-18} {Text.Safe(f.Site)}");
                o.WriteLine($"       {Text.Safe(f.Message)}");
            }
            if (reported.Count > 60) o.WriteLine($"  … and {reported.Count - 60} more (--json has all of them)");
        }
        o.WriteLine();
        o.WriteLine("  " + Summary.Scope);
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
          xamla11y rules                     list the rules, their WCAG criteria and default severity
          xamla11y --version

        options:
          --json <file>            full report
          --sarif <file>           SARIF 2.1.0 for code scanning
          --md <file>              Markdown summary (for a PR comment)
          --msbuild                also print MSBuild diagnostics (XA0001 gated, XA0002 not gated, XA0003 not trustworthy)
          --baseline <file>        only report findings that are not in this file
          --write-baseline <file>  record today's findings as known and exit
          --fail-on fail|warn      lowest severity that fails the run (default: fail)
          --min-coverage <0-100>   exit 3 when less than this share could be judged (default: 60)
          --config <file>          default: xamla11y.config.json in <root>
          --exclude <text>         skip files whose path contains this (repeatable)

        exit codes:
          0 pass   1 findings at or above --fail-on   2 usage, config or input error   3 result not trustworthy

        Only WPF XAML is checked. Suppress one element with a comment right before it (the reason is required):
          <!-- xamla11y-ignore: decorative, the row itself is the control -->
        """);

    public static void PrintRules(TextWriter o)
    {
        foreach (var k in Kinds.All)
        {
            o.WriteLine($"{k.Slug,-18} {k.Rule}  {Text.Slug(k.Default),-4}  WCAG {k.Wcag}");
            o.WriteLine($"    {k.Title}. {k.Help}");
        }
    }
}
