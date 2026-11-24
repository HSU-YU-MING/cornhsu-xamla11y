using System.Globalization;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;
using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

internal static class AuditCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var (opts, error) = Options.Parse(args);
        if (error is not null) { stderr.WriteLine(Text.Safe(error)); return 2; }
        if (opts.Root is null) { stderr.WriteLine("audit needs a root directory"); return 2; }
        if (!Directory.Exists(opts.Root)) { stderr.WriteLine($"not a directory: {Text.Safe(opts.Root)}"); return 2; }
        if (opts.Baseline is not null && opts.WriteBaseline is not null) { stderr.WriteLine("--baseline and --write-baseline cannot be combined"); return 2; }
        if (opts.WriteBaseline is not null && (opts.Json ?? opts.Sarif ?? opts.Md ?? opts.Html) is not null || opts is { WriteBaseline: not null, MsBuild: true })
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

        ConsoleReport.Print(stdout, result, reported, stale, lang);
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
        foreach (var path in new[] { opts.Json, opts.Sarif, opts.Md, opts.Html })
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
