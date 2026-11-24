using System.Xml;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

public sealed record AuditOptions
{
    /// <summary>規則 → 嚴重度（設定檔 rules）。Off = 這一種不報。</summary>
    public IReadOnlyDictionary<string, Severity> Severities { get; init; } = new Dictionary<string, Severity>();

    /// <summary>依哪個標準、哪個等級：準則全不在裡面的種類預設不報。設定檔 rules 明寫的照寫的。</summary>
    public Scope Scope { get; init; } = Scope.Default;

    /// <summary>使用者擔保：外部佈景在 FocusVisualStyle 關掉之後自己畫焦點（設定檔 themeShowsFocus）。</summary>
    public bool ThemeShowsFocus { get; init; }

    public Severity SeverityOf(Kind kind, Severity? lowered = null)
    {
        var s = Severities.TryGetValue(kind.Slug, out var configured) ? configured : Scope.Covers(kind) ? kind.Default : Severity.Off;
        return lowered is { } l && l < s ? l : s;
    }
}

/// <summary>跑全部規則，收集判定。規則透過 <see cref="Context"/> 回報。</summary>
public static class Auditor
{
    public static AuditResult Run(Project project, AuditOptions? options = null)
    {
        var ctx = new Context(project, options ?? new AuditOptions());
        foreach (var doc in project.Docs)
            foreach (var e in doc.Root.DescendantsAndSelf())
            {
                if (Markup.IsPropertyElement(e) || !ctx.IsElement(e)) continue;
                NameRules.Check(ctx, doc, e);
                MouseRule.Check(ctx, doc, e);
                TabRule.Check(ctx, doc, e);
                SizeRule.Check(ctx, doc, e);
                ItemsRule.Check(ctx, e);
                SilentRule.Check(ctx, e);
                SameNameRule.Check(ctx, e);
                LabelRule.Check(ctx, e);
            }
        FocusRule.Check(ctx);
        ctx.RunLater();
        return ctx.Finish();
    }
}

public sealed class Context(Project project, AuditOptions options)
{
    public Project Project { get; } = project;
    public AuditOptions Options { get; } = options;
    public NameResolver Names { get; } = new(project);

    private readonly List<Finding> _findings = [];
    private readonly List<Judgement> _judgements = [];
    private readonly Dictionary<string, CheckStats> _stats = Kinds.Checks.ToDictionary(c => c.Id, c => new CheckStats(c.Id) { Off = Kinds.CountedIn(c.Id).All(k => options.SeverityOf(k) == Severity.Off) }, StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknown = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _invalidIgnores = new(StringComparer.Ordinal);

    private readonly HashSet<XElement> _mouseOnly = new(ReferenceEqualityComparer.Instance);

    /// <summary>已經報「滑鼠能用，鍵盤不能」的元素：改成 Button 一次修好，不再報「沒聲音」。</summary>
    public bool IsMouseOnly(XElement e) => _mouseOnly.Contains(e);

    private readonly Dictionary<string, (XElement First, Action Report)> _later = new(StringComparer.Ordinal);

    /// <summary>同一個原因只報一次：檔名、行號最前面的那一個。其他的記成不判。</summary>
    public void Later(string key, XElement e, Action report)
    {
        // 壓掉的是這一個用法，不是整個類別
        if (IgnoreOf(e).Reason is { Length: > 0 } reason) { Suppress(Kinds.SilentTabStop, key[..key.IndexOf(':')], e, e, reason); return; }
        if (!_later.TryGetValue(key, out var first)) { _later[key] = (e, report); return; }
        var (keep, drop, action) = Before(e, first.First) ? (e, first.First, report) : (first.First, e, first.Report);
        _later[key] = (keep, action);
        NotJudged(key[..key.IndexOf(':')], drop, "the same class, reported once");
    }

    public void RunLater()
    {
        foreach (var key in _later.Keys.Order(StringComparer.Ordinal)) _later[key].Report();
        _later.Clear();
    }

    private static bool Before(XElement a, XElement b) =>
        string.CompareOrdinal(StyleIndex.FileOf(a), StyleIndex.FileOf(b)) is var c && c != 0 ? c < 0 : LineOf(a) < LineOf(b);

    /// <summary>是不是畫面上的元素（不是 Style、Setter、Trigger、Storyboard、資源裡的值…）。</summary>
    public bool IsElement(XElement e)
    {
        var local = e.Name.LocalName;
        if (e.Name.NamespaceName == Markup.XamlNamespace) return false;
        return local is not ("Style" or "Setter" or "EventSetter" or "Trigger" or "MultiTrigger" or "DataTrigger" or "MultiDataTrigger" or "EventTrigger"
            or "Condition" or "Binding" or "MultiBinding" or "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate" or "HierarchicalDataTemplate"
            or "ResourceDictionary" or "Storyboard" or "BeginStoryboard" or "VisualState" or "VisualStateGroup");
    }

    public void Pass(string check, XElement e, string? reason = null) => Record(check, Verdict.Pass, e, reason, null);

    public void Blocked(string check, XElement e, string reason, string? control = null)
    {
        if (control is not null) _unknown[control] = _unknown.GetValueOrDefault(control) + 1;
        Record(check, Verdict.Blocked, e, reason, null, control);
    }

    private readonly List<Suppression> _suppressions = [];
    private readonly HashSet<XElement> _ignored = new(ReferenceEqualityComparer.Instance);

    private void Suppress(Kind kind, string check, XElement e, XElement site, string reason)
    {
        Record(check, Verdict.Suppressed, e, reason, kind);
        _ignored.Add(site);
        _suppressions.Add(new Suppression(kind, StyleIndex.FileOf(site), LineOf(site), site.Name.LocalName, CutMiddle(XamlA11y.Core.Report.Text.Visible(reason), 300)));
    }

    public void NotJudged(string check, XElement e, string reason) => Record(check, Verdict.NotJudged, e, reason, null);

    /// <param name="at">報在哪裡（樣式的 Setter；預設就是元素）。</param>
    /// <param name="lowered">這一筆最多報到這個嚴重度。</param>
    /// <param name="check">算在哪一項檢查的統計裡（預設是這一種自己的；輸入框的亂碼名稱算輸入框的）。</param>
    /// <param name="control">名稱規則的：當成哪一個 WPF 控制項。</param>
    /// <param name="spokenName">NVDA 會念的名稱（空名稱是 null）。</param>
    public void Report(Kind kind, XElement e, string message, XElement? at = null, Severity? lowered = null, string? identity = null, string? check = null, string? control = null, string? spokenName = null, bool unreadable = false, string? keyFile = null)
    {
        check ??= kind.Check;
        var site = at ?? e;
        var severity = Options.SeverityOf(kind, lowered);
        if (severity == Severity.Off) { Record(check, Verdict.NotJudged, e, Options.Severities.ContainsKey(kind.Slug) ? $"{kind.Slug} is turned off" : $"{kind.Slug} is not in {Options.Scope.Title}", null); return; }

        var (reason, stranded) = IgnoreOf(site);
        if (reason is { Length: > 0 }) { Suppress(kind, check, e, site, reason); return; }
        if (stranded || reason is not null)
            _invalidIgnores.Add($"{StyleIndex.FileOf(site)}:{LineOf(site)}: xamla11y-ignore " + (stranded
                ? "has a commented-out element or text after it, so it does nothing (put it right before the element it is for)"
                : "has no reason, so it does nothing (write xamla11y-ignore: why)"));

        Record(check, Verdict.Finding, e, message, kind);
        if (kind == Kinds.MouseOnly) _mouseOnly.Add(e);
        _findings.Add(new Finding(kind, severity, StyleIndex.FileOf(site), LineOf(site), site.Name.LocalName, CutMiddle(XamlA11y.Core.Report.Text.Visible(message), 600)) { Identity = identity ?? IdentityOf(site), Lowered = lowered is not null, Control = control, SpokenName = spokenName, Unreadable = unreadable, KeyFile = keyFile, Description = control is null ? null : DescriptionOf(e) });
    }

    /// <summary>沒有是 ""，不確定是 null。UIA 的 HelpText：AutomationProperties.HelpText，空的話是字串 ToolTip（WPF 原始碼，NVDA 實跑）。</summary>
    internal string? DescriptionOf(XElement e)
    {
        foreach (var property in (ReadOnlySpan<string>)["AutomationProperties.HelpText", "ToolTip"])
        {
            var r = Project.Styles.Resolve(e, property);
            if (r.Kind == ResolveKind.NotSet || r.IsNull || r.Origin is null) continue;
            if (r.IsValue && r.Element is null && r.Value is { } v && !Markup.IsMarkup(v))
            {
                // 空字串的 HelpText 才往下看 ToolTip；只有空白的就是它了（WPF）
                if (Markup.Literal(v) is { Length: > 0 } text) return text;
                continue;
            }
            return null;      // 綁定、物件：執行時才知道，念法不猜
        }
        return "";
    }

    private void Record(string check, Verdict verdict, XElement e, string? reason, Kind? kind, string? control = null)
    {
        _stats[check].Add(verdict, reason);
        _judgements.Add(new Judgement(check, verdict, e, StyleIndex.FileOf(e), LineOf(e), reason, kind) { Control = control });
    }

    /// <summary>
    /// 用不到的 ignore：它管的元素一項問題都沒有，也沒有「不知道」「不判」（那種說不準）。
    /// </summary>
    private IEnumerable<string> UnusedIgnores()
    {
        var unsure = _judgements.Where(j => j.Verdict is Verdict.Blocked or Verdict.NotJudged or Verdict.Finding).Select(j => j.Element).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var doc in Project.Docs)
            foreach (var e in doc.Root.DescendantsAndSelf())
                if (!_ignored.Contains(e) && !unsure.Contains(e) && IgnoreOf(e) is { Reason.Length: > 0, Stranded: false })
                    yield return $"{doc.File}:{LineOf(e)}";
    }

    public AuditResult Finish()
    {
        var sorted = _findings.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Kind.Slug, StringComparer.Ordinal).ToList();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in sorted)
        {
            var key = f.BaselineKey;
            seen[key] = seen.GetValueOrDefault(key) + 1;
            f.Ordinal = seen[key];
        }
        return new AuditResult
        {
            Findings = sorted,
            Checks = [.. Kinds.Checks.Select(c => _stats[c.Id])],
            Files = Project.FilesScanned,
            Scope = Options.Scope,
            ParseErrors = Project.ParseErrors,
            ParseWhy = Project.ParseWhy,
            Foreign = Project.Foreign,
            Themes = Project.Themes,
            Gaps = Project.Gaps,
            UnknownControls = [.. _unknown.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value))],
            InvalidIgnores = [.. _invalidIgnores],
            Judgements = _judgements,
            Suppressions = [.. _suppressions.OrderBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line).ThenBy(s => s.Kind.Slug, StringComparer.Ordinal)],
            UnusedIgnores = [.. UnusedIgnores()],
            CodeBuilt = Project.CodeBuilt,
        };
    }

    public static int LineOf(XElement e) => e is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    /// <summary>baseline 用的身分：x:Name &gt; AutomationId &gt; 命令 &gt; 處理器 &gt; 提示 &gt; 內容的寫法。</summary>
    public static string IdentityOf(XElement e)
    {
        if (Markup.NameOf(e) is { Length: > 0 } name) return name;
        foreach (var a in (ReadOnlySpan<string>)["AutomationProperties.AutomationId", "Command", "Click", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseDown", "MouseUp", "ToolTip", "Text", "SelectedItem", "IsChecked", "Value", "Header", "Content", "Style", "Property"])
            if ((string?)e.Attribute(a) is { Length: > 0 } v) return a + "=" + Cut(v, 80);
        return "";
    }

    /// <summary>截短，不切在代理對中間（切半的字寫進 JSON 會變成 U+FFFD，baseline 的鍵就再也對不上）。</summary>
    internal static string Cut(string s, int n)
    {
        if (s.Length <= n) return s;
        return s[..(char.IsHighSurrogate(s[n - 1]) ? n - 1 : n)];
    }

    /// <summary>截中間：句子的樣子在頭尾，repo 的字（資源鍵、綁定）在中間。</summary>
    internal static string CutMiddle(string s, int n)
    {
        if (s.Length <= n) return s;
        var tail = n / 3;
        var from = s.Length - tail;
        return Cut(s, n - tail - 1) + "…" + s[(char.IsLowSurrogate(s[from]) ? from + 1 : from)..];
    }

    private const string IgnoreToken = "xamla11y-ignore";

    /// <summary>
    /// 元素前面的 &lt;!-- xamla11y-ignore: 理由 --&gt;。Reason：null = 沒有；"" = 有但沒寫理由（無效）。
    /// Stranded = 有，但停在文字或被註解掉的元素前面（無效）。無效的都要講出來。
    /// 中間可以隔別的註解（四件套的 ignore 註解會疊在同一個元素上），不能隔別的元素或文字 ——
    /// 被註解掉的元素（註解以 &lt; 開頭）也算：前面的 ignore 管的是它，不能因為它被註解掉就改壓下一個。
    /// 同一個註解裡也可以寫好幾個工具的：理由到下一個「xaml…-ignore:」為止。
    /// 註解要**以** xaml…-ignore 開頭：「TODO: remove every xamla11y-ignore」是在談它，不是在用它。
    /// </summary>
    internal static (string? Reason, bool Stranded) IgnoreOf(XElement e)
    {
        var stranded = false;
        for (var node = Markup.Previous(e); node is not null and not XElement; node = Markup.Previous(node))
        {
            if (node is XText t) stranded |= !string.IsNullOrWhiteSpace(t.Value); // 文字也是內容；處理指令不是，跳過
            if (node is not XComment c) continue;
            if (ParseIgnore(c.Value) is { } reason) return stranded ? (null, true) : (reason, false);
            stranded |= c.Value.AsSpan().TrimStart().StartsWith("<");
        }
        return (null, false);
    }

    internal static string? ParseIgnore(string comment)
    {
        var t = comment.Trim();
        if (TokenAt(t, 0) is null) return null;
        for (var i = 0; i < t.Length;)
        {
            // i 在一個 xaml…-ignore 的開頭：找出它的名字、冒號、到下一個為止的理由
            var name = TokenAt(t, i)!;
            var k = i + name.Length;
            while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
            var colon = k < t.Length && t[k] == ':';
            var next = t.Length;
            for (var j = k; j < t.Length; j++)
                if ((j == 0 || !char.IsLetterOrDigit(t[j - 1]) && t[j - 1] != '-') && t[j] == 'x' && TokenAt(t, j) is not null) { next = j; break; }
            if (name == IgnoreToken)
            {
                if (!colon) return "";
                var reason = t[(k + 1)..next].Trim().TrimEnd(';', ',').Trim();
                return reason.Any(char.IsLetterOrDigit) ? reason : "";
            }
            i = next;
        }
        return null;
    }

    /// <summary>這個位置是不是「xaml…-ignore」（後面不能再接字母，xamla11y-ignored 不是）；是的話回傳它。</summary>
    private static string? TokenAt(string t, int i)
    {
        if (string.CompareOrdinal(t, i, "xaml", 0, 4) != 0) return null;
        var j = i + 4;
        while (j < t.Length && j - i < 24 && char.IsAsciiLetterOrDigit(t[j])) j++;
        const string suffix = "-ignore";
        if (string.CompareOrdinal(t, j, suffix, 0, suffix.Length) != 0) return null;
        var end = j + suffix.Length;
        return end < t.Length && (char.IsLetterOrDigit(t[end]) || t[end] is '-' or '_') ? null : t[i..end];
    }

    // ───────────────────────── 位置的問題 ─────────────────────────

    /// <summary>元素在哪個控制項範本裡；不在範本裡回 null。DataTemplate 不算（它畫的是真的內容）。</summary>
    public static XElement? ControlTemplateOf(XElement e) => e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "ControlTemplate");

    /// <summary>範本給哪個控制項用（TargetType；沒寫就看它所在的 Style 或元素）。</summary>
    public string? TemplateTarget(XElement template)
    {
        if (Markup.TypeName((string?)template.Attribute("TargetType")) is { } t) return t;
        var style = template.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Style");
        if (style is not null) return Markup.TypeName((string?)style.Attribute("TargetType"));
        return template.Parent is { } holder && Markup.IsPropertyElement(holder) ? holder.Name.LocalName[..holder.Name.LocalName.IndexOf('.')] : null;
    }

    public bool IsFalse(XElement e, string property) => Project.Styles.Resolve(e, property).IsFalse;

    public static string Show(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = NameResolver.Escape(text.Replace('\r', ' ').Replace('\n', ' '));
        return t.Length <= 40 ? t : Cut(t, 39) + "…";
    }
}
