using System.Text;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// name-differs：畫面上寫一句、名稱是另一句（WCAG 2.5.3）。用說的操作的人照畫面念，程式聽不懂。
/// 名稱要**包含**畫面的字（大小寫、標點、快速鍵的底線不算；WCAG 的說明）。候選只有兩邊都看得到是什麼字的。
/// 符號（「B」「×」）、一整段話、括號裡的補充不算標籤。
/// </summary>
internal static class LabelRule
{
    private const int LongestLabel = 60;

    public static void Check(Context ctx, XElement e)
    {
        if (e.Parent is null || ctx.Project.ControlOf(e) is not { } control || !Wpf.Interactive.Contains(control)) return;
        if (ctx.Names.NameAndShown(e, control) is not { } pair || pair.Shown.Length > LongestLabel) return;
        var shown = Normalize(WithoutParentheses(pair.Shown));
        if (shown.Count(char.IsLetter) < 2) return;
        if (Covers(pair.Name, WithoutParentheses(pair.Shown))) { ctx.Pass(Checks.VisibleText, e); return; }
        ctx.Report(Kinds.NameDiffers, e, $"<{e.Name.LocalName}> shows \"{Context.Show(pair.Shown)}\" but its accessible name is \"{Context.Show(pair.Name)}\", which does not contain it");
    }

    /// <summary>連字號當空格、當沒有，都試：E-mail 與 Email、Set-up 與 Set up 是同一個字。</summary>
    private static bool Covers(string name, string shown)
    {
        string[] names = [Normalize(name), Normalize(name, joinHyphens: true)], labels = [Normalize(shown), Normalize(shown, joinHyphens: true)];
        return names.Any(n => labels.Any(l => Contains(n, l)));
    }

    /// <summary>整個字才算：「Cancel」不包含「Can」。中文、日文沒有空格，照字串比。</summary>
    internal static bool Contains(string name, string shown)
    {
        if (shown.Any(c => c >= 0x2E80)) return name.Replace(" ", "", StringComparison.Ordinal).Contains(shown.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        return $" {name} ".Contains($" {shown} ", StringComparison.Ordinal);
    }

    /// <summary>小寫，字母與數字以外的都當空格，連續的空格收成一個。</summary>
    internal static string Normalize(string s, bool joinHyphens = false)
    {
        if (joinHyphens) s = s.Replace("-", "", StringComparison.Ordinal).Replace("‐", "", StringComparison.Ordinal);
        var sb = new StringBuilder(s.Length);
        // 全形字母自己換：沒有 ICU 的機器上 string.Normalize 什麼都不做，同一個專案兩台機器答案不能不一樣
        var narrow = string.Concat(s.Replace("&", " and ", StringComparison.Ordinal).Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c));
        foreach (var r in narrow.EnumerateRunes().Where(r => Rune.GetUnicodeCategory(r) != System.Globalization.UnicodeCategory.Format))
            if (Rune.IsLetterOrDigit(r) || Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark) sb.Append(Rune.ToLowerInvariant(r).ToString());
            else if (r.Value is '\'' or '’') continue;
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        return sb.ToString().Trim();
    }

    private static string WithoutParentheses(string s)
    {
        var sb = new StringBuilder(s.Length);
        var depth = 0;
        foreach (var c in s)
        {
            if (c is '(' or '（' or '[') depth++;
            else if (c is ')' or '）' or ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0) sb.Append(c);
        }
        return sb.ToString();
    }
}
