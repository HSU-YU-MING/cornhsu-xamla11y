using System.Text.RegularExpressions;

namespace XamlA11y.Tests;

/// <summary>
/// 四件套的版本寫在兩個地方：docs/four-tools.md（使用者複製的）與 ci.yml 的 four-tools job（真的跑的）。
/// 擋的是「改一半」：一邊改了另一邊沒改、Action 改了工具沒改。不是最新版不擋 —— 別支一發版就讓這裡不相干的 PR 變紅。
/// 改的時候用 scripts/bump-tool.sh。
/// </summary>
public sealed partial class FourToolsTests
{
    private const string Docs = "docs/four-tools.md", Ci = ".github/workflows/ci.yml";
    private static readonly string[] Others = ["xamlcontrast", "xamlkeys", "xamlfit"];

    private static string Read(string file) => File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file));

    /// <summary>工具 → 每一處的（Action 的 tag、傳給它的 version）。version 找到下一個 uses 為止。</summary>
    private static ILookup<string, (string Uses, string Version)> Pins(string file) =>
        Pin().Matches(Read(file)).ToLookup(m => m.Groups["tool"].Value, m => (m.Groups["uses"].Value, m.Groups["version"].Value));

    [GeneratedRegex(@"uses:\s*HSU-YU-MING/cornhsu-(?<tool>xaml(?:contrast|keys|fit|a11y))@v(?<uses>\d+\.\d+\.\d+)(?:(?!uses:)[\s\S])*?version:\s*(?<version>[^\s,}]+)")]
    private static partial Regex Pin();

    [GeneratedRegex(@"uses:\s*HSU-YU-MING/cornhsu-xaml(?:contrast|keys|fit|a11y)@")]
    private static partial Regex Uses();

    [Theory]
    [InlineData(Docs)]
    [InlineData(Ci)]
    public void Every_action_runs_the_tool_of_its_own_version(string file)
    {
        var pins = Pins(file).SelectMany(g => g).ToList();
        // 沒寫 version、或 tag 不是三段式的，上面的式子比不到 —— 數量要對得上
        Assert.Equal(Uses().Count(Read(file)), pins.Count);
        Assert.All(pins, p => Assert.Equal(p.Uses, p.Version));
    }

    [Fact]
    public void The_document_and_CI_use_the_same_versions()
    {
        var (docs, ci) = (Pins(Docs), Pins(Ci));
        Assert.All(Others, t => Assert.Equal(Assert.Single(ci[t]), Assert.Single(docs[t].Distinct())));
    }

    [Fact]
    public void The_document_pins_the_released_XamlA11y()
    {
        var latest = Regex.Match(Read("CHANGELOG.md"), @"^## \[(\d+\.\d+\.\d+)\] - \d", RegexOptions.Multiline).Groups[1].Value;
        Assert.Equal((latest, latest), Assert.Single(Pins(Docs)["xamla11y"].Distinct()));
    }
}
