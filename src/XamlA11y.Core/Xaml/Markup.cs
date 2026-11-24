using System.Xml.Linq;

namespace XamlA11y.Core.Xaml;

/// <summary>標記延伸的小工具。</summary>
public static class Markup
{
    public const string WpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    public static readonly XName XKey = XName.Get("Key", XamlNamespace);
    public static readonly XName XName_ = XName.Get("Name", XamlNamespace);
    public static readonly XName XClass = XName.Get("Class", XamlNamespace);

    public static bool IsWpf(XElement e) => IsWpfNamespace(e.Name.NamespaceName);

    public static bool IsMarkup(string? v) =>
        v is not null && v.TrimStart() is { Length: > 1 } t && t[0] == '{' && !t.StartsWith("{}", StringComparison.Ordinal);

    /// <summary>屬性值的字面文字（<c>{}</c> 跳脫去掉）。</summary>
    public static string Literal(string v)
    {
        var t = v.TrimStart();
        return t.StartsWith("{}", StringComparison.Ordinal) ? t[2..] : v;
    }

    /// <summary>「{Binding Path=X, RelativeSource={RelativeSource Self}}」→ "Binding"；「{x:Static a:B.C}」→ "x:Static"。不是標記延伸回 null。</summary>
    public static string? ExtensionName(string? markup)
    {
        if (!IsMarkup(markup)) return null;
        var t = markup!.Trim()[1..];
        var end = 0;
        while (end < t.Length && !char.IsWhiteSpace(t[end]) && t[end] is not ('}' or ',')) end++;
        return t[..end];
    }

    /// <summary>標記延伸名稱之後的內容（不含最外層大括號）。</summary>
    public static string ExtensionBody(string markup)
    {
        var t = markup.Trim();
        var name = ExtensionName(t) ?? "";
        var body = t[(1 + name.Length)..];
        var last = body.LastIndexOf('}');
        return (last >= 0 ? body[..last] : body).Trim();
    }

    /// <summary>標記延伸的參數，以最外層逗號切開。位置參數的鍵是 ""。</summary>
    public static List<(string Key, string Value)> Arguments(string markup)
    {
        var list = new List<(string, string)>();
        var body = ExtensionBody(markup);
        int depth = 0, start = 0;
        char quote = '\0';
        void Add(int end)
        {
            var part = body[start..end].Trim();
            if (part.Length == 0) return;
            var eq = -1;
            var d = 0;
            for (var i = 0; i < part.Length; i++)
            {
                if (part[i] == '{') d++;
                else if (part[i] == '}') d--;
                else if (part[i] == '=' && d == 0) { eq = i; break; }
            }
            list.Add(eq < 0 ? ("", part) : (part[..eq].Trim(), part[(eq + 1)..].Trim().Trim('\'')));
        }
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c == '\'') quote = c;
            else if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (c == ',' && depth == 0) { Add(i); start = i + 1; }
        }
        Add(body.Length);
        return list;
    }

    public static string? Argument(string markup, string key) =>
        Arguments(markup).FirstOrDefault(a => a.Key == key) is { Key: not null } hit && hit.Key == key ? hit.Value : null;

    /// <summary>{Binding X} 或 {Binding Path=X} 的路徑；沒有路徑（綁整個物件）回空字串；不是 Binding 回 null。</summary>
    public static string? BindingPath(string? markup)
    {
        if (ExtensionName(markup) is not ("Binding" or "x:Bind")) return null;
        var args = Arguments(markup!);
        return args.FirstOrDefault(a => a.Key is "" or "Path").Value ?? "";
    }

    /// <summary>{Binding …, ElementName=x} 或 {x:Reference x} 的 x。</summary>
    public static string? ReferencedElement(string? markup) => ExtensionName(markup) switch
    {
        "Binding" => Argument(markup!, "ElementName") ?? (Argument(markup!, "Source") is { } s ? ReferencedElement(s) : null),
        "x:Reference" => Arguments(markup!).FirstOrDefault(a => a.Key is "" or "Name").Value,
        _ => null,
    };

    /// <summary>綁到自己（RelativeSource Self）的屬性名；不是的話回 null。</summary>
    public static string? SelfBindingPath(string? markup)
    {
        if (ExtensionName(markup) != "Binding") return null;
        if (Argument(markup!, "RelativeSource") is not { } rs) return null;
        var t = rs.Replace(" ", "");
        return t is "{RelativeSourceSelf}" or "{RelativeSourceMode=Self}" or "{x:StaticRelativeSource.Self}" ? BindingPath(markup) : null;
    }

    /// <summary>{StaticResource K} / {DynamicResource K} / {ThemeResource K} 的 K；其他寫法回 null。</summary>
    public static string? ResourceKey(string? markup)
    {
        if (ExtensionName(markup) is not ("StaticResource" or "DynamicResource" or "ThemeResource")) return null;
        var arg = Arguments(markup!).FirstOrDefault(a => a.Key is "" or "ResourceKey").Value;
        return string.IsNullOrEmpty(arg) || arg.StartsWith('{') ? null : arg;
    }

    /// <summary>TargetType="{x:Type Button}" 與 TargetType="Button"、"local:Foo" → 區域名稱。</summary>
    public static string? TypeName(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        if (t.StartsWith('{'))
        {
            if (ExtensionName(t) != "x:Type") return null;
            t = Arguments(t).FirstOrDefault(a => a.Key is "" or "TypeName").Value ?? "";
        }
        var colon = t.LastIndexOf(':');
        t = colon >= 0 ? t[(colon + 1)..] : t;
        return t.Length > 0 ? t : null;
    }

    /// <summary>「{x:Type Button}」或「{StaticResource {x:Type Button}}」裡的型別（含前綴）。</summary>
    public static string? TypeKey(string? raw)
    {
        if (raw is null) return null;
        var i = raw.IndexOf("{x:Type", StringComparison.Ordinal);
        if (i < 0) return null;
        var end = raw.IndexOf('}', i);
        if (end < 0) return null;
        var t = Arguments(raw[i..(end + 1)]).FirstOrDefault(a => a.Key is "" or "TypeName").Value;
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>
    /// 型別的識別：WPF 內建的是區域名稱（「Button」），其他的帶命名空間（「lib:wpfui#Button」「clr-namespace:App#Button」）。
    /// ⚠ 只看區域名稱的話，<c>&lt;Style TargetType="ui:Button"&gt;</c> 會套到 WPF 的 Button 上 —— 用 WPF-UI 的專案兩種常常並存。
    /// </summary>
    public static string? TypeId(XElement at, string? typeText)
    {
        if (string.IsNullOrWhiteSpace(typeText)) return null;
        var t = typeText.Trim();
        if (t.StartsWith('{')) t = ExtensionName(t) == "x:Type" ? Arguments(t).FirstOrDefault(a => a.Key is "" or "TypeName").Value ?? "" : "";
        if (t.Length == 0) return null;
        var colon = t.IndexOf(':');
        if (colon < 0)
        {
            var ns = at.GetDefaultNamespace().NamespaceName;
            return ns.Length == 0 || IsWpfNamespace(ns) ? t : IdOf(ns, t);
        }
        return colon == 0 || colon == t.Length - 1 ? null
            : at.GetNamespaceOfPrefix(t[..colon]) is { } prefixed ? IdOf(prefixed.NamespaceName, t[(colon + 1)..]) : "?#" + t;
    }

    public static string TypeIdOf(XElement e) => IdOf(e.Name.NamespaceName, e.Name.LocalName);

    private static string IdOf(string ns, string local)
    {
        if (IsWpfNamespace(ns) || ns.Length == 0) return local;
        if (Analysis.KnownControls.LibraryOf(ns) is { } lib) return $"lib:{lib.Id}#{local}";
        // 同一個類別在自己的組件裡寫不寫 assembly= 都可以
        var semi = ns.IndexOf(";assembly=", StringComparison.Ordinal);
        return (ns.StartsWith("clr-namespace:", StringComparison.Ordinal) && semi > 0 ? ns[..semi] : ns) + "#" + local;
    }

    public static bool IsWpfNamespace(string ns) => ns is WpfNamespace or "http://schemas.microsoft.com/netfx/2007/xaml/presentation"
        // 同一批型別用 CLR 命名空間寫：xmlns:c="clr-namespace:System.Windows.Controls;assembly=PresentationFramework"
        || (ns.StartsWith("clr-namespace:System.Windows", StringComparison.Ordinal) && ns[(ns.IndexOf(';') + 1)..].Trim() is "assembly=PresentationFramework" or "assembly=PresentationCore");

    public static string? NameOf(XElement e) => (string?)e.Attribute(XName_) ?? (string?)e.Attribute("Name");

    /// <summary>屬性元素（&lt;Grid.Children&gt;）不是物件。</summary>
    public static bool IsPropertyElement(XElement e) => e.Name.LocalName.Contains('.', StringComparison.Ordinal);

    /// <summary>元素的屬性元素：&lt;X.prop&gt;（也認附加寫法 &lt;Owner.prop&gt;）。</summary>
    public static XElement? PropertyElement(XElement e, string property)
    {
        foreach (var c in e.Elements())
        {
            var local = c.Name.LocalName;
            var dot = local.IndexOf('.');
            if (dot < 0) continue;
            if (local == property) return c;
            // <Button.Content>、<ContentControl.Content>（基底的名字）；<hc:X.Content> 是附加屬性，不算
            if (!property.Contains('.', StringComparison.Ordinal) && local[(dot + 1)..] == property
                && (StyleIndex.NormalizeProperty(local) == property || local[..dot] == e.Name.LocalName)) return c;
        }
        return null;
    }

    private sealed record NodeIndex(List<XNode> Nodes, Dictionary<XNode, int> Index);

    /// <summary>前一個節點。LINQ to XML 的 PreviousNode 每次都從頭數（三萬個兄弟元素就是平方），兄弟多的記一份索引。</summary>
    public static XNode? Previous(XNode node)
    {
        if (node.Parent is not { } parent) return node.PreviousNode;
        if (parent.Annotation<NodeIndex>() is not { } index)
        {
            var nodes = parent.Nodes().ToList();
            var map = new Dictionary<XNode, int>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < nodes.Count; i++) map[nodes[i]] = i;
            parent.AddAnnotation(index = new NodeIndex(nodes, map));
        }
        return index.Index.TryGetValue(node, out var at) ? at > 0 ? index.Nodes[at - 1] : null : node.PreviousNode;
    }

    /// <summary>元素的內容（去掉屬性元素）。</summary>
    public static List<XElement> ContentElements(XElement e) => [.. e.Elements().Where(c => !IsPropertyElement(c))];

    /// <summary>元素自己的文字節點（不含子元素的），前後空白修掉。</summary>
    public static string OwnText(XElement e) => string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
}
