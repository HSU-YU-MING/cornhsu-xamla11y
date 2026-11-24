using System.Globalization;
using System.Text;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

public static class Text
{
    public static string Slug(Severity s) => s switch { Severity.Fail => "fail", Severity.Warn => "warn", Severity.Info => "info", _ => "off" };

    /// <summary>
    /// 看不見的：格式字元（雙向控制會改掉別的文字的順序；標籤字元 U+E00xx 在 BMP 外，要一個碼位一個碼位看）、行分隔、沒有字形的諺文填充。
    /// </summary>
    private static bool Hidden(Rune r) =>
        Rune.GetUnicodeCategory(r) == UnicodeCategory.Format || r.Value is 0x0085 or 0x2028 or 0x2029 or 0x115F or 0x1160 or 0x3164 or 0xFFA0;

    private static string Escaped(Rune r) => r.IsBmp ? $"\\u{r.Value:x4}" : $"\\U{r.Value:x8}";

    /// <summary>看不見的字元寫成 \uXXXX。表情用的零寬連字 U+200D 留著。落單的代理（XML 讀不進來，保險）變成 U+FFFD。</summary>
    public static string Visible(string s)
    {
        if (s.All(c => c is >= ' ' and < (char)0x7F)) return s;
        var sb = new StringBuilder(s.Length + 8);
        foreach (var r in s.EnumerateRunes()) sb.Append(Hidden(r) && r.Value != 0x200D ? Escaped(r) : r.ToString());
        return sb.ToString();
    }

    /// <summary>
    /// 印到 console 的字串：控制字元逃掉（MSBuild 與 CI 逐行解析，訊息裡夾一個換行就多一筆假的診斷），
    /// Azure Pipelines／GitHub Actions 的舊指令格式（##vso[、##[）拆開。文字來自使用者的 XAML，是不可信的輸入。
    /// </summary>
    public static string Safe(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var r in s.EnumerateRunes())
            sb.Append(r.Value switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\t",
                // 看不見的格式字元（軟連字號、雙向控制、零寬）：夾在 ## 與 vso[ 之間可以躲過比對，順序也會被改掉
                _ when Rune.IsControl(r) || Hidden(r) => Escaped(r),
                _ => r.ToString(),
            });
        return sb.ToString().Replace("##", "#\\u0023", StringComparison.Ordinal);
    }
}
