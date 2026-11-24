using XamlA11y.Core.Fix;
using XamlA11y.Core.Report;
using XamlA11y.Core.Xaml;
using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

/// <summary>xamla11y fix：把修法固定的接上去。預設只說會改什麼，--write 才寫。</summary>
internal static class FixCommand
{
    private static readonly string[] Allowed = ["--write", "--guess", "--only", "--exclude", "--config", "--lang", "--standard", "--level"];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var (opts, error) = Options.Parse(args);
        if (error is not null) { stderr.WriteLine(Text.Safe(error)); return 2; }
        if (opts.Given.FirstOrDefault(g => !Allowed.Contains(g)) is { } other) { stderr.WriteLine($"{other} belongs to 'xamla11y audit'"); return 2; }
        if (opts.Root is null) { stderr.WriteLine("fix needs a folder, project or XAML file"); return 2; }
        if (Target.Resolve(opts) is { } wrong) { stderr.WriteLine(Text.Safe(wrong)); return 2; }
        if (Setup.Load(opts, stderr) is not { } setup) return 2;
        var lang = setup.Lang;

        IReadOnlySet<string>? only = null;
        if (opts.Only.Count > 0)
        {
            var (files, missing) = Target.Files(opts, setup.LoadProject(opts));
            if (missing is not null) { stderr.WriteLine(Text.Safe(missing)); return 2; }
            only = files;
        }

        string PathOf(string file) => Path.Combine(opts.Root!, file);
        FixPlan plan;
        try { plan = Fixer.Plan(replaced => setup.LoadProject(opts, replaced), f => File.ReadAllBytes(PathOf(f)), setup.Audit, opts.Guess, only); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { stderr.WriteLine(Text.Safe(ex.Message)); return 2; }

        stdout.WriteLine(lang.T($"xamla11y fix — {plan.Edits.Count} change(s) in {plan.Files.Count} file(s)", $"xamla11y fix：{plan.Files.Count} 個檔案，{plan.Edits.Count} 處可以改"));
        foreach (var e in plan.Edits)
            stdout.WriteLine($"  {Text.Safe(e.File)}:{e.Line} <{Text.Safe(e.Element)}> {e.Attribute}=\"{Text.Safe(e.Value)}\"  ({Because(e, lang)})");
        if (plan.Edits.Count(e => e.AtRunTime) is > 0 and var later) stdout.WriteLine(lang.T($"  {later} of them get a name that is only known at run time (bound): check them in the app", $"  其中 {later} 處接上之後名稱是什麼要執行才知道（綁定的）：開起來看"));
        foreach (var f in plan.Untouched) stdout.WriteLine(lang.T("  not touched: ", "  沒有動：") + Text.Safe(f) + lang.T(" (the file would not come back the same, or the change did not help)", "（寫回去會不一樣，或改了沒有用）"));
        stdout.WriteLine(lang.T($"  left for a person: {plan.Left} finding(s)", $"  剩下要人改的：{plan.Left} 筆"));

        if (!opts.Write)
        {
            if (plan.Edits.Count > 0) stdout.WriteLine(lang.T("  nothing was written; add --write to change the files (commit first, then read the diff)", "  還沒寫進去；加 --write 才會改檔案（先 commit，改完看 diff）"));
            return 0;
        }
        foreach (var (file, bytes) in plan.Files)
            try { File.WriteAllBytes(PathOf(file), bytes); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { stderr.WriteLine($"cannot write {Text.Safe(file)}: {Text.Safe(ex.Message)}"); return 2; }
        stdout.WriteLine(lang.T($"  wrote {plan.Files.Count} file(s)", $"  寫好 {plan.Files.Count} 個檔案"));
        return 0;
    }

    private static string Because(FixEdit e, Lang lang) => (e.Guess ? lang.T("GUESS: ", "猜的：") : "") + e.Source switch
    {
        FixSource.Label => lang.T($"the Label at line {e.From} points at it", $"第 {e.From} 行的 Label 指到它"),
        FixSource.ShownText => lang.T("the text it shows", "畫面上的字"),
        FixSource.ToolTip => lang.T("its ToolTip", "它的 ToolTip"),
        _ => lang.T($"the text before it, line {e.From}", $"前面第 {e.From} 行的字"),
    };
}
