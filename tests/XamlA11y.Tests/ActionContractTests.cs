using System.Text.RegularExpressions;

namespace XamlA11y.Tests;

/// <summary>
/// action.yml 的契約（照三件套）：inputs 的集合、每個 input 都寫在兩份 README 裡、第三方 action 釘 SHA、最後一步才擋，
/// 以及「四支可以放在同一個 job」：檔名、工具資料夾、artifact 名稱都有自己的名字。
/// </summary>
public class ActionContractTests
{
    private static readonly string[] Inputs =
    [
        "root", "working-directory", "version", "config", "baseline", "fail-on", "min-coverage", "comment", "upload-report", "sarif", "artifact-name",
    ];

    /// <summary>三件套都有的輸入：合併的 Action 要能直接分派。XamlContrast 沒有 config（它找 root 旁的檔案）。</summary>
    private static readonly string[] Shared =
    [
        "root", "working-directory", "version", "baseline", "fail-on", "min-coverage", "comment", "upload-report", "sarif", "artifact-name",
    ];

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "XamlA11y.slnx"))) return dir.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative)).Replace("\r\n", "\n");

    private static List<string> Declared(string yaml)
    {
        var block = yaml[yaml.IndexOf("inputs:", StringComparison.Ordinal)..yaml.IndexOf("\nruns:", StringComparison.Ordinal)];
        return [.. Regex.Matches(block, @"^  ([a-z][a-z-]*):\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void The_inputs_are_the_documented_ones() => Assert.Equal(Inputs, Declared(Read("action.yml")));

    [Fact]
    public void The_inputs_the_family_shares_are_all_there() => Assert.Empty(Shared.Except(Declared(Read("action.yml"))));

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.zh-Hant.md")]
    public void Every_input_is_documented(string readme) => Assert.All(Inputs, i => Assert.Contains($"`{i}`", Read(readme)));

    [Theory]
    [InlineData("action.yml")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/workflows/release.yml")]
    public void Third_party_actions_are_pinned_to_commit_shas(string file)
    {
        var uses = Regex.Matches(Read(file), @"^\s+(?:- )?uses:\s*(\S+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value)
            .Where(u => u != "./" && !u.StartsWith("HSU-YU-MING/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(uses);
        Assert.All(uses, u => Assert.Matches("^[^@]+@[0-9a-f]{40}$", u));
    }

    [Fact]
    public void The_shape_matches_the_family()
    {
        var yaml = Read("action.yml");
        Assert.Contains("- name: Validate inputs", yaml);
        Assert.Contains("::stop-commands::", yaml);
        Assert.Contains("args+=(-- \"$XA_ROOT\")", yaml);
        Assert.Contains("xamla11y audit \"${args[@]}\"", yaml);
        Assert.Contains("sed '$d'", yaml);                                     // PR 留言截在整行上
        Assert.EndsWith("run: exit \"$XA_EXIT\"", yaml.TrimEnd());
        foreach (Match block in Regex.Matches(yaml, @"run: \|((?:\n(?: {8}|\s*$).*)+)", RegexOptions.Multiline))
            Assert.DoesNotContain("${{ inputs.", block.Groups[1].Value);       // inputs 只經由 env 進 shell
    }

    [Fact]
    public void Nothing_collides_with_the_other_three_in_one_job()
    {
        var yaml = Read("action.yml");
        foreach (var other in (string[])["xamlcontrast", "xamlkeys", "xamlfit"])
            Assert.DoesNotMatch($@"RUNNER_TEMP/{other}|default: '{other}|{other}-tool", yaml);
        Assert.Contains("$RUNNER_TEMP/xamla11y-tool", yaml);
        Assert.Contains("default: 'xamla11y-report'", yaml);
    }

    /// <summary>三件套的 repo 在旁邊時（維護者的機器）：真的比它們的 inputs，抓漂移。</summary>
    [Theory]
    [InlineData("XamlContrast")]
    [InlineData("XamlKeys")]
    [InlineData("XamlFit")]
    public void The_sister_repositories_still_have_the_shared_inputs(string repo)
    {
        var path = Path.Combine(RepoRoot(), "..", repo, "action.yml");
        if (!File.Exists(path)) return;
        Assert.Empty(Shared.Except(Declared(File.ReadAllText(path).Replace("\r\n", "\n"))));
    }

    [Fact]
    public void The_csproj_version_is_not_a_real_version()
    {
        Assert.Contains("<Version>0.0.0-dev</Version>", Read("src/XamlA11y.Cli/XamlA11y.Cli.csproj"));
        Assert.Contains("-p:Version=${{ steps.version.outputs.version }}", Read(".github/workflows/release.yml"));
    }
}
