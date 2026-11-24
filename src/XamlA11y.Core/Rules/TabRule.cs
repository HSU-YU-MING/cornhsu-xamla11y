using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Rules;

/// <summary>
/// R5：看得見、能用，但 Tab 到不了（IsTabStop="False"、Focusable="False"）。
/// 候選只有**設了**這兩個屬性的元素：沒設的不算「檢查過沒問題」，不然每個控制項都自動通過，可解析率會被灌水。
/// </summary>
internal static class TabRule
{
    private static readonly HashSet<string> Eligible = new(StringComparer.Ordinal)
    { "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "RichTextBox", "ComboBox", "Slider", "Hyperlink" };

    public static void Check(Context ctx, XamlDoc doc, XElement e)
    {
        if (ctx.Project.BaseOf(e) is not { } control || !Eligible.Contains(control) || e.Parent is null) return;

        var styles = ctx.Project.Styles;
        // KeyboardNavigation.IsTabStop 跟 IsTabStop 是同一個屬性（StyleIndex 正規化成同一個名字）
        var focusable = styles.Resolve(e, "Focusable");
        var tabStop = styles.Resolve(e, "IsTabStop");

        if (!focusable.IsFalse && !tabStop.IsFalse)
        {
            if (!focusable.IsSet && !tabStop.IsSet) return;
            // 樣式在專案外：兩個屬性都讀不出來，不是「設了讀不出來」
            if (focusable is { IsUnreadable: true, Origin: null } || tabStop is { IsUnreadable: true, Origin: null }) { ctx.NotJudged("R5", e, "its style is outside the project"); return; }
            if (focusable.IsUnreadable || tabStop.IsUnreadable) { ctx.Blocked("R5", e, "Focusable or IsTabStop is set to something this tool cannot read"); return; }
            if (Markup.IsMarkup(focusable.Value) || Markup.IsMarkup(tabStop.Value)) { ctx.NotJudged("R5", e, "Focusable or IsTabStop is bound"); return; }
            ctx.Pass("R5", e);
            return;
        }

        var (set, property) = focusable.IsFalse ? (focusable, "Focusable") : (tabStop, "IsTabStop");

        if (Context.ControlTemplateOf(e) is not null) { ctx.NotJudged("R5", e, "a template part"); return; }

        // 可編輯的 ComboBox：裡面的輸入框照樣進 Tab（實測）
        if (control == "ComboBox" && styles.Resolve(e, "IsEditable").IsTrue) { ctx.Pass("R5", e, "its text box is in the tab order"); return; }
        if (control is "TextBox" or "RichTextBox" && styles.Resolve(e, "IsReadOnly").IsTrue) { ctx.NotJudged("R5", e, "read-only text"); return; }

        // 有別的鍵盤入口：Enter／Esc、系統命令、存取鍵
        if (styles.Resolve(e, "IsDefault").IsTrue || styles.Resolve(e, "IsCancel").IsTrue) { ctx.Pass("R5", e, "Enter or Esc presses it"); return; }
        if (styles.Resolve(e, "Command") is { IsValue: true, Value: { } cmd } && cmd.Contains("SystemCommands.", StringComparison.Ordinal)) { ctx.NotJudged("R5", e, "a window caption button (the system menu does the same)"); return; }
        if (Wpf.Buttons.Contains(control) && HasAccessKey(styles, e)) { ctx.Pass("R5", e, "it has an access key"); return; }

        // 清單項目裡的控制項：項目自己拿焦點，鍵盤可能由清單處理
        if (e.Ancestors().Any(a => a.Name.LocalName is "DataTemplate" && a.Parent?.Name.LocalName is { } h && (h.EndsWith(".ItemTemplate", StringComparison.Ordinal) || h.EndsWith(".CellTemplate", StringComparison.Ordinal)) && !h.StartsWith("ItemsControl.", StringComparison.Ordinal)))
        {
            ctx.NotJudged("R5", e, "inside a list item template (the list may handle the keys)");
            return;
        }

        var fromStyle = set.Origin is { } o && o != e && o.Name.LocalName == "Setter";
        ctx.Report(Kinds.NotInTabOrder, e,
            $"<{e.Name.LocalName}> has {property}=\"False\"{(fromStyle ? $" (from the style at {StyleIndex.FileOf(set.Origin!)}:{Context.LineOf(set.Origin!)})" : "")}, so Tab never reaches it");
    }

    private static bool HasAccessKey(StyleIndex styles, XElement e)
    {
        var content = styles.Resolve(e, "Content");
        var text = content is { IsValue: true, Value: { } v } && !Markup.IsMarkup(v) ? v : Markup.OwnText(e);
        var i = text.Replace("__", "", StringComparison.Ordinal).IndexOf('_');
        return i >= 0 && i < text.Length - 1;
    }
}
