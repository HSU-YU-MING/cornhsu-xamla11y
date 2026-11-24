using System.Text.Json;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Tests;

/// <summary>
/// 跟真的 NVDA 比：nvda-facts.json 是 NVDA（繁體中文、英文各一遍）用 Tab 走過真的 WPF 視窗時念的話（prototype/UiaOracle/nvda.mjs 錄的）。
/// 檔案裡只有 NVDA 念的，工具的判定與預告是測試當場算的。
/// </summary>
public class NvdaFactsTests
{
    private static readonly JsonElement[] Cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "nvda-facts.json")))
        .RootElement.GetProperty("cases").EnumerateArray().ToArray();

    public static TheoryData<string, string> Titles
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var c in Cases) foreach (var lang in (string[])["zh", "en"]) data.Add(lang, c.GetProperty("title").GetString()!);
            return data;
        }
    }

    private static readonly string[] Lists = ["清單", "樹狀檢視", "資料格", "工具列", "list", "tree view", "data grid", "tool bar"];

    private static readonly string[] Roles =
    [
        .. Lists,
        "按鈕", "切換按鈕", "核取方塊", "選擇鈕", "編輯區", "文件", "下拉式方塊", "滑桿", "群組", "連結", "索引標籤", "功能表項目",
        "button", "toggle button", "check box", "radio button", "edit", "document", "combo box", "slider", "grouping", "link", "tab", "menu item",
    ];

    private static JsonElement Case(string title) => Cases.Single(c => c.GetProperty("title").GetString() == title);
    private static string? Said(JsonElement c, string lang) => c.GetProperty(lang).GetString();
    private static string[] Pieces(string sentence) => sentence.Split(", ");

    private static readonly string Code = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "uia-facts.json"))).RootElement.GetProperty("code").GetString()!;

    private static AuditResult Audit(JsonElement c)
    {
        var xaml = $"<StackPanel xmlns='{Markup.WpfNamespace}' xmlns:x='{Markup.XamlNamespace}'>{c.GetProperty("xaml").GetString()}</StackPanel>";
        if (!xaml.Contains("<o:", StringComparison.Ordinal)) return Auditor.Run(Project.FromText(xaml));
        using var f = new Folder().Add("Main.xaml", xaml).Add("Code.cs", Code);
        return f.Audit();
    }

    private static IEnumerable<Judgement> Judged(JsonElement c) => Audit(c).Judgements
        .Where(j => Markup.NameOf(j.Element) == "p" && j.Rule is "R1" or "R2" or "R7" && j.Verdict is not (Verdict.Blocked or Verdict.NotJudged));

    /// <summary>NVDA 先念名稱、再念型別；沒有名稱就直接念型別。清單的名稱在清單那一句，其他的在最後一句。</summary>
    private static string SpokenName(string said, bool list)
    {
        var sentences = said.Split(". ");
        var sentence = list ? sentences.First(s => Pieces(s).Any(p => Lists.Contains(p))) : sentences[^1];
        var first = Pieces(sentence)[0].TrimStart(',').Trim();
        return Roles.Contains(first) ? "" : first;
    }

    private static bool Meaningful(string name) =>
        name.Length > 0 && !name.StartsWith("System dot", StringComparison.Ordinal) && !name.All(c => char.IsWhiteSpace(c) || c is >= '' and <= '');

    [Theory, MemberData(nameof(Titles))]
    public void Name_verdicts_never_contradict_what_NVDA_says(string lang, string title)
    {
        var c = Case(title);
        if (Said(c, lang) is not { } said) return;                       // 停用的命令按鈕：Tab 停不到
        foreach (var j in Judged(c))
        {
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

    /// <summary>報告上的「NVDA 會念」：每一段照順序都真的念了，而且型別前面沒有別的字。</summary>
    [Theory, MemberData(nameof(Titles))]
    public void What_the_reports_say_NVDA_says_is_what_it_said(string lang, string title)
    {
        var c = Case(title);
        foreach (var f in Audit(c).Findings)
        {
            if (Nvda.Says(f, lang == "zh" ? Lang.ZhHant : Lang.En) is not { } says) continue;
            Assert.NotNull(Said(c, lang));
            Assert.Contains(Said(c, lang)!.Split(". "), sentence => Matches(Pieces(sentence), says));
        }

        static bool Matches(string[] heard, string says)
        {
            var at = 0;
            foreach (var part in says.Split("…, "))
                foreach (var piece in part == says ? Pieces(says) : [part])
                {
                    var i = Array.FindIndex(heard, at, h => h == piece || part != says && h.StartsWith(piece, StringComparison.Ordinal));
                    if (i < 0 || at == 0 && i != 0) return false;
                    at = i + 1;
                }
            return true;
        }
    }

    /// <summary>R8：工具說「念成型別名」的，NVDA 真的念型別名；說沒問題的沒有。收起來的 ComboBox 也是。</summary>
    [Theory, MemberData(nameof(Titles))]
    public void Items_are_read_the_way_the_tool_says(string lang, string title)
    {
        var c = Case(title);
        foreach (var j in Audit(c).Judgements.Where(j => j.Rule == "R8" && j.Verdict is Verdict.Finding or Verdict.Pass))
            Assert.Equal(j.Verdict == Verdict.Finding, Said(c, lang)!.Contains("Uia Oracle dot", StringComparison.Ordinal));
    }

    /// <summary>R9：工具說「沒聲音」的，NVDA 停在那裡真的什麼都沒念。</summary>
    [Theory, MemberData(nameof(Titles))]
    public void Silent_stops_are_silent(string lang, string title)
    {
        var c = Case(title);
        foreach (var j in Audit(c).Judgements.Where(j => j.Rule == "R9" && Markup.NameOf(j.Element) == "p" && j.Verdict == Verdict.Finding))
            Assert.Equal("", Said(c, lang));
    }

    [Fact]
    public void Both_new_rules_were_heard()
    {
        var all = Cases.SelectMany(c => Audit(c).Judgements).ToList();
        Assert.True(all.Count(j => j.Rule == "R8" && j.Verdict == Verdict.Finding) >= 4);
        Assert.True(all.Count(j => j.Rule == "R8" && j.Verdict == Verdict.Pass) >= 4);
        Assert.True(all.Count(j => j.Rule == "R9" && j.Verdict == Verdict.Finding) >= 3);
    }

    /// <summary>念法表上的每一種控制項、每一種會念描述的，都真的跑過。</summary>
    [Fact]
    public void Every_control_in_the_table_was_run()
    {
        var run = Cases.Where(c => Said(c, "zh") is not null && Said(c, "en") is not null).SelectMany(c => Audit(c).Findings).Where(f => Nvda.Says(f, Lang.En) is not null).ToList();
        Assert.Equal(Nvda.Controls.Order(StringComparer.Ordinal), run.Select(f => f.Control!).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(Nvda.WithDescription.Order(StringComparer.Ordinal), run.Where(f => f.Description!.Length > 0).Select(f => f.Control!).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_readmes_say_how_much_was_compared()
    {
        var heard = Cases.Where(c => Said(c, "zh") is not null).ToList();
        var judged = heard.Count(c => Judged(c).Any());
        Assert.True(judged >= 72, $"{judged} of {heard.Count}");
        foreach (var file in (string[])["README.md", "README.zh-Hant.md"])
            Assert.Contains($"| {heard.Count} | {judged} | {heard.Count - judged} | **0** |", File.ReadAllText(Path.Combine(ActionContractTests.RepoRoot(), file)), StringComparison.Ordinal);
    }

    /// <summary>ToolTip 不是名稱，但 NVDA 會當描述念在型別後面（docs/standards.md）。</summary>
    [Theory]
    [InlineData("Button icon, ToolTip only", "按鈕, Delete", "button, Delete")]
    [InlineData("TextBox ToolTip only", "編輯區, Name, 空白", "edit, Name, blank")]
    [InlineData("TextBox Label Target", "編輯區, 空白", "edit, blank")]
    public void The_documents_quote_what_NVDA_says(string title, string zh, string en)
    {
        Assert.Equal(zh, Said(Case(title), "zh"));
        Assert.Equal(en, Said(Case(title), "en"));
    }
}
