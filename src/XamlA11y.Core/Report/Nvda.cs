using System.Text.RegularExpressions;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>
/// NVDA 會怎麼念：「button, Delete」。只寫真的用 NVDA 2026.2（英文與繁體中文）跑過的控制項與情況
/// （tests/XamlA11y.Tests/nvda-facts.json），不確定就不寫：自己的類別、停用或不能聚焦的、有快速鍵的、綁定的提示、
/// 提示裡有符號的（怎麼念看 NVDA 的符號字典）。狀態（沒勾選、折疊、空白…）不寫，執行時才知道。
/// </summary>
public static partial class Nvda
{
    /// <summary>WPF 控制項 → NVDA 念的型別（英文是 NVDA 的原文，中文是它的 zh_TW 翻譯）。</summary>
    private static readonly Dictionary<string, (string En, string Zh)> Roles = new(StringComparer.Ordinal)
    {
        ["Button"] = ("button", "按鈕"),
        ["RepeatButton"] = ("button", "按鈕"),
        ["ToggleButton"] = ("toggle button", "切換按鈕"),
        ["CheckBox"] = ("check box", "核取方塊"),
        ["RadioButton"] = ("radio button", "選擇鈕"),
        ["TextBox"] = ("edit", "編輯區"),
        ["PasswordBox"] = ("edit", "編輯區"),
        ["DatePicker"] = ("edit", "編輯區"),
        ["RichTextBox"] = ("document", "文件"),
        ["ComboBox"] = ("combo box", "下拉式方塊"),
        ["Slider"] = ("slider", "滑桿"),
        ["ListBox"] = ("list", "清單"),
        ["ListView"] = ("list", "清單"),
        ["TreeView"] = ("tree view", "樹狀檢視"),
        ["DataGrid"] = ("data grid", "資料格"),
        ["TabItem"] = ("tab", "索引標籤"),
        ["MenuItem"] = ("menu item", "功能表項目"),
    };

    /// <summary>描述念在型別後面，只在這幾種上跑過。</summary>
    private static readonly HashSet<string> Described = new(StringComparer.Ordinal)
        { "Button", "RepeatButton", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "RichTextBox", "ComboBox", "Slider", "ListBox", "TreeView" };

    public static IEnumerable<string> Controls => Roles.Keys;
    public static IEnumerable<string> WithDescription => Described;

    /// <summary>不確定的回 null。</summary>
    public static string? Says(Finding f, Lang lang)
    {
        if (f.Control is not { } control || !Roles.TryGetValue(control, out var role) || f.Description is not { } description) return null;
        var type = lang.T(role.En, role.Zh);
        if (description.Length > 0)
        {
            if (f.SpokenName is not null || !Described.Contains(control) || !PlainRegex().IsMatch(description)) return null;
            return $"{type}, {description}";
        }
        return f.SpokenName is { } item ? $"System dot Windows dot Controls dot {CamelRegex().Replace(item, " ")}…, {type}" : type;
    }

    /// <summary>有名稱的：「Save, button」；沒有的只念型別。名稱裡有符號、有快速鍵的不猜。</summary>
    public static string? Reads(string control, NameResult name, string? description, Lang lang)
    {
        // 清單、頁籤一站會連項目與位置一起念，不是這個樣子
        if (!Roles.TryGetValue(control, out var role) || control is "TabItem" or "ListBox" or "ListView" or "DataGrid" or "MenuItem" || description is null) return null;
        var type = lang.T(role.En, role.Zh);
        if (description.Length > 0)
        {
            if (!Described.Contains(control) || !PlainRegex().IsMatch(description) || description == name.Text) return null;
            type += ", " + description;
        }
        return name.Kind switch
        {
            NameKind.Missing or NameKind.TextNotUsed => type,
            NameKind.Named when name.Text is { } t && PlainRegex().IsMatch(t) => $"{t}, {type}",
            _ => null,
        };
    }

    /// <summary>照原樣念的文字：字母、數字、單一空白、NVDA 預設不念的標點。</summary>
    [GeneratedRegex(@"^(?=.{1,120}$)[\p{L}\p{M}\p{Nd}]+(?:[,.?!:]? [\p{L}\p{M}\p{Nd}]+)*[.?!]?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlainRegex();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelRegex();
}
