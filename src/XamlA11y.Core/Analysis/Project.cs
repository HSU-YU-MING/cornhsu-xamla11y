using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Analysis;

public sealed record XamlDoc(string File, string FullPath, XDocument Doc)
{
    public XElement Root => Doc.Root!;
    public override string ToString() => File;
}

/// <summary>套在專案上的外部佈景。</summary>
/// <param name="Scope">影響的範圍：檔案，或目錄（"" = 全部）。</param>
/// <param name="Source">合併的字典（pack URI）。</param>
public sealed record ThemeUse(string Name, string DeclaredIn, string Scope, string? LibraryId, string? Source);

/// <summary>掃進來的專案：文件、樣式、類別、佈景。規則只跟它問話。</summary>
public sealed partial class Project
{
    public required string Root { get; init; }
    public required IReadOnlyList<XamlDoc> Docs { get; init; }
    public required StyleIndex Styles { get; init; }
    public IReadOnlyList<string> ParseErrors { get; init; } = [];
    /// <summary>解析不了的檔案 → 原因（重複的屬性、巢狀太深、編碼）。</summary>
    public IReadOnlyDictionary<string, string> ParseWhy { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<(string File, string Dialect)> Foreign { get; init; } = [];
    public IReadOnlyList<ThemeUse> Themes { get; init; } = [];
    public int FilesScanned { get; init; }
    public ScanGaps Gaps { get; init; } = new();

    /// <summary>使用者宣告：第三方控制項照哪個 WPF 控制項算（設定檔 treatAs）。</summary>
    public IReadOnlyDictionary<string, string> TreatAs { get; init; } = new Dictionary<string, string>();

    private Dictionary<string, XamlDoc> _byClass = new(StringComparer.Ordinal);
    private Dictionary<string, ClassInfo> _classes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ownXmlns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeBehind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _versions = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="replaced">這些檔案（相對路徑）用這個內容，不讀硬碟上的（自動修先在記憶體裡驗）。</param>
    public static Project Load(string root, IReadOnlyList<string> exclude, IReadOnlyDictionary<string, string>? treatAs = null, IReadOnlyDictionary<string, string>? replaced = null)
    {
        var gaps = new ScanGaps();
        var files = XamlFiles.Enumerate(root, ".xaml", exclude, gaps);
        var rels = files.Select(f => XamlFiles.Relative(root, f)).ToList();
        var docs = new XDocument?[files.Count];
        var dialects = new string?[files.Count];
        var marked = new bool[files.Count];
        var errors = new List<string>();
        var why = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < files.Count; i++)
        {
            try { docs[i] = replaced is not null && replaced.TryGetValue(rels[i], out var text) ? SafeXml.Parse(text) : SafeXml.Load(files[i]); }
            catch (Exception e) { errors.Add(rels[i]); why[rels[i]] = e.Message.ReplaceLineEndings(" "); continue; }
            if (docs[i]!.Root is { } r) (dialects[i], marked[i]) = XamlFiles.DialectOf(r);
        }
        XamlFiles.ResolveAmbiguous(rels, dialects, marked);

        var styles = new StyleIndex();
        var list = new List<XamlDoc>();
        var foreign = new List<(string, string)>();
        var appDirs = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            if (docs[i]?.Root is null) continue;
            if (dialects[i] is { } d) { foreign.Add((rels[i], d)); continue; }
            docs[i]!.AddAnnotation(new SourceFile(rels[i]));
            list.Add(new XamlDoc(rels[i], files[i], docs[i]!));
            styles.Add(docs[i]!, rels[i]);
            if (XamlFiles.IsApplication(docs[i]!.Root!, rels[i])) appDirs.Add(XamlFiles.DirOf(rels[i]));
        }
        styles.SetAppDirectories(appDirs);

        var project = new Project
        {
            Root = root, Docs = list, Styles = styles, ParseErrors = errors, ParseWhy = why, Foreign = foreign, FilesScanned = files.Count,
            TreatAs = treatAs ?? new Dictionary<string, string>(),
            Themes = FindThemes(list, rels, appDirs), Gaps = gaps,
        };
        var mentioned = project.ReadClasses(root, exclude);
        project.Link(mentioned);
        return project;
    }

    /// <summary>類別與合併關係：要在文件都讀進來之後。</summary>
    private void Link(IReadOnlySet<string> mentionedInCode)
    {
        foreach (var doc in Docs)
            if (((string?)doc.Root.Attribute(Markup.XClass))?.Trim() is { Length: > 0 } cls) _byClass.TryAdd(cls, doc);
        Styles.OwnRoot = e => DocOfClass(e)?.Root;
        Styles.BaseOf = BaseOf;

        // ── 哪本字典看得到 ──
        var merges = Docs.ToDictionary(d => d.File, d => MergedFrom(d), StringComparer.Ordinal);
        var apps = Docs.Where(d => XamlFiles.IsApplication(d.Root, d.File)).Select(d => d.File).ToList();
        // Themes/Generic.xaml 永遠看得到，它合併的字典也是（套件拆成一個控制項一個檔是標準做法）
        var generic = Docs.Where(d => IsGeneric(d.File)).Select(d => d.File).ToList();
        HashSet<string> Reach(string from)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { from };
            var queue = new Queue<string>(seen);
            while (queue.TryDequeue(out var f))
                foreach (var next in merges.GetValueOrDefault(f) ?? [])
                    if (seen.Add(next)) queue.Enqueue(next);
            return seen;
        }
        // 掃描範圍裡沒有 App.xaml（控制項庫、只掃一個資料夾）：有任何檔案合併它就當成看得到
        var fromApps = (apps.Count > 0 ? apps.Concat(generic).SelectMany(Reach) : merges.Values.SelectMany(m => m)).ToHashSet(StringComparer.Ordinal);
        var cache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Styles.IsVisible = (dictionary, file) =>
        {
            if (fromApps.Contains(dictionary) || mentionedInCode.Contains(Path.GetFileName(dictionary)) || IsGeneric(dictionary)) return true;
            if (!cache.TryGetValue(file, out var mine)) cache[file] = mine = Reach(file);
            return mine.Contains(dictionary);
        };
    }

    private static bool IsGeneric(string file) =>
        file.EndsWith("Themes/Generic.xaml", StringComparison.OrdinalIgnoreCase) && (file.Length == "Themes/Generic.xaml".Length || file[^("Themes/Generic.xaml".Length + 1)] == '/');

    /// <summary>這個檔案用 Source 合併了哪些檔案（相對路徑、根目錄路徑、pack URI；對不準時同名的全部算）。</summary>
    private List<string> MergedFrom(XamlDoc doc)
    {
        var found = new List<string>();
        foreach (var source in doc.Root.DescendantsAndSelf().Select(e => e.Name.LocalName == "ResourceDictionary.Source" ? e.Value.Trim() : (string?)e.Attribute("Source")).OfType<string>())
        {
            if (!source.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) continue;
            var path = source.Replace('\\', '/');
            var component = path.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
            if (component >= 0) path = path[(component + ";component/".Length)..];
            else if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase) && path.IndexOf(",,,/", StringComparison.Ordinal) is > 0 and var comma) path = path[(comma + 4)..];
            else if (!path.StartsWith('/'))
            {
                var exact = Collapse(XamlFiles.DirOf(doc.File) + path);
                if (Docs.Any(d => d.File.Equals(exact, StringComparison.OrdinalIgnoreCase))) { found.Add(Docs.First(d => d.File.Equals(exact, StringComparison.OrdinalIgnoreCase)).File); continue; }
            }
            path = Collapse(path.TrimStart('/'));
            found.AddRange(Docs.Where(d => d.File.EndsWith(path, StringComparison.OrdinalIgnoreCase) && (d.File.Length == path.Length || d.File[d.File.Length - path.Length - 1] == '/')).Select(d => d.File));
        }
        return found;

        static string Collapse(string p)
        {
            var parts = new List<string>();
            foreach (var part in p.Split('/'))
            {
                if (part is "" or ".") continue;
                if (part == ".." && parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                else if (part != "..") parts.Add(part);
            }
            return string.Join('/', parts);
        }
    }

    /// <summary>單一份 XAML（測試與 Oracle 用）。</summary>
    public static Project FromText(string xaml, IReadOnlyDictionary<string, string>? treatAs = null, params (string File, string Xaml)[] more)
    {
        var styles = new StyleIndex();
        var list = new List<XamlDoc>();
        foreach (var (file, text) in ((string, string)[])[("Main.xaml", xaml), .. more])
        {
            var doc = SafeXml.Parse(text);
            doc.AddAnnotation(new SourceFile(file));
            styles.Add(doc, file);
            list.Add(new XamlDoc(file, file, doc));
        }
        var project = new Project
        {
            Root = ".", Docs = list, Styles = styles, FilesScanned = list.Count, TreatAs = treatAs ?? new Dictionary<string, string>(),
            Themes = FindThemes(list, [.. list.Select(l => l.File)], []),
        };
        project.Link(new HashSet<string>());
        return project;
    }

    // ───────────────────────── 控制項是什麼 ─────────────────────────

    /// <summary>專案 C# 的型別（清單項目念出來是什麼）。</summary>
    public CodeTypes Code { get; } = new();

    /// <summary>專案原始碼裡的一個類別。</summary>
    /// <param name="Bases">寫過的基底（不同檔案的同名類別可能不一樣）。</param>
    /// <param name="OwnName">類別自己換了 AutomationPeer 或在程式裡設名稱：名稱規則不能照基底套。</param>
    /// <param name="OwnContent">類別在自己的程式碼裡設的內容屬性（Content、Header、Text）：沒寫內容不等於沒有。</param>
    private sealed record ClassInfo(List<string> Bases, bool OwnName, HashSet<string> OwnContent);

    /// <summary>
    /// 這個元素的名稱照哪個 WPF 控制項算；null = 不知道。
    /// 順序：WPF 內建 → 使用者宣告（treatAs）→ 量過的函式庫 → 專案自己的 XAML 類別（根元素）→ 專案 C# 的 class X : Button。
    /// </summary>
    public string? ControlOf(XElement e)
    {
        if (Markup.IsWpf(e)) return e.Name.LocalName;
        var local = e.Name.LocalName;
        if (TreatAs.TryGetValue(local, out var declared)) return declared;
        if (KnownControls.LibraryOf(e) is { } lib) return InLibrary(lib, local);
        if (_classes.TryGetValue(local, out var info) && (info.OwnName || info.Bases.Count > 1)) return null;
        return Underlying(e, named: true);
    }

    /// <summary>
    /// 行為上是哪個 WPF 控制項（鍵盤、焦點、大小）：類別自己設名稱或換 AutomationPeer 不影響這個答案。
    /// </summary>
    public string? BaseOf(XElement e) => ControlOf(e) ?? (Markup.IsWpf(e) || KnownControls.LibraryOf(e) is not null ? null : Underlying(e, named: false));

    private string? Underlying(XElement e, bool named)
    {
        if (DerivedFrom(e.Name.LocalName, named, 0) is { } derived) return derived;
        // x:Class 的根寫了基底（<Button x:Class="App.IconButton">），C# 那一半沒寫
        return DocOfClass(e) is { } own && !ReferenceEquals(own.Root, e) && Markup.IsWpf(own.Root) ? own.Root.Name.LocalName : null;
    }

    private static string? InLibrary(KnownLibrary lib, string local) =>
        lib.Controls.TryGetValue(local, out var known) ? known
        : lib.IconPrefix is { } prefix && local.StartsWith(prefix, StringComparison.Ordinal) ? "Image" : null;

    /// <summary>樣式的 TargetType 照哪個 WPF 控制項算。</summary>
    public string? ControlOfStyle(StyleRecord style)
    {
        if (style.TargetType is not { } name || style.TargetId is not { } id) return null;
        if (!id.Contains('#', StringComparison.Ordinal)) return Wpf.IsKnownType(name) ? name : null;
        if (TreatAs.TryGetValue(name, out var declared)) return declared;
        if (id.StartsWith("lib:", StringComparison.Ordinal))
            return KnownControls.Libraries.FirstOrDefault(l => id.StartsWith($"lib:{l.Id}#", StringComparison.Ordinal)) is { } lib ? InLibrary(lib, name) : null;
        return DerivedFrom(name, named: false, 0);
    }

    /// <summary>型別名字（範本的 TargetType）行為上是哪個 WPF 控制項；不認得就是它自己的名字。</summary>
    public string BaseOfName(string name) => TreatAs.TryGetValue(name, out var declared) ? declared : DerivedFrom(name, named: false, 0) ?? name;

    /// <summary>這個元素的類別（或它在專案裡的基底）在程式碼裡設了這個內容屬性。</summary>
    public bool ClassSets(XElement e, string property)
    {
        if (Markup.IsWpf(e)) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([e.Name.LocalName]);
        while (queue.TryDequeue(out var name))
        {
            if (!seen.Add(name) || !_classes.TryGetValue(name, out var info)) continue;
            if (info.OwnContent.Contains(property)) return true;
            foreach (var b in info.Bases) queue.Enqueue(b);
        }
        return false;
    }

    private readonly Dictionary<(string, bool), string?> _derived = [];

    /// <remarks>記住答案：同名類別互相當基底時，沒記住的話步數是分支的次方（140 KB 的 .cs 跑不完）。</remarks>
    private string? DerivedFrom(string name, bool named, int depth)
    {
        if (_derived.TryGetValue((name, named), out var known)) return known;
        if (depth > 8) return null;
        _derived[(name, named)] = null;                        // 繞回自己：不知道
        return _derived[(name, named)] = Derive(name, named, depth);
    }

    private string? Derive(string name, bool named, int depth)
    {
        if (!_classes.TryGetValue(name, out var info)) return null;
        if (named && info.OwnName) return null;
        string? answer = null;
        foreach (var b in info.Bases)
        {
            var one = TreatAs.TryGetValue(b, out var declared) ? declared
                // 基底跟自己同名（class Button : System.Windows.Controls.Button）→ 就是 WPF 的
                : b != name && _classes.ContainsKey(b) ? DerivedFrom(b, named, depth + 1)
                : Wpf.NameSourceOf(b) == NameSource.Unknown ? null : b;
            if (one is null || (answer is not null && answer != one)) return null;      // 同名類別的基底不一樣
            answer = one;
        }
        return answer;
    }

    /// <summary>專案自己的類別：元素的 XAML 檔（x:Class 對得上時）。</summary>
    public XamlDoc? DocOfClass(XElement e)
    {
        var ns = e.Name.NamespaceName;
        // 專案自己的 XmlnsDefinition("http://myapp/controls", …)：同名的類別只有一個才認
        if (_ownXmlns.Contains(ns))
            return _byClass.Where(p => p.Key.EndsWith("." + e.Name.LocalName, StringComparison.Ordinal)).Select(p => p.Value).Take(2).ToList() is [var only] ? only : null;
        if (!ns.StartsWith("clr-namespace:", StringComparison.Ordinal)) return null;
        var end = ns.IndexOf(';');
        var clr = (end < 0 ? ns["clr-namespace:".Length..] : ns["clr-namespace:".Length..end]).Trim();
        return _byClass.GetValueOrDefault(clr + "." + e.Name.LocalName);
    }

    [GeneratedRegex(@"\bclass\s{1,50}(?<name>[A-Za-z_]\w{0,200})\s{0,50}(?:<[^>{]{0,200}>)?\s{0,50}(?:\([^){]{0,2000}\)\s{0,50})?:\s{0,50}(?<base>[A-Za-z_][\w.:]{0,200})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ClassRegex();

    /// <summary>讀專案的 C#：類別的基底，以及程式碼裡提到的 .xaml 檔名（可能是執行期合併的字典）。</summary>
    private HashSet<string> ReadClasses(string root, IReadOnlyList<string> exclude)
    {
        var mentioned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var built = new List<(string File, string Code)>();
        foreach (var file in XamlFiles.Enumerate(root, ".cs", exclude))
        {
            string text;
            try
            {
                if (new FileInfo(file).Length > 2_000_000) continue;
                text = File.ReadAllText(file);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            try
            {
                if (text.Contains(".xaml", StringComparison.OrdinalIgnoreCase))
                    foreach (Match m in XamlNameRegex().Matches(text)) mentioned.Add(m.Value);
                foreach (Match m in NameStringRegex().Matches(text)) _codeStrings.Add(m.Groups["n"].Value);
                if (text.Contains("XmlnsDefinition", StringComparison.Ordinal))
                    foreach (Match m in XmlnsRegex().Matches(text)) _ownXmlns.Add(m.Groups["uri"].Value);

                var code = CSharp.Strip(text, keepStrings: false);
                if (code.Contains("IsEnabled", StringComparison.Ordinal) || code.Contains("Visibility", StringComparison.Ordinal))
                    foreach (Match m in OpensRegex().Matches(code)) _codeOpens.Add((m.Groups["n"].Value, m.Groups["p"].Value));
                if (code.Contains("abel", StringComparison.Ordinal))
                    foreach (Match m in LabelNameRegex().Matches(code)) _labelNames.Add(m.Value);
                built.Add((file, code));
                _wpfGlobalUsing |= code.Contains("global using System.Windows", StringComparison.Ordinal) && !code.Contains("global using System.Windows.Forms", StringComparison.Ordinal);
                try { Code.Read(code); }
                catch (RegexMatchTimeoutException) { }      // 型別當成沒看到；下面讀控制項的照讀
                if (!text.Contains("class ", StringComparison.Ordinal)) continue;
                if (code.Contains("Command", StringComparison.Ordinal)) ReadCommands(code);
                var matches = ClassRegex().Matches(code).Cast<Match>().ToList();
                for (var i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    // 到下一個類別為止就是這個類別的內容
                    var body = code[m.Index..(i + 1 < matches.Count ? matches[i + 1].Index : code.Length)];
                    var own = body.Contains("OnCreateAutomationPeer", StringComparison.Ordinal) || body.Contains("AutomationProperties.", StringComparison.Ordinal);
                    AddClass(m.Groups["name"].Value, m.Groups["base"].Value, own, ContentSetRegex().Matches(body).Where(x => !x.Value.EndsWith('=') || IsStatement(body, x.Index + x.Length)).Select(x => x.Groups["p"].Value));
                }
            }
            catch (RegexMatchTimeoutException) { }       // 這個檔案的類別當成沒看到：用到的控制項會是「不認得」
        }
        foreach (var (file, code) in built)
            try { CountCodeBuilt(file, code); }
            catch (RegexMatchTimeoutException) { }
        return mentioned;
    }

    /// <summary>專案自己的靜態命令：「Commands.Save」→ 有沒有文字（null = 看不出來）。</summary>
    private readonly Dictionary<string, bool?> _commands = new(StringComparer.Ordinal);

    /// <summary>「Commands.Save」是哪一種：true = RoutedUICommand 有文字，false = 沒有文字（RoutedCommand、RelayCommand…），null = 不知道。</summary>
    public bool? CommandHasText(string owner, string member) => _commands.GetValueOrDefault(owner + "." + member);

    private void ReadCommands(string code)
    {
        var classes = AnyClassRegex().Matches(code).Select(m => (m.Index, Name: m.Groups["name"].Value)).ToList();
        foreach (Match m in CommandRegex().Matches(code))
        {
            var owner = classes.LastOrDefault(c => c.Index < m.Index).Name;
            if (owner is null) continue;
            var type = m.Groups["type"].Value.TrimEnd('?');
            type = type[(type.LastIndexOf('.') + 1)..];
            var rest = Declaration(code, m.Index + m.Length);
            // 宣告寫 ICommand、實際是 RoutedUICommand 的看不出來；自己的 RelayCommand 之類沒有文字
            bool? text = type switch
            {
                "RoutedUICommand" => rest.IndexOf("new", StringComparison.Ordinal) is >= 0 and var n && rest.IndexOf('(', n) is >= 0 and var open && rest[open..].Contains(',', StringComparison.Ordinal) ? true : null,
                "ICommand" => null,
                _ => rest.Contains("RoutedUICommand", StringComparison.Ordinal) ? null : false,
            };
            var key = owner + "." + m.Groups["name"].Value;
            _commands[key] = !_commands.TryGetValue(key, out var before) || before == text ? text : null;
        }
    }

    /// <summary>宣告剩下的部分：「{ get; } = new(…);」「= new(…);」「;」。</summary>
    private static string Declaration(string code, int from)
    {
        var depth = 0;
        for (var i = from; i < code.Length && i < from + 2000; i++)
        {
            if (code[i] is '(' or '[' or '{') depth++;
            else if (code[i] is ')' or ']') depth--;
            else if (code[i] == ';' && depth == 0) return code[from..i];
            else if (code[i] == '}' && --depth <= 0)
            {
                var next = i + 1;
                while (next < code.Length && char.IsWhiteSpace(code[next])) next++;
                if (next >= code.Length || code[next] is not ('=' or ';' or ')')) return code[from..i];
            }
        }
        return code[from..Math.Min(code.Length, from + 2000)];
    }

    [GeneratedRegex(@"\b(?:class|struct|record)\s{1,50}(?<name>[A-Za-z_]\w{0,200})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex AnyClassRegex();

    [GeneratedRegex(@"\bstatic\s{1,20}(?:readonly\s{1,20}|new\s{1,20}){0,2}(?<type>(?:\w{1,60}\.){0,6}\w{0,60}Command\??)\s{1,20}(?<name>[A-Za-z_]\w{0,100})\s{0,20}(?=[={;])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CommandRegex();

    /// <summary>「Content = …;」是設自己的；「new ToolTip { Content = …, }」是在建別的物件。看這個等號後面先遇到什麼。</summary>
    private static bool IsStatement(string code, int from)
    {
        var depth = 0;
        for (var i = from; i < code.Length && i < from + 5000; i++)
            switch (code[i])
            {
                case '(' or '[' or '{': depth++; break;
                case ')' or ']' or '}': if (--depth < 0) return false; break;
                case ',' when depth == 0: return false;
                case ';' when depth == 0: return true;
            }
        return false;
    }

    private void AddClass(string name, string baseName, bool own, IEnumerable<string> content)
    {
        var cut = baseName.LastIndexOfAny(['.', ':']);
        if (cut >= 0) baseName = baseName[(cut + 1)..];
        // 介面（partial class 的另一半）不是基底
        var isInterface = baseName.Length > 1 && baseName[0] == 'I' && char.IsUpper(baseName[1]);
        if (!_classes.TryGetValue(name, out var info)) _classes[name] = info = new ClassInfo([], false, new HashSet<string>(StringComparer.Ordinal));
        info.OwnContent.UnionWith(content);
        if (own && !info.OwnName) _classes[name] = info = info with { OwnName = true };
        if (!isInterface && !info.Bases.Contains(baseName)) info.Bases.Add(baseName);
    }

    /// <summary>類別自己設內容：Content = …、this.Text = …、SetBinding(ContentProperty, …)。別的物件的（x.Content = …）不算。</summary>
    [GeneratedRegex(@"(?:(?<![\w.])(?:this\s{0,5}\.\s{0,5}|base\s{0,5}\.\s{0,5})?(?<p>Content|Header|Text)\s{0,10}(?:\?\?|\+)?=(?![=>])|\b(?:SetBinding|SetResourceReference|SetCurrentValue|OverrideMetadata)\s{0,5}\(\s{0,5}(?:\w{1,60}\.){0,6}(?<p>Content|Header|Text)Property\b|(?<![\w.])(?:this\s{0,5}\.\s{0,5})?(?<p>Inlines)\s{0,5}\.\s{0,5}Add|\b(?<p>Content|Header|Text)Property\s{0,5}\.\s{0,5}OverrideMetadata\b)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ContentSetRegex();

    /// <summary>GetTemplateChild("PART_Close")、FindName("Play")、[TemplatePart(Name = "…")]、const string X = "…"。</summary>
    [GeneratedRegex(@"""(?<n>[A-Za-z_]\w{0,100})""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex NameStringRegex();

    [GeneratedRegex(@"XmlnsDefinition(?:Attribute)?\s{0,5}\(\s{0,5}""(?<uri>[^""\r\n]{1,300})""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex XmlnsRegex();

    [GeneratedRegex(@"[A-Za-z0-9_.\-]{1,100}\.xaml", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex XamlNameRegex();

    // ───────────────────────── code-behind ─────────────────────────

    private readonly HashSet<string> _visualBasic = new(StringComparer.Ordinal);

    /// <summary>VB 不分大小寫：x:Name="Play" 在程式裡可以寫 play.Content。</summary>
    private RegexOptions CodeOptions(string rel) => RegexOptions.CultureInvariant | (_visualBasic.Contains(rel) ? RegexOptions.IgnoreCase : RegexOptions.None);

    /// <summary>XAML 檔自己的 code-behind（Foo.xaml.cs／.vb）。別的檔案一律不看。</summary>
    public string CodeBehind(string rel)
    {
        if (_codeBehind.TryGetValue(rel, out var cached)) return cached;
        var doc = Docs.FirstOrDefault(d => d.File == rel);
        var text = "";
        if (doc is not null)
            foreach (var ext in (ReadOnlySpan<string>)[".cs", ".vb"])
                try
                {
                    if (File.Exists(doc.FullPath + ext) && !XamlFiles.IsFileLink(doc.FullPath + ext) && new FileInfo(doc.FullPath + ext).Length <= 2_000_000)
                    {
                        text += CSharp.Strip(File.ReadAllText(doc.FullPath + ext), keepStrings: false, vb: ext == ".vb") + "\n";
                        if (ext == ".vb") _visualBasic.Add(rel);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        return _codeBehind[rel] = text;
    }

    /// <summary>code-behind 有沒有設這個名稱的某個成員（btn.Content = …、btn.KeyDown += …、btn.Inlines.Add）。讀取不算。</summary>
    public bool CodeSets(XElement e, params ReadOnlySpan<string> members)
    {
        if (Markup.NameOf(e) is not { Length: > 0 } name) return false;
        var file = StyleIndex.FileOf(e);
        var code = CodeBehind(file);
        if (code.Length == 0) return false;
        try
        {
            var n = Regex.Escape(name);
            foreach (var member in members)
            {
                // btn.Content = …（+=、??=）、btn.Inlines.Add(…)
                if (Regex.IsMatch(code, $@"\b{n}\s{{0,5}}[?!]?\.\s{{0,20}}{member}\s{{0,20}}(?:(?:\+|\?\?)?=(?![=>])|\.\s{{0,20}}(?:Add|Insert)\b)", CodeOptions(file), RegexLimit)) return true;
                // btn.SetBinding(Button.ContentProperty, …)、btn.SetResourceReference(…)、btn.AddHandler(UIElement.KeyDownEvent, …)
                if (Regex.IsMatch(code, $@"\b{n}\s{{0,5}}[?!]?\.\s{{0,20}}(?:Set(?:Binding|ResourceReference|Value|CurrentValue)|AddHandler)\s{{0,20}}\(\s{{0,20}}(?:\w{{1,60}}\.){{0,6}}{member}(?:Property|Event)\b", CodeOptions(file), RegexLimit)) return true;
                // BindingOperations.SetBinding(btn, Button.ContentProperty, …)
                if (Regex.IsMatch(code, $@"\bSetBinding\s{{0,20}}\(\s{{0,20}}(?:this\.|Me\.)?{n}\s{{0,20}},\s{{0,20}}(?:\w{{1,60}}\.){{0,6}}{member}Property\b", CodeOptions(file), RegexLimit)) return true;
                // VB：AddHandler btn.KeyDown, AddressOf …
                if (_visualBasic.Contains(file) && Regex.IsMatch(code, $@"\bAddHandler\s{{1,20}}(?:Me\.)?{n}\s{{0,5}}\.\s{{0,20}}{member}\b", CodeOptions(file), RegexLimit)) return true;
            }
            return false;
        }
        // 讀的是別人的程式碼：跑太久就當成「它動了這個控制項」（不知道），不是「沒動」
        catch (RegexMatchTimeoutException) { return true; }
    }

    private readonly HashSet<string> _codeStrings = new(StringComparer.Ordinal);

    /// <summary>
    /// 寫死的 Visibility、IsEnabled 之後會不會被改：範本觸發器（TargetName）、動畫、程式碼提到這個名字（連 GetTemplateChild("…") 這種字串）。
    /// 沒有名字的改不到：本地值贏過樣式的觸發器。
    /// </summary>
    public bool ChangesLater(XElement e, string property)
    {
        if (CodeLoops(e, property)) return true;
        if (Markup.NameOf(e) is not { Length: > 0 } name) return false;
        // 別的類別、partial 的另一半也改得到：m.other.IsEnabled = true
        if (_codeStrings.Contains(name) || _codeOpens.Contains((name, property))) return true;
        foreach (var d in e.Document?.Root?.Descendants() ?? [])
        {
            // <Setter TargetName>、Storyboard.TargetName／Target、Blend 的 ChangePropertyAction TargetName／TargetObject
            var aims = false;
            foreach (var a in d.Attributes())
                aims |= a.Name.LocalName is "TargetName" or "Storyboard.TargetName" && a.Value == name
                        || a.Name.LocalName is "Storyboard.Target" or "TargetObject" or "Target" && Markup.ReferencedElement(a.Value) == name;
            if (!aims) continue;
            if ((string?)d.Attribute("Property") is { } p) { if (StyleIndex.NormalizeProperty(p) == property) return true; continue; }
            if ((string?)d.Attribute("PropertyName") is { } pn && pn.Contains(property, StringComparison.Ordinal)) return true;
            if (d.AncestorsAndSelf().Select(x => x.Attributes().FirstOrDefault(a => a.Name.LocalName == "Storyboard.TargetProperty")?.Value).FirstOrDefault(v => v is not null) is { } path && path.Contains(property, StringComparison.Ordinal)) return true;
        }
        if (CodeSets(e, property)) return true;
        var file = StyleIndex.FileOf(e);
        // Show(btn)、new[] { btn, … }、var b = btn：交給別人的；btn.FadeIn() 這種擴充方法。btn.Text 這種只讀別的屬性的不算
        try { return Regex.IsMatch(CodeBehind(file), $@"\b{Regex.Escape(name)}\b(?!\s{{0,5}}[?!]?\.)|\b{Regex.Escape(name)}\s{{0,5}}[?!]?\.\s{{0,20}}\w*(?:Show|Visib|Enabl|Display|Toggle|Fade|Appear|Reveal)\w*\s{{0,5}}\(", CodeOptions(file), RegexLimit); }
        catch (RegexMatchTimeoutException) { return true; }
    }

    /// <summary>樣式寫死的（Setter Visibility=Collapsed 加 DataTrigger 打開，最常見的寫法）：樣式的觸發器也會打開。</summary>
    public bool OpensLater(XElement e, string property) =>
        ChangesLater(e, property) || !Styles.LocalValue(e, property).IsSet && StyleTriggerSets(e, property);

    /// <summary>樣式的觸發器會設這個屬性。</summary>
    public bool StyleTriggerSets(XElement e, string property) =>
        Styles.ChainsOf(e).Any(c => c.Styles.Any(s => s.Triggers.SelectMany(t => t.Descendants()).Any(x =>
            x.Name.LocalName == "Setter" && x.Attribute("TargetName") is null && (string?)x.Attribute("Property") is { } p && StyleIndex.NormalizeProperty(p) == property)));

    private readonly HashSet<(string, string)> _codeOpens = [];
    private readonly HashSet<string> _labelNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>專案的 C# 裡有叫這個名字的東西（自動修要取的名字一律以 Label 或 Label＋數字結尾）。</summary>
    public bool CodeNamesLabel(string name) => _labelNames.Contains(name);

    /// <summary>ToolTipService.SetToolTip(btn, …)。</summary>
    public bool CodeTips(XElement e)
    {
        if (Markup.NameOf(e) is not { Length: > 0 } name) return false;
        var file = StyleIndex.FileOf(e);
        try { return Regex.IsMatch(CodeBehind(file), $@"\bSetToolTip\s{{0,20}}\(\s{{0,20}}(?:this\.|Me\.)?{Regex.Escape(name)}\b", CodeOptions(file), RegexLimit); }
        catch (RegexMatchTimeoutException) { return true; }
    }

    /// <summary>專案自己的控制項（clr-namespace 沒有寫組件、自己的 XmlnsDefinition）。</summary>
    public bool IsOwn(XElement e)
    {
        var ns = e.Name.NamespaceName;
        return _ownXmlns.Contains(ns) || ns.StartsWith("clr-namespace:", StringComparison.Ordinal) && (!ns.Contains(";assembly=", StringComparison.Ordinal) || _classes.ContainsKey(e.Name.LocalName) || DocOfClass(e) is not null);
    }

    [GeneratedRegex(@"\b(?<n>[A-Za-z_]\w{0,100})\s{0,5}[?!]?\.\s{0,20}(?:(?<p>IsEnabled|Visibility)\s{0,20}(?:\?\?)?=(?![=>])|(?:BeginAnimation|ClearValue|InvalidateProperty|Set(?:Value|CurrentValue|Binding|ResourceReference))\s{0,5}\(\s{0,20}(?:\w{1,60}\.){0,6}(?<p>IsEnabled|Visibility)Property\b)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex OpensRegex();

    [GeneratedRegex(@"\b[A-Za-z_]\w{0,100}[Ll]abel[0-9]{0,4}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex LabelNameRegex();

    /// <summary>
    /// 迴圈設的：foreach (ComboBoxItem item in List.Items) item.Content = …（Text-Grab）。只看自己的 code-behind、往上三層。
    /// </summary>
    public bool CodeLoops(XElement e, string property)
    {
        var file = StyleIndex.FileOf(e);
        var code = CodeBehind(file);
        if (!code.Contains("each", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var holder in e.Ancestors().Where(a => !Markup.IsPropertyElement(a)).Take(3))
        {
            if (Markup.NameOf(holder) is not { Length: > 0 } name) continue;
            try
            {
                foreach (Match m in Regex.Matches(code, $@"(?:\bforeach|\b[Ee]ach)\b[^;{{}}]{{0,200}}?\b(?<v>[A-Za-z_]\w{{0,60}})(?:\s{{1,20}}As\s{{1,20}}[\w.]{{1,60}})?\s{{1,20}}[Ii]n\s{{1,20}}(?:this\.|Me\.)?{Regex.Escape(name)}\s{{0,5}}[?!]?\.\s{{0,20}}(?:Items|Children)\b", CodeOptions(file), RegexLimit))
                {
                    var body = LoopBody(code, m.Index + m.Length, _visualBasic.Contains(file));
                    // 迴圈的變數，與「child is Button b」換的名字
                    var names = Regex.Matches(body, $@"\b{m.Groups["v"].Value}\s{{1,20}}is\s{{1,20}}[\w.]{{1,60}}\s{{1,20}}(?<n>[A-Za-z_]\w{{0,60}})", CodeOptions(file), RegexLimit).Select(x => x.Groups["n"].Value).Append(m.Groups["v"].Value);
                    foreach (var v in names)
                        if (Regex.IsMatch(body, $@"\b{v}\s{{0,5}}[?!]?\.\s{{0,20}}{property}\s{{0,20}}=(?![=>])|\bSetName\s{{0,5}}\(\s{{0,20}}{v}\b|\b{v}\s{{0,5}}\.\s{{0,20}}Set\w{{1,20}}\s{{0,5}}\(\s{{0,20}}(?:\w{{1,60}}\.){{0,6}}{property}Property\b", CodeOptions(file), RegexLimit)) return true;
                }
            }
            catch (RegexMatchTimeoutException) { return true; }
        }
        return false;
    }

    /// <summary>迴圈的內容：大括號配對，或到分號；VB 到 Next。</summary>
    private static string LoopBody(string code, int from, bool vb)
    {
        if (vb) return code[from..(code.IndexOf("Next", from, StringComparison.OrdinalIgnoreCase) is > 0 and var next ? next : code.Length)];
        // 先走完 foreach (…) 的括號
        var depth = 1;
        while (from < code.Length && depth > 0) { if (code[from] == '(') depth++; else if (code[from] == ')') depth--; from++; }
        for (int i = from, braces = 0; i < code.Length; i++)
        {
            if (code[i] == '{') braces++;
            else if (code[i] == '}' && --braces <= 0) return code[from..i];
            else if (code[i] == ';' && braces == 0) return code[from..i];
        }
        return code[Math.Min(from, code.Length)..];
    }

    /// <summary>code-behind 對這個元素掛的點按：box.MouseLeftButtonUp += OnOpen;。拖曳、移動視窗的 lambda 不算。</summary>
    public (string Event, string? Handler)? CodeClick(XElement e, IReadOnlyCollection<string> events)
    {
        // 範本裡的名字在 code-behind 是別的東西
        if (Markup.NameOf(e) is not { Length: > 0 } name || e.Ancestors().Any(a => a.Name.LocalName is "ControlTemplate" or "DataTemplate" or "HierarchicalDataTemplate")) return null;
        var file = StyleIndex.FileOf(e);
        var code = CodeBehind(file);
        if (!code.Contains("+=", StringComparison.Ordinal)) return null;
        try
        {
            // 同名的區域變數（var item = new Border()）、別的物件的成員（child.box.MouseDown）不是這個元素
            var n = Regex.Escape(name);
            if (Regex.IsMatch(code, $@"\b(?:var|[A-Z]\w{{0,60}})\s{{1,20}}{n}\s{{0,20}}(?:=|\bin\b|[,)])", CodeOptions(file), RegexLimit)) return null;
            var m = Regex.Match(code, $@"(?<![\w.])(?:this\s{{0,5}}\.\s{{0,5}}|Me\s{{0,5}}\.\s{{0,5}})?{n}\s{{0,5}}[?!]?\.\s{{0,20}}(?<ev>{string.Join('|', events)})\s{{0,20}}\+=\s{{0,20}}(?:new\s{{1,20}}\w{{1,60}}\s{{0,20}}\(\s{{0,20}})?(?<h>[A-Za-z_]\w{{0,100}})?(?<rest>\s{{0,20}}[;)]|)", CodeOptions(file), RegexLimit);
            if (!m.Success) return null;
            if (m.Groups["h"].Success && m.Groups["rest"].Length > 0)
                // 同一個處理器也掛在按鈕上：鍵盤本來就有路
                return Regex.IsMatch(code, $@"\.\s{{0,20}}(?:Click|Checked|Unchecked|KeyDown|KeyUp|PreviewKeyDown)\s{{0,20}}\+=\s{{0,20}}(?:new\s{{1,20}}\w{{1,60}}\s{{0,20}}\(\s{{0,20}})?{m.Groups["h"].Value}\b", CodeOptions(file), RegexLimit) ? null : (m.Groups["ev"].Value, m.Groups["h"].Value);
            var body = code.Substring(m.Index, Math.Min(800, code.Length - m.Index));
            // 只是擋住事件：(s, e) => e.Handled = true
            if (Regex.IsMatch(body, @"^[^;]{0,200}=>\s{0,20}(?:\w{1,40}\.Handled\s{0,10}=\s{0,10}true\s{0,10};|\{\s{0,20}\w{1,40}\.Handled\s{0,10}=\s{0,10}true\s{0,10};\s{0,20}\})", RegexOptions.CultureInvariant, RegexLimit)) return null;
            return body.Contains("DragMove", StringComparison.Ordinal) || body.Contains("CaptureMouse", StringComparison.Ordinal) || body.Contains("DoDragDrop", StringComparison.Ordinal) ? null : (m.Groups["ev"].Value, null);
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>C# 裡建的畫面有多少（這支看不到的）。</summary>
    public CodeBuilt CodeBuilt { get; private set; } = CodeBuilt.None;

    private void CountCodeBuilt(string file, string code)
    {
        // 沒碰 WPF 的檔案（WinForms 也有 Button）不算
        if (!code.Contains("System.Windows", StringComparison.Ordinal) && !_wpfGlobalUsing) return;
        if (code.Contains("System.Windows.Forms", StringComparison.Ordinal) && !code.Contains("System.Windows.Controls", StringComparison.Ordinal)) return;
        var controls = NewControlRegex().Count(code);
        var handlers = 0;
        if (code.Contains("+=", StringComparison.Ordinal))
        {
            var names = file.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) && Docs.FirstOrDefault(d => string.Equals(d.FullPath, file[..^3], StringComparison.OrdinalIgnoreCase)) is { } doc
                ? doc.Root.Descendants().Select(Markup.NameOf).OfType<string>().ToHashSet(StringComparer.Ordinal) : [];
            handlers = MouseAddRegex().Matches(code).Count(m => !names.Contains(m.Groups["n"].Value));
        }
        CodeBuilt = new CodeBuilt(CodeBuilt.Controls + controls, CodeBuilt.MouseHandlers + handlers);
    }

    private bool _wpfGlobalUsing;

    [GeneratedRegex(@"\bnew\s{1,20}(?:System\.Windows\.Controls\.(?:Primitives\.)?)?(?:Button|ToggleButton|RepeatButton|CheckBox|RadioButton|TextBox|PasswordBox|RichTextBox|ComboBox|ComboBoxItem|Slider|DatePicker|ListBox|ListBoxItem|ListView|ListViewItem|TreeView|TreeViewItem|DataGrid|TabItem|MenuItem|Expander|Hyperlink|ProgressBar)\s{0,20}[({]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex NewControlRegex();

    [GeneratedRegex(@"\b(?<n>[A-Za-z_]\w{0,100})\s{0,5}[?!]?\.\s{0,20}(?:MouseLeftButtonDown|MouseLeftButtonUp|MouseDown|MouseUp|MouseDoubleClick)\s{0,20}\+=", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MouseAddRegex();

    /// <summary>code-behind 有沒有設這個名稱的無障礙名稱（AutomationProperties.SetName、SetLabeledBy、SetValue）。</summary>
    public bool CodeNames(XElement e)
    {
        if (Markup.NameOf(e) is not { Length: > 0 } name) return false;
        var file = StyleIndex.FileOf(e);
        var code = CodeBehind(file);
        if (!code.Contains("AutomationProperties", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var n = Regex.Escape(name);
            return Regex.IsMatch(code, $@"\bSet(?:Name|LabeledBy)\s{{0,20}}\(\s{{0,20}}(?:element\s{{0,5}}:\s{{0,5}})?(?:this\.|Me\.)?{n}\b|\b{n}\s{{0,5}}[?!]?\.\s{{0,20}}Set(?:Value|Binding|ResourceReference|CurrentValue)\s{{0,20}}\(\s{{0,20}}(?:\w{{1,60}}\.){{0,6}}AutomationProperties\.|\bSetBinding\s{{0,20}}\(\s{{0,20}}(?:this\.|Me\.)?{n}\s{{0,20}},\s{{0,20}}(?:\w{{1,60}}\.){{0,6}}AutomationProperties\.", CodeOptions(file), RegexLimit);
        }
        catch (RegexMatchTimeoutException) { return true; }
    }

    private static readonly TimeSpan RegexLimit = TimeSpan.FromSeconds(2);

    /// <summary>
    /// code-behind 裡某個方法的內容（大括號配對）；找不到回 null。只給「這個處理器是不是在拖動視窗」這種判斷用。
    /// </summary>
    public string? MethodBody(string rel, string method)
    {
        var code = CodeBehind(rel);
        if (_visualBasic.Contains(rel))
        {
            try
            {
                var vb = Regex.Match(code, $@"\b(?:Sub|Function)\s{{1,20}}{Regex.Escape(method)}\s{{0,20}}\((?<body>.*?)\bEnd\s{{1,20}}(?:Sub|Function)\b", CodeOptions(rel) | RegexOptions.Singleline, RegexLimit);
                if (vb.Success) return vb.Groups["body"].Value;
            }
            catch (RegexMatchTimeoutException) { return null; }
        }
        Match m;
        try { m = Regex.Match(code, $@"\b{Regex.Escape(method)}\s{{0,20}}\([^)]{{0,2000}}\)\s{{0,50}}(?<body>\{{|=>)", RegexOptions.CultureInvariant, RegexLimit); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!m.Success) return null;
        if (m.Groups["body"].Value == "=>")
        {
            var end = code.IndexOf(';', m.Index + m.Length);
            return end < 0 ? null : code[(m.Index + m.Length)..end];
        }
        var depth = 0;
        for (var i = m.Index + m.Length - 1; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[(m.Index + m.Length)..i];
        }
        return null;
    }

    // ───────────────────────── 佈景 ─────────────────────────

    private static List<ThemeUse> FindThemes(List<XamlDoc> docs, List<string> rels, List<string> appDirs)
    {
        var result = new List<ThemeUse>();
        foreach (var doc in docs)
        {
            var appWide = doc.Root.Name.LocalName is "ResourceDictionary" || XamlFiles.IsApplication(doc.Root, doc.File);
            var scope = doc.File;
            if (appWide)
            {
                scope = "";
                foreach (var dir in appDirs)
                    if (doc.File.StartsWith(dir, StringComparison.Ordinal) && dir.Length > scope.Length) scope = dir;
            }
            foreach (var merged in doc.Root.Descendants().Where(e => e.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal)))
                foreach (var entry in merged.Elements())
                {
                    string? name = null;
                    KnownLibrary? lib = null;
                    var source = (string?)entry.Attribute("Source");
                    if (entry.Name.LocalName == "ResourceDictionary" && Markup.IsWpf(entry))
                    {
                        if (source is not null) name = ExternalAssembly(source, rels);
                        if (name is not null) lib = KnownControls.Libraries.FirstOrDefault(l => l.Assemblies.Contains(name, StringComparer.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var ns = entry.Name.NamespaceName;
                        lib = KnownControls.LibraryOf(ns);
                        if (lib is not null) name = lib.Id;
                        else if (ns.StartsWith("clr-namespace:", StringComparison.Ordinal))
                        {
                            var marker = ns.IndexOf(";assembly=", StringComparison.Ordinal);
                            if (marker >= 0) name = ns[(marker + ";assembly=".Length)..].Trim();
                        }
                        else if (!Markup.IsWpf(entry)) name = entry.Name.LocalName;
                    }
                    if (string.IsNullOrEmpty(name) || result.Any(t => t.Name == name && t.Scope == scope && t.Source == source)) continue;
                    result.Add(new ThemeUse(name, doc.File, scope, lib?.Id, source));
                }

            // WPF 的 Fluent 佈景（.NET 9+）：<Application ThemeMode="System">
            if (doc.Root.Attribute("ThemeMode") is not null && !result.Any(t => t.Name == "PresentationFramework.Fluent" && t.Scope == scope))
                result.Add(new ThemeUse("PresentationFramework.Fluent", doc.File, scope, null, null));
        }
        return result;
    }

    private static string? ExternalAssembly(string source, List<string> rels)
    {
        var marker = source.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;
        var path = source[(marker + ";component/".Length)..].Replace('\\', '/').TrimStart('/');
        if (rels.Any(r => r.EndsWith(path, StringComparison.OrdinalIgnoreCase) && (r.Length == path.Length || r[r.Length - path.Length - 1] == '/')))
            return null;
        var head = source[..marker];
        var slash = head.LastIndexOf('/');
        var assembly = slash >= 0 ? head[(slash + 1)..] : head;
        return assembly.Length > 0 ? assembly : null;
    }

    public IReadOnlyList<ThemeUse> ThemesOf(string file) =>
        [.. Themes.Where(t => t.Scope.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? t.Scope == file : file.StartsWith(t.Scope, StringComparison.Ordinal))];

    /// <summary>專案檔裡寫的某個套件的版本。</summary>
    public HashSet<string> PackageVersions(string package)
    {
        if (_versions.TryGetValue(package, out var cached)) return cached;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        try
        {
            foreach (var ext in (ReadOnlySpan<string>)[".csproj", ".vbproj", ".props", ".targets", ".config"])
                files.AddRange(XamlFiles.Enumerate(Root, ext, [])
                    .Where(f => ext != ".config" || Path.GetFileName(f).Equals("packages.config", StringComparison.OrdinalIgnoreCase)));
            for (var dir = Directory.GetParent(Path.GetFullPath(Root)); dir is not null; dir = dir.Parent)
                foreach (var name in (ReadOnlySpan<string>)["Directory.Packages.props", "Directory.Build.props"])
                    if (File.Exists(Path.Combine(dir.FullName, name))) files.Add(Path.Combine(dir.FullName, name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        foreach (var file in files.Distinct())
        {
            XDocument doc;
            try { doc = SafeXml.Load(file); }
            catch (Exception) { continue; }
            foreach (var el in doc.Descendants())
            {
                var id = (string?)el.Attribute("Include") ?? (string?)el.Attribute("Update") ?? (string?)el.Attribute("id");
                if (!string.Equals(id?.Trim(), package, StringComparison.OrdinalIgnoreCase)) continue;
                var version = (string?)el.Attribute("Version") ?? (string?)el.Attribute("VersionOverride") ?? (string?)el.Attribute("version")
                              ?? el.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value;
                if (!string.IsNullOrWhiteSpace(version)) found.Add(version.Trim().Trim('[', ']', '(', ')'));
            }
        }
        return _versions[package] = found;
    }
}
