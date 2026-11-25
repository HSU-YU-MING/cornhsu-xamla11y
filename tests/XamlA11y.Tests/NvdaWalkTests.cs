using System.Text.Json;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Walk;
using XamlA11y.Core.Xaml;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>
/// xamla11y walk 跟真的 NVDA 比：nvda-walk-facts.json 是按真的 Tab 鍵走過真的 WPF 視窗，每一站停在哪、NVDA 念了什麼
/// （繁體中文、英文各一遍；prototype/UiaOracle/nvda.mjs … nvda-walk.txt 錄的）。
/// </summary>
public sealed class NvdaWalkTests
{
    private static readonly JsonElement[] Cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "nvda-walk-facts.json")))
        .RootElement.GetProperty("cases").EnumerateArray().ToArray();

    public static TheoryData<string> Titles => [.. Cases.Select(c => c.GetProperty("title").GetString()!)];

    private static (Project Project, WalkResult Walk, JsonElement[] Stops) Of(string title)
    {
        var c = Cases.Single(x => x.GetProperty("title").GetString() == title);
        var project = Project.FromText($"<StackPanel xmlns='{Markup.WpfNamespace}' xmlns:x='{Markup.XamlNamespace}'><StackPanel.Resources>{c.GetProperty("resources").GetString()}</StackPanel.Resources>{c.GetProperty("xaml").GetString()}</StackPanel>");
        return (project, TabWalk.Of(project, project.Docs[0]), [.. c.GetProperty("stops").EnumerateArray()]);
    }

    private static string NameOf(JsonElement stop) => stop.GetProperty("name").GetString()!;

    /// <summary>真的 Tab 鍵停的地方與順序；最後一站是走過的，就是繞在裡面出不來。</summary>
    [Theory, MemberData(nameof(Titles))]
    public void Tab_stops_where_the_tool_says(string title)
    {
        var (_, walk, stops) = Of(title);
        var real = stops.Select(NameOf).ToList();
        var loops = real.Count != real.Distinct().Count();
        Assert.Equal(real.Distinct(), walk.Stops.Select(s => Markup.NameOf(s.Element)));
        Assert.All(walk.Stops, s => Assert.Equal(WalkKind.Stop, s.Kind));
        Assert.Equal(loops, walk.After > 0);
    }

    /// <summary>印出來的 NVDA 念法，每一段照順序都真的念了，而且前面沒有別的字。</summary>
    [Theory, MemberData(nameof(Titles))]
    public void NVDA_reads_what_the_tool_says(string title)
    {
        var (project, walk, stops) = Of(title);
        var context = new Context(project, new AuditOptions());
        foreach (var s in walk.Stops)
            foreach (var (lang, key) in ((Lang, string)[])[(Lang.ZhHant, "zh"), (Lang.En, "en")])
            {
                if (WalkReport.Reads(context, s, lang) is not { } reads) continue;
                var said = stops.First(x => NameOf(x) == Markup.NameOf(s.Element)).GetProperty(key).GetString()!;
                Assert.True(said.Split(". ").Any(sentence => Starts(sentence.Split(", "), reads)), $"{Markup.NameOf(s.Element)}: the tool says '{reads}', NVDA said '{said}'");
            }

        // 名稱與型別一定連著念在最前面；描述在狀態後面
        static bool Starts(string[] heard, string reads)
        {
            var at = 0;
            foreach (var piece in reads.Split(", "))
            {
                var i = Array.IndexOf(heard, piece, at);
                if (i < 0 || at == 0 && i != 0) return false;
                at = i + 1;
            }
            return true;
        }
    }

    /// <summary>有名稱的、沒名稱的、有描述的，都真的比過，不是全部跳過。</summary>
    [Fact]
    public void Enough_was_compared()
    {
        var all = Cases.Select(c => Of(c.GetProperty("title").GetString()!)).SelectMany(x => x.Walk.Stops.Select(s => WalkReport.Reads(new Context(x.Project, new AuditOptions()), s, Lang.En))).ToList();
        var read = all.Count(r => r is not null);
        Assert.Equal((88, 69), (all.Count, read));
        foreach (var file in (string[])["docs/verification.md", "docs/驗證.md"])
            Assert.Contains($"| {all.Count} | {read} | {all.Count - read} | **0** |", File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)), StringComparison.Ordinal);
        Assert.Contains($"有寫念法的 {read} 站", File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), "docs/WPF實測.md")), StringComparison.Ordinal);
        Assert.Contains("button, Delete", all);
        Assert.Contains("edit", all);
        Assert.Contains("Sign in, button", all);
    }
}
