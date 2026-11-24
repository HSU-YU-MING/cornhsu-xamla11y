using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>指令列：退出碼、旗標、說明、主控台。</summary>
public sealed class CliTests
{
    [Fact]
    public void Help_works_after_the_command()
    {
        using var p = new Folder();
        Assert.Equal(0, p.Run("audit", "--help").Exit);
    }

    /// <summary>主控台：多的時候依種類分組、列檔案；忘了打 audit 會提示。</summary>
    [Fact]
    public void Many_findings_are_grouped()
    {
        using var f = new Folder().Add("A.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<TextBox/>", 30)))).Add("B.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<TextBox/>", 20))));
        var o = f.Run("audit", f.Root).Out;
        Assert.Contains("fail input-no-name ×50", o, StringComparison.Ordinal);
        Assert.Contains("… and 46 more", o, StringComparison.Ordinal);
        Assert.Contains("most in: A.xaml ×30, B.xaml ×20", o, StringComparison.Ordinal);
        var forgot = f.Run(f.Root);
        Assert.Equal(2, forgot.Exit);
        Assert.Contains("did you mean 'xamla11y audit <folder>'?", forgot.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("did you mean", f.Run("audti").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_in_chinese()
    {
        using var f = new Folder();
        Assert.Contains("控制項沒有名稱", f.Run("rules", "--lang", "zh-Hant").Out, StringComparison.Ordinal);
        Assert.Contains("Control has no accessible name", f.Run("rules").Out, StringComparison.Ordinal);
        Assert.Equal(2, f.Run("rules", "--lang", "zh").Exit);
        Assert.Equal(2, f.Run("rules", "extra").Exit);
    }

    [Theory]
    [InlineData("--standard", "wcag30", "--standard takes one of: wcag22, wcag21, wcag20, en301549, en301549-v4, section508")]
    [InlineData("--level", "AAA", "--level takes 'A' or 'AA'")]
    [InlineData("--level", "a", "--level takes 'A' or 'AA'")]
    [InlineData("--lang", "zh", "--lang takes one of: en, zh-Hant")]
    [InlineData("--lang", "ZH-HANT", "--lang takes one of: en, zh-Hant")]
    public void Wrong_values_are_refused(string flag, string value, string message)
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<Button/>"));
        var r = f.Run("audit", f.Root, flag, value);
        Assert.Equal(2, r.Exit);
        Assert.Contains(message, r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_codes()
    {
        using var good = Kit.Good();
        using var bad = Kit.Bad();
        using var empty = new Folder();
        using var foreign = new Folder().Add("Main.xaml", "<Window xmlns=\"https://github.com/avaloniaui\"/>");
        using var broken = new Folder().Add("Main.xaml", "<Window");
        using var nothing = new Folder().Add("Main.xaml", Kit.Window("<TextBlock Text=\"x\"/>"));
        using var unknown = new Folder().Add("Main.xaml", Kit.Window("<local:FancyButton/><local:FancyButton/><Button Content=\"x\"/>"));
        Assert.Equal(0, good.Run("audit", good.Root).Exit);
        Assert.Equal(1, bad.Run("audit", bad.Root).Exit);
        foreach (var f in new[] { empty, foreign, broken, nothing, unknown }) Assert.Equal(3, f.Run("audit", f.Root).Exit);
        Assert.Equal(0, unknown.Run("audit", unknown.Root, "--min-coverage", "30").Exit);
    }

    [Theory]
    [InlineData()]
    [InlineData("nope")]
    [InlineData("audit")]
    [InlineData("audit", "/definitely/not/here")]
    [InlineData("audit", "ROOT", "--nope")]
    [InlineData("audit", "ROOT", "extra")]
    [InlineData("audit", "ROOT", "--fail-on", "info")]
    [InlineData("audit", "ROOT", "--min-coverage", "NaN")]
    [InlineData("audit", "ROOT", "--min-coverage", "101")]
    [InlineData("audit", "ROOT", "--json")]
    [InlineData("audit", "ROOT", "--json", "--md")]
    [InlineData("audit", "ROOT", "--json", "a", "--json", "b")]
    [InlineData("audit", "ROOT", "--baseline", "/not/here.json")]
    [InlineData("audit", "ROOT", "--config", "/not/here.json")]
    [InlineData("audit", "ROOT", "--json", "/no/such/dir/x/y.json")]
    public void Usage_errors_exit_2(params string[] args)
    {
        using var p = Kit.Good();
        Assert.Equal(2, p.Run([.. args.Select(a => a == "ROOT" ? p.Root : a)]).Exit);
    }

    [Fact]
    public void A_folder_named_like_a_flag_can_be_audited()
    {
        using var p = new Folder().Add("--version/Main.xaml", Kit.Window("<Button Content=\"Save\"/>"));
        Assert.Equal(0, p.Run("audit", "--", p.At("--version")).Exit);
    }

    [Fact]
    public void Fail_on_warn_gates_warnings()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\" IsTabStop=\"False\"/>"));
        Assert.Equal(0, p.Run("audit", p.Root).Exit);
        Assert.Equal(1, p.Run("audit", p.Root, "--fail-on", "warn").Exit);
    }

    [Fact]
    public void A_long_first_run_points_at_the_baseline()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window(string.Concat(Enumerable.Repeat("<TextBox/>", 12))));
        Assert.Contains("--write-baseline", p.Run("audit", p.Root).Err);
        using var few = Kit.Bad();
        Assert.DoesNotContain("--write-baseline", few.Run("audit", few.Root).Err);
    }

    [Fact]
    public void Output_is_the_same_whatever_the_culture()
    {
        using var p = Kit.Bad();
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var invariant = p.Run("audit", p.Root).Out;
            try { System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE"); }
            catch (System.Globalization.CultureNotFoundException) { return; }
            Assert.Equal(invariant, p.Run("audit", p.Root).Out);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Help_lists_every_exit_code_and_rules_lists_every_kind()
    {
        using var p = new Folder();
        var help = p.Run("--help");
        Assert.Equal(0, help.Exit);
        Assert.Contains("exit codes", help.Out);
        var rules = p.Run("rules").Out;
        Assert.All(Kinds.All, k => Assert.Contains(k.Slug, rules));
    }

    /// <summary>主控台先總結：幾筆、分哪幾組、看得到多少，然後才是清單，最後是看不到的。</summary>
    [Theory]
    [InlineData("en", "    not read out: 10 (missing-name 1, ", "    out of reach of the keyboard: 2 (mouse-only 1, not-in-tab-order 1)", "  resolution ", "\n  fail ", "  unknown: 1", "    1 × names of controls: a control this tool does not know")]
    [InlineData("zh-Hant", "    念不出來：10（missing-name 1、", "    鍵盤到不了：2（mouse-only 1、not-in-tab-order 1）", "  可解析率 ", "\n  fail ", "  判不出來：1", "    1 × 控制項的名稱：這支不認得的控制項")]
    public void The_summary_comes_first(string lang, params string[] parts)
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Kit.Everything + "<local:FancyButton/><ListBox ItemsSource=\"{Binding N}\" AutomationProperties.Name=\"n\"/>", Kit.EverythingTemplate)).Add("N.cs", "class Note { } class V { public List<Note> N { get; } }");
        var output = f.Run("audit", f.Root, "--lang", lang).Out.Replace("\r\n", "\n", StringComparison.Ordinal);
        var at = -1;
        foreach (var p in parts)
        {
            var next = output.IndexOf(p, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"{p}\n\n{output}");
            at = next;
        }
    }

    [Fact]
    public void What_is_turned_off_is_said()
    {
        using var f = Kit.Bad().Add("xamla11y.config.json", "{ \"rules\": { \"mouse-only\": \"off\", \"small-target\": \"off\" } }");
        Assert.Contains("  turned off: mouse-only elements, target size", f.Run("audit", f.Root).Out, StringComparison.Ordinal);
        Assert.Contains("  關掉的：只能用滑鼠、點擊範圍", f.Run("audit", f.Root, "--lang", "zh-Hant").Out, StringComparison.Ordinal);
        Assert.DoesNotContain("turned off", f.Run("audit", f.Root, "--config", f.Add("empty.json", "{}").At("empty.json")).Out, StringComparison.Ordinal);
    }

    /// <summary>沒有的不列：乾淨的專案只有總結。</summary>
    [Fact]
    public void A_clean_run_is_short()
    {
        using var f = Kit.Good();
        var output = f.Run("audit", f.Root).Out;
        Assert.Contains("  0 fail, 0 warn, 0 info", output, StringComparison.Ordinal);
        foreach (var word in (string[])["not read out", "unknown:", "baseline", "suppressed", "excluded", "!!"]) Assert.DoesNotContain(word, output, StringComparison.Ordinal);
        Assert.True(output.Trim().Split('\n').Length <= 6, output);
    }

    /// <summary>原因很多的時候列五個、說還有幾個。</summary>
    [Fact]
    public void Many_reasons_are_counted()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(string.Concat(Enumerable.Range(0, 7).Select(i => $"<Button Content=\"{{StaticResource k{i}}}\"/>"))));
        var output = f.Run("audit", f.Root, "--min-coverage", "0").Out;
        Assert.Contains("  unknown: 7", output, StringComparison.Ordinal);
        Assert.Equal(5, output.Split("× names of controls:").Length - 1);
        Assert.Contains("    … and 2 more reasons", output, StringComparison.Ordinal);
    }
}
