using System.Globalization;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// R6：點擊範圍小於 24×24（WCAG 2.5.8）。
///
/// 只判**作者自己設的**大小：沒設的是平台預設，2.5.8 有「使用者代理決定」的例外（WPF 預設的 Button 本來就約 20 高）。
/// 間距例外要知道鄰居在哪：只有在同一個 StackPanel／WrapPanel／ToolBar 裡緊鄰另一個可點的元素、算得出距離時才報；
/// 其餘說不知道（不進分母 —— 靜態不可能知道鄰居的位置）。
/// </summary>
internal static class SizeRule
{
    private const double Min = 24;

    private static readonly HashSet<string> Targets = new(StringComparer.Ordinal)
    { "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "ComboBox", "TextBox", "PasswordBox" };

    public static void Check(Context ctx, XamlDoc doc, XElement e)
    {
        if (ctx.Project.BaseOf(e) is not { } control || !Targets.Contains(control) || e.Parent is null) return;

        var w = Bound(ctx, e, horizontal: true);
        var h = Bound(ctx, e, horizontal: false);
        if (w is null && h is null) return;                    // 大小沒設：平台預設，不是候選
        if (!(w < Min) && !(h < Min))
        {
            // 上界夠大不等於真的夠大：兩個方向都確定至少 24 才算沒問題
            if (AtLeast(ctx, e, true) && AtLeast(ctx, e, false)) ctx.Pass("R6", e);
            else ctx.NotJudged("R6", e, "only part of its size is set in the XAML");
            return;
        }

        if (Context.ControlTemplateOf(e) is not null) { ctx.NotJudged("R6", e, "a template part"); return; }
        if (Scaled(e)) { ctx.NotJudged("R6", e, "it is inside a Viewbox or has a transform"); return; }

        var (panel, horizontal) = PanelOf(ctx, e);
        if (panel is null) { ctx.NotJudged("R6", e, "its neighbours' positions are not known (WCAG 2.5.8 allows small targets with enough space around them)"); return; }

        var size = horizontal ? w : h;
        if (size is not { } own || own >= Min) { ctx.NotJudged("R6", e, "its neighbours' positions are not known (WCAG 2.5.8 allows small targets with enough space around them)"); return; }

        foreach (var (neighbour, before) in Neighbours(e))
        {
            if (ctx.Project.BaseOf(neighbour) is not { } nc || !(Targets.Contains(nc) || nc is "Hyperlink" or "Slider")) continue;
            if (Margin(ctx, e) is not { } mine || Margin(ctx, neighbour) is not { } theirs) continue;
            var gap = horizontal
                ? before ? mine.Left + theirs.Right : mine.Right + theirs.Left
                : before ? mine.Top + theirs.Bottom : mine.Bottom + theirs.Top;

            var nw = Bound(ctx, neighbour, true);
            var nh = Bound(ctx, neighbour, false);
            var nAlong = horizontal ? nw : nh;
            var small = nw < Min || nh < Min;

            // 24 px 的圓：跟鄰居本身（一般大小）或鄰居的圓（也太小）碰到就沒有間距例外
            var edge = own / 2 + gap;
            var tooClose = edge < Min / 2 || (small && nAlong is { } na && edge + na / 2 < Min);
            if (!tooClose) continue;

            var axis = horizontal ? "wide" : "high";
            ctx.Report(Kinds.SmallTarget, e,
                string.Create(CultureInfo.InvariantCulture, $"<{e.Name.LocalName}> is {own:0.#} DIP {axis} and {Math.Max(0, gap):0.#} DIP away from <{neighbour.Name.LocalName}>{NameOf(neighbour)}; targets under 24 need more space around them"));
            return;
        }
        ctx.NotJudged("R6", e, "its neighbours' positions are not known (WCAG 2.5.8 allows small targets with enough space around them)");
    }

    /// <summary>這個方向確定至少 24：明寫的大小（沒被 Max 夾小）或 Min，而且不在更小的固定格子裡。</summary>
    private static bool AtLeast(Context ctx, XElement e, bool horizontal)
    {
        var styles = ctx.Project.Styles;
        double? Read(string p) => styles.Resolve(e, p) is { IsValue: true, Value: { } v } && Number(v) is { } n ? n : null;
        bool Unreadable(string p) => styles.Resolve(e, p) is var r && (r.IsUnreadable || (r.IsValue && Markup.IsMarkup(r.Value)));
        var (sizeName, minName, maxName) = horizontal ? ("Width", "MinWidth", "MaxWidth") : ("Height", "MinHeight", "MaxHeight");
        var low = Read(minName) ?? 0;
        if (Read(sizeName) is { } size && !Unreadable(maxName)) low = Math.Max(low, Math.Min(size, Read(maxName) ?? double.PositiveInfinity));
        return low >= Min && !(Slot(ctx, e, horizontal) < Min);
    }

    private static string NameOf(XElement e) => Markup.NameOf(e) is { } n ? $" '{n}'" : "";

    /// <summary>
    /// 這個方向上的大小上界：明寫的 Width／Height（被 Min 撐大、被 Max 夾住），以及放進固定大小的 Grid 格子。
    /// 沒有明寫、也不在固定格子裡 → null。讀不出來 → null（不猜）。
    /// </summary>
    internal static double? Bound(Context ctx, XElement e, bool horizontal)
    {
        var styles = ctx.Project.Styles;
        double? Read(string p) => styles.Resolve(e, p) is { IsValue: true, Value: { } v } && Number(v) is { } n ? n : null;
        bool Unreadable(string p) => styles.Resolve(e, p) is { } r && (r.IsUnreadable || (r.IsValue && Markup.IsMarkup(r.Value)));

        var (sizeName, minName, maxName) = horizontal ? ("Width", "MinWidth", "MaxWidth") : ("Height", "MinHeight", "MaxHeight");
        if (Unreadable(minName) || Unreadable(sizeName) && !double.IsNaN(Read(sizeName) ?? double.NaN)) return null;
        var min = Read(minName) ?? 0;
        double? bound = Read(sizeName);
        if (Read(maxName) is { } max) bound = bound is { } b ? Math.Min(b, max) : max;
        if (bound is { } v2) bound = Math.Max(v2, min);

        if (Slot(ctx, e, horizontal) is { } slot) bound = bound is { } b2 ? Math.Min(b2, slot) : slot;
        return bound;
    }

    /// <summary>放在固定大小的 Grid 格子裡（單格）：那一格的大小扣掉自己的 Margin。</summary>
    private static double? Slot(Context ctx, XElement e, bool horizontal)
    {
        if (e.Parent is not { } grid || grid.Name.LocalName != "Grid") return null;
        var (defs, def, index, span, size) = horizontal
            ? ("ColumnDefinitions", "ColumnDefinition", "Grid.Column", "Grid.ColumnSpan", "Width")
            : ("RowDefinitions", "RowDefinition", "Grid.Row", "Grid.RowSpan", "Height");
        var list = grid.Elements().FirstOrDefault(x => x.Name.LocalName == "Grid." + defs)?.Elements().Where(x => x.Name.LocalName == def).ToList();
        if (list is null || list.Count == 0) return null;
        if (e.Attribute(span) is not null) return null;
        if (!int.TryParse((string?)e.Attribute(index) ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i < 0) return null;
        i = Math.Min(i, list.Count - 1);
        if (Number((string?)list[i].Attribute(size)) is not { } cell || Margin(ctx, e) is not { } m) return null;
        return Math.Max(0, cell - (horizontal ? m.Left + m.Right : m.Top + m.Bottom));
    }

    private static (XElement? Panel, bool Horizontal) PanelOf(Context ctx, XElement e)
    {
        if (e.Parent is not { } p || Markup.IsPropertyElement(p)) return (null, false);
        return p.Name.LocalName switch
        {
            "StackPanel" => ctx.Project.Styles.Resolve(p, "Orientation") is { IsValue: true, Value: "Horizontal" } ? (p, true)
                : ctx.Project.Styles.Resolve(p, "Orientation").IsUnreadable ? (null, false) : (p, false),
            "WrapPanel" => ctx.Project.Styles.Resolve(p, "Orientation").IsSet || ctx.Project.Styles.Resolve(p, "ItemWidth").IsSet ? (null, false) : (p, true),
            "ToolBar" => p.Attribute("Orientation") is null && !p.Ancestors().Any(a => (string?)a.Attribute("Orientation") == "Vertical") ? (p, true) : (null, false),
            _ => (null, false),
        };
    }

    /// <summary>緊鄰的兄弟（文件順序就是排列順序）。收合的跳過。</summary>
    private static IEnumerable<(XElement Element, bool Before)> Neighbours(XElement e)
    {
        static bool Visual(XElement x) => !Markup.IsPropertyElement(x) && (string?)x.Attribute("Visibility") is not "Collapsed";
        if (e.ElementsBeforeSelf().LastOrDefault(Visual) is { } prev) yield return (prev, true);
        if (e.ElementsAfterSelf().FirstOrDefault(Visual) is { } next) yield return (next, false);
    }

    private static bool Scaled(XElement e) =>
        e.AncestorsAndSelf().Any(a => a.Name.LocalName == "Viewbox" || a.Attribute("LayoutTransform") is not null || a.Attribute("RenderTransform") is not null
                                      || a.Elements().Any(c => c.Name.LocalName.EndsWith(".LayoutTransform", StringComparison.Ordinal) || c.Name.LocalName.EndsWith(".RenderTransform", StringComparison.Ordinal)));

    internal readonly record struct Thickness(double Left, double Top, double Right, double Bottom);

    internal static Thickness? Margin(Context ctx, XElement e)
    {
        var r = ctx.Project.Styles.Resolve(e, "Margin");
        if (!r.IsSet) return default(Thickness);
        if (!r.IsValue || r.Value is not { } v || Markup.IsMarkup(v)) return null;
        var parts = v.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var n = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (Number(parts[i]) is not { } d) return null;
            n[i] = d;
        }
        return n.Length switch
        {
            1 => new Thickness(n[0], n[0], n[0], n[0]),
            2 => new Thickness(n[0], n[1], n[0], n[1]),
            4 => new Thickness(n[0], n[1], n[2], n[3]),
            _ => null,
        };
    }

    /// <summary>「24」「24px」「0.25in」；Auto、星號 → null。</summary>
    internal static double? Number(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim().ToLowerInvariant();
        var factor = 1.0;
        foreach (var (unit, f) in (ReadOnlySpan<(string, double)>)[("px", 1), ("in", 96), ("cm", 96 / 2.54), ("pt", 96 / 72.0)])
            if (t.EndsWith(unit, StringComparison.Ordinal)) { t = t[..^2].Trim(); factor = f; break; }
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d * factor : null;
    }
}
