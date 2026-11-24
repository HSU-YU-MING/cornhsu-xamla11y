using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlA11y.Core.Fix;

/// <summary>
/// XAML 檔的原文。只插屬性，其他一個字都不動（縮排、換行、註解、編碼、BOM）。
/// 讀進來再寫出去不是同樣位元組的檔案不碰。
/// </summary>
public sealed partial class XamlSource
{
    private readonly Encoding _encoding;
    private readonly int _preamble;
    private readonly List<int> _lines = [0];
    private readonly SortedDictionary<int, List<string>> _inserts = [];

    public string Text { get; }

    private XamlSource(string text, Encoding encoding, int preamble)
    {
        (Text, _encoding, _preamble) = (text, encoding, preamble);
        // XmlReader 的行：\r\n、\n、單獨的 \r 都是一行
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n' || text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) _lines.Add(i + 1);
    }

    /// <summary>讀不回原樣的（編碼猜錯、壞掉的位元組）回 null。</summary>
    public static XamlSource? Read(byte[] bytes)
    {
        var (encoding, preamble) = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => ((Encoding)new UTF8Encoding(false), 3),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, false), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, false), 2),
            _ => (Declared(bytes) ?? new UTF8Encoding(false), 0),
        };
        // BOM 說一種、宣告說另一種：誰對不猜
        if (preamble == 3 && Declared(bytes[3..]) is { } declared && declared.CodePage != encoding.CodePage) return null;
        var text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
        return encoding.GetBytes(text).AsSpan().SequenceEqual(bytes.AsSpan(preamble)) ? new XamlSource(text, encoding, preamble) : null;
    }

    private static Encoding? Declared(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(200, bytes.Length));
        if (EncodingRegex().Match(head) is not { Success: true } m) return null;
        try { return Encoding.GetEncoding(m.Groups["e"].Value); }
        catch (ArgumentException) { return null; }
    }

    public bool CanInsert(XElement e) => TagOf(e) is not null;

    /// <summary>在這個元素的開始標籤加屬性。找不到位置（行號對不上）回 false。</summary>
    public bool Insert(XElement e, string attribute, string value)
    {
        if (TagOf(e) is not { } tag) return false;
        var escaped = value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
        var separator = tag.NewLine is { } line ? line + tag.Indent : " ";
        if (!_inserts.TryGetValue(tag.At, out var list)) _inserts[tag.At] = list = [];
        list.Add($"{separator}{attribute}=\"{escaped}\"");
        return true;
    }

    public bool Changed => _inserts.Count > 0;

    public string Result()
    {
        var sb = new StringBuilder(Text.Length + 200);
        var from = 0;
        foreach (var (at, list) in _inserts)
        {
            sb.Append(Text, from, at - from);
            foreach (var s in list) sb.Append(s);
            from = at;
        }
        return sb.Append(Text, from, Text.Length - from).ToString();
    }

    /// <summary>這個編碼裝不下的字（宣告 us-ascii 的檔案加中文）會變成「?」：寫不回去就回 null。</summary>
    public byte[]? ToBytes(byte[] original)
    {
        var text = Result();
        var body = _encoding.GetBytes(text);
        return _encoding.GetString(body) == text ? [.. original.AsSpan(0, _preamble), .. body] : null;
    }

    /// <summary>插在哪裡（最後一個屬性的後面）、屬性是一行一個的話用什麼換行與縮排。</summary>
    private (int At, string? NewLine, string Indent)? TagOf(XElement e)
    {
        if (e is not IXmlLineInfo info || !info.HasLineInfo() || info.LineNumber > _lines.Count) return null;
        var start = _lines[info.LineNumber - 1] + info.LinePosition - 1;       // 元素名稱的第一個字
        if (start <= 0 || start >= Text.Length || Text[start - 1] != '<') return null;
        var end = start;
        while (end < Text.Length && !char.IsWhiteSpace(Text[end]) && Text[end] is not ('>' or '/')) end++;
        var name = Text[start..end];
        if (name[(name.IndexOf(':') + 1)..] != e.Name.LocalName) return null;

        var at = end;                   // 最後一個屬性的結尾
        var firstAttribute = -1;
        var lastAttribute = -1;
        for (var i = end; i < Text.Length;)
        {
            var c = Text[i];
            if (c == '>') break;
            if (c == '/' && i + 1 < Text.Length && Text[i + 1] == '>') break;
            if (c is '"' or '\'')
            {
                var close = Text.IndexOf(c, i + 1);
                if (close < 0) return null;
                i = at = close + 1;
                continue;
            }
            if (!char.IsWhiteSpace(c) && char.IsWhiteSpace(Text[i - 1]) && c != '=' && PreviousVisible(i) != '=')
            {
                if (firstAttribute < 0) firstAttribute = i;
                lastAttribute = i;
            }
            i++;
        }
        if (lastAttribute < 0) return (at, null, "");
        var line = LineStart(lastAttribute);
        if (line <= start) return (at, null, "");                               // 最後一個屬性跟元素名稱同一行
        var indent = Text[line..lastAttribute];
        if (indent.Trim().Length > 0) indent = Text[line..(line + Text[line..].TakeWhile(x => x is ' ' or '\t').Count())];
        var newLine = Text[line - 1] == '\n' && line >= 2 && Text[line - 2] == '\r' ? "\r\n" : Text[line - 1].ToString();
        return (at, newLine, indent);
    }

    private char PreviousVisible(int i)
    {
        for (i--; i >= 0; i--)
            if (!char.IsWhiteSpace(Text[i])) return Text[i];
        return '\0';
    }

    private int LineStart(int i)
    {
        while (i > 0 && Text[i - 1] is not ('\n' or '\r')) i--;
        return i;
    }

    [GeneratedRegex(@"<\?xml[^>]{0,100}\bencoding\s{0,5}=\s{0,5}[""'](?<e>[A-Za-z0-9_.\-]{1,40})[""']", RegexOptions.CultureInvariant)]
    private static partial Regex EncodingRegex();
}
