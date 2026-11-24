using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>
/// OpenACR 草稿（<c>--openacr</c>）：美國 GSA 的機器可讀合規報告格式。**只寫找到問題的準則**，等級一律是 partially-supports ——
/// 「有一部分不符合」是這支真的知道的；沒找到問題不是 supports，所以不寫。已知債（baseline）也寫進去：它還是問題。
/// 公司、聯絡方式、其他準則要人填。
/// </summary>
public static class OpenAcrReport
{
    private const int SitesPerKind = 10;

    /// <summary>選的標準 → OpenACR 的目錄。</summary>
    public static string CatalogOf(Standard s) => s.Id switch
    {
        "wcag21" => "2.4-edition-wcag-2.1-en",
        "wcag20" or "section508" => "2.5-edition-wcag-2.0-508-en",
        "en301549" => "2.4-edition-wcag-2.1-508-eu-en",
        "en301549-v4" => "2.5-edition-wcag-2.2-508-eu-en",
        _ => "2.5-edition-wcag-2.2-en",
    };

    public static string Create(AuditResult r, string product, string toolVersion, DateOnly date)
    {
        var sb = new StringBuilder();
        var fails = r.Findings.Where(f => f.Severity == Severity.Fail).ToList();
        sb.Append("# Draft written by XamlA11y. It only lists criteria with problems of severity fail (warnings are not in it). Fill in the company, the contact,\n");
        sb.Append("# the criteria that are missing and the ones a person has to judge before publishing it.\n");
        sb.Append($"title: {Q($"{product} Accessibility Conformance Report (draft)")}\n");
        sb.Append($"product:\n  name: {Q(product)}\n");
        sb.Append("author:\n  email: \"you@example.com\"\n");
        sb.Append($"report_date: {Q(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}\n");
        sb.Append("version: 1\n");
        sb.Append($"notes: {Q("DRAFT. Not a conformance claim: only criteria with problems of severity fail found by a static check of the XAML are listed; nothing is said about the others.")}\n");
        sb.Append($"evaluation_methods_used: {Q(string.Create(CultureInfo.InvariantCulture, $"XamlA11y {toolVersion}, static audit of {r.Files} XAML file(s) against {r.Scope.Title}. {r.Judged} places judged, {r.Blocked} unknown, {r.NotJudged} left out (known only at run time or not the job of the check)."))}\n");
        sb.Append($"catalog: {Q(CatalogOf(r.Scope.Standard))}\n");

        var criteria = fails.SelectMany(f => r.Scope.CriteriaOf(f.Kind)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var empty = fails.Where(f => f.NameIsEmpty).ToList();
        var chapters = new List<(string Id, List<(string Num, List<Finding> Findings)>)>
        {
            ("success_criteria_level_a", [.. criteria.Where(c => Wcag.Level(c) == "A").Select(c => (c, With(c)))]),
            ("success_criteria_level_aa", [.. criteria.Where(c => Wcag.Level(c) == "AA").Select(c => (c, With(c)))]),
        };
        if (CatalogOf(r.Scope.Standard).Contains("508", StringComparison.Ordinal) && empty.Count > 0) chapters.Add(("software", [("502.3.1", empty)]));
        if (CatalogOf(r.Scope.Standard).Contains("-eu-", StringComparison.Ordinal) && empty.Count > 0) chapters.Add(("en_301_549_software", [("11.5.2.5", empty)]));

        if (chapters.All(c => c.Item2.Count == 0)) return sb.Append("chapters: {}\n").ToString();
        sb.Append("chapters:\n");
        foreach (var (id, list) in chapters.Where(c => c.Item2.Count > 0))
        {
            sb.Append($"  {id}:\n    criteria:\n");
            foreach (var (num, findings) in list)
            {
                sb.Append($"      - num: {Q(num)}\n        components:\n          - name: {(id.EndsWith("software", StringComparison.Ordinal) ? "none" : "software")}\n            adherence:\n              level: partially-supports\n");
                sb.Append($"              notes: {Q(Notes(findings))}\n");
            }
        }
        return sb.ToString();

        List<Finding> With(string criterion) => [.. fails.Where(f => r.Scope.CriteriaOf(f.Kind).Contains(criterion))];
    }

    private static string Notes(List<Finding> findings) => string.Join(" ", findings.GroupBy(f => f.Kind).Select(g =>
        string.Create(CultureInfo.InvariantCulture, $"{g.Key.Title} ({g.Key.Slug}): {g.Count()} place(s), {string.Join(", ", g.Take(SitesPerKind).Select(f => Text.Visible(f.Site)))}{(g.Count() > SitesPerKind ? ", …" : "")}.")));

    /// <summary>JSON 的字串就是 YAML 的雙引號字串（路徑、名字來自 repo，一律這樣寫）。</summary>
    private static string Q(string s) => JsonSerializer.Serialize(Text.Visible(s), Options);

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
