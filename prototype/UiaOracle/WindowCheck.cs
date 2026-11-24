using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Xaml;
using XamlMarkup = XamlA11y.Core.Xaml.Markup;

namespace UiaOracle;

/// <summary>
/// 整個畫面：真實專案的每一個 XAML，連同它自己的資源、App.xaml 合併的字典，載進真的 WPF（視窗放在螢幕外、不搶焦點），
/// 每個判過的元素插一個 AutomationId，讀 UIA 名稱跟工具對**原始檔案**的判定比。check-corpus 只取一小段，
/// 這裡連樣式、範本、命令、隱含樣式怎麼套上去都對到。
///
/// ⚠ 讀的是別人的 XAML：只留 WPF（與四個量過的函式庫）的型別，專案自己的類別換成它的基底；
/// 會執行程式、上網、讀檔的型別與屬性全部拿掉。做過手腳的地方（找不到的資源）標記起來，不拿來比。
/// </summary>
internal static partial class WindowCheck
{
    private static readonly XNamespace Wpf = XamlMarkup.WpfNamespace;
    private static readonly XNamespace X = XamlMarkup.XamlNamespace;

    private static readonly HashSet<string> Denied = new(StringComparer.Ordinal)
    {
        "ObjectDataProvider", "XmlDataProvider", "Frame", "WebBrowser", "MediaElement", "MediaTimeline", "SoundPlayerAction", "BitmapImage", "CroppedBitmap",
        "FormatConvertedBitmap", "BitmapFrame", "EventSetter", "EventTrigger", "Storyboard", "BeginStoryboard", "VisualStateManager", "VisualStateGroup",
        "VisualState", "VisualTransition", "ImageBrush", "VideoDrawing", "ImageDrawing", "StaticExtension", "ArrayExtension", "ComponentResourceKey",
        "CommandBinding", "KeyBinding", "MouseBinding", "InputBinding", "DocumentViewer", "FlowDocumentPageViewer", "NavigationWindow", "WindowsFormsHost",
        "Viewport3D", "InkCanvas", "ThumbButtonInfo", "TaskbarItemInfo", "WindowChrome", "JumpList", "Popup", "ContextMenu", "ToolTip", "PrintDialog",
        "FontFamily", "Glyphs", "PageContent", "FixedDocument", "FixedPage", "BitmapCache", "DrawingImage", "VisualBrush",
    };

    private static readonly HashSet<string> DeniedAttributes = new(StringComparer.Ordinal)
    {
        "Visibility", "Source", "UriSource", "ImageSource", "NavigateUri", "DataContext", "ItemsSource", "CommandParameter", "CommandTarget", "Cursor", "Uid",
        "InputScope", "TargetNullValue", "StartupUri", "ContextMenu", "ToolTipService.ToolTip", "ItemTemplateSelector", "ContentTemplateSelector", "Language",
        "ItemContainerStyleSelector", "GroupStyleSelector", "CellTemplateSelector", "ValidatesOnTargetUpdated", "PrintQueue", "IsOpen", "IsDropDownOpen", "IsSubmenuOpen",
        "FontUri", "BaseUri", "Document", "Effect",
    };

    /// <summary>名稱從這些屬性來：綁定換成字面字串，形狀不變、值有了。</summary>
    private static readonly HashSet<string> NameAttributes = new(StringComparer.Ordinal)
    { "Content", "Header", "Text", "Title", "AutomationProperties.Name", "AutomationProperties.HelpText" };

    private static readonly Assembly[] WpfAssemblies =
        [typeof(FrameworkElement).Assembly, typeof(UIElement).Assembly, typeof(DependencyObject).Assembly, typeof(System.Windows.Media.Animation.Timeline).Assembly];

    private static readonly Dictionary<string, Type> Types = WpfAssemblies.Distinct().SelectMany(a => a.GetExportedTypes())
        .Where(t => t.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true && !t.IsNested)
        .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.OrderBy(t => t.Namespace!.Length).First(), StringComparer.Ordinal);

    internal static Type? TypeNamed(string name) => Types.GetValueOrDefault(name);

    /// <summary>給 SafeMarkup.SelfTest：一份文件走完清理。</summary>
    internal static XElement CleanForTest(XElement root)
    {
        var project = Project.FromText(root.ToString());
        return new Cleaner(project).Document(project.Docs[0].Root, [], []).Xaml;
    }

    private sealed class Tally { public int Docs, NotLoadable, Ok, Wrong, Tainted, NotInTree, AxeMissed; public readonly Dictionary<string, int> Other = new(StringComparer.Ordinal); }

    public static int Run(string[] args)
    {
        var axe = args.Contains("--axe");
        var verbose = Environment.GetEnvironmentVariable("ORACLE_VERBOSE") is not null;
        var t = new Tally();
        foreach (var root in args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
        {
            var project = Project.Load(root, []);
            var audit = Auditor.Run(project);
            var apps = project.Docs.Where(d => XamlFiles.IsApplication(d.Root, d.File)).ToList();
            foreach (var group in project.Docs.GroupBy(d => AppFor(d, apps)))
            {
                var cleaner = new Cleaner(project);
                cleaner.LoadApp(group.Key, verbose);
                foreach (var doc in group) One(project, audit, cleaner, doc, t, axe, verbose, Path.GetFileName(root.TrimEnd('/', '\\')));
            }
        }
        Console.WriteLine($"whole windows: {t.Docs} files loaded, {t.NotLoadable} not loadable; ok {t.Ok}, not in tree {t.NotInTree}, touched {t.Tainted}, axe-only {t.AxeMissed}, WRONG {t.Wrong}");
        foreach (var (k, n) in t.Other.OrderByDescending(p => p.Value).Take(25)) Console.WriteLine($"  axe, not judged: {n,5} {k}");
        return t.Wrong;
    }

    private static XamlDoc? AppFor(XamlDoc doc, List<XamlDoc> apps) =>
        apps.Where(a => doc.File.StartsWith(XamlFiles.DirOf(a.File), StringComparison.Ordinal)).OrderByDescending(a => a.File.Length).FirstOrDefault()
        ?? (apps.Count == 1 ? apps[0] : null);

    private static void One(Project project, AuditResult audit, Cleaner cleaner, XamlDoc doc, Tally t, bool axe, bool verbose, string name)
    {
        if (XamlFiles.IsApplication(doc.Root, doc.File) || doc.Root.Name.LocalName == "ResourceDictionary") return;
        var judged = audit.Judgements.Where(j => j.Check is "control-names" or "input-names" or "list-names" && j.File == doc.File && j.Verdict is Verdict.Pass or Verdict.Finding).ToList();
        var considered = audit.Judgements.Where(j => j.File == doc.File).Select(j => j.Element).ToHashSet();
        if (judged.Count == 0 && !axe) return;
        var ids = new Dictionary<XElement, string>();
        foreach (var e in axe ? doc.Root.DescendantsAndSelf().Where(e => !XamlMarkup.IsPropertyElement(e)) : judged.Select(j => j.Element))
            ids.TryAdd(e, "q" + ids.Count);

        var missing = new HashSet<string>(StringComparer.Ordinal);
        Window? window = null;
        HashSet<string> tainted = [];
        string? error = null;
        for (var attempt = 0; attempt < 25 && window is null; attempt++)
        {
            var (xaml, touched) = cleaner.Document(doc.Root, ids, missing);
            tainted = touched;
            try
            {
                window = Host(SafeMarkup.Parse(xaml, TypeNamed));
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message.Split('\n')[0];
                if (Environment.GetEnvironmentVariable("ORACLE_DUMP") is not null && error.Contains("Parameter")) Console.WriteLine(ex.GetBaseException().StackTrace + xaml);
                // 「Cannot find resource named 'X'」→ 拿掉用到它的屬性，那些元素不比
                var m = MissingRegex().Match(error);
                if (!m.Success || !missing.Add(m.Groups[1].Value)) break;
            }
        }
        if (window is null)
        {
            t.NotLoadable++;
            if (verbose) Console.WriteLine($"  not loadable {name}/{doc.File}: {error}");
            if (Environment.GetEnvironmentVariable("ORACLE_DUMP") is { } dump) File.WriteAllText(dump, cleaner.Document(doc.Root, ids, missing).Xaml.ToString());
            return;
        }
        t.Docs++;

        List<Node> nodes = [];
        List<AxeHit> hits = [];
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            (window.Left, window.Top, window.Width, window.Height) = axe ? (0, 0, 1200, 900) : (-20000, 0, 1600, 1600);
            (window.ShowActivated, window.ShowInTaskbar) = (false, false);
            window.Show();
            UiaOracle.Host.Pump();
            Expand(window);
            var handle = new WindowInteropHelper(window).Handle;
            nodes.AddRange(UiaOracle.Host.Tree(handle));
            foreach (var tabs in Visuals(window).OfType<TabControl>().ToList())
                for (var i = 0; i < Math.Min(tabs.Items.Count, 12); i++)
                {
                    if (tabs.SelectedIndex == i) continue;
                    tabs.SelectedIndex = i;
                    UiaOracle.Host.Pump();
                    Expand(window);
                    nodes.AddRange(UiaOracle.Host.Tree(handle));
                }
            if (axe) hits = UiaOracle.Host.Axe();
        }
        catch (Exception ex)
        {
            t.Docs--; t.NotLoadable++;
            if (verbose) Console.WriteLine($"  failed {name}/{doc.File}: {ex.GetBaseException().GetType().Name} {ex.GetBaseException().Message.Split('\n')[0]}");
            return;
        }
        finally { try { window.Close(); } catch (Exception) { /* 關不掉就算了 */ } }

        var byId = nodes.Where(n => n.Id.StartsWith('q')).ToLookup(n => n.Id);
        foreach (var j in judged)
        {
            var id = ids[j.Element];
            if (tainted.Contains(id) || cleaner.IsTainted(j.Element)) { t.Tainted++; continue; }
            var found = byId[id].ToList();
            if (found.Count == 0) { t.NotInTree++; continue; }
            if (found.All(n => Check.Agrees(j, n))) { t.Ok++; continue; }
            t.Wrong++;
            var bad = found.First(n => !Check.Agrees(j, n));
            var s = j.Element.ToString(SaveOptions.DisableFormatting);
            Console.WriteLine($"  WRONG {name}/{j.File}:{j.Line} tool {j.Verdict} {j.Kind?.Slug} ({j.Reason}) — UIA {bad.NameShown} ({bad.Type})\n        {NamespaceRegex().Replace(s, "")[..Math.Min(260, NamespaceRegex().Replace(s, "").Length)]}");
        }

        if (!axe) return;
        var back = ids.ToDictionary(p => p.Value, p => p.Key);
        foreach (var h in hits.DistinctBy(h => (h.Rule, h.Id)))
        {
            if (!back.TryGetValue(h.Id, out var e) || tainted.Contains(h.Id)) continue;
            var mine = audit.Judgements.Where(j => ReferenceEquals(j.Element, e)).ToList();
            if (mine.Any(j => j.Verdict is Verdict.Finding or Verdict.Suppressed or Verdict.Blocked or Verdict.NotJudged)) continue;
            var key = $"{h.Rule} {(mine.Count > 0 ? "PASSED " : "")}{e.Name.LocalName}";
            t.Other[key] = t.Other.GetValueOrDefault(key) + 1;
            if (mine.Count > 0 || verbose) { t.AxeMissed++; Console.WriteLine($"  axe {h.Rule} {name}/{doc.File}:{Context.LineOf(e)} {(mine.Count > 0 ? "tool Pass" : "not judged")} <{e.Name.LocalName}>"); }
        }
    }

    private static Window Host(object root)
    {
        if (root is Window w) return w;
        var window = new Window();
        if (root is Page p) window.Content = p; else window.Content = root as UIElement ?? throw new InvalidOperationException("root is not a UIElement: " + root.GetType().Name);
        return window;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject o)
    {
        yield return o;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            foreach (var c in Visuals(VisualTreeHelper.GetChild(o, i))) yield return c;
    }

    private static void Expand(Window window)
    {
        for (var round = 0; round < 4; round++)
        {
            var closed = Visuals(window).Where(v => v is Expander { IsExpanded: false } or TreeViewItem { IsExpanded: false, HasItems: true }).ToList();
            if (closed.Count == 0) break;
            foreach (var v in closed)
            {
                if (v is Expander e) e.SetCurrentValue(Expander.IsExpandedProperty, true);
                if (v is TreeViewItem i) i.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
            }
            UiaOracle.Host.Pump();
        }
    }

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex MissingRegex();

    [GeneratedRegex(" xmlns(:\\w+)?=\"[^\"]*\"")]
    private static partial Regex NamespaceRegex();

    // ───────────────────────── 清理 ─────────────────────────

    private sealed class Cleaner(Project project)
    {
        private readonly HashSet<string> _failedTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _appKeys = new(StringComparer.Ordinal);
        private Dictionary<XElement, string> _ids = [];
        private HashSet<string> _missing = [];
        private HashSet<string> _touched = [];
        private HashSet<string> _localKeys = [];

        /// <summary>隱含樣式沒載進來、或專案替這個類別寫了樣式（換成基底之後就套不到）。</summary>
        public bool IsTainted(XElement e) =>
            _failedTypes.Contains(XamlMarkup.IsWpf(e) ? e.Name.LocalName : "?#" + e.Name.LocalName);

        public void LoadApp(XamlDoc? app, bool verbose)
        {
            var target = Application.Current.Resources;
            target.MergedDictionaries.Clear();
            foreach (var k in target.Keys.Cast<object>().ToList()) target.Remove(k);
            _appKeys.Clear();
            foreach (var doc in project.Docs)
                foreach (var s in doc.Root.DescendantsAndSelf().Where(s => s.Name.LocalName == "Style"))
                    if ((string?)s.Attribute("TargetType") is { } tt && XamlMarkup.TypeId(s, tt) is { } id && id.Contains('#')) _failedTypes.Add("?#" + XamlMarkup.TypeName(tt));
            if (app is null) return;
            foreach (var assembly in (string[])["MahApps.Metro", "MaterialDesignThemes.Wpf", "HandyControl", "Wpf.Ui", "Wpf.Ui.Abstractions"])
                try { Assembly.Load(assembly); } catch (IOException) { }
            if (app.Root.Elements().FirstOrDefault(c => c.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)) is { } res)
                Load(app, res, target, [], verbose);
        }

        private void Load(XamlDoc doc, XElement container, ResourceDictionary into, HashSet<string> seen, bool verbose)
        {
            foreach (var e in container.Elements())
            {
                if (e.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
                {
                    foreach (var d in e.Elements())
                    {
                        var inner = new ResourceDictionary();
                        into.MergedDictionaries.Add(inner);
                        LoadDictionary(doc, d, inner, seen, verbose);
                    }
                    continue;
                }
                if (e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is null) { LoadDictionary(doc, e, into, seen, verbose); continue; }
                if (XamlMarkup.IsPropertyElement(e)) continue;
                Entry(e, into, verbose);
            }
        }

        private void LoadDictionary(XamlDoc doc, XElement d, ResourceDictionary into, HashSet<string> seen, bool verbose)
        {
            var source = (string?)d.Attribute("Source");
            if (KnownControls.LibraryOf(d) is not null || (source is not null && SafeMarkup.IsThemeSource(source)))
            {
                try { if (SafeMarkup.ParseTheme(d) is { } theme) { into.MergedDictionaries.Add(theme); CollectKeys(into); } }
                catch (Exception ex) { if (verbose) Console.WriteLine($"  theme not loadable {source ?? d.Name.LocalName}: {ex.GetBaseException().Message.Split('\n')[0]}"); }
                return;
            }
            if (source is not null && Find(doc, source) is { } other && seen.Add(other.File)) { Load(other, other.Root, into, seen, verbose); seen.Remove(other.File); }
            Load(doc, d, into, seen, verbose);
        }

        private void CollectKeys(ResourceDictionary d)
        {
            foreach (var k in d.Keys) if (k is string s) _appKeys.Add(s);
            foreach (var m in d.MergedDictionaries) CollectKeys(m);
        }

        private XamlDoc? Find(XamlDoc from, string source)
        {
            var path = source.Replace('\\', '/');
            var c = path.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
            if (c >= 0) path = path[(c + 11)..];
            else if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)) path = path[(path.LastIndexOf(",,,/", StringComparison.Ordinal) + 4)..];
            else if (!path.StartsWith('/'))
            {
                var exact = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from.FullPath)!, path));
                if (project.Docs.FirstOrDefault(d => Path.GetFullPath(d.FullPath).Equals(exact, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
            }
            path = path.TrimStart('/');
            return project.Docs.FirstOrDefault(d => d.File.Equals(path, StringComparison.OrdinalIgnoreCase) || d.File.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase));
        }

        private void Entry(XElement e, ResourceDictionary into, bool verbose)
        {
            string? why = null;
            (_ids, _missing, _touched, _localKeys) = ([], [], [], []);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var cleaned = Clean(e, resources: true);
                if (cleaned is null) { why = "dropped"; break; }
                try
                {
                    var parsed = (ResourceDictionary)SafeMarkup.Parse(Doc(new XElement(Wpf + "ResourceDictionary", cleaned)), TypeNamed);
                    foreach (var k in parsed.Keys.Cast<object>().ToList())
                    {
                        var v = parsed[k];
                        Try(v);
                        parsed.Remove(k);
                        into[k] = v;
                        if (k is string s) _appKeys.Add(s);
                    }
                    if (_missing.Count > 0 && e.Name.LocalName == "Style") Fail(e);
                    return;
                }
                catch (Exception ex)
                {
                    why = ex.GetBaseException().Message.Split('\n')[0];
                    var m = MissingRegex().Match(why);
                    if (!m.Success || !_missing.Add(m.Groups[1].Value)) break;
                }
            }
            if (e.Name.LocalName == "Style") Fail(e);
            if (verbose) Console.WriteLine($"  resource {(string?)e.Attribute(X + "Key") ?? e.Name.LocalName + " " + (string?)e.Attribute("TargetType")}: {why}");
        }

        /// <summary>範本是用到的時候才解析的：先試一次，壞的不要留在 App 的資源裡拖垮每一個畫面。</summary>
        private static void Try(object? v)
        {
            if (v is Style s) { s.Seal(); foreach (var t in s.Setters.OfType<Setter>().Select(x => x.Value).OfType<FrameworkTemplate>()) Try(t); }
            if (v is FrameworkTemplate ft) { 
                ft.Seal();
                // 單獨試的時候看不到後面才載入的資源：那不算壞
                try { ft.LoadContent(); }
                catch (XamlParseException ex) when (MissingRegex().IsMatch(ex.GetBaseException().Message)) { }
            }
        }

        private void Fail(XElement style)
        {
            if (style.Attribute(X + "Key") is null && XamlMarkup.TypeName((string?)style.Attribute("TargetType")) is { } type) _failedTypes.Add(type);
        }

        public (XElement Xaml, HashSet<string> Touched) Document(XElement root, Dictionary<XElement, string> ids, HashSet<string> missing)
        {
            (_ids, _missing, _touched) = (ids, missing, []);
            _localKeys = [.. root.Descendants().Select(d => (string?)d.Attribute(X + "Key")).OfType<string>()];
            var type = project.BaseOf(root) is { } b && Types.TryGetValue(b, out var bt) ? bt : null;
            var name = type is not null && typeof(Window).IsAssignableFrom(type) ? "Window" : type is not null && typeof(Page).IsAssignableFrom(type) ? "Page"
                : root.Name.LocalName.EndsWith("Window", StringComparison.Ordinal) ? "Window" : "UserControl";
            var copy = new XElement(Wpf + name);
            var content = new List<XElement>();
            foreach (var c in root.Elements())
            {
                if (XamlMarkup.IsPropertyElement(c))
                {
                    var p = c.Name.LocalName[(c.Name.LocalName.IndexOf('.') + 1)..];
                    if (p == "Resources") copy.AddFirst(Resources(c, name));
                    else if (p == "Content") content.AddRange(c.Elements().Select(i => Clean(i)).OfType<XElement>());
                    continue;
                }
                if (Clean(c) is { } k) content.Add(k);
            }
            // 資料範本也要看得到：每一個放一份出來
            var templates = root.Descendants().Where(d => d.Name.LocalName == "DataTemplate").ToList();
            var panel = new XElement(Wpf + "DockPanel");
            if (templates.Count > 0)
            {
                var host = new XElement(Wpf + "StackPanel", new XAttribute("DockPanel.Dock", "Bottom"));
                foreach (var d in templates)
                {
                    if (Clean(d) is not { } ct) continue;
                    ct.Attribute(X + "Key")?.Remove();
                    ct.Attribute("DataType")?.Remove();
                    host.Add(new XElement(Wpf + "ContentControl", new XAttribute("Content", "value"), new XElement(Wpf + "ContentControl.ContentTemplate", ct)));
                }
                panel.Add(host);
            }
            foreach (var c in content) panel.Add(c.Name.LocalName is "ContextMenu" or "Style" ? null : c);
            copy.Add(panel);
            return (Doc(copy), _touched);
        }

        private XElement Resources(XElement property, string owner)
        {
            var holder = new XElement(Wpf + (owner + ".Resources"));
            var dict = new XElement(Wpf + "ResourceDictionary");
            foreach (var e in Entries(property, []))
                if (Clean(e, resources: true) is { } k) dict.Add(k);
                else if (e.Name.LocalName == "Style") Fail(e);
            // 同一個鍵寫兩次（合併的字典蓋過）留最後一個
            foreach (var dup in dict.Elements().GroupBy(KeyOf).Where(g => g.Count() > 1).ToList())
                foreach (var extra in dup.SkipLast(1).ToList()) extra.Remove();
            holder.Add(dict);
            return holder;

            static string KeyOf(XElement e) => (string?)e.Attribute(X + "Key") ?? (string?)e.Attribute("TargetType") ?? (string?)e.Attribute("DataType") ?? Guid.NewGuid().ToString();
        }

        /// <summary>字典的項目，合併進來的檔案先、自己的後（後面的蓋過前面的）。</summary>
        private IEnumerable<XElement> Entries(XElement container, HashSet<string> seen)
        {
            foreach (var e in container.Elements().OrderBy(e => e.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal) ? 0 : 1))
            {
                var isDictionary = e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is null;
                if (isDictionary || e.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
                {
                    if (isDictionary && (string?)e.Attribute("Source") is { } s && project.Docs.FirstOrDefault(d => d.File == StyleIndex.FileOf(e)) is { } from
                        && Find(from, s) is { } other && seen.Add(other.File))
                        foreach (var i in Entries(other.Root, seen)) yield return i;
                    foreach (var i in Entries(e, seen)) yield return i;
                    continue;
                }
                if (!XamlMarkup.IsPropertyElement(e)) yield return e;
            }
        }

        private static XElement Doc(XElement e)
        {
            e.SetAttributeValue(XNamespace.Xmlns + "x", X.NamespaceName);
            e.SetAttributeValue(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=System.Runtime");
            return e;
        }

        private Type? TypeOf(XElement e) =>
            (XamlMarkup.IsWpf(e) ? TypeNamed(e.Name.LocalName) : project.BaseOf(e) is { } b ? TypeNamed(b) : null) is { } t && SafeMarkup.Allows(t) ? t : null;

        /// <summary>這個 Setter 改的屬性跟名稱有關（字型、顏色沒載進來不影響答案）。</summary>
        private static bool Matters(XElement setter) =>
            ((string?)setter.Attribute("Property"))?.Split('.').Last() is "Template" or "Content" or "Header" or "Text" or "ContentTemplate" or "HeaderTemplate" or "Command" or "Style" or "Name" or "HelpText"
                or "ItemContainerStyle" or "ItemTemplate";

        private void Touch(XElement e, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            // 元素自己（或它所在的樣式的使用者不好找，就整個型別）
            if (Environment.GetEnvironmentVariable("ORACLE_TOUCH") is not null) Console.WriteLine($"  touch@{line} {e.Name.LocalName} {StyleIndex.FileOf(e)}:{Context.LineOf(e)}");
            if (_ids.TryGetValue(e, out var id)) _touched.Add(id);
            if (e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Style") is { } s) Fail(s);
        }

        private XElement? Clean(XElement e, bool resources = false)
        {
            var local = e.Name.LocalName;
            if (e.Name.NamespaceName.StartsWith("clr-namespace:System", StringComparison.Ordinal) && local is "String" or "Double" or "Int32" or "Boolean" or "Thickness")
            {
                if (local is "Thickness") return null;
                var v = new XElement(XNamespace.Get("clr-namespace:System;assembly=System.Runtime") + local, e.Value);
                if ((string?)e.Attribute(X + "Key") is { } k) v.SetAttributeValue(X + "Key", k);
                return v;
            }
            if (Denied.Contains(local)) { if (local is "Frame" or "Popup" or "DocumentViewer" or "EventSetter") Touch(e.Parent ?? e); return null; }
            if (local == "Setter" && ((string?)e.Attribute("Property"))?.Split('.').Last() is { } sp && (DeniedAttributes.Contains(sp) || !SafeMarkup.AllowsProperty(sp) || sp is "ToolTip" or "FontFamily")) return null;
            var type = TypeOf(e);
            if (type is null || type.IsAbstract)
            {
                if (resources) return null;
                // 不認得的控制項：留它裡面的東西
                var box = new XElement(Wpf + "StackPanel");
                foreach (var c in e.Elements())
                    if (!XamlMarkup.IsPropertyElement(c) && TypeOf(c) is { } ct && typeof(UIElement).IsAssignableFrom(ct) && Clean(c) is { } k) box.Add(k);
                return e.Parent is { } p && TypeOf(p) is { } pt && (typeof(FrameworkTemplate).IsAssignableFrom(pt) || typeof(UIElement).IsAssignableFrom(pt)) ? box : null;
            }
            var copy = new XElement(Wpf + type.Name);
            var changedType = e.Name != copy.Name;
            foreach (var a in e.Attributes()) Attribute(e, copy, type, a, changedType);
            if (_ids.TryGetValue(e, out var id) && (typeof(UIElement).IsAssignableFrom(type) || typeof(ContentElement).IsAssignableFrom(type)))
            {
                copy.SetAttributeValue("AutomationProperties.AutomationId", id);
            }
            if (type == typeof(Style) && XamlMarkup.TypeName((string?)e.Attribute("TargetType")) is not null && copy.Attribute("TargetType") is null) return null;

            foreach (var node in e.Nodes())
            {
                if (node is XText text) { copy.Add(new XText(text.Value)); continue; }
                if (node is not XElement c) continue;
                if (XamlMarkup.IsPropertyElement(c)) { Property(e, copy, type, c); continue; }
                if (Clean(c, resources: type == typeof(ResourceDictionary)) is { } child) copy.Add(child);
            }
            // <Setter> 的值被拿掉了
            if (type == typeof(Setter) && (copy.Attribute("Property") is null || (copy.Attribute("Value") is null && !copy.HasElements))) { if (copy.Attribute("Property") is not null && Matters(e)) Touch(e); return null; }
            if (type == typeof(Trigger) && copy.Attribute("Property") is null) return null;
            if (type == typeof(Condition) && copy.Attribute("Property") is null) return null;
            if (type == typeof(MultiTrigger) && !copy.Descendants().Any(c => c.Name.LocalName == "Condition")) return null;
            if (e.Attribute(X + "Key") is not null && copy.Attribute(X + "Key") is null) return null;
            if (type == typeof(Trigger) && copy.Attribute("Value") is null && !copy.HasElements) return null;
            if (typeof(DataTrigger) == type && copy.Attribute("Binding") is null && !copy.Elements().Any(c => c.Name.LocalName == "DataTrigger.Binding")) return null;
            if (type == typeof(MultiDataTrigger)) return null;
            if (typeof(BindingBase).IsAssignableFrom(type)) return null;
            if (typeof(FrameworkTemplate).IsAssignableFrom(type)) DropDanglingSetters(copy);
            return copy;
        }

        /// <summary>範本觸發器指到的元素被拿掉了、或換成基底之後沒有那個屬性。</summary>
        private static void DropDanglingSetters(XElement template)
        {
            var names = new Dictionary<string, Type?>(StringComparer.Ordinal);
            foreach (var d in template.Descendants())
                if (XamlMarkup.NameOf(d) is { } n) names.TryAdd(n, Types.GetValueOrDefault(d.Name.LocalName));
            foreach (var s in template.Descendants().Where(d => d.Attribute("TargetName") is not null || d.Attribute("SourceName") is not null).ToList())
            {
                var target = (string?)s.Attribute("TargetName") ?? (string?)s.Attribute("SourceName");
                var property = (string?)s.Attribute("Property") ?? "";
                if (names.TryGetValue(target!, out var t) && t is not null && (property.Contains('.') || t.GetProperty(property) is not null)) continue;
                if (s.Parent is not null) s.Remove();
            }
            foreach (var m in template.Descendants().Where(d => d.Name.LocalName == "MultiTrigger" && !d.Descendants().Any(c => c.Name.LocalName == "Condition")).ToList()) m.Remove();
        }

        private void Property(XElement owner, XElement copy, Type type, XElement c)
        {
            var dot = c.Name.LocalName.IndexOf('.');
            var property = c.Name.LocalName[(dot + 1)..];
            if (property is "Triggers" && type != typeof(Style) && !typeof(FrameworkTemplate).IsAssignableFrom(type)) return;
            if (property is "EnterActions" or "ExitActions" or "CommandBindings" or "InputBindings" or "ContextMenu" or "DataContext" or "ItemsSource" or "Effect" or "VisualStateGroups" or "FontFamily" || DeniedAttributes.Contains(property) || !SafeMarkup.AllowsProperty(property)) return;
            if (property == "Resources")
            {
                if (type == typeof(Style) || typeof(FrameworkTemplate).IsAssignableFrom(type) || typeof(FrameworkElement).IsAssignableFrom(type) || typeof(FrameworkContentElement).IsAssignableFrom(type))
                    copy.AddFirst(Resources(c, type.Name));
                return;
            }
            var ownerType = c.Name.LocalName[..dot] == owner.Name.LocalName ? type : Types.GetValueOrDefault(c.Name.LocalName[..dot]);
            if (ownerType is null || Denied.Contains(ownerType.Name)) return;
            var attached = !ownerType.IsAssignableFrom(type);
            if (!attached && type.GetProperty(property) is null) return;
            if (property == "ToolTip") { copy.SetAttributeValue("ToolTip", "value"); return; }
            var holder = new XElement(Wpf + ((attached ? ownerType.Name : type.Name) + "." + property));
            var bound = false;
            foreach (var node in c.Nodes())
            {
                if (node is XText t) { holder.Add(new XText(t.Value)); continue; }
                if (node is not XElement k) continue;
                if (k.Name.LocalName is "Binding" or "MultiBinding" or "PriorityBinding") { bound = true; continue; }
                if (Clean(k) is { } cleaned) holder.Add(cleaned);
            }
            if (bound && NameAttributes.Contains(property)) holder.Add("value");
            if (holder.HasElements || holder.Value.Trim().Length > 0) copy.Add(holder);
            else if (c.HasElements && property is "Template" or "Content" or "Header" or "ContentTemplate" or "Style" or "Text" or "Inlines" or "Child" or "Children" or "Value") { if (owner.Name.LocalName != "Setter" || Matters(owner)) Touch(owner); }
        }

        private void Attribute(XElement e, XElement copy, Type type, XAttribute a, bool changedType)
        {
            if (a.IsNamespaceDeclaration) return;
            var name = a.Name.LocalName;
            if (a.Name.Namespace == X)
            {
                if (name is "Key" && KeyText(e, a.Value) is { } key) copy.SetAttributeValue(X + "Key", key);
                if (name is "Name") copy.SetAttributeValue(X + "Name", a.Value);
                return;
            }
            if (a.Name.NamespaceName.Length > 0 && !XamlMarkup.IsWpfNamespace(a.Name.NamespaceName))
            {
                // 函式庫用附加屬性給名稱（HintAssist.Hint）：這裡沒留，不能比
                if (KnownControls.LibraryOf(a.Name.NamespaceName) is { } lib && lib.NameFrom.Keys.Contains(name)) Touch(e);
                return;
            }
            if (DeniedAttributes.Contains(name) || !SafeMarkup.AllowsProperty(name) || name == "AutomationProperties.AutomationId") return;
            var dot = name.IndexOf('.');
            if (dot >= 0)
            {
                if (!Types.TryGetValue(name[..dot], out var owner) || owner.GetEvent(name[(dot + 1)..]) is not null) return;
                if (owner.GetMethod("Set" + name[(dot + 1)..]) is null && owner.GetField(name[(dot + 1)..] + "Property") is null) return;
                if (owner.Name is "VisualStateManager" or "WindowChrome" or "Storyboard" or "FocusManager") return;
            }
            else if (type.GetEvent(name) is not null || type.GetProperty(name) is null) return;

            var value = a.Value;
            // 字型（可以是路徑）跟名稱無關，整個不要；路徑、網址、UNC 形狀的值也是
            if (name.EndsWith("FontFamily", StringComparison.Ordinal) || (name == "Value" && ((string?)e.Attribute("Property"))?.EndsWith("FontFamily", StringComparison.Ordinal) == true)) return;
            if (!NameAttributes.Contains(name) && !XamlMarkup.IsMarkup(value) && (value.Contains("//", StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal) || value.Contains(";component/", StringComparison.OrdinalIgnoreCase))) return;
            if (name == "ToolTip") { if (value.Length > 0) copy.SetAttributeValue("ToolTip", "value"); return; }
            if (type == typeof(Style) && name == "TargetType" || name is "TargetType" or "DataType")
            {
                if (TypeText(e, value) is { } tt) copy.SetAttributeValue(name, tt);
                return;
            }
            if (type == typeof(Setter) || typeof(TriggerBase).IsAssignableFrom(type) || type == typeof(Condition))
            {
                if (name == "Property")
                {
                    if (value.Contains(':', StringComparison.Ordinal) || value.Contains('(', StringComparison.Ordinal)) return;
                    copy.SetAttributeValue(name, value);
                    return;
                }
                if (name is "TargetName" or "SourceName") { copy.SetAttributeValue(name, value); return; }
            }
            if (!XamlMarkup.IsMarkup(value)) { copy.SetAttributeValue(name, value); return; }

            var isName = NameAttributes.Contains(name) || (name == "Value" && ((string?)e.Attribute("Property"))?.Split('.').Last() is "Content" or "Header" or "Text" or "Name");
            switch (XamlMarkup.ExtensionName(value))
            {
                case "StaticResource" or "DynamicResource":
                {
                    if (XamlMarkup.ResourceKey(value) is { } rk)
                    {
                        if (!_missing.Contains(rk) && (_localKeys.Contains(rk) || _appKeys.Contains(rk))) { copy.SetAttributeValue(name, value); return; }
                        if (isName || name is "Style" or "Template" or "BasedOn" or "ContentTemplate" or "HeaderTemplate" or "Command" || (name == "Value" && Matters(e))) Touch(e);
                        if (isName) copy.SetAttributeValue(name, "value");
                        return;
                    }
                    if (XamlMarkup.TypeKey(value) is { } tk && !tk.Contains(':') && Types.ContainsKey(tk) && !_missing.Any(m => m.EndsWith("." + tk, StringComparison.Ordinal)))
                    { copy.SetAttributeValue(name, value); return; }
                    if (name is "BasedOn" or "Style") Touch(e);
                    return;
                }
                case "TemplateBinding":
                {
                    var arg = XamlMarkup.ExtensionBody(value);
                    if (!arg.Contains(':')) copy.SetAttributeValue(name, value);
                    return;
                }
                case "x:Null":
                    copy.SetAttributeValue(name, value);
                    return;
                case "x:Type":
                    if (TypeText(e, value) is { } ty) copy.SetAttributeValue(name, ty);
                    return;
                case "x:Static":
                {
                    var arg = XamlMarkup.Arguments(value).FirstOrDefault().Value ?? "";
                    if (!arg.Contains(':') && Types.ContainsKey(arg.Split('.')[0])) { copy.SetAttributeValue(name, value); return; }
                    if (isName) copy.SetAttributeValue(name, "value");
                    return;
                }
                case "Binding":
                {
                    // 綁到別的元素或自己（沒有轉換器）：留著，工具看得懂這種
                    var args = XamlMarkup.Arguments(value);
                    if (args.Any(x => x.Key is "ElementName" or "RelativeSource") && args.All(x => x.Key is "" or "Path" or "ElementName" or "RelativeSource" or "Mode")
                        && !value.Contains(':', StringComparison.Ordinal) && (isName || name is "AutomationProperties.LabeledBy" or "Target" or "Binding"))
                    { copy.SetAttributeValue(name, value); return; }
                    if (isName) { copy.SetAttributeValue(name, "value"); return; }
                    if (name is "Style" or "Template") Touch(e);
                    return;
                }
                default:
                    // {materialDesign:PackIcon X} 是圖示元素，不是文字
                    if (isName && XamlMarkup.ExtensionName(value) is { } ext && (ext.Contains("Icon", StringComparison.Ordinal) || ext.Contains("Image", StringComparison.Ordinal))) Touch(e);
                    if (isName) copy.SetAttributeValue(name, "value");
                    return;
            }
        }

        private static string? KeyText(XElement e, string v)
        {
            if (!XamlMarkup.IsMarkup(v)) return v;
            return XamlMarkup.ExtensionName(v) == "x:Type" && TypeTextStatic(v) is { } t ? t : null;
        }

        private string? TypeText(XElement at, string v)
        {
            if (TypeTextStatic(v) is { } t) return t;
            return null;
        }

        private static string? TypeTextStatic(string v)
        {
            var t = v.Trim();
            if (t.StartsWith('{')) t = XamlMarkup.ExtensionName(t) == "x:Type" ? XamlMarkup.Arguments(t).FirstOrDefault(a => a.Key is "" or "TypeName").Value ?? "" : "";
            return t.Length > 0 && !t.Contains(':') && Types.ContainsKey(t) ? "{x:Type " + t + "}" : null;
        }
    }

}
