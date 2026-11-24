using System.Text;

namespace XamlA11y.Core.Analysis;

/// <summary>
/// 把註解（與字串）換成空白，位置不動。讀程式碼的判斷只看真的程式碼：
/// 註解掉的 <c>// save.Content = "x"</c> 不是設內容，<c>// not DragMove here</c> 不是拖視窗。
/// </summary>
internal static class CSharp
{
    public static string Strip(string code, bool keepStrings, bool vb = false)
    {
        var sb = new StringBuilder(code);
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (vb)
            {
                if (c == '"') i = Blank(sb, i, EndOfVbString(code, i), keepStrings);
                else if (c == '\'') i = Blank(sb, i, EndOfLine(code, i), false);
                continue;
            }
            if (c == '/' && next == '/') i = Blank(sb, i, EndOfLine(code, i), false);
            else if (c == '/' && next == '*')
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = Blank(sb, i, end < 0 ? code.Length : end + 2, false);
            }
            else if (c == '"')
            {
                var quotes = 1;
                while (i + quotes < code.Length && code[i + quotes] == '"') quotes++;
                if (quotes >= 3)
                {
                    // 原始字串 """…"""
                    var end = code.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                    i = Blank(sb, i, end < 0 ? code.Length : end + quotes, keepStrings);
                }
                else
                {
                    var verbatim = i > 0 && (code[i - 1] == '@' || (code[i - 1] == '$' && i > 1 && code[i - 2] == '@'));
                    i = Blank(sb, i, EndOfString(code, i, verbatim), keepStrings);
                }
            }
            else if (c == '\'')
            {
                var end = i + 1;
                while (end < code.Length && code[end] != '\'' && code[end] != '\n') end += code[end] == '\\' ? 2 : 1;
                i = Blank(sb, i, Math.Min(end + 1, code.Length), keepStrings);
            }
        }
        return sb.ToString();
    }

    private static int Blank(StringBuilder sb, int from, int to, bool keep)
    {
        if (!keep)
            for (var k = from; k < to && k < sb.Length; k++)
                if (sb[k] is not ('\n' or '\r')) sb[k] = ' ';
        return Math.Max(from, to - 1);
    }

    private static int EndOfLine(string code, int i)
    {
        var end = code.IndexOf('\n', i);
        return end < 0 ? code.Length : end;
    }

    private static int EndOfString(string code, int i, bool verbatim)
    {
        for (var k = i + 1; k < code.Length; k++)
        {
            if (code[k] == '"')
            {
                if (!verbatim) return k + 1;
                if (k + 1 < code.Length && code[k + 1] == '"') { k++; continue; }
                return k + 1;
            }
            if (verbatim) continue;
            if (code[k] == '\\') k++;
            else if (code[k] == '\n') return k;       // 沒收尾的字串：到行尾
        }
        return code.Length;
    }

    private static int EndOfVbString(string code, int i)
    {
        for (var k = i + 1; k < code.Length; k++)
        {
            if (code[k] == '\n') return k;
            if (code[k] != '"') continue;
            if (k + 1 < code.Length && code[k + 1] == '"') { k++; continue; }
            return k + 1;
        }
        return code.Length;
    }
}
