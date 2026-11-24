using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlMarkup = XamlA11y.Core.Xaml.Markup;

namespace UiaOracle;

/// <summary>
/// 同一段 XAML：工具的判定 vs. 真的 WPF。任何一案「工具說錯」→ 回 1。
/// 工具說「不知道」（Blocked／NotJudged）不算錯，但要數出來：全部都不知道的比對等於沒比。
/// </summary>
internal static class Check
{
    private sealed class Tally
    {
        public int Ok, Unknown, Wrong;
        public readonly Dictionary<string, int> Why = new(StringComparer.Ordinal);
        public void Skip(string? why) { Unknown++; Why[why ?? ""] = Why.GetValueOrDefault(why ?? "") + 1; }
        public void Print(string title)
        {
            Console.WriteLine($"{title}: ok {Ok}, unknown {Unknown}, WRONG {Wrong}");
            foreach (var (w, n) in Why.OrderByDescending(x => x.Value).Take(12)) Console.WriteLine($"    unknown {n,4}  {w}");
        }
    }

    /// <summary>寫進暫存資料夾、照正式流程載入（App.xaml 合併佈景、csproj 寫套件版本）。</summary>
    private static AuditResult Audit(string windowXaml, string? theme)
    {
        var dir = Directory.CreateTempSubdirectory("xamla11y-oracle-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.xaml"), windowXaml);
            if (theme is not null)
            {
                File.WriteAllText(Path.Combine(dir, "App.xaml"),
                    $"<Application {Host.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>{Parts.Themes[theme].Replace('\'', '"')}</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>");
                File.WriteAllText(Path.Combine(dir, "App.csproj"),
                    "<Project><ItemGroup><PackageReference Include=\"MahApps.Metro\" Version=\"2.4.11\"/><PackageReference Include=\"MaterialDesignThemes\" Version=\"5.3.0\"/><PackageReference Include=\"HandyControl\" Version=\"3.5.1\"/><PackageReference Include=\"WPF-UI\" Version=\"4.3.0\"/></ItemGroup></Project>");
            }
            return Auditor.Run(Project.Load(dir, []));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string WindowXaml(string body, string resources) =>
        $"<Window {Host.Ns} Title=\"UiaOracle\"><Window.Resources><ResourceDictionary>{resources}</ResourceDictionary></Window.Resources><StackPanel x:Name=\"host\">{body}</StackPanel></Window>";

    /// <summary>UIA 的名稱念得出意思嗎。</summary>
    private static bool Meaningful(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(?:\w+\.){1,6}\w+( |$)") && (name.StartsWith("System.Windows.", StringComparison.Ordinal) || name.Contains(".Controls.", StringComparison.Ordinal))) return false;      // 型別名
        if (name.EnumerateRunes().All(r => Rune.GetUnicodeCategory(r) == System.Globalization.UnicodeCategory.PrivateUse)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(name, "^#[0-9A-F]{8}$")) return false;      // 筆刷的 ToString()
        return !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[MF]\s*-?\d+(\.\d+)?[ ,]");
    }

    private static void CompareName(Tally t, string title, Judgement j, Node? node)
    {
        if (Show) Console.WriteLine($"  {title,-52} UIA {node?.NameShown ?? "(not in tree)",-24} tool {j.Verdict} {j.Kind?.Slug}");
        if (j.Verdict is Verdict.Blocked or Verdict.NotJudged or Verdict.Suppressed) { t.Skip($"{j.Verdict}: {j.Reason}"); return; }
        if (node is null)
        {
            // UIA 樹上沒有：工具說有名稱或沒名稱都沒得比
            t.Skip("not in the UIA tree");
            return;
        }
        if (Agrees(j, node)) { t.Ok++; return; }
        t.Wrong++;
        Console.WriteLine($"  WRONG {title}: tool {j.Verdict} {j.Kind?.Slug} — UIA name {node.NameShown} ({node.Type})\n        {j.Element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)[..Math.Min(300, j.Element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Length)]}");
    }

    internal static bool Agrees(Judgement j, Node node) => j.Kind?.Slug switch
    {
        null or "symbol-name" => Meaningful(node.Name),
        "missing-name" or "text-not-in-name" or "input-no-name" => string.IsNullOrWhiteSpace(node.Name),
        "list-no-name" => !Meaningful(node.Name),
        "meaningless-name" => !Meaningful(node.Name) || j.Reason?.Contains("font", StringComparison.Ordinal) == true,
        _ => true,
    };

    private static IEnumerable<Judgement> NameJudgements(AuditResult r) => r.Judgements.Where(j => j.Rule is "R1" or "R2" or "R7");

    public static int Run()
    {
        var wrong = 0;
        wrong += Facts();
        wrong += Themes();
        wrong += Generated();
        wrong += Tabs();
        wrong += FocusCases("focus.txt", null);
        wrong += ThemeFocusCases();
        wrong += Sizes();
        Console.WriteLine(wrong == 0 ? "\nOK — the tool never contradicts WPF" : $"\n{wrong} WRONG");
        return wrong == 0 ? 0 : 1;
    }

    /// <summary>名稱的案例檔；指令列可以只跑一份（check-names round3-names.txt）。</summary>
    internal static bool Show;
    internal static string[] NameFiles = ["names.txt", "templates.txt", "more-names.txt", "round3-names.txt", "lists.txt"];

    public static int Facts()
    {
        var t = new Tally();
        foreach (var file in NameFiles)
            foreach (var c in Probe.Load(file))
            {
                try
                {
                    var result = Audit(WindowXaml(c.Body, c.Resources), null);
                    using var shown = Host.Show(c.Body, c.Resources);
                    var tree = Host.Tree(shown.Handle);
                    foreach (var j in NameJudgements(result))
                        if (XamlMarkup.NameOf(j.Element) is { } id && id is "p" or "t")
                            CompareName(t, c.Title, j, tree.FirstOrDefault(n => n.Id == XamlMarkup.NameOf(j.Element)));
                }
                catch (Exception ex) { Console.WriteLine($"  !! {c.Title}: {ex.GetBaseException().Message.Split('\n')[0]}"); t.Wrong++; }
            }
        t.Print("facts (names, templates)");
        return t.Wrong;
    }

    /// <summary>
    /// 把 facts 的 XAML 與 WPF 的答案錄成 JSON，給跨平台的單元測試比（CI 沒有 WPF）。
    /// 工具的答案**不**錄進去：錄的是真值，測試自己跑工具。
    /// </summary>
    public static void Record(string path)
    {
        var list = new List<object>();
        foreach (var file in NameFiles)
            foreach (var c in Probe.Load(file))
            {
                using var shown = Host.Show(c.Body, c.Resources);
                var names = Host.Tree(shown.Handle).Where(n => n.Id is "p" or "t").GroupBy(n => n.Id)
                    .ToDictionary(g => g.Key, g => new { name = g.First().Name, type = g.First().Type, control = g.First().Control });
                list.Add(new { file, title = c.Title, xaml = WindowXaml(c.Body, c.Resources), uia = names });
            }
        Diag.ShowFocusVisuals();
        var focus = new List<object>();
        foreach (var c in Probe.Load("focus.txt"))
        {
            var body = "<Button x:Name=\"first\" Content=\"first\"/><Border Height=\"10\"/>" + c.Body;
            using var shown = Host.Show(body, c.Resources);
            Probe.Measure(shown);
            if (Probe.ReachedTarget) focus.Add(new { title = c.Title, xaml = WindowXaml(body, c.Resources), visible = Probe.ChangedPixels > 0 });
        }
        var tabs = new List<object>();
        foreach (var c in Probe.Load("tabs.txt").Concat(Probe.Load("more-tabs.txt")))
        {
            var body = "<Button x:Name=\"a\"/>" + c.Body + "<Button x:Name=\"z\"/>";
            using var shown = Host.Show(body, c.Resources);
            tabs.Add(new { title = c.Title, xaml = WindowXaml(body, c.Resources), stops = Host.TabStops(shown) });
        }
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            recordedWith = $".NET {Environment.Version}, Windows {Environment.OSVersion.Version}, UI language {System.Globalization.CultureInfo.CurrentUICulture.Name}",
            names = list, focus, tabs,
        }, options).Replace("\r\n", "\n") + "\n");
        Console.WriteLine($"recorded {list.Count} names, {focus.Count} focus, {tabs.Count} tab cases → {path}");
    }

    public static int Themes()
    {
        var t = new Tally();
        foreach (var group in Probe.Load("themes.txt").GroupBy(x => x.Group))
        {
            Parts.UseTheme(group.Key);
            foreach (var c in group)
            {
                try
                {
                    var result = Audit(WindowXaml(c.Body, c.Resources), group.Key);
                    using var shown = Host.Show(c.Body, c.Resources);
                    var tree = Host.Tree(shown.Handle);
                    foreach (var j in NameJudgements(result))
                        if (XamlMarkup.NameOf(j.Element) == "p") CompareName(t, $"{group.Key}/{c.Title}", j, tree.FirstOrDefault(n => n.Id == "p"));
                }
                catch (Exception ex) { Console.WriteLine($"  !! {group.Key}/{c.Title}: {ex.GetBaseException().Message.Split('\n')[0]}"); t.Wrong++; }
            }
        }
        Parts.UseTheme(null);
        t.Print("themes");
        return t.Wrong;
    }

    // ───────────────────────── 窮舉 ─────────────────────────

    private static readonly (string Tag, string Open, string Close, string Property)[] Controls =
    [
        ("Button", "", "", "Content"), ("ToggleButton", "", "", "Content"), ("RepeatButton", "", "", "Content"), ("CheckBox", "", "", "Content"), ("RadioButton", "", "", "Content"),
        ("MenuItem", "<Menu>", "</Menu>", "Header"), ("TabItem", "<TabControl>", "</TabControl>", "Header"), ("Expander", "", "", "Header"),
        ("ListBoxItem", "<ListBox>", "</ListBox>", "Content"), ("ListViewItem", "<ListView>", "</ListView>", "Content"), ("TreeViewItem", "<TreeView>", "</TreeView>", "Header"),
    ];

    /// <summary>內容：屬性寫法（Attr）或元素寫法（Body）。</summary>
    private static readonly (string Title, string? Attr, string? Body)[] Contents =
    [
        ("none", null, null),
        ("text", "Save", null), ("access", "_Save", null), ("empty", "", null), ("space", "  ", null), ("symbol", "×", null), ("glyph", "&#xE74D;", null), ("digits", "42", null),
        ("text+glyph", "&#xE74D; Save", null), ("escaped", "{}{Save}", null), ("underscores", "a__b", null), ("cjk", "儲存", null),
        ("textblock", null, "<TextBlock Text='Save'/>"), ("textblock-body", null, "<TextBlock>Save</TextBlock>"), ("runs", null, "<TextBlock><Run Text='Sa'/><Bold>ve</Bold></TextBlock>"),
        ("textblock-empty", null, "<TextBlock/>"), ("textblock-glyph", null, "<TextBlock FontFamily='Segoe MDL2 Assets' Text='&#xE74D;'/>"), ("textblock-marlett", null, "<TextBlock FontFamily='Marlett' Text='r'/>"),
        ("textblock-image", null, "<TextBlock><InlineUIContainer><Image Width='9'/></InlineUIContainer></TextBlock>"), ("accesstext", null, "<AccessText Text='_Save'/>"),
        ("label", null, "<Label Content='Save'/>"), ("label-panel", null, "<Label><StackPanel><TextBlock Text='Save'/></StackPanel></Label>"), ("contentcontrol-empty", null, "<ContentControl/>"),
        ("contentcontrol-image", null, "<ContentControl><Image Width='9'/></ContentControl>"), ("usercontrol", null, "<UserControl><TextBlock Text='Save'/></UserControl>"),
        ("groupbox", null, "<GroupBox Header='Save'/>"), ("checkbox", null, "<CheckBox Content='Save'/>"),
        ("panel", null, "<StackPanel><Image Width='9'/><TextBlock Text='Save'/></StackPanel>"), ("panel-icon", null, "<StackPanel><Image Width='9'/></StackPanel>"), ("border", null, "<Border><TextBlock Text='Save'/></Border>"),
        ("viewbox", null, "<Viewbox Height='9'><Path Data='M0,0 L9,9 Z' Fill='Black'/></Viewbox>"), ("path", null, "<Path Data='M0,0 L9,9 Z' Fill='Black'/>"), ("image", null, "<Image Width='9'/>"), ("ellipse", null, "<Ellipse Width='9' Height='9'/>"),
        ("image-named", null, "<Image Width='9' AutomationProperties.Name='Save'/>"), ("textblock-named", null, "<TextBlock AutomationProperties.Name='Delete' Text='Save'/>"), ("textbox", null, "<TextBox Text='Save'/>"),
        ("string", null, "<sys:String>Save</sys:String>"), ("int", null, "<sys:Int32>5</sys:Int32>"), ("geometry", null, "<PathGeometry Figures='M0,0 L9,9 Z'/>"), ("drawingimage", null, "<DrawingImage/>"), ("brush", null, "<SolidColorBrush Color='Red'/>"),
        ("res-string", "{StaticResource s}", null), ("res-image", "{StaticResource i}", null), ("res-geometry", "{StaticResource g}", null), ("res-textblock", "{StaticResource t}", null), ("res-dynamic", "{DynamicResource s}", null),
        ("xnull", "{x:Null}", null), ("bound-self-tag", "{Binding Tag, RelativeSource={RelativeSource Self}}", null),
    ];

    private static readonly (string Title, string Attr, string Extra)[] Namings =
    [
        ("plain", "", ""),
        ("name", "AutomationProperties.Name='Named'", ""),
        ("name-empty", "AutomationProperties.Name=''", ""),
        ("name-glyph", "AutomationProperties.Name='&#xE74D;'", ""),
        ("name-res", "AutomationProperties.Name='{DynamicResource s}'", ""),
        ("name-tooltip", "ToolTip='Tip' AutomationProperties.Name='{Binding ToolTip, RelativeSource={RelativeSource Self}}'", ""),
        ("name-no-tooltip", "AutomationProperties.Name='{Binding ToolTip, RelativeSource={RelativeSource Self}}'", ""),
        ("tooltip", "ToolTip='Tip'", ""),
        ("labeled", "AutomationProperties.LabeledBy='{Binding ElementName=lab~}'", "<TextBlock x:Name='lab~' Text='Label'/>"),
        ("labeled-empty", "AutomationProperties.LabeledBy='{Binding ElementName=lab~}'", "<StackPanel x:Name='lab~'/>"),
        ("labeled-label", "AutomationProperties.LabeledBy='{Binding ElementName=lab~}'", "<Label x:Name='lab~'><TextBlock Text='Label'/></Label>"),
        ("target", "", "<Label Content='Label' Target='{Binding ElementName=p~}'/>"),
        ("command", "Command='ApplicationCommands.Copy'", ""),
        ("command-name", "Command='ApplicationCommands.Copy' AutomationProperties.Name='Named'", ""),
        ("style-name", "Style='{StaticResource named}'", ""),
        ("style-trigger", "Style='{StaticResource trig}'", ""),
    ];

    private static readonly string[] InputTags = ["TextBox", "PasswordBox", "RichTextBox", "ComboBox", "Slider", "DatePicker"];

    public static int Generated()
    {
        var t = new Tally();
        var cases = new List<(string Title, string Xaml)>();
        var id = 0;
        foreach (var c in Controls)
            foreach (var content in Contents)
                foreach (var naming in Namings)
                {
                    if (naming.Title.StartsWith("command") && c.Tag is "TabItem" or "Expander" or "ListBoxItem" or "ListViewItem" or "TreeViewItem" && naming.Title.StartsWith("command")) continue;
                    id++;
                    var attr = (naming.Attr + (content.Attr is { } a ? $" {c.Property}='{a}'" : "")).Replace("~", id.ToString());
                    // 樣式的 TargetType 是 FrameworkElement：任何控制項都能套
                    var body = content.Body is null ? "" : c.Property == "Content" ? content.Body : $"<{c.Tag}.Header>{content.Body}</{c.Tag}.Header>";
                    var el = $"<{c.Tag} x:Name='p{id}' {attr}>{body}</{c.Tag}>";
                    cases.Add(($"{c.Tag}/{content.Title}/{naming.Title}",
                        $"<StackPanel><StackPanel.Resources><sys:String x:Key='s'>Res</sys:String><Image x:Key='i' Width='9'/><Geometry x:Key='g'>M0,0 L9,9 Z</Geometry><TextBlock x:Key='t' Text='ResText'/>" +
                        $"<Style x:Key='named' TargetType='FrameworkElement'><Setter Property='AutomationProperties.Name' Value='Styled'/></Style>" +
                        $"<Style x:Key='trig' TargetType='FrameworkElement'><Style.Triggers><Trigger Property='IsEnabled' Value='True'><Setter Property='AutomationProperties.Name' Value='Trig'/></Trigger></Style.Triggers></Style></StackPanel.Resources>" +
                        $"{naming.Extra.Replace("~", id.ToString())}{c.Open}{el}{c.Close}</StackPanel>"));
                }
        foreach (var tag in InputTags)
            foreach (var naming in Namings)
            {
                if (naming.Title.StartsWith("command")) continue;
                id++;
                cases.Add(($"{tag}/{naming.Title}",
                    $"<StackPanel><StackPanel.Resources><sys:String x:Key='s'>Res</sys:String><Style x:Key='named' TargetType='FrameworkElement'><Setter Property='AutomationProperties.Name' Value='Styled'/></Style><Style x:Key='trig' TargetType='FrameworkElement'><Style.Triggers><Trigger Property='IsEnabled' Value='True'><Setter Property='AutomationProperties.Name' Value='Trig'/></Trigger></Style.Triggers></Style></StackPanel.Resources>" +
                    $"{naming.Extra.Replace("~", id.ToString())}<{tag} x:Name='p{id}' {naming.Attr.Replace("~", id.ToString())}/></StackPanel>"));
            }

        const int perWindow = 60;
        for (var start = 0; start < cases.Count; start += perWindow)
        {
            var batch = cases.Skip(start).Take(perWindow).ToList();
            var body = string.Concat(batch.Select(b => b.Xaml)).Replace('\'', '"');
            var result = Audit(WindowXaml(body, ""), null);
            var titles = batch.SelectMany((b, i) => new[] { (Id: "p" + (start + i + 1), b.Title) }).ToDictionary(x => x.Id, x => x.Title);
            using var shown = Host.Show(body, "", 420, 300);
            var tree = Host.Tree(shown.Handle).Where(n => n.Id.Length > 0).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var j in NameJudgements(result))
                if (XamlMarkup.NameOf(j.Element) is { } name && titles.TryGetValue(name, out var title))
                    CompareName(t, title, j, tree.GetValueOrDefault(name));
        }
        t.Print($"generated ({cases.Count} cases)");
        return t.Wrong;
    }

    // ───────────────────────── 鍵盤、焦點、大小 ─────────────────────────

    public static int Tabs()
    {
        var t = new Tally();
        foreach (var c in Probe.Load("tabs.txt").Concat(Probe.Load("more-tabs.txt")))
        {
            try
            {
                var body = "<Button x:Name=\"a\"/>" + c.Body + "<Button x:Name=\"z\"/>";
                var result = Audit(WindowXaml(body, c.Resources), null);
                using var shown = Host.Show(body, c.Resources);
                var stops = Host.TabStops(shown);
                foreach (var j in result.Judgements.Where(x => x.Rule is "R5" or "R4" && XamlMarkup.NameOf(x.Element) == "p"))
                {
                    if (j.Verdict is Verdict.Blocked or Verdict.NotJudged) { t.Skip($"{j.Rule} {j.Reason}"); continue; }
                    // PART_EditableTextBox 之類的內部零件算到得了
                    var reached = stops.Contains("p") || stops.Any(s => s.StartsWith("PART_", StringComparison.Ordinal));
                    var excused = j.Verdict == Verdict.Pass && j.Reason is not null;      // Enter／Esc、存取鍵：不靠 Tab
                    var ok = j.Verdict == Verdict.Finding ? !reached : j.Rule == "R4" || reached || excused || NotLive(c.Body);
                    if (ok) t.Ok++;
                    else { t.Wrong++; Console.WriteLine($"  WRONG {c.Title}: tool {j.Rule} {j.Verdict}; stops {string.Join(" → ", stops)}"); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  !! {c.Title}: {ex.GetBaseException().Message.Split('\n')[0]}"); t.Wrong++; }
        }
        t.Print("tab order");
        return t.Wrong;

        // 停用、收合、容器的 TabNavigation=None：R5 只判元素自己明寫的 False，這些刻意不管
        static bool NotLive(string body) => body.Contains("IsEnabled=\"False\"") || body.Contains("Collapsed") || body.Contains("TabNavigation=");
    }

    public static int FocusCases(string file, string? theme)
    {
        Diag.ShowFocusVisuals();
        var t = new Tally();
        foreach (var c in Probe.Load(file))
        {
            try
            {
                var body = "<Button x:Name=\"first\" Content=\"first\"/><Border Height=\"10\"/>" + c.Body;
                var result = Audit(WindowXaml(body, c.Resources), theme);
                using var shown = Host.Show(body, c.Resources);
                var measured = Probe.Measure(shown);
                var judged = result.Judgements.Where(x => x.Rule == "R3").ToList();
                if (judged.Count == 0) continue;
                if (!Probe.ReachedTarget) { t.Skip("Tab did not reach it in the oracle"); continue; }
                foreach (var j in judged)
                {
                    if (j.Verdict is Verdict.Blocked or Verdict.NotJudged) { t.Skip(j.Reason); continue; }
                    var ok = (j.Verdict == Verdict.Finding) == (Probe.ChangedPixels == 0);
                    if (ok) t.Ok++;
                    else { t.Wrong++; Console.WriteLine($"  WRONG {c.Title}: tool {j.Verdict} ({j.Reason}); {measured}"); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  !! {c.Title}: {ex.GetBaseException().Message.Split('\n')[0]}"); t.Wrong++; }
        }
        t.Print("focus visible");
        return t.Wrong;
    }

    /// <summary>佈景底下的 R3：每個佈景一組（等動畫跑完再比）。</summary>
    public static int ThemeFocusCases()
    {
        var wrong = 0;
        Probe.SettleMilliseconds = 600;
        foreach (var theme in (string[])["mahapps", "materialdesign", "hc", "wpfui", "fluent"])
        {
            Parts.UseTheme(theme);
            Console.WriteLine($"[{theme}]");
            wrong += FocusCases("focus-themed.txt", theme);
        }
        Probe.SettleMilliseconds = 0;
        Parts.UseTheme(null);
        return wrong;
    }

    /// <summary>R6：工具算的上界 ≥ 真的大小（BoundingRectangle），而且工具報的真的小於 24。</summary>
    public static int Sizes()
    {
        var t = new Tally();
        foreach (var c in Probe.Load("sizes.txt"))
        {
            try
            {
                var result = Audit(WindowXaml(c.Body, c.Resources), null);
                using var shown = Host.Show(c.Body, c.Resources);
                var dpi = VisualTreeHelper.GetDpi(shown.Window).DpiScaleX;
                var tree = Host.Tree(shown.Handle);
                foreach (var j in result.Judgements.Where(x => x.Rule == "R6" && XamlMarkup.NameOf(x.Element) == "p"))
                {
                    var n = tree.First(x => x.Id == "p");
                    double w = n.Width / dpi, h = n.Height / dpi;
                    if (j.Verdict is Verdict.NotJudged or Verdict.Blocked) { t.Skip(j.Reason); continue; }
                    var small = w < 23.6 || h < 23.6;
                    var expectFinding = c.Group == "fail";
                    var ok = j.Verdict == Verdict.Finding ? small && expectFinding : !expectFinding;
                    if (ok) t.Ok++;
                    else { t.Wrong++; Console.WriteLine($"  WRONG {c.Title}: tool {j.Verdict} ({j.Reason}); WPF {w:0.#}×{h:0.#}"); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  !! {c.Title}: {ex.GetBaseException().Message.Split('\n')[0]}"); t.Wrong++; }
        }
        t.Print("target size");
        return t.Wrong;
    }
}
