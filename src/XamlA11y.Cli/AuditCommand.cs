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
        if (opts.Write || opts.Guess) { stderr.WriteLine("--write and --guess belong to 'xamla11y fix'"); return 2; }
        if (Target.Resolve(opts) is { } wrong) { stderr.WriteLine(Text.Safe(wrong)); return 2; }
        if (opts.Baseline is not null && opts.WriteBaseline is not null) { stderr.WriteLine("--baseline and --write-baseline cannot be combined"); return 2; }
        if (opts.WriteBaseline is not null && (opts.Json ?? opts.Sarif ?? opts.Md ?? opts.Html) is not null || opts is { WriteBaseline: not null, MsBuild: true })
        { stderr.WriteLine("--write-baseline only writes the baseline; run again for the reports"); return 2; }
        if (opts.PruneBaseline && opts.Baseline is null) { stderr.WriteLine("--prune-baseline needs --baseline <file>"); return 2; }
        if (opts.Only.Count > 0 && (opts.WriteBaseline is not null || opts.PruneBaseline)) { stderr.WriteLine("--only looks at a part of the project; write or prune the baseline for all of it"); return 2; }
        if (opts.Baseline is not null && !File.Exists(opts.Baseline)) { stderr.WriteLine($"baseline not found: {Text.Safe(opts.Baseline)}"); return 2; }

        if (Setup.Load(opts, stderr) is not { } setup) return 2;
        var (_, scope, lang) = setup;

        var project = setup.LoadProject(opts);
        var auditOptions = setup.Audit;
        var result = Auditor.Run(project, auditOptions);
        var info = opts.Html is not null || opts.Json is not null ? ReportInfo.For(opts.Root, opts.Title) : null;

        if (opts.Only.Count > 0)
        {
            var (files, missing) = Target.Files(opts, project);
            if (missing is not null) { stderr.WriteLine(Text.Safe(missing)); return 2; }
            result = result.Only(files);
        }

        // ── baseline ──
        if (opts.WriteBaseline is not null)
        {
            // 什麼都沒看到的時候寫出一份空的 baseline，等於替「沒檢查」蓋章
            if (Trust.Why(result, double.Epsilon, lang) is { } empty) { stderr.WriteLine(empty); stderr.WriteLine("baseline not written"); return 3; }
            if (!TryWrite(opts.WriteBaseline, Baseline.From(result.Findings, DateOnly.FromDateTime(DateTime.Now)).ToJson(), stderr)) return 2;
            stdout.WriteLine(lang.T($"wrote baseline with {result.Findings.Count} known finding(s) → {Text.Safe(opts.WriteBaseline)}", $"寫好 baseline，記下 {result.Findings.Count} 筆已知的 → {Text.Safe(opts.WriteBaseline)}"));
            // 照著接下去跑會是 exit 3：現在就講
            if (Trust.Why(result, opts.MinCoverage, lang) is { } soon)
                stderr.WriteLine(lang.T($"note: a run with this baseline exits 3: {Text.Safe(soon)}. Declare the controls it does not know with treatAs, or lower --min-coverage.", $"注意：用這份 baseline 跑會是 exit 3：{Text.Safe(soon)}。用 treatAs 說明它不認得的控制項，或調低 --min-coverage。"));
            return 0;
        }

        IReadOnlyList<Finding> reported = result.Findings;
        var baselined = 0;
        var stale = 0;
        if (opts.Baseline is not null)
        {
            try
            {
                var baseline = Baseline.Parse(File.ReadAllText(opts.Baseline));
                (reported, baselined, stale) = baseline.Split(result.Findings);
                if (opts.Only.Count > 0) stale = 0;      // 別的檔案的沒有看
                if (opts.PruneBaseline && stale > 0)
                {
                    // 看不到不等於修好：不可信、一筆都對不上的時候不動它
                    if (Trust.Why(result, opts.MinCoverage, lang) is { } unsure) { stderr.WriteLine(lang.T("baseline not pruned: ", "baseline 沒有動：") + Text.Safe(unsure)); }
                    else if (baselined == 0) { stderr.WriteLine(lang.T("baseline not pruned: none of its entries match (written for another root folder?)", "baseline 沒有動：一筆都對不上（是不是在別的資料夾寫的？）")); }
                    else
                    {
                        var off = Kinds.All.Where(k => auditOptions.SeverityOf(k) == Severity.Off).Select(k => k.Slug).ToHashSet(StringComparer.Ordinal);
                        var read = project.Docs.Select(d => d.File).ToHashSet(StringComparer.Ordinal);
                        // 同一個檔案、同一種元素變成「不知道」「不判」的：說不準是不是它
                        var vague = result.Judgements.Where(j => j.Verdict is Verdict.Blocked or Verdict.NotJudged).Select(j => (j.Check, j.File, j.Element.Name.LocalName)).ToHashSet();
                        bool Gone(string file) => read.Contains(file) || !File.Exists(Path.Combine(opts.Root!, file));
                        var pruned = baseline.Prune(result.Findings, DateOnly.FromDateTime(DateTime.Now), (kind, file, element) =>
                            off.Contains(kind) || file.Length > 0 && file != "(class)" && !Gone(file)
                            || Kinds.Checks.Any(c => Kinds.CountedIn(c.Id).Any(k => k.Slug == kind) && vague.Contains((c.Id, file, element))));
                        if (!TryWrite(opts.Baseline, pruned.ToJson(), stderr)) return 2;
                        stdout.WriteLine(lang.T($"pruned {baseline.Known.Count - pruned.Known.Count} entries from {Text.Safe(opts.Baseline)}", $"從 {Text.Safe(opts.Baseline)} 拿掉 {baseline.Known.Count - pruned.Known.Count} 筆"));
                        stale -= baseline.Known.Count - pruned.Known.Count;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                stderr.WriteLine($"cannot read baseline: {Text.Safe(ex.Message)}");
                return 2;
            }
        }

        // ── 結論 ──
        // ⚠ 「結果不可信」排在「有沒有 finding」前面：只看到三成就說「沒問題」是謊報健康
        var why = Trust.Why(result, opts.MinCoverage, lang);
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

        if (opts.Json is not null && !TryWrite(opts.Json, JsonReport.Create(result, reported, baselined, Program.Version(), exit, info), stderr)) return 2;
        if (opts.Sarif is not null && !TryWrite(opts.Sarif, SarifReport.Create(reported, Program.Version(), scope), stderr)) return 2;
        if (opts.Md is not null && !TryWrite(opts.Md, MarkdownReport.Create(result, reported, exit, baselined, why, lang, Known(result, reported)), stderr)) return 2;
        if (opts.Html is not null && !TryWrite(opts.Html, HtmlReport.Create(result, reported, exit, baselined, why, Program.Version(), lang, stale, Known(result, reported), info), stderr)) return 2;
        foreach (var path in new[] { opts.Json, opts.Sarif, opts.Md, opts.Html })
            if (path is not null) stdout.WriteLine(lang.T("wrote ", "寫好 ") + Text.Safe(path));
        if (opts.MsBuild) stdout.Write(MsBuildReport.Create(reported, opts.Root, opts.FailOn, exit));
        return exit;
    }

    /// <summary>baseline 蓋住的：還沒修的問題。</summary>
    private static IReadOnlyList<Finding> Known(AuditResult r, IReadOnlyList<Finding> reported) => [.. r.Findings.Except(reported)];

    internal static bool TryWrite(string path, string content, TextWriter stderr)
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
