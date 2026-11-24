using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

internal static class Usage
{
    public static void Print(TextWriter o) => o.WriteLine("""
        xamla11y — static accessibility audit for WPF XAML

        usage:
          xamla11y audit <folder | .csproj | .sln | .xaml> [options]
          xamla11y fix <folder | .csproj | .sln | .xaml> [--write] [--guess]
                                             name what already has a label, a text or a ToolTip; shows the changes, --write makes them
          xamla11y walk <file.xaml>          where Tab stops in this file, in order, and what a screen reader says there
          xamla11y rules [--lang zh-Hant]    what it looks for, why, and how to fix it
          xamla11y --version

        what to check:
          --standard <id>          wcag22 (default), wcag21, wcag20, en301549, en301549-v4, section508
          --level A|AA             AA (default) includes A
          --only <file|folder>     only report these; the rest is still read for styles (repeatable)
          --exclude <text>         skip files whose path contains this (repeatable)
          --config <file>          default: xamla11y.config.json in <root>

        when to fail:
          --baseline <file>        only report findings that are not in this file
          --write-baseline <file>  record today's findings as known and exit
          --prune-baseline         drop what is no longer found from --baseline; accepts nothing new
          --fail-on fail|warn      lowest severity that fails the run (default: fail)
          --min-coverage <0-100>   exit 3 when less than this share could be judged (default: 60)

        reports:
          --lang en|zh-Hant        language of the console, Markdown and HTML (default: en)
          --title <text>           heading of the HTML report (default: the name of the folder)
          --html <file>            one page for people: why, how to fix, what NVDA says, manual checks
          --md <file>              Markdown summary (for a PR comment)
          --json <file>            everything
          --sarif <file>           SARIF 2.1.0 for code scanning
          --msbuild                also print MSBuild diagnostics (XA0001 gated, XA0002 not gated, XA0003 not trustworthy)

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
        foreach (var group in Kinds.Groups)
        {
            o.WriteLine();
            o.WriteLine($"── {lang.GroupTitle(group)} ──");
            foreach (var k in Kinds.All.Where(k => k.Group == group.Id)) One(k);
        }

        void One(Kind k)
        {
            o.WriteLine($"{k.Slug,-18} {Text.Slug(k.Default),-4}  WCAG {k.WcagWithLevels}");
            o.WriteLine($"    {lang.Title(k)}{lang.T(". ", "。")}{lang.Help(k)}");
            o.WriteLine(lang.T("    Examples: ", "    寫法範例：") + k.Uri(lang.IsZh));
            o.WriteLine($"    {string.Join("; ", Core.Analysis.Standard.All.Where(s => s.HasClauses).Select(s => (s.Title, Clauses: s.ClausesOf(k.Criteria.Where(s.Has), k.NameIsEmpty))).Where(x => x.Clauses.Count > 0).Select(x => $"{x.Title}: {string.Join(", ", x.Clauses)}"))}");
        }
    }
}
