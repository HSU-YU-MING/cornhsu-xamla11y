using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Tests;

/// <summary>
/// 跟真的 NVDA 比：nvda-facts.json 是 NVDA（繁體中文）用 Tab 走過真的 WPF 視窗時念的話（prototype/UiaOracle/nvda.mjs 錄的）。
/// 檔案裡只有 NVDA 念的，工具的判定是測試當場算的。
/// </summary>
public class NvdaFactsTests
{
    private static readonly JsonElement[] Cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "nvda-facts.json")))
        .RootElement.GetProperty("cases").EnumerateArray().ToArray();

    public static TheoryData<string> Titles => [.. Cases.Select(c => c.GetProperty("title").GetString()!)];

    private static readonly string[] Roles =
        ["按鈕", "切換按鈕", "核取方塊", "選擇鈕", "編輯區", "下拉式方塊", "滑桿", "清單", "樹狀檢視", "資料格", "群組", "連結", "索引標籤"];

    /// <summary>NVDA 先念名稱、再念型別；沒有名稱就直接念型別。清單的名稱在容器那一句，其他的在最後一句。</summary>
    private static string SpokenName(string said, bool container)
    {
        var sentences = said.Split(". ");
        var sentence = container ? sentences[^Math.Min(2, sentences.Length)] : sentences[^1];
        var first = sentence.Split(", ")[0].TrimStart(',').Trim();
        return Roles.Contains(first) ? "" : first;
    }

    private static bool Meaningful(string name) =>
        name.Length > 0 && !name.StartsWith("System dot", StringComparison.Ordinal) && !name.All(c => char.IsWhiteSpace(c) || c is >= '' and <= '');

    [Theory, MemberData(nameof(Titles))]
    public void Name_verdicts_never_contradict_what_NVDA_says(string title)
    {
        var f = Cases.Single(c => c.GetProperty("title").GetString() == title);
        if (f.GetProperty("heard").GetArrayLength() == 0) return;                       // 停用的命令按鈕：Tab 停不到
        var said = f.GetProperty("heard")[0].GetProperty("said").GetString()!;
        foreach (var j in Auditor.Run(Project.FromText($"<StackPanel xmlns='{Markup.WpfNamespace}' xmlns:x='{Markup.XamlNamespace}'>{f.GetProperty("xaml").GetString()}</StackPanel>")).Judgements)
        {
            if (Markup.NameOf(j.Element) != "p" || j.Rule is not ("R1" or "R2" or "R7") || j.Verdict is Verdict.Blocked or Verdict.NotJudged) continue;
            var name = SpokenName(said, j.Rule == "R7");
            var ok = j.Kind?.Slug switch
            {
                null or "symbol-name" => Meaningful(name),
                "meaningless-name" => !Meaningful(name),
                _ => name.Length == 0,
            };
            Assert.True(ok, $"tool: {j.Verdict} {j.Kind?.Slug}; NVDA: '{said}'");
        }
    }

    [Fact]
    public void Enough_of_it_is_judged()
    {
        var judged = Cases.Count(f => Auditor.Run(Project.FromText($"<StackPanel xmlns='{Markup.WpfNamespace}' xmlns:x='{Markup.XamlNamespace}'>{f.GetProperty("xaml").GetString()}</StackPanel>"))
            .Judgements.Any(j => j.Rule is "R1" or "R2" or "R7" && Markup.NameOf(j.Element) == "p" && j.Verdict is not (Verdict.Blocked or Verdict.NotJudged)));
        Assert.True(judged >= 46, $"{judged} of {Cases.Length}");
        // 兩份 README 的那一列：聽得到的、其中判得出來的（停用的命令按鈕聽不到）
        var heard = Cases.Count(c => c.GetProperty("heard").GetArrayLength() > 0);
        foreach (var file in (string[])["README.md", "README.zh-Hant.md"])
            Assert.Contains($"| {heard} | {judged - 1} | {heard - judged + 1} | **0** |", File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)), StringComparison.Ordinal);
    }

    /// <summary>ToolTip 不是名稱，但 NVDA 會當描述念在型別後面（docs/standards.md）。</summary>
    [Theory]
    [InlineData("Button icon, ToolTip only", "按鈕, Delete")]
    [InlineData("TextBox ToolTip only", "編輯區, Name, 空白")]
    [InlineData("TextBox Label Target", "編輯區, 空白")]
    public void The_documents_quote_what_NVDA_says(string title, string said) =>
        Assert.Equal(said, Cases.Single(c => c.GetProperty("title").GetString() == title).GetProperty("heard")[0].GetProperty("said").GetString());
}
