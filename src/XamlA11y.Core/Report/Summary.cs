using System.Globalization;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

public static class Summary
{
    /// <summary>四組各幾筆（沒有的不列）：先講是哪一類的人、哪一種操作碰到問題，再講細節。</summary>
    public static IReadOnlyList<(string Title, int Count, string Kinds)> Groups(IReadOnlyList<Finding> findings, Lang lang) =>
        [.. Analysis.Kinds.Groups.Select(g => (lang.GroupTitle(g), findings.Count(f => f.Kind.Group == g.Id),
            string.Join(lang.T(", ", "、"), Analysis.Kinds.All.Where(k => k.Group == g.Id).Select(k => (k.Slug, N: findings.Count(f => f.Kind == k))).Where(x => x.N > 0)
                .Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Slug} {x.N}"))))).Where(x => x.Item2 > 0)];

    /// <summary>baseline 蓋住的要跟「通過」放在一起講：通過的是「沒有新的」，不是沒有問題。</summary>
    public static string? KnownDebt(IReadOnlyList<Finding>? known, int exitCode, Lang lang)
    {
        if (known is not { Count: > 0 }) return null;
        int Count(Severity s) => known.Count(f => f.Severity == s);
        var inv = CultureInfo.InvariantCulture;
        var pass = exitCode == 0 ? lang.T("Pass means no new findings. ", "通過的意思是沒有新的問題。") : "";
        return pass + lang.T(
            string.Create(inv, $"{known.Count} known problem(s) in the baseline are not fixed: {Count(Severity.Fail)} fail, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info."),
            string.Create(inv, $"baseline 裡還有 {known.Count} 個已知的問題沒修：{Count(Severity.Fail)} fail、{Count(Severity.Warn)} warn、{Count(Severity.Info)} info。"));
    }

    /// <summary>查了多少：幾條準則裡的幾條。</summary>
    public static string Coverage(Scope scope, Lang lang)
    {
        var total = Wcag.Total(scope.Standard.Wcag, scope.Level);
        var here = Kinds.All.SelectMany(scope.CriteriaOf).Distinct(StringComparer.Ordinal).Count();
        return lang.T(
            string.Create(CultureInfo.InvariantCulture, $"This checks {here} of the {total} success criteria of WCAG {scope.Standard.Wcag} {(scope.Level == "A" ? "A" : "A and AA")}, and only as far as the XAML decides them. Passing is not a claim of conformance."),
            string.Create(CultureInfo.InvariantCulture, $"WCAG {scope.Standard.Wcag} 的 {(scope.Level == "A" ? "A" : "A 與 AA")} 共 {total} 條成功準則，這裡查其中 {here} 條，而且只查到 XAML 決定得了的部分。通過不等於符合規範。"));
    }

    public static string Manual(Lang lang) => lang.T(
        "This tool cannot do these. They are the ones that go wrong most in desktop software, not the rest of WCAG.",
        "這支查不到這些。是桌面軟體最常出問題的，不是 WCAG 剩下的全部。");

    /// <summary>每一份報告都要講的：看了什麼、沒看什麼。</summary>
    public const string Scope =
        "Static: the XAML was read, nothing was run; of the code, only type and property declarations and each XAML file's own code-behind (does it set a name, a content or a key handler?). Passing is not a claim of WCAG or EAA conformance.";
}
