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
    public IReadOnlyList<(string File, string Dialect)> Foreign { get; init; } = [];
    public IReadOnlyList<ThemeUse> Themes { get; init; } = [];
    public int FilesScanned { get; init; }
    public ScanGaps Gaps { get; init; } = new();

    /// <summary>使用者宣告：第三方控制項照哪個 WPF 控制項算（設定檔 treatAs）。</summary>
    public IReadOnlyDictionary<string, string> TreatAs { get; init; } = new Dictionary<string, string>();

    private Dictionary<string, XamlDoc> _byClass = new(StringComparer.Ordinal);
    private Dictionary<string, ClassInfo> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeBehind = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _versions = new(StringComparer.OrdinalIgnoreCase);

    public static Project Load(string root, IReadOnlyList<string> exclude, IReadOnlyDictionary<string, string>? treatAs = null)
    {
        var gaps = new ScanGaps();
        var files = XamlFiles.Enumerate(root, ".xaml", exclude, gaps);
        var rels = files.Select(f => XamlFiles.Relative(root, f)).ToList();
        var docs = new XDocument?[files.Count];
        var dialects = new string?[files.Count];
        var marked = new bool[files.Count];
        var errors = new List<string>();

        for (var i = 0; i < files.Count; i++)
        {
            try { docs[i] = SafeXml.Load(files[i]); }
            catch (Exception) { errors.Add(rels[i]); continue; }
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
            if (docs[i]!.Root!.Name.LocalName.EndsWith("Application", StringComparison.Ordinal)) appDirs.Add(XamlFiles.DirOf(rels[i]));
        }
        styles.SetAppDirectories(appDirs);

        var project = new Project
        {
            Root = root, Docs = list, Styles = styles, ParseErrors = errors, Foreign = foreign, FilesScanned = files.Count,
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

        // ── 哪本字典看得到 ──
        var merges = Docs.ToDictionary(d => d.File, d => MergedFrom(d), StringComparer.Ordinal);
        var apps = Docs.Where(d => d.Root.Name.LocalName.EndsWith("Application", StringComparison.Ordinal)).Select(d => d.File).ToList();
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
        var fromApps = (apps.Count > 0 ? apps.SelectMany(Reach) : merges.Values.SelectMany(m => m)).ToHashSet(StringComparer.Ordinal);
        var cache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Styles.IsVisible = (dictionary, file) =>
        {
            if (fromApps.Contains(dictionary) || mentionedInCode.Contains(Path.GetFileName(dictionary))
                || dictionary.EndsWith("Themes/Generic.xaml", StringComparison.OrdinalIgnoreCase)) return true;
            if (!cache.TryGetValue(file, out var mine)) cache[file] = mine = Reach(file);
            return mine.Contains(dictionary);
        };
    }

    /// <summary>這個檔案用 Source 合併了哪些檔案（相對路徑、根目錄路徑、pack URI；對不準時同名的全部算）。</summary>
    private List<string> MergedFrom(XamlDoc doc)
    {
        var found = new List<string>();
        foreach (var source in doc.Root.DescendantsAndSelf().Select(e => (string?)e.Attribute("Source")).OfType<string>())
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

    /// <summary>專案原始碼裡的一個類別。</summary>
    /// <param name="Bases">寫過的基底（不同檔案的同名類別可能不一樣）。</param>
    /// <param name="OwnName">類別自己換了 AutomationPeer 或在程式裡設名稱：名稱規則不能照基底套。</param>
    private sealed record ClassInfo(List<string> Bases, bool OwnName);

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

    private string? DerivedFrom(string name, bool named, int depth)
    {
        if (depth > 8 || !_classes.TryGetValue(name, out var info)) return null;
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

                if (!text.Contains("class ", StringComparison.Ordinal)) continue;
                var code = CSharp.Strip(text, keepStrings: false);
                var matches = ClassRegex().Matches(code).Cast<Match>().ToList();
                for (var i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    // 到下一個類別為止就是這個類別的內容
                    var body = code[m.Index..(i + 1 < matches.Count ? matches[i + 1].Index : code.Length)];
                    var own = body.Contains("OnCreateAutomationPeer", StringComparison.Ordinal) || body.Contains("AutomationProperties.", StringComparison.Ordinal);
                    AddClass(m.Groups["name"].Value, m.Groups["base"].Value, own);
                }
            }
            catch (RegexMatchTimeoutException) { }       // 這個檔案的類別當成沒看到：用到的控制項會是「不認得」
        }
        return mentioned;
    }

    private void AddClass(string name, string baseName, bool own)
    {
        var cut = baseName.LastIndexOfAny(['.', ':']);
        if (cut >= 0) baseName = baseName[(cut + 1)..];
        // 介面（partial class 的另一半）不是基底
        var isInterface = baseName.Length > 1 && baseName[0] == 'I' && char.IsUpper(baseName[1]);
        if (!_classes.TryGetValue(name, out var info)) _classes[name] = info = new ClassInfo([], false);
        if (own && !info.OwnName) _classes[name] = info = info with { OwnName = true };
        if (!isInterface && !info.Bases.Contains(baseName)) info.Bases.Add(baseName);
    }

    [GeneratedRegex(@"[A-Za-z0-9_.\-]{1,100}\.xaml", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex XamlNameRegex();

    // ───────────────────────── code-behind ─────────────────────────

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
                    if (File.Exists(doc.FullPath + ext) && new FileInfo(doc.FullPath + ext).Length <= 2_000_000)
                        text += CSharp.Strip(File.ReadAllText(doc.FullPath + ext), keepStrings: false, vb: ext == ".vb") + "\n";
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        return _codeBehind[rel] = text;
    }

    /// <summary>code-behind 有沒有設這個名稱的某個成員（btn.Content = …、btn.KeyDown += …、btn.Inlines.Add）。讀取不算。</summary>
    public bool CodeSets(XElement e, params ReadOnlySpan<string> members)
    {
        if (Markup.NameOf(e) is not { Length: > 0 } name) return false;
        var code = CodeBehind(StyleIndex.FileOf(e));
        if (code.Length == 0) return false;
        try
        {
            foreach (var member in members)
                if (Regex.IsMatch(code, $@"\b{Regex.Escape(name)}\s{{0,5}}[?!]?\.\s{{0,20}}{member}\s{{0,20}}(?:\+?=(?!=)|\.\s{{0,20}}(?:Add|Insert)\b)", RegexOptions.CultureInvariant, RegexLimit)) return true;
            return false;
        }
        // 讀的是別人的程式碼：跑太久就當成「它動了這個控制項」（不知道），不是「沒動」
        catch (RegexMatchTimeoutException) { return true; }
    }

    /// <summary>code-behind 有沒有設這個名稱的無障礙名稱（AutomationProperties.SetName、SetLabeledBy、SetValue）。</summary>
    public bool CodeNames(XElement e)
    {
        if (Markup.NameOf(e) is not { Length: > 0 } name) return false;
        var code = CodeBehind(StyleIndex.FileOf(e));
        if (!code.Contains("AutomationProperties", StringComparison.Ordinal)) return false;
        try
        {
            return Regex.IsMatch(code, $@"\bSet(?:Name|LabeledBy)\s{{0,20}}\(\s{{0,20}}(?:this\.)?{Regex.Escape(name)}\b|\b{Regex.Escape(name)}\s{{0,5}}[?!]?\.\s{{0,20}}SetValue\s{{0,20}}\(\s{{0,20}}AutomationProperties\.", RegexOptions.CultureInvariant, RegexLimit);
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
            var appWide = doc.Root.Name.LocalName is "ResourceDictionary" || doc.Root.Name.LocalName.EndsWith("Application", StringComparison.Ordinal);
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
