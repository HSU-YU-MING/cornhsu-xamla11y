using XamlA11y.Core.Analysis;
using System.Text.Json;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

/// <summary>xamla11y-ignore 註解：理由、位置、沒作用時的警告。</summary>
public sealed class IgnoreCommentTests
{
    [Theory]
    [InlineData("<!-- xamla11y-ignore: decorative -->", 1)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- xamlfit-ignore: fixed -->", 1)]
    [InlineData("<!-- xamlfit-ignore: fixed; xamla11y-ignore: decorative -->", 1)]
    [InlineData("<!-- xamla11y-ignore -->", 0)]
    [InlineData("<!-- xamla11y-ignore: xamlfit-ignore: only the other tool gave a reason -->", 0)]
    [InlineData("<!-- xamlfit-ignore: fixed -->", 0)]
    public void Ignore_comments_need_a_reason_and_stack_with_the_other_tools(string comment, int suppressed)
    {
        var r = Kit.Audit($"{comment}<Button>{Kit.Icon}</Button>");
        Assert.Equal(suppressed, r.Suppressed);
        Assert.Equal(1 - suppressed, r.Findings.Count);
    }

    [Theory] // 被註解掉的元素也是隔了元素：ignore 原本管的是它，不能因為它被註解掉就改壓下一個（四件套一致）
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- <Button Content='x'/> -->", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!--\n  <Button Content='x'/>\n--><!-- xamlfit-ignore: fixed -->", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- note: the <Button/> below is an icon -->", 1)] // 在談元素，不是註解掉的元素
    [InlineData("<!-- <Button Content='x'/> --><!-- xamla11y-ignore: decorative -->", 1)]
    // 文字也是內容，停；處理指令不是，跳過
    [InlineData("<!-- xamla11y-ignore: decorative -->text", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><!-- xamlfit-ignore: fixed -->text<!-- note -->", 0)]
    [InlineData("<!-- xamla11y-ignore: decorative --><![CDATA[text]]>", 0)]
    [InlineData("text<!-- xamla11y-ignore: decorative -->", 1)]
    [InlineData("<!-- xamla11y-ignore: decorative --><?note x?>", 1)]
    public void Ignore_comments_do_not_reach_past_a_commented_out_element_or_text(string comment, int suppressed)
    {
        var r = Kit.Audit($"{comment}<Button>{Kit.Icon}</Button>");
        Assert.Equal(suppressed, r.Suppressed);
        Assert.Equal(1 - suppressed, r.Findings.Count);
        // 停下要講出來；不然使用者只看到問題重新報出來，看不出自己的註解為什麼沒生效
        if (suppressed > 0) Assert.Empty(r.InvalidIgnores);
        else Assert.Contains("commented-out element or text", Assert.Single(r.InvalidIgnores), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<!-- xamla11y-ignore -->", "has no reason")]
    [InlineData("<!-- xamla11y-ignore --><!-- xamlfit-ignore: f -->", "has no reason")]
    [InlineData("<!-- xamla11y-ignore: far --><!-- xamla11y-ignore -->", "has no reason")] // 最近的說了算
    [InlineData("<!-- xamla11y-ignore: r --><!-- <Button/> -->", "commented-out element")]
    public void An_ignore_comment_that_does_nothing_says_why(string comment, string why)
    {
        var r = Kit.Audit($"{comment}<Button>{Kit.Icon}</Button>");
        Assert.Equal(0, r.Suppressed);
        Assert.Contains(why, Assert.Single(r.InvalidIgnores), StringComparison.Ordinal);
    }

    [Theory] // 沒有標記停在那裡，就沒有東西要講
    [InlineData("<!-- <Button/> -->")]
    [InlineData("<!-- xamlfit-ignore: f --><!-- <Button/> -->")]
    [InlineData("<!-- xamlfit-ignore: f -->text")]
    [InlineData("<!-- xamla11y-ignore: r --><Border/><!-- <Button/> -->")]
    public void A_commented_out_element_alone_is_not_warned_about(string comment)
        => Assert.Empty(Kit.Audit($"<StackPanel>{comment}<Button>{Kit.Icon}</Button></StackPanel>").InvalidIgnores);

    [Theory]
    [InlineData(" xamla11y-ignore: decorative ", "decorative")]
    [InlineData("xamlfit-ignore: a; xamla11y-ignore: b", "b")]
    [InlineData("xamla11y-ignore: b; xamlfit-ignore: a", "b")]
    [InlineData("xamla11y-ignore: see xamlcontrast-ignore", "see")]
    [InlineData("xamla11y-ignore", "")]
    [InlineData("xamla11y-ignore:", "")]
    [InlineData("xamla11y-ignore::", "")]
    [InlineData("xamla11y-ignore - because", "")]
    [InlineData("xamla11y-ignored: no", null)]
    [InlineData("TODO: remove every xamla11y-ignore in this file", null)]
    [InlineData("xamlfit-ignore: a", null)]
    [InlineData("", null)]
    public void Ignore_comments_are_read_strictly(string comment, string? reason) => Assert.Equal(reason, Context.ParseIgnore(comment));

    /// <summary>放過的與理由都在報告上：審的人查得到誰被放過、為什麼。</summary>
    [Fact]
    public void Accepted_findings_are_listed_with_their_reason()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<!-- xamla11y-ignore: the row <b> is the control -->\n<TextBox/>\n<Button Content=\"x\"/>"));
        var s = Assert.Single(f.Audit().Suppressions);
        Assert.Equal((Kinds.InputNoName, "M.xaml", 2, "TextBox", "the row <b> is the control"), (s.Kind, s.File, s.Line, s.Element, s.Reason));
        f.Run("audit", f.Root, "--json", f.At("r.json"), "--md", f.At("r.md"), "--html", f.At("r.html"));
        using var json = JsonDocument.Parse(File.ReadAllText(f.At("r.json")));
        Assert.Equal("the row <b> is the control", json.RootElement.GetProperty("suppressed")[0].GetProperty("reason").GetString());
        Assert.Contains("| `M.xaml:2` | input-no-name | `the row <b> is the control` |", File.ReadAllText(f.At("r.md")), StringComparison.Ordinal);
        Assert.Contains("<td>the row &lt;b&gt; is the control</td>", File.ReadAllText(f.At("r.html")), StringComparison.Ordinal);
    }

    /// <summary>修好之後留著的 ignore：之後再壞會被蓋住，所以要說。</summary>
    [Theory]
    [InlineData("<!-- xamla11y-ignore: fixed long ago --><Button Content=\"OK\"/>", 1)]
    [InlineData("<!-- xamla11y-ignore: on a panel --><Grid/>", 1)]
    [InlineData("<!-- xamla11y-ignore: used --><TextBox/>", 0)]
    [InlineData("<!-- xamla11y-ignore: cannot tell --><local:FancyButton/>", 0)]
    [InlineData("<!-- xamla11y-ignore: cannot tell --><Button Content=\"{Binding T}\"/>", 0)]
    [InlineData("<!-- xamla11y-ignore --><Button Content=\"OK\"/>", 0)]
    [InlineData("<Button Content=\"OK\"/>", 0)]
    public void An_ignore_with_nothing_to_accept(string xaml, int expected) => Assert.Equal(expected, Kit.Audit(xaml).UnusedIgnores.Count);

    [Fact]
    public void Unused_ignores_are_printed_and_not_counted_when_the_kind_is_off()
    {
        using var f = new Folder().Add("M.xaml", Kit.Window("<!-- xamla11y-ignore: a --><Button Content=\"OK\"/><!-- xamla11y-ignore: b --><TextBox/>"));
        Assert.Contains("xamla11y-ignore with nothing to accept (remove it): M.xaml:1", f.Run("audit", f.Root).Out, StringComparison.Ordinal);
        Assert.Contains("沒有東西要放過的 xamla11y-ignore（可以拿掉）：M.xaml:1", f.Run("audit", f.Root, "--lang", "zh-Hant").Out, StringComparison.Ordinal);
        Assert.Single(f.Audit(new AuditOptions { Severities = new Dictionary<string, Severity> { ["input-no-name"] = Severity.Off } }).UnusedIgnores);
    }

    [Fact]
    public void An_accepted_class_is_listed_once_with_its_kind()
    {
        using var f = Kit.Files("<!-- xamla11y-ignore: handled elsewhere --><local:Pad/>", "namespace App; public class Pad : Control { }");
        Assert.Equal(Kinds.SilentTabStop, Assert.Single(f.Audit().Suppressions).Kind);
    }
}
