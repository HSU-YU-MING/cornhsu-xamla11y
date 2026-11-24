using System.Globalization;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Cli;

internal sealed class Options
{
    public string? Root, Json, Sarif, Md, Html, Baseline, WriteBaseline, Config, Standard, Level, Language, Title;
    public Severity FailOn = Severity.Fail;
    public double MinCoverage = 60;
    public bool MsBuild, PruneBaseline, Write, Guess;
    public readonly List<string> Exclude = [];

    /// <summary>只看這些檔案或資料夾（相對於現在的位置）。空的是全部。</summary>
    public readonly List<string> Only = [];
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

            if (!o.Given.Add(a) && a is not ("--exclude" or "--only")) return (o, $"{a} was given twice");
            string? value = null;
            if (a is "--json" or "--sarif" or "--md" or "--html" or "--standard" or "--level" or "--lang" or "--baseline" or "--write-baseline" or "--config" or "--fail-on" or "--min-coverage" or "--exclude" or "--only" or "--title")
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
                case "--only": o.Only.Add(value!); break;
                case "--title": o.Title = value; break;
                case "--prune-baseline": o.PruneBaseline = true; break;
                case "--write": o.Write = true; break;
                case "--guess": o.Guess = true; break;
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
