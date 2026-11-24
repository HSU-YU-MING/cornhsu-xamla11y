using System.Xml;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

public sealed record AuditOptions
{
    /// <summary>規則 → 嚴重度（設定檔 rules）。Off = 這一種不報。</summary>
    public IReadOnlyDictionary<string, Severity> Severities { get; init; } = new Dictionary<string, Severity>();

    /// <summary>使用者擔保：外部佈景在 FocusVisualStyle 關掉之後自己畫焦點（設定檔 themeShowsFocus）。</summary>
    public bool ThemeShowsFocus { get; init; }

    public Severity SeverityOf(Kind kind, Severity? lowered = null)
    {
        var s = Severities.TryGetValue(kind.Slug, out var configured) ? configured : kind.Default;
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
            }
        FocusRule.Check(ctx);
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
    private readonly Dictionary<string, RuleStats> _stats = Kinds.Rules.ToDictionary(r => r, r => new RuleStats(r), StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknown = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _invalidIgnores = new(StringComparer.Ordinal);

    /// <summary>是不是畫面上的元素（不是 Style、Setter、Trigger、Storyboard、資源裡的值…）。</summary>
    public bool IsElement(XElement e)
    {
        var local = e.Name.LocalName;
        if (e.Name.NamespaceName == Markup.XamlNamespace) return false;
        return local is not ("Style" or "Setter" or "EventSetter" or "Trigger" or "MultiTrigger" or "DataTrigger" or "MultiDataTrigger" or "EventTrigger"
            or "Condition" or "Binding" or "MultiBinding" or "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate" or "HierarchicalDataTemplate"
            or "ResourceDictionary" or "Storyboard" or "BeginStoryboard" or "VisualState" or "VisualStateGroup");
    }

    public void Pass(string rule, XElement e, string? reason = null) => Record(rule, Verdict.Pass, e, reason, null);

    public void Blocked(string rule, XElement e, string reason, string? control = null)
    {
        if (control is not null) _unknown[control] = _unknown.GetValueOrDefault(control) + 1;
        Record(rule, Verdict.Blocked, e, reason, null);
    }

    public void NotJudged(string rule, XElement e, string reason) => Record(rule, Verdict.NotJudged, e, reason, null);

    /// <param name="at">報在哪裡（樣式的 Setter；預設就是元素）。</param>
    /// <param name="lowered">這一筆最多報到這個嚴重度。</param>
    /// <param name="rule">算在哪條規則的統計裡（預設是這一種自己的規則；輸入框的亂碼名稱算 R2）。</param>
    public void Report(Kind kind, XElement e, string message, XElement? at = null, Severity? lowered = null, string? identity = null, string? rule = null)
    {
        rule ??= kind.Rule;
        var site = at ?? e;
        var severity = Options.SeverityOf(kind, lowered);
        if (severity == Severity.Off) { Record(rule, Verdict.NotJudged, e, $"{kind.Slug} is turned off", null); return; }

        if (IgnoreReason(site) is { } reason)
        {
            if (reason.Length > 0) { Record(rule, Verdict.Suppressed, e, reason, kind); return; }
            _invalidIgnores.Add($"{StyleIndex.FileOf(site)}:{LineOf(site)}");
        }

        Record(rule, Verdict.Finding, e, message, kind);
        _findings.Add(new Finding(kind, severity, StyleIndex.FileOf(site), LineOf(site), site.Name.LocalName, Cut(message, 600)) { Identity = identity ?? IdentityOf(site), Lowered = lowered is not null });
    }

    private void Record(string rule, Verdict verdict, XElement e, string? reason, Kind? kind)
    {
        var s = _stats[rule];
        switch (verdict)
        {
            case Verdict.Pass: s.Pass++; break;
            case Verdict.Finding: s.Findings++; break;
            case Verdict.Suppressed: s.Suppressed++; break;
            case Verdict.Blocked: s.Blocked++; Count(s.BlockedReasons, reason); break;
            case Verdict.NotJudged: s.NotJudged++; Count(s.NotJudgedReasons, reason); break;
        }
        _judgements.Add(new Judgement(rule, verdict, e, StyleIndex.FileOf(e), LineOf(e), reason, kind));

        // 原因裡夾了 repo 的字（資源鍵、綁定），一個二十萬字的鍵不能把報告撐爆
        static void Count(Dictionary<string, int> map, string? reason)
        {
            var r = Cut(reason ?? "", 200);
            map[r] = map.GetValueOrDefault(r) + 1;
        }
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
            Rules = [.. Kinds.Rules.Select(r => _stats[r])],
            Files = Project.FilesScanned,
            ParseErrors = Project.ParseErrors,
            Foreign = Project.Foreign,
            Themes = Project.Themes,
            Gaps = Project.Gaps,
            UnknownControls = [.. _unknown.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value))],
            InvalidIgnores = [.. _invalidIgnores],
            Judgements = _judgements,
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

    private const string IgnoreToken = "xamla11y-ignore";

    /// <summary>
    /// 元素前面的 &lt;!-- xamla11y-ignore: 理由 --&gt;。null = 沒有；"" = 有但沒寫理由（無效）。
    /// 中間可以隔別的註解（四件套的 ignore 註解會疊在同一個元素上），不能隔別的元素。
    /// 同一個註解裡也可以寫好幾個工具的：理由到下一個「xaml…-ignore:」為止。
    /// 註解要**以** xaml…-ignore 開頭：「TODO: remove every xamla11y-ignore」是在談它，不是在用它。
    /// </summary>
    internal static string? IgnoreReason(XElement e)
    {
        for (var node = e.PreviousNode; node is not null and not XElement; node = node.PreviousNode)
            if (node is XComment c && ParseIgnore(c.Value) is { } reason) return reason;
        return null;
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
