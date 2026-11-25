using System.Text.Json;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>SARIF 2.1.0。依選的標準標條文。info 不輸出：不是缺陷，灌進 Security 分頁是噪音（JSON 裡照樣有）。</summary>
public static class SarifReport
{
    public static string Create(IReadOnlyList<Finding> reported, string toolVersion, Scope? chosen = null)
    {
        var scope = chosen ?? Scope.Default;
        var text = JsonSerializer.Serialize(new
        {
            version = "2.1.0",
            schema__ = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "XamlA11y",
                            informationUri = "https://cornhsu.com/xaml-audit/xamla11y",
                            version = toolVersion,
                            rules = Kinds.All.Select(k => new
                            {
                                id = k.Slug,
                                name = string.Concat(k.Slug.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..])),
                                shortDescription = new { text = k.Title },
                                fullDescription = new { text = k.Help },
                                helpUri = k.Uri(),
                                help = new { text = $"{k.Help} ({scope.Title}: {scope.WcagOf(k)})" },
                                defaultConfiguration = new { level = Level(k.Default) },
                                properties = new { tags = scope.CriteriaOf(k).Select(c => "WCAG " + c).Prepend("accessibility"), clauses = JsonReport.Clauses(k, k.NameIsEmpty) },
                            }),
                        },
                    },
                    results = reported.Where(f => f.Severity >= Severity.Warn).Select(f => new
                    {
                        ruleId = f.Kind.Slug,
                        level = Level(f.Severity),
                        // SARIF 的純文字訊息裡 [文字](網址) 是連結，訊息夾了 repo 的字：方括號跳脫
                        message = new { text = f.Message.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal) },
                        locations = new[]
                        {
                            new
                            {
                                physicalLocation = new
                                {
                                    artifactLocation = new { uri = Uri(f.File) },
                                    region = new { startLine = Math.Max(f.Line, 1) },
                                },
                            },
                        },
                        partialFingerprints = new Dictionary<string, string> { ["xamla11yKey/v1"] = f.BaselineKey },
                    }),
                },
            },
        }, Json.Options);
        // C# 的匿名型別不能有 $schema 這個名字
        return text.Replace("\"schema__\"", "\"$schema\"", StringComparison.Ordinal) + "\n";
    }

    /// <summary>SARIF 的 uri 要是合法的 URI：資料夾名有空白或中文時逐段編碼（斜線留著）。</summary>
    private static string Uri(string file) => string.Join('/', file.Split('/').Select(System.Uri.EscapeDataString));

    private static string Level(Severity s) => s switch { Severity.Fail => "error", Severity.Warn => "warning", _ => "note" };
}
