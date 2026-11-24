using XamlA11y.Core.Report;
using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

/// <summary>xamla11y walk：一個畫面按 Tab 會依序停在哪裡、念什麼。</summary>
internal static class WalkCommand
{
    private static readonly string[] Allowed = ["--exclude", "--config", "--lang", "--standard", "--level"];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var (opts, error) = Options.Parse(args);
        if (error is not null) { stderr.WriteLine(Text.Safe(error)); return 2; }
        if (opts.Given.FirstOrDefault(g => !Allowed.Contains(g)) is { } other) { stderr.WriteLine($"{other} does not belong to 'xamla11y walk'"); return 2; }
        if (opts.Root is null) { stderr.WriteLine("walk needs a XAML file"); return 2; }
        if (Target.Resolve(opts) is { } wrong) { stderr.WriteLine(Text.Safe(wrong)); return 2; }
        if (opts.Only.Count == 0) { stderr.WriteLine("walk needs one XAML file (a window, page or user control), not a folder"); return 2; }
        if (Setup.Load(opts, stderr) is not { } setup) return 2;

        var project = setup.LoadProject(opts);
        var (files, missing) = Target.Files(opts, project);
        if (missing is not null) { stderr.WriteLine(Text.Safe(missing)); return 2; }
        if (project.Docs.FirstOrDefault(d => files.Contains(d.File)) is not { } doc)
        {
            var file = files.First();
            stderr.WriteLine(project.ParseErrors.Contains(file) ? $"could not parse {Text.Safe(file)}: {Text.Safe(project.ParseWhy.GetValueOrDefault(file, ""))}" : $"not WPF: {Text.Safe(file)}");
            return 2;
        }
        WalkReport.Print(stdout, project, doc, setup.Audit, setup.Lang);
        return 0;
    }
}
