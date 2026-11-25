using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Fix;

/// <summary>名稱從哪裡來。</summary>
public enum FixSource
{
    /// <summary>已經指到它的 &lt;Label Target&gt;。</summary>
    Label,
    /// <summary>畫面上已經有的字。</summary>
    ShownText,
    /// <summary>已經寫好的 ToolTip。</summary>
    ToolTip,
    /// <summary>前面那段字（猜的，要人看過；--guess 才做）。</summary>
    TextBefore,
}

/// <summary>一處修改。</summary>
/// <param name="Value">寫進去的值。</param>
/// <param name="From">名稱來源在第幾行（標籤）；沒有是 0。</param>
/// <param name="Guess">要人看過的：猜的標籤、綁定的字（執行時才知道是什麼）。</param>
/// <param name="AtRunTime">接上之後名稱是什麼，靜態看不到（標籤的字是綁定的）。</param>
public sealed record FixEdit(string File, int Line, string Element, Kind Kind, FixSource Source, string Attribute, string Value, int From, bool Guess, bool AtRunTime);

public sealed record FixPlan(IReadOnlyList<FixEdit> Edits, IReadOnlyDictionary<string, byte[]> Files, int Left, IReadOnlyList<string> Untouched);

/// <summary>
/// 修法固定的名稱問題：標籤已經在那裡、字已經在畫面上、ToolTip 已經寫好。把它接上去，其他不動。
/// 接錯比沒有名稱糟（閘門變綠，以後也查不出來），所以不確定的一律不接：會變的字、看得到一下子的字、說明、快速鍵、執行時才知道的。
/// 改完在記憶體裡重新稽核一次：問題沒消失、或多出別的問題的那一處就不改。
/// </summary>
public static partial class Fixer
{
    private const string NameProperty = "AutomationProperties.Name";
    private const string LabeledBy = "AutomationProperties.LabeledBy";
    private const int MaxRounds = 5;

    /// <param name="load">用改過的內容（檔案 → 內容）再讀一次專案；空的就是原本的。</param>
    /// <param name="read">檔案的位元組。</param>
    /// <param name="only">只改這些檔案；null 是全部。</param>
    public static FixPlan Plan(Func<IReadOnlyDictionary<string, string>, Project> load, Func<string, byte[]> read, AuditOptions options, bool guess, IReadOnlySet<string>? only = null)
    {
        var skip = new HashSet<(string, int, string)>();
        for (var round = 0; ; round++)
        {
            var project = load(new Dictionary<string, string>());
            var before = Auditor.Run(project, options);
            var sources = new Dictionary<string, XamlSource>(StringComparer.Ordinal);
            var untouched = new SortedSet<string>(StringComparer.Ordinal);
            var edits = new List<FixEdit>();
            var planner = new Planner(project, options, guess);
            foreach (var j in before.Judgements.Where(j => j.Verdict == Verdict.Finding && j.Kind is not null && (only is null || only.Contains(j.File))))
            {
                if (skip.Contains((j.File, j.Line, j.Element.Name.LocalName)) || untouched.Contains(j.File)) continue;
                if (planner.Suggest(j) is not { } s) continue;
                if (!sources.TryGetValue(j.File, out var source))
                {
                    if (XamlSource.Read(read(j.File)) is not { } ok) { untouched.Add(j.File); continue; }
                    sources[j.File] = source = ok;
                }
                // 標籤取名字與接上去是一起的：有一個插不進去就都不做
                if (!source.CanInsert(j.Element) || s.Name is { } n && !source.CanInsert(n.Element)) continue;
                if (s.Name is { } named) source.Insert(named.Element, named.Attribute, named.Value);
                source.Insert(j.Element, s.Attribute, s.Value);
                edits.Add(new FixEdit(j.File, j.Line, j.Element.Name.LocalName, j.Kind!, s.Source, s.Attribute, s.Value, s.From, s.Guess, s.AtRunTime));
            }

            // 寫得回去的才算：編不進這個編碼的字會變成「?」
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (file, source) in sources.Where(p => p.Value.Changed))
                if (source.ToBytes(read(file)) is { } written) bytes[file] = written;
                else untouched.Add(file);
            edits.RemoveAll(e => untouched.Contains(e.File));

            var texts = bytes.Keys.ToDictionary(f => f, f => sources[f].Result(), StringComparer.Ordinal);
            var wrong = edits.Count == 0 ? [] : Check(before, load, texts, options, edits);
            if (wrong.Count == 0 || round >= MaxRounds)
            {
                // 五輪還有問題：有問題的檔案整個不動
                var bad = wrong.Select(w => w.File).ToHashSet(StringComparer.Ordinal);
                foreach (var f in bad) untouched.Add(f);
                var kept = edits.Where(e => !bad.Contains(e.File)).ToList();
                var mine = before.Findings.Count(f => only is null || only.Contains(f.File));
                return new FixPlan(kept, bytes.Where(b => !bad.Contains(b.Key)).ToDictionary(StringComparer.Ordinal), mine - kept.Count, [.. untouched]);
            }
            foreach (var w in wrong) skip.Add((w.File, w.Line, w.Element));
        }
    }

    /// <summary>改完沒有用的、弄出別的問題的。讀不懂的檔案：它的每一處都算。</summary>
    internal static List<FixEdit> Check(AuditResult before, Func<IReadOnlyDictionary<string, string>, Project> load, Dictionary<string, string> texts, AuditOptions options, List<FixEdit> edits)
    {
        var after = Auditor.Run(load(texts), options);
        var broken = after.ParseErrors.ToHashSet(StringComparer.Ordinal);
        // 插屬性可能換行：行號會動，用「第幾個」對
        static Dictionary<(string, string, string), int> Count(AuditResult r) => r.Findings.GroupBy(f => (f.File, f.Kind.Slug, f.Element)).ToDictionary(g => g.Key, g => g.Count());
        var (a, b) = (Count(before), Count(after));
        var wrong = edits.Where(e => broken.Contains(e.File)).ToList();
        foreach (var g in edits.Where(e => !broken.Contains(e.File)).GroupBy(e => (e.File, e.Kind.Slug, e.Element)))
            if (b.GetValueOrDefault(g.Key) > a.GetValueOrDefault(g.Key) - g.Count()) wrong.AddRange(g);
        // 多出來的（同一組名稱的 info 不算：兩顆本來就沒名稱的按鈕，接上之後念起來一樣，是真的一樣）
        foreach (var (key, n) in b)
            if (n > a.GetValueOrDefault(key) && key.Item2 != Kinds.SameName.Slug && !broken.Contains(key.Item1)) wrong.AddRange(edits.Where(e => e.File == key.Item1 && !wrong.Contains(e)));
        return wrong;
    }

    private sealed record Suggestion(FixSource Source, string Attribute, string Value, int From, bool Guess = false, bool AtRunTime = false, (XElement Element, string Attribute, string Value)? Name = null);

    private sealed partial class Planner(Project project, AuditOptions options, bool guess)
    {
        private readonly NameResolver _names = new(project);
        private readonly Dictionary<XElement, string> _given = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<XDocument, Dictionary<string, XElement>> _labels = [];
        private readonly Dictionary<XDocument, HashSet<string>> _taken = [];

        public Suggestion? Suggest(Judgement j)
        {
            var e = j.Element;
            if (j.Kind is not { } kind || options.SeverityOf(kind) == Severity.Off) return null;
            // 附加屬性不寫前綴：預設命名空間要是 WPF
            if (e.GetDefaultNamespace().NamespaceName != Markup.WpfNamespace) return null;
            // 已經用別的寫法設過（屬性元素、別的前綴）：再加一個是重複的屬性
            if (Sets(e, NameProperty) || Sets(e, LabeledBy)) return null;
            if (kind == Kinds.TextNotName) return Shown(e);
            // 圖示字型的字元、× 這種符號：ToolTip 寫了它做什麼的就用它（頁籤、清單項目的是型別名，一樣）
            if (kind == Kinds.MeaninglessName || kind == Kinds.SymbolName) return FromLabel(e) ?? FromToolTip(e);
            if (kind != Kinds.MissingName && kind != Kinds.InputNoName && kind != Kinds.ListNoName && kind != Kinds.WidgetNoName) return null;
            return FromLabel(e) ?? FromToolTip(e) ?? (guess ? FromTextBefore(e) : null);
        }

        private static bool Sets(XElement e, string property) =>
            e.Attributes().Any(a => a.Name.LocalName == property) || e.Elements().Any(c => c.Name.LocalName == property);

        // ───────────────────────── 標籤 ─────────────────────────

        private Suggestion? FromLabel(XElement e)
        {
            if (Markup.NameOf(e) is not { Length: > 0 } name) return null;
            if (!_labels.TryGetValue(e.Document!, out var labels))
            {
                _labels[e.Document!] = labels = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (var d in e.Document!.Root!.Descendants().Where(d => d.Name.LocalName == "Label" && Markup.IsWpf(d)))
                    if (Markup.ReferencedElement((string?)d.Attribute("Target")) is { } target) labels.TryAdd(target, d);
            }
            return labels.TryGetValue(name, out var label) ? By(e, label, FixSource.Label) : null;
        }

        private Suggestion? FromTextBefore(XElement e) =>
            NameRules.LabelBefore(project, e) is { } label && Markup.IsWpf(label) ? By(e, label, FixSource.TextBefore) : null;

        /// <summary>LabeledBy 指到標籤；標籤沒有 x:Name 就給一個。</summary>
        private Suggestion? By(XElement e, XElement label, FixSource source)
        {
            if (Scope(e) != Scope(label) || project.ControlOf(label) is not { } control) return null;
            var said = _names.Resolve(label, control).Kind;
            if (said is not (NameKind.Named or NameKind.DataBound)) return null;
            var name = Markup.NameOf(label) ?? _given.GetValueOrDefault(label);
            (XElement, string, string)? add = null;
            if (name is null)
            {
                if (label.Elements().Any(c => c.Name.LocalName.EndsWith(".Name", StringComparison.Ordinal))) return null;
                // 放在專案自己的控制項裡面的元素不能取名字（MC3093）：它的 XAML 在哪裡、是不是繼承來的看不準，一律不取
                if (label.Ancestors().Any(a => a.Parent is not null && !Markup.IsPropertyElement(a) && project.IsOwn(a))) return null;
                if (label.GetPrefixOfNamespace(Markup.XamlNamespace) is not { Length: > 0 } x) return null;
                _given[label] = name = FreshName(e, label);
                add = (label, x + ":Name", name);
            }
            return new Suggestion(source, LabeledBy, $"{{Binding ElementName={name}}}", Context.LineOf(label), source == FixSource.TextBefore, said == NameKind.DataBound, add);
        }

        /// <summary>名稱範圍：最近的範本，沒有就是整個檔案。</summary>
        private static XElement? Scope(XElement e) => e.Ancestors().FirstOrDefault(a => a.Name.LocalName is "ControlTemplate" or "DataTemplate" or "HierarchicalDataTemplate" or "ItemsPanelTemplate");

        /// <summary>不跟檔案裡的名字、專案 C# 裡任何叫這個的東西撞（x:Name 會變成類別的欄位；partial 的另一半、基底類別都在別的檔案）。</summary>
        private string FreshName(XElement e, XElement label)
        {
            var text = (string?)label.Attribute("Content") ?? (string?)label.Attribute("Text") ?? label.Value;
            var words = Markup.IsMarkup(text) ? [] : AsciiWord().Matches(text.Replace("_", "", StringComparison.Ordinal)).Select(m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..]).Take(3).ToArray();
            var stem = words.Length > 0 ? string.Concat(words) : Markup.NameOf(e) is { Length: > 0 } n && char.IsAsciiLetter(n[0]) ? char.ToUpperInvariant(n[0]) + n[1..] : "Field";
            if (!_taken.TryGetValue(e.Document!, out var taken))
                _taken[e.Document!] = taken = e.Document!.Root!.DescendantsAndSelf().Select(Markup.NameOf).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var code = project.CodeBehind(StyleIndex.FileOf(e));
            for (var i = 1; ; i++)
            {
                var name = stem + "Label" + (i == 1 ? "" : i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (project.CodeNamesLabel(name) || code.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (taken.Add(name)) return name;
            }
        }

        // ───────────────────────── 畫面的字、ToolTip ─────────────────────────

        /// <summary>畫面上的字：只有一段、一直看得到、寫死的。綁定的要 --guess（可能是徽章、狀態，執行時才知道）。</summary>
        private Suggestion? Shown(XElement e)
        {
            if (_names.SingleShownText(e) is not { } shown) return null;
            if (Usable(e, shown.Raw, shown.Literal) is not { } v) return null;
            if (!shown.Literal && Markup.IsMarkup(shown.Raw))
            {
                // 資源寫在裡面（<StackPanel.Resources>）：外面的屬性找不到，執行時丟例外
                foreach (Match m in ResourceRef().Matches(shown.Raw))
                    if (project.Styles.FindResources(e, m.Groups["k"].Value) is not [var found, ..] || found.Ancestors().Contains(e)
                        || project.Styles.FindResources(shown.At, m.Groups["k"].Value) is not [var inside, ..] || inside != found) return null;
                if (Markup.ResourceKey(shown.Raw) is { } key && project.Styles.FindResources(e, key) is not [{ Name.LocalName: "String" }, ..]) return null;
                if (Markup.ExtensionName(shown.Raw) == "Binding")
                {
                    if (!guess || shown.At.AncestorsAndSelf().TakeWhile(a => a != e).Any(a => a.Attribute("DataContext") is not null || a.Elements().Any(c => c.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal)))) return null;
                    return new Suggestion(FixSource.ShownText, NameProperty, v, 0, Guess: true, AtRunTime: true);
                }
            }
            return new Suggestion(FixSource.ShownText, NameProperty, v, 0);
        }

        /// <summary>短的、寫死的 ToolTip。一句話的是說明，「Ctrl+S」是快速鍵，程式碼或觸發器會換的不能寫死。</summary>
        private Suggestion? FromToolTip(XElement e)
        {
            if ((string?)e.Attribute("ToolTip") is not { Length: > 0 } tip) return null;
            if (project.CodeSets(e, "ToolTip") || project.CodeTips(e) || _names.TriggerSets(e, "ToolTip")) return null;
            if (!Markup.IsMarkup(tip))
            {
                var text = Markup.Literal(tip);
                if (text.Length > 40 || text.IndexOfAny(['。', '；', ';', '！', '!']) >= 0 || text.Contains(". ", StringComparison.Ordinal) || Shortcut().IsMatch(text.Trim())) return null;
                return Usable(e, tip, literal: false) is { } v ? new Suggestion(FixSource.ToolTip, NameProperty, v, 0) : null;
            }
            switch (Markup.ExtensionName(tip))
            {
                case "Binding":
                    // 跟自己的值綁同一個路徑：名稱會變成目前的值。輸入框的提示多半是錯誤訊息
                    if (!guess || project.BaseOf(e) is not { } control || !Wpf.Buttons.Contains(control) && control is not ("MenuItem" or "Hyperlink")) return null;
                    var path = Markup.BindingPath(tip);
                    if (path is null or "" || e.Attributes().Any(a => a.Name.LocalName != "ToolTip" && Markup.BindingPath(a.Value) == path)) return null;
                    return Usable(e, tip, literal: false) is { } b ? new Suggestion(FixSource.ToolTip, NameProperty, b, 0, Guess: true, AtRunTime: true) : null;
                case "x:Static":
                    // 可能是 ToolTip 物件，執行時丟例外：類別名字看得出是字串資源的才抄
                    if (!TextClass().IsMatch(Markup.ExtensionBody(tip))) return null;
                    break;
                case "StaticResource" or "DynamicResource":
                    // 資源要真的是字（同一個鍵也可能是 ToolTip 物件、圖示）
                    if (Markup.ResourceKey(tip) is not { } key || project.Styles.FindResources(e, key) is not [{ Name.LocalName: "String" }, ..]) return null;
                    break;
            }
            return Usable(e, tip, literal: false) is { } copy ? new Suggestion(FixSource.ToolTip, NameProperty, copy, 0) : null;
        }

        /// <summary>可以照抄成名稱的值。元素裡的文字不是標記延伸（{0} items），寫進屬性要跳脫。</summary>
        private static string? Usable(XElement e, string value, bool literal)
        {
            if (literal || !Markup.IsMarkup(value))
            {
                var text = (literal ? value : Markup.Literal(value)).Trim();
                if (text.Length is 0 or > 80 || text.Any(char.IsControl) || text.Count(char.IsLetter) == 0 || NameResolver.Judge(text, null).Kind != NameKind.Named) return null;
                return text[0] == '{' ? "{}" + text : text;
            }
            if (value.Any(char.IsControl) || value.Contains("RelativeSource", StringComparison.Ordinal) || value.Contains("ElementName", StringComparison.Ordinal)) return null;
            // 讀進來的時候把 xaml:Static 改寫成 x:Static 了，抄回去前綴不對
            if (value.Contains("{x:", StringComparison.Ordinal) && e.GetNamespaceOfPrefix("x")?.NamespaceName != Markup.XamlNamespace) return null;
            return Markup.ExtensionName(value) is null or "" or "TemplateBinding" or "MultiBinding" or "PriorityBinding" or "x:Null" or "x:Reference" or "x:Type" or "x:Bind" ? null : value.Trim();
        }

        [GeneratedRegex("[A-Za-z][A-Za-z0-9]{0,30}", RegexOptions.CultureInvariant)]
        private static partial Regex AsciiWord();

        [GeneratedRegex(@"^(?:(?:Ctrl|Control|Alt|Shift|Win)\s{0,3}\+\s{0,3})+\S{1,12}$|^F[0-9]{1,2}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex Shortcut();

        [GeneratedRegex(@"\{(?:StaticResource|DynamicResource)\s{1,5}(?:ResourceKey\s{0,3}=\s{0,3})?(?<k>[^{}\s,]{1,200})\s{0,5}\}", RegexOptions.CultureInvariant)]
        private static partial Regex ResourceRef();

        [GeneratedRegex(@"(?:Resource|String|Lang|Locali[sz]|L10n|I18n|Translat)\w{0,40}\.\w{1,200}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex TextClass();
    }
}
