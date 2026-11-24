using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// R4：滑鼠按得到、鍵盤按不到。只看 XAML 裡寫的處理器（程式碼裡 += 的看不到，報告有講）。
///
/// 誤報最多的情形逐一排除：拖動視窗的標題列、拖曳手勢（同時有 MouseMove）、容器底下有真的控制項、
/// 根元素、Preview 事件（通道事件多半是旁聽，不是「按這裡」）。
/// </summary>
internal static partial class MouseRule
{
    private static readonly string[] Clicks =
        ["MouseLeftButtonDown", "MouseLeftButtonUp", "MouseDown", "MouseUp", "MouseDoubleClick", "TouchDown", "TouchUp"];

    private static readonly string[] PreviewClicks =
        ["PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewMouseDown", "PreviewMouseUp"];

    public static void Check(Context ctx, XamlDoc doc, XElement e)
    {
        var control = ctx.Project.BaseOf(e);
        if (control is null || !Wpf.NonControls.Contains(control)) return;

        var (gesture, handler, preview) = ClickOn(ctx, e);
        if (gesture is null) return;
        var local = e.Name.LocalName;

        if (e.Parent is null) { ctx.NotJudged("R4", e, "a handler on the root element"); return; }

        if (handler is not null && IsWindowDrag(ctx, doc, handler))
        {
            ctx.NotJudged("R4", e, "a handler that moves the window (the system menu does it from the keyboard)");
            return;
        }

        // 拖曳：同時處理移動（畫布、分隔線、調整大小）。路徑本身就是輸入，WCAG 2.1.1 有例外，靜態分不出來
        if (HasAny(e, "MouseMove", "PreviewMouseMove", "DragDelta")
            || (handler is not null && ctx.Project.MethodBody(doc.File, handler) is { } drag && (drag.Contains("CaptureMouse", StringComparison.Ordinal) || drag.Contains("DoDragDrop", StringComparison.Ordinal))))
        {
            ctx.NotJudged("R4", e, "looks like a drag gesture (it also handles mouse movement)");
            return;
        }

        if (control is "Run" or "Span" && e.Ancestors().Any(a => a.Name.LocalName == "Hyperlink")) { ctx.Pass("R4", e); return; }

        // 底下有真的控制項：處理器多半是「點背景」「順便記住哪一列」，按得到的事由那些控制項做
        if (HasControls(ctx, e) || (ctx.Project.DocOfClass(e) is { } inner && HasControls(ctx, inner.Root)))
        {
            ctx.NotJudged("R4", e, "a container around real controls");
            return;
        }

        // 裝在真的控制項裡（Button 的內容）：鍵盤從那個控制項走
        // （裡面那一塊做的可能是另一件事：頁籤上的 ✕。看不出來，不說沒問題）
        if (e.Ancestors().Any(a => !Markup.IsPropertyElement(a) && ctx.Project.BaseOf(a) is { } c && (Wpf.Buttons.Contains(c) || c is "MenuItem" or "Hyperlink")))
        {
            ctx.NotJudged("R4", e, "inside a control that the keyboard reaches (whether both do the same is not visible)");
            return;
        }

        // 同一個元素自己有鍵盤處理（KeyDown／KeyBinding）而且能拿焦點
        var keyboard = HasAny(e, "KeyDown", "KeyUp", "PreviewKeyDown", "PreviewKeyUp")
                       || e.Elements().Any(c => c.Name.LocalName.EndsWith(".InputBindings", StringComparison.Ordinal) && c.Elements().Any(b => b.Name.LocalName == "KeyBinding"));
        var focusable = ctx.Project.Styles.Resolve(e, "Focusable");
        if (keyboard && (focusable.IsTrue || (control == "ContentControl" && !focusable.IsFalse)))
        {
            ctx.NotJudged("R4", e, "has its own keyboard handler (which keys it handles is not visible)");
            return;
        }

        // 範本：控制項的外觀（範本擁有者可能自己處理鍵盤）
        if (Context.ControlTemplateOf(e) is { } template)
        {
            var target = ctx.TemplateTarget(template);
            ctx.NotJudged("R4", e, target is "Window" ? "window chrome" : "a part of a control template (the control may handle the keyboard itself)");
            return;
        }

        // 清單項目的範本：項目本身拿得到焦點，鍵盤處理可能寫在清單上
        if (InItemTemplate(ctx, e))
        {
            ctx.NotJudged("R4", e, "inside a list item template (the list may handle Enter or Space)");
            return;
        }

        if (preview) { ctx.NotJudged("R4", e, "only a Preview (tunnelling) handler"); return; }

        // 同一個動作也綁在鍵盤到得了的控制項上（MAA 的 TooltipBlock 疊在按鈕上、點了做一樣的事）
        if (handler is not null && SameAction(ctx, e, handler))
        {
            ctx.NotJudged("R4", e, "a control in the same file that the keyboard reaches has the same action");
            return;
        }

        // 程式碼改了 Focusable 或掛了鍵盤事件
        if (ctx.Project.CodeSets(e, "KeyDown", "PreviewKeyDown", "KeyUp", "Focusable"))
        {
            ctx.Blocked("R4", e, "the code-behind handles keys for it");
            return;
        }

        ctx.Report(Kinds.MouseOnly, e, $"<{local}> reacts to {gesture}{(handler is null ? "" : $" ({Context.Show(handler)})")} but the keyboard cannot reach it");
    }

    private static bool SameAction(Context ctx, XElement e, string action)
    {
        static string Squeeze(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
        var wanted = Squeeze(action);
        foreach (var d in e.Document!.Root!.Descendants())
        {
            if (ReferenceEquals(d, e)) continue;
            foreach (var a in d.Attributes())
                if (a.Name.LocalName is "Command" or "Click" && Squeeze(a.Value) == wanted
                    && d.Name.LocalName is not ("MouseBinding" or "InputBinding" or "KeyBinding")
                    && ctx.Project.BaseOf(d) is { } c && (Wpf.Buttons.Contains(c) || c is "MenuItem" or "Hyperlink")
                    && !ctx.IsFalse(d, "Focusable") && !ctx.IsFalse(d, "IsTabStop"))
                    return true;
            // <KeyBinding Command="…"/>（視窗或父元素的快速鍵）
            if (d.Name.LocalName == "KeyBinding" && (string?)d.Attribute("Command") is { } key && Squeeze(key) == wanted) return true;
        }
        return false;
    }

    /// <summary>元素上的點按：事件屬性、MouseBinding、Behaviors 的 EventTrigger、Caliburn 的 Message.Attach、樣式的 EventSetter。</summary>
    private static (string? Gesture, string? Handler, bool Preview) ClickOn(Context ctx, XElement e)
    {
        foreach (var c in Clicks)
            if (Handler(e, c) is { } h) return (c, h, false);

        foreach (var holder in e.Elements().Where(x => x.Name.LocalName.EndsWith(".InputBindings", StringComparison.Ordinal)))
            if (holder.Elements().FirstOrDefault(b => b.Name.LocalName == "MouseBinding") is { } binding)
            {
                var g = (string?)binding.Attribute("Gesture") ?? (string?)binding.Attribute("MouseAction") ?? "LeftClick";
                if (g.Contains("Left", StringComparison.Ordinal)) return ($"a {g} MouseBinding", (string?)binding.Attribute("Command"), false);
            }

        foreach (var trigger in e.Elements().Where(x => x.Name.LocalName.EndsWith("Interaction.Triggers", StringComparison.Ordinal)).SelectMany(x => x.Elements()))
            if (trigger.Name.LocalName == "EventTrigger" && (string?)trigger.Attribute("EventName") is { } ev && Clicks.Contains(ev))
                return (ev, null, false);

        if (e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Message.Attach") is { } attach && CaliburnRegex().Match(attach.Value) is { Success: true } m)
            return (m.Groups["event"].Value, null, false);

        foreach (var chain in ctx.Project.Styles.ChainsOf(e))
            foreach (var s in chain.Styles)
                foreach (var setter in s.EventSetters)
                    if ((string?)setter.Attribute("Event") is { } ev2 && Clicks.Contains(StyleIndex.NormalizeProperty(ev2)))
                        return (StyleIndex.NormalizeProperty(ev2), (string?)setter.Attribute("Handler"), false);

        foreach (var c in PreviewClicks)
            if (Handler(e, c) is { } h) return (c, h, true);
        return (null, null, false);
    }

    [GeneratedRegex(@"\[\s*Event\s+(?<event>Mouse(?:Left)?(?:Button)?(?:Down|Up)|MouseDoubleClick)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex CaliburnRegex();

    private static bool HasAny(XElement e, params ReadOnlySpan<string> events)
    {
        foreach (var name in events)
            if (Handler(e, name) is not null) return true;
        return false;
    }

    /// <summary>事件屬性：MouseDown、UIElement.MouseDown、Mouse.MouseDown 都是。</summary>
    private static string? Handler(XElement e, string ev)
    {
        foreach (var a in e.Attributes())
        {
            if (a.IsNamespaceDeclaration || a.Value.Length == 0) continue;
            var n = a.Name.LocalName;
            if (n == ev || (n.EndsWith("." + ev, StringComparison.Ordinal) && n[..^(ev.Length + 1)] is "UIElement" or "Mouse" or "Control" or "FrameworkElement" or "ContentElement" or "Keyboard")) return a.Value;
        }
        return null;
    }

    /// <summary>底下（畫面上的部分）有沒有真的控制項。選單、提示、資源、樣式裡的不算。</summary>
    private static bool HasControls(Context ctx, XElement root)
    {
        foreach (var d in root.Elements())
        {
            if (Markup.IsPropertyElement(d))
            {
                var name = d.Name.LocalName[(d.Name.LocalName.IndexOf('.') + 1)..];
                if (name is "ContextMenu" or "ToolTip" or "Resources" or "Style" or "InputBindings" or "Triggers" or "CommandBindings" or "DataContext") continue;
            }
            else
            {
                var local = d.Name.LocalName;
                var control = ctx.Project.BaseOf(d);
                if (control is null ? NameRules.LooksInteractive(local) || NameRules.LooksInput(local)
                    : Wpf.Interactive.Contains(control) || Wpf.Inputs.Contains(control) || control is "ListBox" or "ListView" or "TreeView" or "DataGrid" or "ScrollViewer" || (control == "ItemsControl" && d != root)) return true;
            }
            if (HasControls(ctx, d)) return true;
        }
        return false;
    }

    /// <summary>處理器在拖動視窗（DragMove、WM_NCLBUTTONDOWN）。讀不到 code-behind 時看名字。</summary>
    private static bool IsWindowDrag(Context ctx, XamlDoc doc, string handler)
    {
        if (ctx.Project.MethodBody(doc.File, handler) is { } body)
            return body.Contains("DragMove", StringComparison.Ordinal) || body.Contains("WM_NCLBUTTONDOWN", StringComparison.Ordinal) || body.Contains("HTCAPTION", StringComparison.OrdinalIgnoreCase);
        return handler.Contains("DragMove", StringComparison.OrdinalIgnoreCase) || handler.Contains("DragWindow", StringComparison.OrdinalIgnoreCase);
    }

    private static bool InItemTemplate(Context ctx, XElement e)
    {
        foreach (var a in e.Ancestors())
        {
            if (a.Name.LocalName is "DataTemplate" or "HierarchicalDataTemplate")
            {
                var holder = a.Parent?.Name.LocalName ?? "";
                // 明寫在項目清單上的 ItemTemplate；ItemsControl 的項目沒有容器焦點，不算
                if (holder.EndsWith(".ItemTemplate", StringComparison.Ordinal) || holder.EndsWith(".CellTemplate", StringComparison.Ordinal))
                    return !holder.StartsWith("ItemsControl.", StringComparison.Ordinal);
                // 資源裡的範本：不知道誰用
                return a.Attribute(Markup.XKey) is not null || a.Attribute("DataType") is not null;
            }
            if (Markup.IsPropertyElement(a)) continue;
            if (ctx.Project.ControlOf(a) is "ListBoxItem" or "ListViewItem" or "TreeViewItem" or "DataGridCell" or "DataGridRow" or "ComboBoxItem") return true;
            if (a.Name.LocalName == "TabItem") return e.Ancestors().TakeWhile(x => x != a).Any(x => x.Name.LocalName == "TabItem.Header");
        }
        return false;
    }
}
