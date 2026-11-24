using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// silent-tab-stop：Tab 會停、UIA 樹上卻沒有的元素 —— 螢幕閱讀器停在那裡什麼都不念（實測 facts/silent.txt，NVDA 也是）。
/// ContentControl 與 Control 預設就能聚焦；自訂控制項沒寫 OnCreateAutomationPeer 的，跟著基底看。
/// 候選只有「停得到」的：不能聚焦的不算檢查過。
/// </summary>
internal static class SilentRule
{
    /// <summary>沒有 AutomationPeer、預設就能聚焦的基底。</summary>
    private static readonly HashSet<string> Focusable = new(StringComparer.Ordinal) { "ContentControl", "Control", "HeaderedContentControl", "ButtonBase", "RangeBase" };

    public static void Check(Context ctx, XElement e)
    {
        if (e.Parent is null || ctx.Project.ControlOf(e) is not { } control) return;
        // 量過的函式庫：樣式不在專案裡，停不停得到不知道
        if (KnownControls.LibraryOf(e) is not null) return;
        var styles = ctx.Project.Styles;
        var byDefault = Focusable.Contains(control);
        if (!byDefault && (Wpf.HasPeer(control) || Wpf.NameSourceOf(control) != NameSource.Nothing)) return;

        var focusable = styles.Resolve(e, "Focusable");
        var tabStop = styles.Resolve(e, "IsTabStop");
        if (!byDefault && !focusable.IsTrue) return;
        if (focusable.IsFalse || tabStop.IsFalse || ctx.IsMouseOnly(e)) return;
        if (e.Name.LocalName == "Popup" || Hidden(ctx, e) || NeverReached(e)) return;

        if (focusable.IsUnreadable || tabStop.IsUnreadable) { ctx.NotJudged(Checks.SilentStops, e, "its style is outside the project"); return; }
        if (Markup.IsMarkup(focusable.Value) || Markup.IsMarkup(tabStop.Value)) { ctx.NotJudged(Checks.SilentStops, e, "Focusable or IsTabStop is bound"); return; }
        if (ctx.Project.CodeSets(e, "Focusable", "IsTabStop") || !Markup.IsWpf(e) && ctx.Project.Code.TouchesFocus(e.Name.LocalName)) { ctx.NotJudged(Checks.SilentStops, e, "the code sets whether it can take focus"); return; }
        // 拿到焦點就轉給別人的（工具列外框常這樣寫）：停不停在這裡看程式
        if (e.Attributes().Any(a => a.Name.LocalName is "GotFocus" or "GotKeyboardFocus" or "PreviewGotKeyboardFocus")) { ctx.NotJudged(Checks.SilentStops, e, "a focus handler may pass the focus on"); return; }
        if (TriggerTouches(ctx, e)) { ctx.NotJudged(Checks.SilentStops, e, "a trigger changes whether it can take focus"); return; }

        // 清單、頁籤、選單的項目裡面：Tab 進不進得去看容器
        if (MouseRule.InItemTemplate(ctx, e)) { ctx.NotJudged(Checks.SilentStops, e, "inside a list item template (the list may handle the keys)"); return; }
        if (Context.ControlTemplateOf(e) is { } template)
        {
            var target = ctx.TemplateTarget(template) is { } t ? ctx.Project.BaseOfName(t) : null;
            if (target is null || Wpf.Items.Contains(target) || ctx.Project.Code.TouchesFocus(target) || target is "MenuItem" or "ScrollBar" or "ScrollViewer" or "Slider" or "ComboBox" or "ToolTip" or "DataGridCell" or "DataGridRow" or "GridViewColumnHeader" or "DataGridColumnHeader" or "DataGridRowHeader" or "DataGridRowsPresenter")
            {
                ctx.NotJudged(Checks.SilentStops, e, "a template part");
                return;
            }
        }
        if (e.Ancestors().Any(a => !Markup.IsPropertyElement(a) && styles.Resolve(a, "KeyboardNavigation.TabNavigation") is { IsValue: true, Value: "None" or "Once" })) { ctx.NotJudged(Checks.SilentStops, e, "inside a container that limits Tab"); return; }

        if (!Markup.IsWpf(e))
        {
            // 自己的類別：要改的是類別，報一次（它的樣式那裡，沒有樣式就第一個用它的地方）
            var own = styles.StylesFor(e.Name.LocalName);
            // 連 BasedOn 一起看：函式庫作者的 Generic.xaml 常只寫 <Style BasedOn="…" TargetType="…"/>
            if (own.SelectMany(styles.ChainsOfStyle).Any(c => ((ReadOnlySpan<string>)["Focusable", "IsTabStop"]).ToArray().Any(p => c.Setter(p) is { } f && styles.SetterValue(f).IsFalse)))
            {
                ctx.NotJudged(Checks.SilentStops, e, "a style of its class keeps it out of the tab order");
                return;
            }
            ctx.Later(Checks.SilentStops + ":" + e.Name.LocalName, e, () => ctx.Report(Kinds.SilentTabStop, e,
                $"<{e.Name.LocalName}> takes a Tab stop but is not in the UI Automation tree (it derives from {control} and has no AutomationPeer), so a screen reader says nothing there",
                at: own.Count > 0 ? own[0].Style : null, identity: e.Name.LocalName, keyFile: "(class)"));
            return;
        }

        var named = styles.Resolve(e, "AutomationProperties.Name").IsSet ? "; its AutomationProperties.Name is not used" : "";
        ctx.Report(Kinds.SilentTabStop, e, $"<{e.Name.LocalName}> takes a Tab stop but is not in the UI Automation tree, so a screen reader says nothing there{named}");
    }

    /// <summary>自己或外層寫死看不到、不能用（樣式寫死的也算）。</summary>
    private static bool Hidden(Context ctx, XElement e) =>
        ctx.Project.Styles.Resolve(e, "Visibility") is { IsValue: true, Value: "Collapsed" or "Hidden" } && !ctx.Project.OpensLater(e, "Visibility")
        || e.AncestorsAndSelf().Any(a => string.Equals((string?)a.Attribute("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase));

    /// <summary>不在 Tab 走得到的畫面上：提示、畫成筆刷的、放在資源裡等人拿的、DataGrid 的展開列（Tab 從儲存格直接離開表格）。</summary>
    private static bool NeverReached(XElement e)
    {
        foreach (var a in e.AncestorsAndSelf())
        {
            var local = a.Name.LocalName;
            if (local is "ToolTip" or "VisualBrush" || local.EndsWith(".ToolTip", StringComparison.Ordinal) || local.EndsWith(".RowDetailsTemplate", StringComparison.Ordinal)) return true;
            if (local == "DataTemplate" && a.Parent?.Name.LocalName.EndsWith(".RowDetailsTemplate", StringComparison.Ordinal) == true) return true;
            if (local is "ControlTemplate" or "DataTemplate" or "HierarchicalDataTemplate" or "ItemsPanelTemplate" or "Style") return false;
            if (a.Attribute(Markup.XKey) is not null) return true;
        }
        return false;
    }

    private static bool TriggerTouches(Context ctx, XElement e) =>
        ctx.Project.Styles.ChainsOf(e).Any(c => c.Styles.Any(s => s.Triggers.SelectMany(t => t.Descendants()).Any(x =>
            x.Name.LocalName == "Setter" && (string?)x.Attribute("Property") is { } p && StyleIndex.NormalizeProperty(p) is "Focusable" or "IsTabStop")));
}
