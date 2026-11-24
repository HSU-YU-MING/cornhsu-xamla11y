using XamlA11y.Core.Report;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>baseline：鍵、已知與新的、壞掉的檔案。</summary>
public sealed class BaselineTests
{
    [Theory]
    [InlineData("{\"keyVersion\":1,\"known\":null}")]
    [InlineData("{\"keyVersion\":1,\"known\":[null]}")]
    [InlineData("{\"keyVersion\":-5,\"known\":[]}")]
    [InlineData("{\"Known\":[]}")]
    public void A_malformed_baseline_exits_2(string json)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox/>")).Add("b.json", json);
        Assert.Equal(2, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void A_finding_that_gets_worse_is_new()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button Content=\"ok\"/><Button x:Name=\"q\" Focusable=\"False\">{Kit.Icon}</Button>"));
        p.Run("audit", p.Root, "--write-baseline", p.At("b.json"));
        p.Add("Main.xaml", Kit.Window($"<Button Content=\"ok\"/><Button x:Name=\"q\">{Kit.Icon}</Button>"));
        Assert.Equal(1, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void A_key_is_never_cut_inside_a_surrogate_pair()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<TextBox ToolTip=\"{new string('a', 71)}😀bbb\"/><TextBox ToolTip=\"{new string('a', 72)}😀bbb\"/><TextBox ToolTip=\"{new string('a', 79)}😀bbb\"/>"));
        p.Run("audit", p.Root, "--write-baseline", p.At("b.json"));
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void Keys_cannot_collide_through_the_separator()
    {
        var a = new Finding(Kinds.MissingName, Severity.Fail, "f", 1, "Button", "") { Identity = "a|#2" };
        var b = new Finding(Kinds.MissingName, Severity.Fail, "f", 1, "Button", "") { Identity = "a", Ordinal = 2 };
        Assert.NotEqual(a.BaselineKey, b.BaselineKey);
    }

    [Fact]
    public void Write_baseline_does_not_silently_drop_other_outputs()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal(2, p.Run("audit", p.Root, "--write-baseline", p.At("b.json"), "--json", p.At("r.json")).Exit);
        Assert.False(File.Exists(p.At("b.json")));
    }

    [Fact]
    public void A_baseline_is_not_written_for_a_project_nothing_could_be_judged_in()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<local:WhatButton/>"));
        Assert.Equal(3, p.Run("audit", p.Root, "--write-baseline", p.At("b.json")).Exit);
        Assert.False(File.Exists(p.At("b.json")));
    }

    [Fact]
    public void A_newer_baseline_asks_to_upgrade_even_with_new_fields()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button/>")).Add("b.json", "{\"keyVersion\":2,\"somethingNew\":1,\"known\":[]}");
        var (exit, _, err) = p.Run("audit", p.Root, "--baseline", p.At("b.json"));
        Assert.Equal(2, exit);
        Assert.Contains("upgrade xamla11y", err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("x:Name=\"a\"", "a")]
    [InlineData("AutomationProperties.AutomationId=\"a\"", "AutomationProperties.AutomationId=a")]
    [InlineData("Command=\"{Binding A}\"", "Command={Binding A}")]
    [InlineData("Click=\"A\"", "Click=A")]
    [InlineData("MouseLeftButtonDown=\"A\"", "MouseLeftButtonDown=A")]
    [InlineData("MouseLeftButtonUp=\"A\"", "MouseLeftButtonUp=A")]
    [InlineData("MouseDown=\"A\"", "MouseDown=A")]
    [InlineData("MouseUp=\"A\"", "MouseUp=A")]
    [InlineData("ToolTip=\"A\"", "ToolTip=A")]
    [InlineData("IsChecked=\"{Binding A}\"", "IsChecked={Binding A}")]
    public void The_baseline_recognises_an_element_by(string attribute, string identity) =>
        Assert.Equal(identity, Kit.Audit($"<ToggleButton {attribute}>{Kit.Icon}</ToggleButton>").Findings.Select(f => f.Identity).Single());

    [Fact]
    public void Equal_elements_are_numbered_in_the_baseline()
    {
        var keys = Kit.Audit($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button>").Findings.Select(f => f.BaselineKey).ToList();
        Assert.Equal(3, keys.Distinct().Count());
        Assert.EndsWith("|#2", keys[1], StringComparison.Ordinal);
        Assert.EndsWith("|#3", keys[2], StringComparison.Ordinal);
    }

    [Fact]
    public void The_baseline_key_does_not_depend_on_the_standard_or_the_language()
    {
        var a = Kit.AuditEverything();
        var b = Kit.AuditEverything(new Scope(Standard.Section508, "AA"));
        Assert.Subset(a.Findings.Select(f => f.BaselineKey).ToHashSet(), b.Findings.Select(f => f.BaselineKey).ToHashSet());
    }

    [Fact]
    public void A_stale_baseline_does_not_claim_that_things_were_fixed()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window(Kit.Everything, Kit.EverythingTemplate));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        Assert.Contains("not found this time: fixed, ignored, or not in WCAG 2.2 A;", f.Run("audit", f.Root, "--level", "A", "--baseline", f.At("b.json")).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_hides_known_findings_survives_moves_and_still_catches_new_ones()
    {
        using var p = Kit.Bad();
        var b = p.At("b.json");
        Assert.Equal(0, p.Run("audit", p.Root, "--write-baseline", b).Exit);
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", b).Exit);

        // 上面插十行、改縮排：還是已知的
        p.Add("Main.xaml", Kit.Window($"{new string('\n', 10)}<TextBlock Text=\"new\"/>   <Button Content=\"ok\"/><Button>{Kit.Icon}</Button>"));
        Assert.Equal(0, p.Run("audit", p.Root, "--baseline", b).Exit);

        // 多一個一模一樣的：是新的
        p.Add("Main.xaml", Kit.Window($"<Button>{Kit.Icon}</Button><Button>{Kit.Icon}</Button>"));
        var (exit, output, _) = p.Run("audit", p.Root, "--baseline", b);
        Assert.Equal(1, exit);
        Assert.Contains("1 fail, 0 warn, 0 info (new; 1 more in the baseline)", output);

        // 修好了：baseline 有過期的
        p.Add("Main.xaml", Kit.Window("<Button Content=\"ok\"/>"));
        Assert.Contains("stale baseline", p.Run("audit", p.Root, "--baseline", b).Out);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"keyVersion\": 99, \"known\": []}")]
    [InlineData("null")]
    public void A_bad_baseline_exits_2(string content)
    {
        using var p = Kit.Bad();
        p.Add("b.json", content);
        Assert.Equal(2, p.Run("audit", p.Root, "--baseline", p.At("b.json")).Exit);
    }

    [Fact]
    public void An_empty_scan_does_not_write_a_baseline()
    {
        using var p = new Folder();
        Assert.Equal(3, p.Run("audit", p.Root, "--write-baseline", p.At("b.json")).Exit);
        Assert.False(File.Exists(p.At("b.json")));
    }

    /// <summary>只拿掉沒有的，不把新的記進去：「重寫一次」會讓新問題變成已知（2026-11-24 審查）。</summary>
    [Fact]
    public void Pruning_drops_what_is_gone_and_accepts_nothing_new()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<TextBox x:Name=\"a\"/><TextBox x:Name=\"b\"/>"));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        f.Add("M.xaml", Kit.Window("<TextBox x:Name=\"b\"/><TextBox x:Name=\"c\"/>"));
        Assert.Contains("stale baseline: 1 (not found this time", f.Run("audit", f.Root, "--baseline", f.At("b.json")).Out, StringComparison.Ordinal);
        var r = f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--prune-baseline");
        Assert.Equal(1, r.Exit);
        Assert.Contains("pruned 1 entries", r.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", r.Out, StringComparison.Ordinal);
        Assert.Equal(["input-no-name|M.xaml|TextBox|b"], Baseline.Parse(File.ReadAllText(f.At("b.json"))).Known);
        Assert.Equal(1, f.Run("audit", f.Root, "--baseline", f.At("b.json")).Exit);
    }

    /// <summary>沒看到不等於修好：這次沒檢查的種類、讀不懂的檔案、不可信的結果、別的資料夾寫的都不動。</summary>
    [Fact]
    public void Pruning_keeps_what_was_not_looked_at()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<TextBox x:Name=\"a\"/>" + $"<Button x:Name=\"k\">{Kit.Icon}</Button>")).Add("N|x.xaml".Replace("|", "_"), Kit.Window("<TextBox x:Name=\"n\"/>"));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        string[] Known() => [.. Baseline.Parse(File.ReadAllText(f.At("b.json"))).Known];
        var all = Known();
        Assert.Equal(3, all.Length);
        f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--prune-baseline", "--config", f.Add("off.json", "{ \"rules\": { \"missing-name\": \"off\" } }").At("off.json"));
        Assert.Equal(all, Known());
        f.Add("N_x.xaml", "<Window");
        f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--prune-baseline", "--min-coverage", "0");
        Assert.Equal(all, Known());
        f.Add("M.xaml", Kit.Window("<local:FancyButton/><local:FancyTextBox/><TextBox x:Name=\"a\"/>"));
        Assert.Contains("baseline not pruned: ", f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--prune-baseline").Err, StringComparison.Ordinal);
        Assert.Equal(all, Known());
    }

    [Fact]
    public void A_baseline_of_another_root_says_so()
    {
        using var f = new Folder().Add("src/M.xaml", Kit.Window("<TextBox x:Name=\"a\"/>"));
        f.Run("audit", f.At("src"), "--write-baseline", f.At("b.json"));
        var r = f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--prune-baseline");
        Assert.Contains("!! none of the 1 entries of the baseline match: was it written for another root folder?", r.Out, StringComparison.Ordinal);
        Assert.Contains("baseline not pruned", r.Err, StringComparison.Ordinal);
        Assert.Single(Baseline.Parse(File.ReadAllText(f.At("b.json"))).Known);
        Assert.Contains("一筆都對不上", f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--lang", "zh-Hant").Out, StringComparison.Ordinal);
        Assert.Contains("--prune-baseline needs --baseline", f.Run("audit", f.Root, "--prune-baseline").Err, StringComparison.Ordinal);
    }

    /// <summary>寫的時候就說接下去會是 exit 3，不要等下一步才失敗。</summary>
    [Fact]
    public void Writing_a_baseline_says_when_the_next_run_will_not_be_trusted()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<TextBox/><local:FancyButton/><local:FooButton/>"));
        var r = f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        Assert.Equal(0, r.Exit);
        Assert.Contains("note: a run with this baseline exits 3: resolution 33.3% is below --min-coverage 60%", r.Err, StringComparison.Ordinal);
        Assert.Contains("treatAs", r.Err, StringComparison.Ordinal);
        Assert.Contains("會是 exit 3：可解析率 33.3% 低於", f.Run("audit", f.Root, "--write-baseline", f.At("b.json"), "--lang", "zh-Hant").Err, StringComparison.Ordinal);
        Assert.Equal("", f.Run("audit", f.Root, "--write-baseline", f.At("b.json"), "--min-coverage", "20").Err);
    }

    /// <summary>這次排除的檔案、變成「不知道」的，不是修好。真的刪掉的檔案才拿掉。</summary>
    [Fact]
    public void Pruning_keeps_excluded_files_and_unknown_places()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window($"<TextBox x:Name=\"t\"/><Button x:Name=\"a\">{Kit.Icon}</Button><TextBox x:Name=\"gone\"/>")).Add("Old/O.xaml", Kit.Window("<TextBox x:Name=\"o\"/>")).Add("Del.xaml", Kit.Window("<TextBox x:Name=\"d\"/>"));
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"));
        File.Delete(f.At("Del.xaml"));
        f.Add("M.xaml", Kit.Window("<TextBox x:Name=\"t\"/><Button x:Name=\"a\"/>")).Add("M.xaml.cs", "class M { void L() { a.Content = 1; } }");
        f.Run("audit", f.Root, "--baseline", f.At("b.json"), "--prune-baseline", "--exclude", "/Old/", "--min-coverage", "0");
        Assert.Equal(["input-no-name|M.xaml|TextBox|t", "input-no-name|Old/O.xaml|TextBox|o", "missing-name|M.xaml|Button|a"], Baseline.Parse(File.ReadAllText(f.At("b.json"))).Known);
    }

    /// <summary>檔名裡的直線與反斜線是跳脫的：讀得回來，才知道那個檔案在不在。</summary>
    [Fact]
    public void Pruning_reads_escaped_names()
    {
        var b = new Baseline { Known = ["missing-name|a\\|b\\\\c.xaml|Button|x", "input-no-name~|d.xaml|TextBox|y"] };
        var seen = new List<(string, string, string)>();
        b.Prune([], DateOnly.MinValue, (kind, file, element) => { seen.Add((kind, file, element)); return false; });
        Assert.Equal([("missing-name", "a|b\\c.xaml", "Button"), ("input-no-name", "d.xaml", "TextBox")], seen);
    }
}
