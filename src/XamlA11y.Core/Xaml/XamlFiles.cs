using System.Xml;
using System.Xml.Linq;

namespace XamlA11y.Core.Xaml;

/// <summary>讀 XML 的唯一入口（不解外部實體、限制深度）。沿用 XamlFit。</summary>
public static class SafeXml
{
    /// <summary>超過這個深度當成解析錯誤：十萬層巢狀的檔案會讓整條 CI 卡到逾時。</summary>
    public const int MaxDepth = 512;

    /// <summary>宣告 gb2312、windows-1252 的檔案（wpfsamples 就有）：.NET 預設不認這些編碼。</summary>
    static SafeXml() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <remarks>開檔案流，不要把路徑交給 XmlReader：它會當成 URI 解（<c>A%41.xaml</c> 讀成 <c>AA.xaml</c>）。</remarks>
    public static XDocument Load(string path)
    {
        using (var s = File.OpenRead(path))
        using (var probe = XmlReader.Create(s, Settings())) CheckDepth(probe);
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, Settings());
        return Normalize(XDocument.Load(reader, LoadOptions.SetLineInfo));
    }

    public static XDocument Parse(string xml)
    {
        using (var probe = XmlReader.Create(new StringReader(xml), Settings())) CheckDepth(probe);
        using var reader = XmlReader.Create(new StringReader(xml), Settings());
        return Normalize(XDocument.Load(reader, LoadOptions.SetLineInfo));
    }

    private const string BlendPrefix = "http://schemas.microsoft.com/expression/blend/";

    /// <summary>
    /// 1. 設計期的屬性與元素（<c>d:Content="Save"</c>）執行時不存在，拿掉 —— 當成真的會把沒名稱的按鈕說成有名稱。
    /// 2. XAML 命名空間的前綴不叫 x 時（<c>xmlns:xaml=…</c>），標記延伸裡的前綴改寫成 x，後面的判斷只認一種。
    /// </summary>
    private static XDocument Normalize(XDocument doc)
    {
        if (doc.Root is null) return doc;
        foreach (var e in doc.Root.Descendants().Where(e => e.Name.NamespaceName.StartsWith(BlendPrefix, StringComparison.Ordinal)).ToList()) e.Remove();

        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in doc.Root.DescendantsAndSelf())
            foreach (var a in e.Attributes().ToList())
            {
                if (a.IsNamespaceDeclaration)
                {
                    if (a.Value == Markup.XamlNamespace && a.Name.LocalName is not ("x" or "xmlns")) aliases.Add(a.Name.LocalName);
                    continue;
                }
                if (a.Name.NamespaceName.StartsWith(BlendPrefix, StringComparison.Ordinal)) a.Remove();
            }

        if (aliases.Count > 0)
            foreach (var a in doc.Root.DescendantsAndSelf().SelectMany(e => e.Attributes()).Where(a => !a.IsNamespaceDeclaration && a.Value.Contains('{', StringComparison.Ordinal)))
                foreach (var alias in aliases) a.Value = a.Value.Replace("{" + alias + ":", "{x:", StringComparison.Ordinal);
        return doc;
    }

    private static void CheckDepth(XmlReader probe)
    {
        while (probe.Read())
            if (probe.Depth > MaxDepth)
                throw new XmlException($"elements are nested more than {MaxDepth} deep");
    }
}

/// <summary>掃描時沒看的東西：要講出來，不能默默變少。</summary>
public sealed class ScanGaps
{
    public int Excluded { get; set; }
    public List<string> Unreadable { get; } = [];
}

/// <summary>檔案列舉與方言判斷。沿用 XamlFit（每一條排除規則都是它踩過的坑）。</summary>
public static class XamlFiles
{
    /// <summary>
    /// 列舉指定副檔名的檔案。比對相對路徑（掃描根位在 bin 或點開頭的目錄底下時不能全被排除）、
    /// 副檔名不分大小寫（Linux 的萬用字元分大小寫）、目錄剪枝、不進連結、排序。
    /// </summary>
    public static List<string> Enumerate(string root, string extension, IReadOnlyList<string> exclude, ScanGaps? gaps = null)
    {
        exclude = [.. exclude.Select(NormalizeExclude)];
        var stack = new Stack<string>();
        stack.Push(root);
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = new List<string>();

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subdirs, files;
            try
            {
                if (!visited.Add(Path.GetFullPath(dir))) continue;
                subdirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                gaps?.Unreadable.Add(Relative(root, dir));
                continue;
            }

            foreach (var f in files)
            {
                if (!f.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                // 連結的檔案不讀：公開 repo 的 PR 可以放 a.cs -> /dev/zero，或指到 repo 外面
                if (IsFileLink(f)) continue;
                var rel = "/" + Relative(root, f);
                if (exclude.Any(g => rel.Contains(g, StringComparison.OrdinalIgnoreCase)))
                {
                    if (gaps is not null) gaps.Excluded++;
                    continue;
                }
                found.Add(f);
            }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (IsExcludedDirectory(name) || IsLink(sub) || IsNuGetPackages(sub)) continue;
                stack.Push(sub);
            }
        }

        // 列舉順序在 Windows 與 Linux 不一樣；報告與 baseline 不該隨機器而變
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>「Views\Old」「./Views/Old」都要對得上「/Views/Old/…」。</summary>
    private static string NormalizeExclude(string e)
    {
        var t = e.Replace('\\', '/');
        return t.StartsWith("./", StringComparison.Ordinal) ? t[1..] : t;
    }

    public static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>檔案是符號連結。只看 LinkTarget：OneDrive 的檔案也有 ReparsePoint 屬性，那不是連結。</summary>
    public static bool IsFileLink(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>App.xaml：根叫 …Application，或是專案自己的子類別（&lt;local:BootApp x:Class="App.App"&gt;）放在 App.xaml 裡。</summary>
    public static bool IsApplication(XElement root, string file) =>
        root.Name.LocalName.EndsWith("Application", StringComparison.Ordinal) || root.Attribute("StartupUri") is not null
        || (!Markup.IsWpf(root) && root.Name.LocalName != "ResourceDictionary" && file[(file.LastIndexOfAny(['/', '\\']) + 1)..].Equals("App.xaml", StringComparison.OrdinalIgnoreCase));

    private static bool IsLink(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            return info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static bool IsExcludedDirectory(string name) =>
        name.StartsWith('.')
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase);

    /// <summary>packages.config 時代的 NuGet 資料夾：底下的資料夾裡有 .nupkg。只看名字會把原始碼的 Views/Packages 一起跳過。</summary>
    private static bool IsNuGetPackages(string dir)
    {
        if (!Path.GetFileName(dir).Equals("packages", StringComparison.OrdinalIgnoreCase)) return false;
        try { return Directory.EnumerateDirectories(dir).Any(d => Directory.EnumerateFiles(d, "*.nupkg").Any()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // ───────────────────────── 方言 ─────────────────────────

    /// <summary>(方言或 null = WPF, 有沒有明確的記號)。WinUI 與 WPF 的預設 xmlns 是同一個 URI，唯一可靠的記號是 using: 對 clr-namespace:。</summary>
    public static (string? Dialect, bool Marked) DialectOf(XElement root)
    {
        var ns = root.Name.NamespaceName;
        if (ns.Contains("avaloniaui", StringComparison.OrdinalIgnoreCase)) return ("Avalonia", true);
        if (ns.Contains("/dotnet/2021/maui", StringComparison.OrdinalIgnoreCase)) return (".NET MAUI", true);
        if (ns.Contains("xamarin.com/schemas", StringComparison.OrdinalIgnoreCase)) return ("Xamarin.Forms", true);

        bool sawUsing = false, sawClr = false;
        foreach (var el in root.DescendantsAndSelf())
            foreach (var a in el.Attributes())
            {
                if (!a.IsNamespaceDeclaration) continue;
                if (a.Value.StartsWith("using:", StringComparison.Ordinal)) sawUsing = true;
                else if (a.Value.StartsWith("clr-namespace:", StringComparison.Ordinal)) sawClr = true;
            }

        if (sawUsing && !sawClr) return ("WinUI/UWP", true);
        return (null, sawClr);
    }

    /// <summary>沒有記號的檔案：往上找最近一個「底下有帶記號的檔案」的目錄，跟那裡的多數一樣。</summary>
    public static void ResolveAmbiguous(IReadOnlyList<string> rels, string?[] dialects, bool[] marked)
    {
        var votes = new Dictionary<string, (int Wpf, int Foreign, string? Dialect)>(StringComparer.Ordinal);
        for (var i = 0; i < rels.Count; i++)
        {
            if (!marked[i]) continue;
            for (var dir = DirOf(rels[i]); ; dir = ParentDir(dir))
            {
                var v = votes.GetValueOrDefault(dir);
                votes[dir] = dialects[i] is null ? (v.Wpf + 1, v.Foreign, v.Dialect) : (v.Wpf, v.Foreign + 1, v.Dialect ?? dialects[i]);
                if (dir.Length == 0) break;
            }
        }

        for (var i = 0; i < rels.Count; i++)
        {
            if (marked[i]) continue;
            for (var dir = DirOf(rels[i]); ; dir = ParentDir(dir))
            {
                if (votes.TryGetValue(dir, out var v) && v.Wpf + v.Foreign > 0)
                {
                    if (v.Foreign > v.Wpf) dialects[i] = v.Dialect;
                    break;
                }
                if (dir.Length == 0) break;
            }
        }
    }

    public static string DirOf(string rel)
    {
        var slash = rel.LastIndexOf('/');
        return slash < 0 ? "" : rel[..(slash + 1)];
    }

    private static string ParentDir(string dir)
    {
        if (dir.Length == 0) return "";
        var slash = dir.LastIndexOf('/', dir.Length - 2);
        return slash < 0 ? "" : dir[..(slash + 1)];
    }
}
