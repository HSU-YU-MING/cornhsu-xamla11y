using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;
using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

/// <summary>設定檔、標準、語言：每個指令都一樣。</summary>
internal sealed record Setup(A11yConfig Config, Scope Scope, Lang Lang)
{
    public AuditOptions Audit => new() { Severities = Config.Severities, Scope = Scope, ThemeShowsFocus = Config.ThemeShowsFocus ?? false };

    /// <summary>旗標 &gt; 設定檔 &gt; 預設。設定檔有問題就印出來、回 null。</summary>
    public static Setup? Load(Options opts, TextWriter stderr)
    {
        var config = new A11yConfig();
        var path = opts.Config ?? Path.Combine(opts.Root!, A11yConfig.DefaultFileName);
        if (opts.Config is not null && !File.Exists(opts.Config)) { stderr.WriteLine($"config file not found: {Text.Safe(opts.Config)}"); return null; }
        if (File.Exists(path))
        {
            var (loaded, problems) = A11yConfig.Load(path);
            if (problems.Count > 0)
            {
                stderr.WriteLine($"{Text.Safe(path)} has {problems.Count} problem(s):");
                foreach (var p in problems) stderr.WriteLine("  " + Text.Safe(p.ToString()));
                return null;
            }
            config = loaded!;
            stderr.WriteLine($"config: {Text.Safe(path)}");
        }
        if (!opts.Given.Contains("--min-coverage") && config.MinCoverage is { } mc) opts.MinCoverage = mc;
        return new Setup(config,
            new Scope(Standard.ById(opts.Standard ?? config.Standard ?? "") ?? Scope.Default.Standard, opts.Level ?? config.Level ?? Scope.Default.Level),
            Lang.ById(opts.Language ?? config.Language ?? "") ?? Lang.En);
    }

    public Project LoadProject(Options opts, IReadOnlyDictionary<string, string>? replaced = null) =>
        Project.Load(opts.Root!, [.. opts.Exclude, .. Config.Exclude ?? []], Config.TreatAs, replaced);
}
