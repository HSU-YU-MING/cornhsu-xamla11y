using System.Globalization;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Walk;

public enum WalkKind
{
    Stop,
    /// <summary>要看執行時：綁定、資料、之後才打開的。</summary>
    Maybe,
    /// <summary>不認得的控制項：裡面可能還有。</summary>
    Unknown,
}

/// <summary>Tab 會停的一站。</summary>
/// <param name="Note">為什麼不確定（英文，<see cref="TabWalk.Notes"/> 有中文）。</param>
public sealed record WalkStop(XElement Element, string File, int Line, string Control, WalkKind Kind, string? Note);

/// <param name="Trap">Tab 進去就出不來的容器（工具列、TabNavigation=Cycle）；沒有是 null。</param>
/// <param name="After">它後面還有幾站。</param>
/// <param name="LeftOut">一開始藏起來或停用、沒有東西會打開的。</param>
/// <param name="InPopups">在 Popup、右鍵選單裡的：打開才有，另外走。</param>
/// <param name="Cut">太大，沒走完。</param>
public sealed record WalkResult(IReadOnlyList<WalkStop> Stops, XElement? Trap, int After, int LeftOut, int InPopups, bool Cut);

/// <summary>
/// 一個 XAML 檔按 Tab 會依序停在哪裡。照 WPF 的規則（實測 prototype/UiaOracle/facts/walk.txt，逐案比順序）：
/// 先 TabIndex、再照視覺樹；Continue 的容器不分組，Local、Once、None、Cycle、Contained 各自一組。
/// 只看 XAML：範本裡的零件、程式碼加的、資料產生的不在裡面 —— 可能有的標成「不確定」。
/// </summary>
public sealed class TabWalk
{
    /// <summary>不確定的原因 → 中文。</summary>
    public static readonly IReadOnlyDictionary<string, string> Notes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FromData] = "項目來自資料",
        [Bound] = "停不停得到看綁定",
        [Outside] = "樣式在專案外",
        [Later] = "一開始藏起來或停用，之後會打開",
        [BoundVisibility] = "看不看得到看綁定",
        [Collapsed] = "展開之後才有",
        [NotKnown] = "不認得的控制項，裡面可能還有",
        [NotMeasured] = "沒量過的控制項，可能是一站或好幾站",
        [BoundEnabled] = "能不能用看綁定",
        [Triggered] = "觸發器會改它停不停得到",
        [BoundIndex] = "選到哪一個看綁定",
        [BoundContent] = "內容來自資料，裡面可能還有",
    };

    private const string FromData = "its items come from data";
    private const string Bound = "whether it is a stop is bound";
    private const string Outside = "its style is outside the project";
    private const string Later = "hidden or disabled at first, something opens it later";
    private const string BoundVisibility = "whether it is shown is bound";
    private const string Collapsed = "only when it is expanded";
    private const string NotKnown = "not a control this tool knows; it may have stops of its own";
    private const string NotMeasured = "not measured; it may be a stop, or several";
    private const string BoundEnabled = "whether it is enabled is bound";
    private const string Triggered = "a trigger changes whether it is a stop";
    private const string BoundIndex = "which one is selected is bound";
    private const string BoundContent = "its content comes from data and may have stops";

    private static readonly HashSet<string> Stops = new(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "RichTextBox", "ComboBox", "Slider",
        "DatePicker", "Calendar", "Hyperlink", "Expander", "GridSplitter", "ContentControl", "Control", "HeaderedContentControl", "ItemsControl",
        "HeaderedItemsControl", "ButtonBase", "RangeBase", "TreeView", "Frame", "FlowDocumentScrollViewer", "InkCanvas", Wpf.HeaderedToggle,
    };

    /// <summary>有鍵盤的地方、但沒量過的 WPF 控制項：說不知道，不能當成沒有。</summary>
    private static readonly HashSet<string> Unmeasured = new(StringComparer.Ordinal)
        { "WebBrowser", "WindowsFormsHost", "ElementHost", "FlowDocumentReader", "FlowDocumentPageViewer", "DocumentViewer" };

    /// <summary>超過這麼多站、走過這麼多元素就停：一層包兩個下一層，三十層就是十億，一站都沒有也跑不完。</summary>
    private const int MostStops = 5000;
    private const int MostVisits = 200_000;
    private int _count;
    private int _visits;
    private bool Full => _count >= MostStops || _visits >= MostVisits;
    private readonly HashSet<string> _inside = new(StringComparer.Ordinal);

    private readonly Project _project;
    private int _leftOut;
    private int _popups;
    private XElement? _trap;

    private TabWalk(Project project) => _project = project;

    public static WalkResult Of(Project project, XamlDoc doc)
    {
        var walk = new TabWalk(project);
        var group = new Group();
        walk.Children(doc.Root, group, null, 0);
        var stops = new List<WalkStop>();
        walk.Flatten(group, stops, out var after);
        return new WalkResult(stops, walk._trap, after, walk._leftOut, walk._popups, walk.Full);
    }

    // ───────────────────────── 分組與順序 ─────────────────────────

    private sealed record Entry(int TabIndex, WalkStop? Stop, Group? Group);

    private sealed class Group
    {
        public readonly List<Entry> Entries = [];
        public string Mode = "Continue";
        public XElement? Element;
        public IEnumerable<Entry> Sorted => Entries.OrderBy(e => e.TabIndex);       // OrderBy 是穩定的：一樣的照文件順序
    }

    private void Flatten(Group group, List<WalkStop> into, out int after)
    {
        after = 0;
        foreach (var e in group.Sorted)
        {
            var more = new List<WalkStop>();
            if (e.Stop is { } stop) more.Add(stop);
            else if (e.Group is { } inner && inner.Mode != "None")
            {
                Flatten(inner, more, out var innerAfter);
                if (inner.Mode == "Once" && more.Count > 1) more = More(inner, more);
                after += innerAfter;
            }
            // 進了繞圈的那一組，後面的站都到不了（後面的組，裡面的站已經算過）
            if (_trap is not null && e.Stop is not null) { after++; continue; }
            into.AddRange(more);
            if (e.Group is { Mode: "Cycle" or "Contained" } ring && more.Count > 0 && _trap is null) _trap = ring.Element;
        }
    }

    /// <summary>Once：一站。清單的項目裡面還有控制項的話，那個項目跟它裡面的。</summary>
    private static List<WalkStop> More(Group once, List<WalkStop> stops)
    {
        var first = stops[0];
        return once.Element is { } e && e.Name.LocalName is not ("StackPanel" or "Grid" or "DockPanel" or "WrapPanel" or "Border")
            ? [.. stops.TakeWhile(s => s == first || s.Element.AncestorsAndSelf().Contains(first.Element))]
            : [first];
    }

    // ───────────────────────── 走 ─────────────────────────

    private void Children(XElement e, Group group, string? maybe, int depth)
    {
        // 標題在內容前面，XAML 可能寫在後面
        foreach (var c in e.Elements().Where(x => Is(x, ".Header"))) Children(c, group, maybe, depth + 1);
        foreach (var c in e.Elements())
        {
            if (Markup.IsPropertyElement(c))
            {
                if (Is(c, ".Content", ".Child", ".Children", ".Items", ".Inlines", ".Blocks")) Children(c, group, maybe, depth + 1);
                continue;
            }
            Visit(c, group, maybe, depth + 1);
        }
    }

    private static bool Is(XElement e, params ReadOnlySpan<string> endings)
    {
        foreach (var x in endings)
            if (e.Name.LocalName.EndsWith(x, StringComparison.Ordinal)) return true;
        return false;
    }

    private void Visit(XElement e, Group group, string? maybe, int depth)
    {
        var local = e.Name.LocalName;
        if (Full) return;
        _visits++;
        if (local is "Popup" or "ContextMenu") _popups += Count(e);
        if (depth > 400 || local is "Popup" or "ContextMenu" or "ToolTip" || Markup.IsWpf(e) && local is "ResourceDictionary" or "Style" or "DataTemplate" or "ControlTemplate" or "Storyboard") return;

        // 看不看得到、能不能用
        var styles = _project.Styles;
        var visibility = styles.Resolve(e, "Visibility");
        if (visibility is { IsValue: true, Value: "Collapsed" or "Hidden" })
        {
            if (!_project.OpensLater(e, "Visibility")) { _leftOut += Count(e); return; }
            maybe ??= Later;
        }
        else if (visibility.IsValue && (Markup.IsMarkup(visibility.Value) || visibility.Element is not null)) maybe ??= BoundVisibility;
        var enabled = styles.Resolve(e, "IsEnabled");
        if (enabled.IsFalse)
        {
            if (!_project.OpensLater(e, "IsEnabled")) { _leftOut += Count(e); return; }
            maybe ??= Later;
        }
        else if (enabled.IsValue && (Markup.IsMarkup(enabled.Value) || enabled.Element is not null)) maybe ??= BoundEnabled;
        // 樣式的觸發器會藏起來、停用、移出 Tab
        foreach (var property in (ReadOnlySpan<string>)["Visibility", "IsEnabled", "Focusable", "IsTabStop"])
            if (!styles.LocalValue(e, property).IsSet && _project.StyleTriggerSets(e, property)) maybe ??= Triggered;

        var control = _project.BaseOf(e);
        if (Markup.IsWpf(e) && (Unmeasured.Contains(local) || local.StartsWith("Ribbon", StringComparison.Ordinal))) { Add(e, group, local, WalkKind.Unknown, NotMeasured); return; }
        if (control is null)
        {
            if (Markup.IsWpf(e)) { Children(e, group, maybe, depth); return; }
            // 專案自己的 UserControl：走進它的檔案
            if (_project.DocOfClass(e) is { } own) { Inside(own, group, maybe, depth); return; }
            Add(e, group, local, WalkKind.Unknown, NotKnown);
            Children(e, group, maybe, depth);
            return;
        }

        var inner = group;
        var mode = styles.Resolve(e, "KeyboardNavigation.TabNavigation") is { IsValue: true, Value: { } m } && !Markup.IsMarkup(m) ? m : control switch
        {
            "ListBox" or "ListView" => "Once",
            "ToolBar" => "Cycle",
            "TabControl" => "Local",
            _ => "Continue",
        };
        var own2 = OwnStop(e, control, maybe);
        if (own2 is { } s) Add(e, group, control, s.Note is null ? WalkKind.Stop : WalkKind.Maybe, s.Note);
        if (mode != "Continue")
        {
            inner = new Group { Mode = mode, Element = e };
            group.Entries.Add(new Entry(TabIndexOf(e), null, inner));
        }

        switch (control)
        {
            case "ComboBox" or "DatePicker" or "Calendar" or "Slider" or "TreeView" or "TextBox" or "RichTextBox" or "PasswordBox":
                return;
            case "DataGrid":
                if (HasData(e)) Add(e, inner, control, WalkKind.Maybe, FromData);
                return;
            case "ListBox" or "ListView":
                if (styles.Resolve(e, "ItemsSource").IsSet && e.Attribute("ItemsSource") is not null || Items(e).Count == 0 && HasData(e)) { Add(e, inner, control, WalkKind.Maybe, FromData); return; }
                var items = Items(e);
                var selected = items.FindIndex(i => styles.Resolve(i, "IsSelected").IsTrue);
                var at = styles.Resolve(e, "SelectedIndex");
                if (selected < 0 && at.IsValue && int.TryParse(at.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chosen) && chosen < items.Count) selected = chosen;
                // 選到哪一個要看綁定：停一次，停在哪一項不知道
                if (at.IsValue && Markup.IsMarkup(at.Value) || styles.Resolve(e, "SelectedItem").IsValue || styles.Resolve(e, "SelectedValue").IsValue) maybe ??= BoundIndex;
                if (selected > 0) { items.Insert(0, items[selected]); items.RemoveAt(selected + 1); }
                foreach (var item in items) Item(item, inner, maybe, depth, control == "ListBox" ? "ListBoxItem" : "ListViewItem");
                return;
            case "Menu":
                if (e.Attribute("ItemsSource") is not null) Add(e, inner, "MenuItem", WalkKind.Maybe, FromData);
                foreach (var item in Items(e).Where(i => _project.BaseOf(i) == "MenuItem"))
                {
                    if (styles.Resolve(item, "Focusable").IsFalse || styles.Resolve(item, "IsTabStop").IsFalse) continue;
                    if (Hidden(item, "Visibility") || Hidden(item, "IsEnabled")) { _leftOut++; continue; }
                    Add(item, inner, "MenuItem", WalkKind.Stop, maybe ?? Uncertain(item));
                }
                return;
            case "TabControl":
                Tabs(e, inner, maybe, depth);
                return;
            case "Expander":
                foreach (var c in e.Elements().Where(x => Is(x, ".Header"))) Children(c, inner, maybe, depth + 1);
                var open = styles.Resolve(e, "IsExpanded");
                if (open.IsTrue) Content(e, inner, maybe, depth);
                else if (open.IsValue && Markup.IsMarkup(open.Value)) Content(e, inner, maybe ?? Collapsed, depth);
                else if (_project.ChangesLater(e, "IsExpanded")) Content(e, inner, maybe ?? Collapsed, depth);
                return;
            case "ItemsControl" or "HeaderedItemsControl" when e.Attribute("ItemsSource") is not null:
                Add(e, inner, control, WalkKind.Unknown, FromData);
                return;
            case "ContentControl" or "ContentPresenter" or "UserControl" or "Frame" when e.Attribute("Content") is { } bound && Markup.IsMarkup(bound.Value) || control == "Frame" && e.Attribute("Source") is not null:
                Add(e, inner, control, WalkKind.Unknown, BoundContent);
                break;
        }
        // 專案自己的類別：內容寫在它自己的檔案裡
        if (!Markup.IsWpf(e) && _project.DocOfClass(e) is { } doc && doc.Root != e) Inside(doc, inner, maybe, depth);
        Children(e, inner, maybe, depth);
    }

    /// <summary>走進專案自己的控制項。包到自己的（遞迴的範本）不再進去，站數也有上限：一個檔案 40 個自己，跑不完。</summary>
    private void Inside(XamlDoc doc, Group group, string? maybe, int depth)
    {
        if (Full || !_inside.Add(doc.File)) return;
        Children(doc.Root, group, maybe, depth + 10);
        _inside.Remove(doc.File);
    }

    private void Content(XElement e, Group group, string? maybe, int depth)
    {
        foreach (var c in e.Elements())
            if (Is(c, ".Content")) Children(c, group, maybe, depth + 1);
            else if (!Markup.IsPropertyElement(c)) Visit(c, group, maybe, depth + 1);
    }

    private static List<XElement> Items(XElement e) => [.. e.Elements().Where(x => !Markup.IsPropertyElement(x)).Concat(e.Elements().Where(x => Is(x, ".Items")).SelectMany(x => x.Elements())).Where(x => x.Name.LocalName is not ("GridView" or "Style"))];

    private bool HasData(XElement e) => e.Attribute("ItemsSource") is not null || _project.CodeSets(e, "ItemsSource", "Items") || _project.Styles.Resolve(e, "ItemsSource") is { IsSet: true, Origin: not null };

    private void Item(XElement item, Group group, string? maybe, int depth, string container)
    {
        var styles = _project.Styles;
        if (Hidden(item, "Visibility") || Hidden(item, "IsEnabled")) { _leftOut++; return; }
        maybe ??= Uncertain(item);
        // 不是項目容器的元素，WPF 會包一層
        if (!styles.Resolve(item, "Focusable").IsFalse && !styles.Resolve(item, "IsTabStop").IsFalse) Add(item, group, container, maybe is null ? WalkKind.Stop : WalkKind.Maybe, maybe);
        if (_project.BaseOf(item) is "ListBoxItem" or "ListViewItem" or "ComboBoxItem") Children(item, group, maybe, depth + 1);
        else Visit(item, group, maybe, depth + 1);
    }

    /// <summary>頁籤列一站（選到的那一頁），接著是那一頁的內容（自己一組）。</summary>
    private void Tabs(XElement e, Group group, string? maybe, int depth)
    {
        var styles = _project.Styles;
        if (e.Attribute("ItemsSource") is not null) { Add(e, group, "TabItem", WalkKind.Maybe, FromData); return; }
        // 收起來、停用的也算在索引裡，也會被選到：標題停不到，內容照樣在（實測）
        var tabs = Items(e).Where(t => _project.BaseOf(t) == "TabItem").ToList();
        if (tabs.Count == 0) return;
        var index = styles.Resolve(e, "SelectedIndex");
        var chosen = tabs.FirstOrDefault(t => styles.Resolve(t, "IsSelected").IsTrue)
                     ?? (index.IsValue && int.TryParse(index.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < tabs.Count ? tabs[n] : tabs[0]);
        if (index.IsValue && Markup.IsMarkup(index.Value) || styles.Resolve(e, "SelectedItem").IsValue || _project.CodeSets(e, "SelectedIndex", "SelectedItem")) maybe ??= BoundIndex;
        if (!styles.Resolve(chosen, "Focusable").IsFalse && !styles.Resolve(chosen, "IsTabStop").IsFalse && !Hidden(chosen, "Visibility") && !Hidden(chosen, "IsEnabled"))
            Add(chosen, group, "TabItem", WalkKind.Stop, maybe ?? Uncertain(chosen));
        var content = new Group { Mode = "Local", Element = chosen };
        group.Entries.Add(new Entry(int.MaxValue, null, content));
        Content(chosen, content, maybe, depth);
    }

    /// <summary>寫死藏起來、停用，而且沒有東西會打開。</summary>
    private bool Hidden(XElement e, string property)
    {
        var r = _project.Styles.Resolve(e, property);
        return (property == "Visibility" ? r is { IsValue: true, Value: "Collapsed" or "Hidden" } : r.IsFalse) && !_project.OpensLater(e, property);
    }

    /// <summary>看不看得到、能不能用要看執行時。</summary>
    private string? Uncertain(XElement e)
    {
        var styles = _project.Styles;
        var visibility = styles.Resolve(e, "Visibility");
        var enabled = styles.Resolve(e, "IsEnabled");
        if (visibility is { IsValue: true, Value: "Collapsed" or "Hidden" } || enabled.IsFalse) return Later;
        if (visibility.IsValue && (Markup.IsMarkup(visibility.Value) || visibility.Element is not null)) return BoundVisibility;
        if (enabled.IsValue && (Markup.IsMarkup(enabled.Value) || enabled.Element is not null)) return BoundEnabled;
        return _project.StyleTriggerSets(e, "Visibility") || _project.StyleTriggerSets(e, "IsEnabled") ? Triggered : null;
    }

    private (bool, string? Note)? OwnStop(XElement e, string control, string? maybe)
    {
        var styles = _project.Styles;
        var focusable = styles.Resolve(e, "Focusable");
        var tabStop = styles.Resolve(e, "IsTabStop");
        // 裡面的輸入框照樣停（實測）
        var inside = control == "DatePicker" || control == "ComboBox" && styles.Resolve(e, "IsEditable").IsTrue;
        if (!inside && (focusable.IsFalse || tabStop.IsFalse)) return null;
        if (!Stops.Contains(control) && !focusable.IsTrue) return null;
        if (inside) return (true, maybe);
        if (focusable.IsValue && Markup.IsMarkup(focusable.Value) || tabStop.IsValue && Markup.IsMarkup(tabStop.Value)) return (true, maybe ?? Bound);
        // 專案外的樣式：內建的控制項多半照樣停；本來就模稜兩可的（ContentControl、ItemsControl）說不準
        if ((focusable.IsUnreadable || tabStop.IsUnreadable) && control is ("ContentControl" or "Control" or "ItemsControl" or "HeaderedContentControl")) return (true, maybe ?? Outside);
        return (true, maybe);
    }

    private void Add(XElement e, Group group, string control, WalkKind kind, string? note)
    {
        _count++;
        group.Entries.Add(new Entry(TabIndexOf(e), new WalkStop(e, StyleIndex.FileOf(e), Context.LineOf(e), control, note is null ? kind : kind == WalkKind.Stop ? WalkKind.Maybe : kind, note), null));
    }

    private int TabIndexOf(XElement e)
    {
        var r = _project.Styles.Resolve(e, "TabIndex");
        return r is { IsValue: true, Value: { } v } && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
    }

    private int Count(XElement e) => e.DescendantsAndSelf().Count(x => !Markup.IsPropertyElement(x) && _project.BaseOf(x) is { } c && Stops.Contains(c) && c is not ("ContentControl" or "Control" or "ItemsControl" or "HeaderedContentControl"));
}
