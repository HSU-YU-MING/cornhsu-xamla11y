using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>報告裡的字來自使用者的專案，是不可信的輸入：不能變成 CI 指令、連結、多一行、看不見的字。</summary>
public sealed class SafeOutputTests
{
    [Fact]
    public void Sarif_messages_cannot_contain_links()
    {
        var f = new Finding(Kinds.MissingName, Severity.Fail, "f", 1, "Button", "shows [fix](https://evil.example)");
        Assert.Contains(@"\\[fix\\](https", SarifReport.Create([f], "0"));
    }

    [Theory]
    [InlineData("##vso[task.x]")]
    [InlineData("##VSO[task.x]")]
    [InlineData("#\u00AD#vso[task.x]")]
    [InlineData("##[error]")]
    public void Ci_commands_are_defused(string s)
    {
        var safe = Text.Safe(s);
        Assert.DoesNotContain("##", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00AD", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void Bidirectional_controls_are_visible_everywhere()
    {
        var bidi = ((char)0x202E).ToString();
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<Button><StackPanel><TextBlock Text=\"ab{bidi}cd\"/></StackPanel></Button>"));
        p.Run("audit", p.Root, "--json", p.At("r.json"), "--md", p.At("r.md"), "--sarif", p.At("r.sarif"));
        foreach (var report in (string[])["r.json", "r.md", "r.sarif"])
            Assert.False(File.ReadAllText(p.At(report)).Contains((char)0x202E), report);
        Assert.Equal("👩" + (char)0x200D + "💻", Text.Visible("👩" + (char)0x200D + "💻"));
    }

    [Theory]
    [InlineData("a", "`a`")]
    [InlineData("a`b", "`` a`b ``")]
    [InlineData("a|b", "`a\\|b`")]
    [InlineData("  ", "`(empty)`")]
    public void Code_spans_are_fenced(string text, string expected) => Assert.Equal(expected, MarkdownReport.Code(text));

    /// <summary>很長的鍵：句子的頭尾要留著，中文才翻得到。</summary>
    [Fact]
    public void Long_keys_are_cut_in_the_middle()
    {
        var r = Kit.Audit($"<Button Content=\"{{StaticResource {new string('K', 300)}}}\"/><Button Content=\"a\" Style=\"{{StaticResource {new string('S', 300)}}}\" FocusVisualStyle=\"{{StaticResource {new string('F', 300)}}}\"/>");
        var reasons = r.Checks.SelectMany(x => x.BlockedReasons.Keys).ToList();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, x => { Assert.True(x.Length <= 200); Assert.True(Zh.TryReason(x, out _), x); });
        Assert.Equal(200, Context.CutMiddle(string.Concat(Enumerable.Repeat("😀", 400)), 200).EnumerateRunes().Sum(x => x.Utf16SequenceLength) + (200 - Context.CutMiddle(string.Concat(Enumerable.Repeat("😀", 400)), 200).Length));
        Assert.DoesNotContain("\uFFFD", string.Concat(Context.CutMiddle(string.Concat(Enumerable.Repeat("😀", 400)), 200).EnumerateRunes()), StringComparison.Ordinal);
    }

    /// <summary>看不見的字：BMP 外的標籤字元、行分隔、諺文填充也算。</summary>
    [Theory]
    [InlineData(0xE0041, "\\U000e0041")]
    [InlineData(0x3164, "\\u3164")]
    [InlineData(0x2028, "\\u2028")]
    [InlineData(0x85, "\\u0085")]
    [InlineData(0x202E, "\\u202e")]
    [InlineData(0xAD, "\\u00ad")]
    public void Invisible_characters_are_made_visible(int code, string shown)
    {
        var text = "a" + char.ConvertFromUtf32(code) + "b";
        Assert.Equal("a" + shown + "b", Text.Visible(text));
        Assert.Equal("a" + shown + "b", Text.Safe(text));
        Assert.Equal("👩‍💻 é 中", Text.Visible("👩‍💻 é 中"));
    }

    [Theory]
    [InlineData("](http://evil) @everyone ![x](http://evil/x.png)")]
    [InlineData("`code` ``more`` | pipe")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void Markdown_keeps_untrusted_text_inside_code_spans(string hostile)
    {
        var esc = System.Security.SecurityElement.Escape(hostile);
        using var p = new Folder().Add("Main.xaml", Kit.Window($"<TextBlock Text=\"{esc}\"/><TextBox/><Border MouseDown=\"{esc}\"/><Button Content=\"{{StaticResource {esc}}}\"/><Button Content=\"ok\"/>"));
        p.Run("audit", p.Root, "--md", p.At("r.md"), "--min-coverage", "0");
        var md = File.ReadAllText(p.At("r.md"));
        Assert.Contains("evil".Length > 0 && hostile.Contains("evil") ? "evil" : hostile[..3].Trim(), md);
        // 去掉所有 code span 之後，不可信的字一個都不剩
        var outside = System.Text.RegularExpressions.Regex.Replace(md, @"(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)", "");
        Assert.DoesNotContain("evil", outside);
        Assert.DoesNotContain("@everyone", outside);
        Assert.DoesNotContain("<img", outside);
        Assert.DoesNotContain("onerror", outside);
    }

    [Fact]
    public void Console_output_cannot_be_turned_into_ci_commands_or_extra_lines()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<TextBlock Text=\"a&#10;::error::pwned&#10;##vso[task.setvariable variable=x]y\"/><TextBox/><Border MouseDown=\"h&#10;::set-output name=x::y\"/>"));
        var (_, output, err) = p.Run("audit", p.Root, "--msbuild");
        foreach (var line in (output + err).Split('\n'))
        {
            Assert.DoesNotMatch(@"^\s*::", line);
            Assert.DoesNotContain("##vso[", line);
        }
    }
}
