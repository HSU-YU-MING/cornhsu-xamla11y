using System.Text;
using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Tests;

/// <summary>
/// 跟 WPF 的真值比：uia-facts.json 是 prototype/UiaOracle 在真的 WPF 視窗裡錄的（UIA client 讀的名稱、焦點前後的像素、Tab 停過的元素）。
/// CI 的 Linux 上沒有 WPF，所以錄一份。**檔案裡只有 WPF 的答案**，工具的答案是測試當場算的 ——
/// 改規則不必重錄；加案例或換 WPF 版本才要（uiaoracle record）。
/// </summary>
public class UiaFactsTests
{
    private static readonly JsonDocument Facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json")));

    private static IEnumerable<JsonElement> Group(string name) => Facts.RootElement.GetProperty(name).EnumerateArray();

    private static TheoryData<string> Titles(string group)
    {
        var data = new TheoryData<string>();
        var i = 0;
        foreach (var f in Group(group)) data.Add($"{i++}: {f.GetProperty("title").GetString()}");
        return data;
    }

    private static JsonElement Case(string group, string title) => Group(group).ElementAt(int.Parse(title[..title.IndexOf(':')]));

    public static TheoryData<string> NameCases => Titles("names");
    public static TheoryData<string> FocusCases => Titles("focus");
    public static TheoryData<string> TabCases => Titles("tabs");

    private static bool Meaningful(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("System.Windows.", StringComparison.Ordinal)) return false;
        if (name.EnumerateRunes().All(r => Rune.GetUnicodeCategory(r) == System.Globalization.UnicodeCategory.PrivateUse)) return false;
        return !System.Text.RegularExpressions.Regex.IsMatch(name, @"^([MF]\s*-?\d|#[0-9A-F]{8}$)");
    }

    [Theory, MemberData(nameof(NameCases))]
    public void Name_verdicts_never_contradict_UIA(string title)
    {
        var f = Case("names", title);
        var result = Auditor.Run(Project.FromText(f.GetProperty("xaml").GetString()!));
        foreach (var j in result.Judgements.Where(j => j.Check is "control-names" or "input-names" or "list-names"))
        {
            if (Markup.NameOf(j.Element) is not { } id || !f.GetProperty("uia").TryGetProperty(id, out var node)) continue;
            if (j.Verdict is Verdict.Blocked or Verdict.NotJudged) continue;
            var name = node.GetProperty("name").GetString()!;
            var ok = j.Kind?.Slug switch
            {
                null or "symbol-name" => Meaningful(name),
                "list-no-name" or "widget-no-name" => !Meaningful(name),
                "meaningless-name" => !Meaningful(name) || j.Reason!.Contains("font", StringComparison.Ordinal),
                _ => string.IsNullOrWhiteSpace(name),
            };
            Assert.True(ok, $"tool: {j.Verdict} {j.Kind?.Slug} ({j.Reason}); UIA name: '{name}'");
        }
    }

    /// <summary>「不知道」不算錯，但不能拿它躲：有答案的比例要守住。</summary>
    [Fact]
    public void Most_of_the_recorded_names_get_an_answer()
    {
        int judged = 0, unknown = 0;
        foreach (var f in Group("names"))
            foreach (var j in Auditor.Run(Project.FromText(f.GetProperty("xaml").GetString()!)).Judgements.Where(j => j.Check is "control-names" or "input-names" or "list-names"))
                if (Markup.NameOf(j.Element) is { } id && f.GetProperty("uia").TryGetProperty(id, out _))
                {
                    if (j.Verdict is Verdict.Blocked or Verdict.NotJudged) unknown++; else judged++;
                }
        Assert.True(judged >= 220, $"judged {judged}, unknown {unknown}");
        Assert.True(unknown <= 12, $"judged {judged}, unknown {unknown}");
    }

    [Theory, MemberData(nameof(FocusCases))]
    public void Focus_verdicts_never_contradict_the_pixels(string title)
    {
        var f = Case("focus", title);
        var visible = f.GetProperty("visible").GetBoolean();
        foreach (var j in Auditor.Run(Project.FromText(f.GetProperty("xaml").GetString()!)).Judgements.Where(j => j.Check == "focus"))
        {
            if (j.Verdict is Verdict.Blocked or Verdict.NotJudged) continue;
            Assert.True(visible == (j.Verdict == Verdict.Pass), $"tool: {j.Verdict} ({j.Reason}); focus visible in WPF: {visible}");
        }
    }

    [Theory, MemberData(nameof(TabCases))]
    public void Tab_findings_are_never_reached_by_Tab(string title)
    {
        var f = Case("tabs", title);
        var stops = f.GetProperty("stops").EnumerateArray().Select(s => s.GetString()!).ToList();
        var reached = stops.Contains("p") || stops.Any(s => s.StartsWith("PART_", StringComparison.Ordinal));
        foreach (var j in Auditor.Run(Project.FromText(f.GetProperty("xaml").GetString()!)).Judgements.Where(j => j.Check is "mouse" or "tab-order" && Markup.NameOf(j.Element) == "p"))
            if (j.Verdict == Verdict.Finding) Assert.False(reached, $"{j.Check}: {j.Reason}; stops: {string.Join(" → ", stops)}");
    }

    [Fact]
    public void The_recording_covers_every_group() =>
        Assert.True(Group("names").Count() >= 240 && Group("focus").Count() >= 40 && Group("tabs").Count() >= 55);

    public static TheoryData<string> ItemTitles => [.. Facts.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];

    public static TheoryData<string> SilentTitles => [.. Facts.RootElement.GetProperty("silent").EnumerateArray().Select(x => x.GetProperty("title").GetString()!)];

    private static (AuditResult Result, JsonElement Case) Fact(string group, string title)
    {
        var c = Facts.RootElement.GetProperty(group).EnumerateArray().Single(x => x.GetProperty("title").GetString() == title);
        return (Kit.AuditWithCode(c.GetProperty("xaml").GetString()!, Facts.RootElement.GetProperty("code").GetString()!), c);
    }

    [Theory, MemberData(nameof(ItemTitles))]
    public void Data_items_are_judged_the_way_WPF_names_them(string title)
    {
        var (r, c) = Fact("items", title);
        var typeName = c.GetProperty("name").GetString()!.StartsWith("UiaOracle.", StringComparison.Ordinal);
        var judged = r.Judgements.Where(j => j.Check == "item-names" && j.Verdict is not (Verdict.NotJudged or Verdict.Blocked)).ToList();
        Assert.NotEmpty(judged);
        Assert.All(judged, j => Assert.Equal(typeName, j.Verdict == Verdict.Finding));
    }

    [Theory, MemberData(nameof(SilentTitles))]
    public void Silent_tab_stops_are_the_ones_WPF_has(string title)
    {
        var (r, c) = Fact("silent", title);
        var silent = c.GetProperty("stop").GetBoolean() && !c.GetProperty("inTree").GetBoolean();
        var judged = r.Judgements.Where(j => j.Check == "silent-stops" && (string?)j.Element.Attribute(Markup.XName_) == "p").ToList();
        if (title.Contains("limits Tab", StringComparison.Ordinal)) { Assert.All(judged, j => Assert.Equal(Verdict.NotJudged, j.Verdict)); return; }
        Assert.DoesNotContain(judged, j => j.Verdict is Verdict.NotJudged or Verdict.Blocked);
        Assert.Equal(silent, judged.Any(j => j.Verdict == Verdict.Finding));
    }
}
