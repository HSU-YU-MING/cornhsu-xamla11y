using System.Text.Json;
using System.Xml.Linq;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Analysis;

/// <summary>在真的 WPF 裡量過的控制項函式庫（data/known-controls.json）。</summary>
public sealed record KnownLibrary(
    string Id, string Package, string Version,
    IReadOnlyList<string> Namespaces, IReadOnlyList<string> Assemblies,
    IReadOnlyDictionary<string, string> Controls, string? IconPrefix, IReadOnlyList<string> IconExtensions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NameFrom)
{
    public string MajorMinor => MajorMinorOf(Version);

    public static string MajorMinorOf(string version)
    {
        var parts = version.Trim().Split('.', '-', '+');
        return parts.Length >= 2 ? parts[0] + "." + parts[1] : version.Trim();
    }

    public bool Owns(string xmlNamespace)
    {
        if (Namespaces.Contains(xmlNamespace, StringComparer.Ordinal)) return true;
        if (!xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return false;
        var marker = xmlNamespace.IndexOf(";assembly=", StringComparison.Ordinal);
        return marker >= 0 && Assemblies.Contains(xmlNamespace[(marker + ";assembly=".Length)..].Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

public static class KnownControls
{
    private static (IReadOnlyList<KnownLibrary> Libraries, IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> ThemeFocus, IReadOnlyDictionary<string, string> Requires)? _data;

    /// <summary>佈景 id → 量的時候合併的字典（沒合併它的專案，量的數字不適用）。</summary>
    public static IReadOnlyDictionary<string, string> ThemeRequires => Data.Requires;

    public static IReadOnlyList<KnownLibrary> Libraries => Data.Libraries;

    /// <summary>佈景 id → 控制項 → FocusVisualStyle 關掉之後，佈景自己的範本還看不看得到焦點（量的）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> ThemeFocus => Data.ThemeFocus;

    private static (IReadOnlyList<KnownLibrary> Libraries, IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> ThemeFocus, IReadOnlyDictionary<string, string> Requires) Data => _data ??= Load();

    private static (IReadOnlyList<KnownLibrary>, IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>, IReadOnlyDictionary<string, string>) Load()
    {
        using var s = typeof(KnownControls).Assembly.GetManifestResourceStream("XamlA11y.Core.known-controls.json")
                      ?? throw new InvalidOperationException("embedded known-controls.json is missing");
        using var doc = JsonDocument.Parse(s);
        var libraries = new List<KnownLibrary>();
        foreach (var l in doc.RootElement.GetProperty("libraries").EnumerateArray())
        {
            string Str(string n) => l.TryGetProperty(n, out var v) ? v.GetString() ?? "" : "";
            List<string> Strings(string n) => l.TryGetProperty(n, out var v) ? [.. v.EnumerateArray().Select(x => x.GetString()!)] : [];
            var controls = l.GetProperty("controls").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetProperty("as").GetString()!, StringComparer.Ordinal);
            var nameFrom = l.TryGetProperty("nameFrom", out var nf)
                ? nf.EnumerateObject().ToDictionary(p => p.Name, p => (IReadOnlyList<string>)[.. p.Value.EnumerateArray().Select(x => x.GetString()!)], StringComparer.Ordinal)
                : [];
            libraries.Add(new KnownLibrary(Str("id"), Str("package"), Str("version"), Strings("namespaces"), Strings("assemblies"),
                controls, Str("iconPrefix") is { Length: > 0 } ip ? ip : null, Strings("iconExtensions"), nameFrom));
        }
        var focus = doc.RootElement.GetProperty("themeFocus").EnumerateObject().ToDictionary(
            t => t.Name,
            t => (IReadOnlyDictionary<string, bool>)t.Value.EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetBoolean(), StringComparer.Ordinal),
            StringComparer.Ordinal);
        var requires = doc.RootElement.GetProperty("themeRequires").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        return (libraries, focus, requires);
    }

    public static KnownLibrary? LibraryOf(XElement e) => LibraryOf(e.Name.NamespaceName);
    public static KnownLibrary? LibraryOf(string ns) => Markup.IsMarkup(ns) ? null : Libraries.FirstOrDefault(l => l.Owns(ns));
}

/// <summary>控制項怎麼得到 UIA 名稱（docs/M1-UIA實測.md 第 2 節）。</summary>
public enum NameSource
{
    /// <summary>看 Content 的純文字（Button、CheckBox、Label、ListBoxItem…）。</summary>
    Content,
    /// <summary>看 Header 的純文字（MenuItem、TabItem、Expander、TreeViewItem）。</summary>
    Header,
    /// <summary>行內文字（Hyperlink）。</summary>
    Inlines,
    /// <summary>沒有純文字這一步（TextBox、ComboBox、Slider…）。</summary>
    NoText,
    /// <summary>TextBlock：自己的文字。</summary>
    Text,
    /// <summary>純文字是空的 FrameworkElement（面板、Border、圖形、Image、AccessText）。</summary>
    Nothing,
    Unknown,
}

public static class Wpf
{
    /// <summary>R1 判定的元件。</summary>
    public static readonly HashSet<string> Interactive = new(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "MenuItem", "Hyperlink",
        "TabItem", "Expander", "TreeViewItem", "ListBoxItem", "ListViewItem", "ComboBoxItem", HeaderedToggle,
    };

    /// <summary>名稱看 Header 的開關（MahApps 的 ToggleSwitch；實測）。只在 known-controls.json 裡用。</summary>
    public const string HeaderedToggle = "(headered toggle)";

    /// <summary>項目容器：沒有文字時 UIA 的名稱是 ToString()（型別名）。</summary>
    public static readonly HashSet<string> Items = new(StringComparer.Ordinal)
    { "TabItem", "TreeViewItem", "ListBoxItem", "ListViewItem", "ComboBoxItem" };

    /// <summary>R2 判定的輸入元件。</summary>
    public static readonly HashSet<string> Inputs = new(StringComparer.Ordinal)
    { "TextBox", "PasswordBox", "RichTextBox", "ComboBox", "Slider", "DatePicker" };

    public static readonly HashSet<string> Buttons = new(StringComparer.Ordinal)
    { "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton" };

    private static readonly HashSet<string> Contents = new(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "Label", "ContentControl", "UserControl",
        "ListBoxItem", "ListViewItem", "ComboBoxItem", "StatusBarItem", "ToolTip", "Frame", "ScrollViewer", "ButtonBase",
        "GridViewColumnHeader", "DataGridColumnHeader", "DataGridRowHeader", "DataGridCell",
    };

    private static readonly HashSet<string> Headers = new(StringComparer.Ordinal)
    { "MenuItem", "TabItem", "Expander", "TreeViewItem", "GroupBox", "HeaderedContentControl", "HeaderedItemsControl", "ToolBar", HeaderedToggle };

    private static readonly HashSet<string> NoTexts = new(StringComparer.Ordinal)
    {
        "TextBox", "PasswordBox", "RichTextBox", "ComboBox", "Slider", "DatePicker", "ListBox", "ListView", "TreeView", "DataGrid",
        "Calendar", "ProgressBar", "ItemsControl", "TabControl", "Menu", "ContextMenu", "ScrollBar", "Thumb", "GridSplitter",
        "Separator", "StatusBar", "Control", "DatePickerTextBox", "InkCanvas", "MediaElement", "WebBrowser", "FlowDocumentScrollViewer",
    };

    /// <summary>沒有純文字的 FrameworkElement。</summary>
    private static readonly HashSet<string> Nothings = new(StringComparer.Ordinal)
    {
        "Grid", "StackPanel", "DockPanel", "WrapPanel", "Canvas", "UniformGrid", "VirtualizingStackPanel", "TabPanel", "ToolBarPanel",
        "Border", "Viewbox", "Decorator", "BulletDecorator", "AdornerDecorator", "Image", "Path", "Rectangle", "Ellipse", "Line", "Polygon", "Polyline",
        "AccessText", "ContentPresenter", "ItemsPresenter", "Popup", "Glyphs", "Viewport3D", "InkPresenter", "TickBar", "Track", "ScrollContentPresenter",
    };

    public static NameSource NameSourceOf(string control) =>
        control == "TextBlock" ? NameSource.Text
        : control == "Hyperlink" ? NameSource.Inlines
        : Contents.Contains(control) ? NameSource.Content
        : Headers.Contains(control) ? NameSource.Header
        : NoTexts.Contains(control) ? NameSource.NoText
        : Nothings.Contains(control) ? NameSource.Nothing
        : NameSource.Unknown;

    /// <summary>有 AutomationPeer 的（UIA 樹上看得到）。沒有的：面板、Border、圖形、ContentControl、AccessText…（docs/M1-UIA實測.md）。</summary>
    public static bool HasPeer(string control) =>
        control is "Image" or "MediaElement" or "Viewport3D"
        || (!Nothings.Contains(control) && control is not ("ContentControl" or "Control" or "Run" or "Span" or "StatusBarItem" or "HeaderedContentControl"));

    /// <summary>WPF 的型別名（樣式的 TargetType 只有名字）：名稱來源認得的，加上幾個不是名稱候選的控制項與基底。</summary>
    public static bool IsKnownType(string name) =>
        NameSourceOf(name) != NameSource.Unknown || name is "DataGridRow" or "Window" or "Page" or "FrameworkElement" or "ItemsPresenter" or "TextElement" or "Inline" or "TabControl";

    /// <summary>內建、不是控制項、會被拿來當按鈕的元素（R4）。</summary>
    public static readonly HashSet<string> NonControls = new(StringComparer.Ordinal)
    {
        "Border", "Grid", "StackPanel", "DockPanel", "WrapPanel", "Canvas", "UniformGrid", "Viewbox", "Decorator",
        "Image", "Path", "Rectangle", "Ellipse", "Line", "Polygon", "Polyline", "TextBlock", "Label", "ContentControl", "ContentPresenter",
        "Run", "Span", "Bold", "Italic", "Underline", "UserControl", "GroupBox", "ItemsControl", "StatusBarItem", "HeaderedContentControl",
        "MediaElement", "AccessText",
    };

    /// <summary>文字本身就是圖示的字型：內容是字母也念不出意思。</summary>
    public static bool IsIconFont(string family)
    {
        // 「Segoe UI, Segoe MDL2 Assets」是文字字型加圖示後備：字母用第一個家族畫
        // pack://application:,,,/Fonts/#FontAwesome：那三個逗號不是分隔
        var f = family.Replace("pack://application:,,,", "", StringComparison.OrdinalIgnoreCase).Split(',').FirstOrDefault(x => x.Trim().Length > 0)?.ToLowerInvariant() ?? "";
        return f.Contains("mdl2", StringComparison.Ordinal) || f.Contains("fluent icons", StringComparison.Ordinal) || f.Contains("marlett", StringComparison.Ordinal)
               || f.Contains("webdings", StringComparison.Ordinal) || f.Contains("wingdings", StringComparison.Ordinal) || f.Contains("awesome", StringComparison.Ordinal)
               || f.Contains("material design icons", StringComparison.Ordinal) || f.Contains("material icons", StringComparison.Ordinal) || f.Contains("material symbols", StringComparison.Ordinal)
               || f.Contains("iconfont", StringComparison.Ordinal) || f.Contains("icofont", StringComparison.Ordinal) || f.Contains("icomoon", StringComparison.Ordinal) || f.Contains("ionicons", StringComparison.Ordinal);
    }
}
