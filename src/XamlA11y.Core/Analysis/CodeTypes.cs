using System.Text.RegularExpressions;

namespace XamlA11y.Core.Analysis;

/// <summary>念出來是什麼：看得懂的字、型別名、不知道。</summary>
public enum Reading { Unknown, Text, TypeName }

/// <summary>
/// 專案 C# 的型別與集合屬性，只為了兩個問題：清單的項目念出來是什麼（有沒有覆寫 ToString），自訂控制項有沒有動聚焦。
/// 不是編譯器：同名的型別、同名的屬性要**全部**答案一樣才回答，其他一律不知道。
/// </summary>
public sealed partial class CodeTypes
{
    private sealed class Info
    {
        public required string Kind { get; init; }
        public HashSet<string> Bases { get; } = new(StringComparer.Ordinal);
        public List<string> RawBases { get; } = [];
        public bool OverridesToString { get; set; }
        public bool TouchesFocus { get; set; }
    }

    /// <summary>簡單名字 → 型別（同名、partial 併在一起；種類不一樣的當不知道）。</summary>
    private readonly Dictionary<string, Info> _types = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);

    /// <summary>屬性名 → 宣告的型別（原文）。</summary>
    private readonly Dictionary<string, HashSet<string>> _properties = new(StringComparer.Ordinal);

    /// <summary>讀一個檔（註解與字串已經換成空白）。</summary>
    internal void Read(string code)
    {
        var close = Braces(code);
        var ranges = new List<(int From, int To, Info Info)>();
        foreach (Match m in TypeRegex().Matches(code))
        {
            var name = m.Groups["name"].Value;
            var kind = m.Groups["kind"].Value is var k && k.StartsWith("record", StringComparison.Ordinal) ? "record" : k;
            if (!_types.TryGetValue(name, out var info)) _types[name] = info = new Info { Kind = kind };
            else if (info.Kind != kind) { _ambiguous.Add(name); continue; }
            foreach (var b in Split(m.Groups["bases"].Value))
            {
                info.RawBases.Add(b);
                info.Bases.Add(Simple(b));
            }
            var open = m.Index + m.Length;
            while (open < code.Length && code[open] is not ('{' or ';')) open++;
            if (open < code.Length && code[open] == '{' && close.TryGetValue(open, out var end)) ranges.Add((open, end, info));
        }

        // 一個標記只屬於最裡面的那個型別；一次掃完（每一層都重掃內容的話，巢狀七萬層要跑半分鐘）
        if (ranges.Count > 0)
        {
            var marks = new List<(int At, bool Text)>();
            if (code.Contains("ToString", StringComparison.Ordinal)) marks.AddRange(ToStringRegex().Matches(code).Select(m => (m.Index, true)));
            marks.AddRange(FocusRegex().Matches(code).Select(m => (m.Index, false)));
            marks.Sort((a, b) => a.At.CompareTo(b.At));
            var stack = new Stack<(int From, int To, Info Info)>();
            var next = 0;
            foreach (var (at, text) in marks)
            {
                while (next < ranges.Count && ranges[next].From <= at)
                {
                    while (stack.Count > 0 && stack.Peek().To < ranges[next].From) stack.Pop();
                    stack.Push(ranges[next++]);
                }
                while (stack.Count > 0 && stack.Peek().To < at) stack.Pop();
                if (stack.Count == 0) continue;
                if (text) stack.Peek().Info.OverridesToString = true;
                else stack.Peek().Info.TouchesFocus = true;
            }
            // Fody 的 [ToString]：編譯時才加上去
            if (code.Contains("[ToString", StringComparison.Ordinal)) foreach (var r in ranges) r.Info.OverridesToString = true;
        }

        foreach (Match m in PropertyRegex().Matches(code)) Add(m.Groups["name"].Value, m.Groups["type"].Value);
        // CommunityToolkit：[ObservableProperty] private ObservableCollection<Note> _notes; → Notes
        if (code.Contains("ObservableProperty", StringComparison.Ordinal))
            foreach (Match m in ObservableRegex().Matches(code))
            {
                var field = m.Groups["name"].Value.TrimStart('_');
                if (field.StartsWith("m_", StringComparison.Ordinal)) field = field[2..];
                if (field.Length > 0) Add(char.ToUpperInvariant(field[0]) + field[1..], m.Groups["type"].Value);
            }

        void Add(string name, string type)
        {
            if (!_properties.TryGetValue(name, out var set)) _properties[name] = set = new HashSet<string>(StringComparer.Ordinal);
            if (set.Count < 64) set.Add(type.Replace(" ", "", StringComparison.Ordinal));
        }
    }

    /// <summary>每個左大括號配到哪一個右大括號。</summary>
    private static Dictionary<int, int> Braces(string code)
    {
        var map = new Dictionary<int, int>();
        var stack = new Stack<int>();
        for (var i = 0; i < code.Length; i++)
            if (code[i] == '{') stack.Push(i);
            else if (code[i] == '}' && stack.Count > 0) map[stack.Pop()] = i;
        return map;
    }

    /// <summary>逗號分開，不切泛型裡面的。</summary>
    private static IEnumerable<string> Split(string list)
    {
        int depth = 0, start = 0;
        for (var i = 0; i <= list.Length; i++)
        {
            if (i < list.Length && list[i] is '<' or '(' or '[') depth++;
            else if (i < list.Length && list[i] is '>' or ')' or ']') depth--;
            else if (i == list.Length || list[i] == ',' && depth == 0)
            {
                var part = list[start..i];
                var where = part.IndexOf(" where ", StringComparison.Ordinal);
                if (where >= 0) part = part[..where];
                if (part.Trim() is { Length: > 0 } t) yield return t;
                start = i + 1;
            }
        }
    }

    /// <summary>「System.Collections.Generic.List&lt;Note&gt;?」→「List」。</summary>
    private static string Simple(string type)
    {
        var t = type.Trim().TrimEnd('?');
        var cut = t.IndexOfAny(['<', '(', '[']);
        if (cut >= 0) t = t[..cut];
        return t[(t.LastIndexOfAny(['.', ':']) + 1)..].Trim();
    }

    private static readonly HashSet<string> Collections = new(StringComparer.Ordinal)
    {
        "ObservableCollection", "ReadOnlyObservableCollection", "Collection", "ReadOnlyCollection", "List", "IList", "IEnumerable", "ICollection", "IReadOnlyList", "IReadOnlyCollection",
        "BindingList", "ObservableRangeCollection", "BindableCollection", "IObservableCollection", "HashSet", "LinkedList", "ImmutableArray", "ImmutableList",
    };

    private readonly Dictionary<string, string?> _elements = new(StringComparer.Ordinal);

    /// <summary>集合型別的項目型別；不是認得的集合回 null。記住答案：沒記住的話步數是基底數的次方（40 KB 的 .cs 跑好幾分鐘）。</summary>
    private string? ElementOf(string type, int depth = 0)
    {
        if (_elements.TryGetValue(type, out var known)) return known;
        _elements[type] = null;                    // 繞回自己：不是集合
        return _elements[type] = Element(type, depth);
    }

    private string? Element(string type, int depth)
    {
        var t = type.TrimEnd('?');
        if (t.EndsWith("[]", StringComparison.Ordinal)) return t[..^2];
        var open = t.IndexOf('<');
        if (open > 0 && t.EndsWith('>') && Collections.Contains(Simple(t)))
        {
            var inner = t[(open + 1)..^1];
            return inner.Contains(',', StringComparison.Ordinal) ? null : inner;
        }
        // class NoteList : ObservableCollection<Note>
        return open < 0 && depth < 4 && _types.TryGetValue(Simple(t), out var info) && !_ambiguous.Contains(Simple(t)) && info.Kind == "class"
            ? info.RawBases.Select(b => ElementOf(b.Replace(" ", "", StringComparison.Ordinal), depth + 1)).FirstOrDefault(x => x is not null)
            : null;
    }

    /// <summary>這個名字的屬性（整個專案）是什麼的集合：(項目型別, 念出來是什麼)。同名的屬性答案不一樣就是不知道。</summary>
    public (string? Type, Reading Reading) ItemsOf(string property)
    {
        if (_items.TryGetValue(property, out var known)) return known;
        return _items[property] = Items(property);
    }

    private readonly Dictionary<string, (string?, Reading)> _items = new(StringComparer.Ordinal);

    private (string? Type, Reading Reading) Items(string property)
    {
        if (!_properties.TryGetValue(property, out var types)) return (null, Reading.Unknown);
        string? first = null;
        Reading? answer = null;
        foreach (var type in types)
        {
            if (ElementOf(type) is not { } element) return (null, Reading.Unknown);
            var one = ReadingOf(element);
            if (one == Reading.Unknown || answer is { } a && a != one) return (null, Reading.Unknown);
            answer = one;
            first ??= Simple(element);
        }
        return (types.Count == 1 ? first : null, answer ?? Reading.Unknown);
    }

    private static readonly HashSet<string> Readable = new(StringComparer.Ordinal)
    {
        "string", "String", "char", "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "float", "double", "decimal", "bool", "Int32", "Int64", "Double", "Boolean",
        "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Guid", "Uri", "Version", "CultureInfo", "FileInfo", "DirectoryInfo", "IPAddress", "BigInteger",
    };

    /// <summary>沒有覆寫 ToString 的外部基底（查過原始碼）：MVVM 框架的基底類別與 object。</summary>
    private static readonly HashSet<string> Silent = new(StringComparer.Ordinal)
    {
        "object", "Object", "ObservableObject", "ObservableRecipient", "ObservableValidator", "BindableBase", "PropertyChangedBase", "ReactiveObject", "DependencyObject", "MarshalByRefObject", "NotifyPropertyChangedBase",
    };

    /// <summary>這個型別的物件當項目時，名稱念出來是什麼：看 ToString 有沒有覆寫。</summary>
    public Reading ReadingOf(string type)
    {
        if (_readings.TryGetValue(type, out var known)) return known;
        var r = ReadingOf(type, 0);
        // 集合的宣告是基底，裡面放的是子類別：有一個子類別覆寫了就不能說「每一項」
        if (r == Reading.TypeName && AnyDerivedOverrides(Simple(type))) r = Reading.Unknown;
        return _readings[type] = r;
    }

    private readonly Dictionary<string, Reading> _readings = new(StringComparer.Ordinal);
    private Dictionary<string, List<string>>? _derived;

    private bool AnyDerivedOverrides(string name)
    {
        if (_derived is null)
        {
            _derived = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (type, info) in _types)
                foreach (var b in info.Bases)
                {
                    if (!_derived.TryGetValue(b, out var list)) _derived[b] = list = [];
                    list.Add(type);
                }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal) { name };
        var queue = new Queue<string>([name]);
        while (queue.TryDequeue(out var n))
            foreach (var d in _derived.GetValueOrDefault(n, []))
                if (seen.Add(d))
                {
                    if (_types[d].OverridesToString || _ambiguous.Contains(d)) return true;
                    queue.Enqueue(d);
                }
        return false;
    }

    private Reading ReadingOf(string type, int depth)
    {
        var name = Simple(type);
        // 巢狀泛型、值組：不猜
        if (type.Contains('<', StringComparison.Ordinal) || type.Contains('(', StringComparison.Ordinal) || depth > 8) return Reading.Unknown;
        if (!_types.TryGetValue(name, out var info)) return Readable.Contains(name) ? Reading.Text : Reading.Unknown;
        if (_ambiguous.Contains(name)) return Reading.Unknown;
        switch (info.Kind)
        {
            case "enum" or "record": return Reading.Text;      // record 的 ToString 列出屬性：囉唆，但念得出來
            case "interface": return Reading.Unknown;
        }
        if (info.OverridesToString) return Reading.Text;
        foreach (var b in info.Bases)
        {
            if (b == name) return Reading.Unknown;             // 跟外部的基底同名
            if (_types.TryGetValue(b, out var known))
            {
                if (known.Kind == "interface") continue;
                if (ReadingOf(b, depth + 1) is var r && r != Reading.TypeName) return r;
            }
            else if (!Silent.Contains(b) && !IsInterface(b)) return Reading.Unknown;
        }
        return Reading.TypeName;
    }

    private static bool IsInterface(string name) => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);

    /// <summary>這個類別（或它在專案裡的基底）的程式碼動到 Focusable 或 IsTabStop。</summary>
    public bool TouchesFocus(string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([name]);
        while (queue.TryDequeue(out var n))
        {
            if (!seen.Add(n) || !_types.TryGetValue(n, out var info)) continue;
            if (info.TouchesFocus) return true;
            foreach (var b in info.Bases) queue.Enqueue(b);
        }
        return false;
    }

    [GeneratedRegex(@"\b(?<kind>class|record\s{1,20}struct|record\s{1,20}class|record|struct|enum|interface)\s{1,50}(?<name>[A-Za-z_]\w{0,200})\s{0,50}(?:<[^>{;]{0,200}>)?\s{0,50}(?:\([^){;]{0,2000}\)\s{0,50})?(?::(?<bases>[^{;]{1,1000}))?(?=\s{0,50}(?:[{;]|where\b))", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex TypeRegex();

    /// <summary>修飾詞的順序不限：override public string ToString()、public override global::System.String ToString()。</summary>
    [GeneratedRegex(@"\boverride\b[^;{}()=]{1,80}?\bToString\s{0,20}\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ToStringRegex();

    /// <summary>動到能不能聚焦、把焦點轉給別人、視窗不拿啟用。</summary>
    [GeneratedRegex(@"\b(?:Focusable|IsTabStop|ShowActivated|OnGotFocus|OnGotKeyboardFocus|OnPreviewGotKeyboardFocus|GotFocusEvent|GotKeyboardFocusEvent|PreviewGotKeyboardFocusEvent)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex FocusRegex();

    private const string TypePattern = @"(?<type>[A-Za-z_][\w.]{0,200}(?:<[\w.,<>?\[\] ]{1,200}>)?(?:\[\])?\??)";

    [GeneratedRegex(@"\b(?:public|internal|protected|private)\s{1,20}(?:(?:static|virtual|override|abstract|new|required|sealed|readonly|partial)\s{1,20}){0,4}" + TypePattern + @"\s{1,20}(?<name>[A-Za-z_]\w{0,100})\s{0,50}(?=\{|=>|=)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex PropertyRegex();

    [GeneratedRegex(@"\[[^\]\r\n]{0,100}ObservableProperty[^\]\r\n]{0,100}\](?:\s{0,50}\[[^\]\r\n]{0,200}\]){0,5}\s{0,50}(?:public|internal|protected|private)\s{1,20}(?:(?:readonly|partial|static)\s{1,20}){0,2}" + TypePattern + @"\s{1,20}(?<name>[A-Za-z_]\w{0,100})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ObservableRegex();
}
