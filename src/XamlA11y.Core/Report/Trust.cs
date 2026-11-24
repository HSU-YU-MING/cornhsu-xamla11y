using System.Globalization;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>結果能不能信（exit 3）。沒看到不能當成沒問題。</summary>
public static class Trust
{
    /// <summary>不可信的原因；可信回 null。</summary>
    public static string? Why(AuditResult r, double minCoverage, Lang lang)
    {
        var inv = CultureInfo.InvariantCulture;
        string Say(string en, string zh) => lang.T(en + " — result is not trustworthy", zh + "，結果不可信");
        if (r.Files == 0) return Say("no .xaml files found", "找不到 .xaml 檔");
        if (r.Foreign.Count > 0 && r.Foreign.Count + r.ParseErrors.Count == r.Files) return Say($"none of the {r.Files} XAML file(s) is WPF", $"{r.Files} 個 XAML 檔都不是 WPF");
        if (r.ParseErrors.Count == r.Files) return Say("no XAML file could be parsed", "XAML 檔都讀不懂");
        var wpf = r.Files - r.Foreign.Count;
        if (minCoverage > 0 && 100.0 * (wpf - r.ParseErrors.Count) / wpf < minCoverage)
            return Say(string.Create(inv, $"{r.ParseErrors.Count} of {wpf} XAML file(s) could not be parsed, above what --min-coverage {minCoverage:0.#}% allows"), string.Create(inv, $"{wpf} 個 XAML 檔有 {r.ParseErrors.Count} 個讀不懂，超過 --min-coverage {minCoverage:0.#}% 容許的"));
        // 全部關掉：沒有東西被檢查，不是通過
        if (!r.On.Any()) return Say("every rule is turned off — nothing was checked", "每一種都關掉了，等於沒檢查");
        if (r.Candidates == 0) return Say("no interactive elements found", "沒有互動元件");
        // 全部卡住才是「沒看到」；全部都是執行期的資料（名稱綁定）不是專案改得了的，也不是工具漏看
        if (minCoverage > 0 && r.Judged == 0 && r.Blocked > 0) return Say("not one element could be judged", "沒有一個元件判得出來");
        return r.Resolution < minCoverage
            ? Say(string.Create(inv, $"resolution {r.Resolution:F1}% is below --min-coverage {minCoverage:0.#}%"), string.Create(inv, $"可解析率 {r.Resolution:F1}% 低於 --min-coverage {minCoverage:0.#}%"))
            : null;
    }
}
