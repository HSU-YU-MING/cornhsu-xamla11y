using System.Globalization;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Walk;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Report;

/// <summary>Tab 走訪的主控台輸出：第幾站、在哪一行、名稱、NVDA 怎麼念、有什麼問題。</summary>
public static class WalkReport
{
    public static void Print(TextWriter o, Project project, XamlDoc doc, AuditOptions options, Lang lang)
    {
        var inv = CultureInfo.InvariantCulture;
        var walk = TabWalk.Of(project, doc);
        var context = new Context(project, options);
        var problems = Auditor.Run(project, options).Judgements.Where(j => j.Verdict == Verdict.Finding && j.Kind is not null).ToLookup(j => j.Element, j => j.Kind!.Slug);
        var sure = walk.Stops.Count(s => s.Kind == WalkKind.Stop);
        o.WriteLine(lang.T($"xamla11y walk — {Text.Safe(doc.File)}: {sure} Tab stop(s), {walk.Stops.Count - sure} not sure", $"xamla11y walk：{Text.Safe(doc.File)}，Tab 停 {sure} 站，另外 {walk.Stops.Count - sure} 處不確定"));
        var n = 0;
        foreach (var s in walk.Stops)
        {
            var where = s.File == doc.File ? s.Line.ToString(inv) : $"{s.File}:{s.Line.ToString(inv)}";
            var number = s.Kind == WalkKind.Stop ? (++n).ToString(inv) : "?";
            o.WriteLine($"  {number,3}  {Text.Safe(where),-6} <{Text.Safe(s.Element.Name.LocalName)}>{Name(context, s, lang)}"
                + (s.Note is null ? "" : lang.T($"  ({s.Note})", $"（{TabWalk.Notes.GetValueOrDefault(s.Note, s.Note)}）"))
                + (s.Kind != WalkKind.Unknown && problems[s.Element].Distinct().ToList() is { Count: > 0 } slugs ? "  !! " + string.Join(", ", slugs) : ""));
        }
        if (walk.Trap is { } trap && walk.After > 0)
            o.WriteLine(lang.T($"  Tab stays inside <{Text.Safe(trap.Name.LocalName)}> (line {Context.LineOf(trap)}): {walk.After} stop(s) after it need Ctrl+Tab, the arrow keys or the mouse", $"  Tab 進了 <{Text.Safe(trap.Name.LocalName)}>（第 {Context.LineOf(trap)} 行）就在裡面繞：後面 {walk.After} 站要用 Ctrl+Tab、方向鍵或滑鼠"));
        if (walk.Cut) o.WriteLine(lang.T("  !! too large: stopped here, the rest was not walked", "  !! 太大：走到這裡為止，後面沒走"));
        if (walk.InPopups > 0) o.WriteLine(lang.T($"  not listed: {walk.InPopups} in popups (only there while one is open)", $"  沒列：{walk.InPopups} 個在 Popup 裡（打開才有）"));
        if (walk.LeftOut > 0) o.WriteLine(lang.T($"  not listed: {walk.LeftOut} hidden or disabled, and nothing in the project opens them", $"  沒列：{walk.LeftOut} 個藏起來或停用的，專案裡沒有東西會打開"));
        o.WriteLine(lang.T(
            "  From the XAML only: what templates, code and data add is not in it, and a Command may disable a button. Names in quotes are literal; NVDA reads the type and the name, states are left out.",
            "  只看 XAML：範本、程式碼、資料加進來的不在裡面，Command 也可能讓按鈕停用。引號裡的是寫死的名稱；NVDA 的念法只列名稱與型別，狀態沒有列。"));
    }

    private static string Name(Context context, WalkStop s, Lang lang)
    {
        if (s.Kind == WalkKind.Unknown || context.Project.ControlOf(s.Element) is not { } control) return "";
        if (IsWrapped(s, control)) return "";
        var name = context.Names.Resolve(s.Element, control);
        var says = Reads(context, s, control, name, lang);
        var text = name.Kind switch
        {
            NameKind.Named or NameKind.SymbolOnly => name.Text is { Length: > 0 } t ? $" \"{Context.Show(t)}\"" : lang.T(" (named by a resource)", "（名稱是資源）"),
            NameKind.DataBound => lang.T(" (name from data)", "（名稱來自資料）"),
            NameKind.Unknown => lang.T(" (name not known)", "（名稱不知道）"),
            NameKind.Meaningless => lang.T(" (unreadable name)", "（名稱念不出意思）"),
            _ => lang.T(" (no name)", "（沒有名稱）"),
        };
        return Text.Safe(text) + (says is null ? "" : lang.T("  NVDA: ", "　NVDA：") + Text.Safe(says));
    }

    /// <summary>走到這一站 NVDA 怎麼念；不確定的 null。</summary>
    internal static string? Reads(Context context, WalkStop s, Lang lang) =>
        s.Kind == WalkKind.Unknown || context.Project.ControlOf(s.Element) is not { } control || IsWrapped(s, control) ? null : Reads(context, s, control, context.Names.Resolve(s.Element, control), lang);

    private static string? Reads(Context context, WalkStop s, string control, NameResult name, Lang lang) =>
        NameRules.SpokenAs(context, s.Element, control) is { } spoken ? Nvda.Reads(spoken, name, context.DescriptionOf(s.Element), lang) : null;

    /// <summary>清單、頁籤、選單的一站念的是 WPF 包的項目，不是裡面那個元素；資料來的不知道。</summary>
    private static bool IsWrapped(WalkStop s, string control) => s.Control != control && s.Element.Name.LocalName != s.Control;
}
