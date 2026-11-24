using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UiaOracle;

/// <summary>UIA 樹上的一個節點（從另一條執行緒用 UIA client 讀，跟螢幕閱讀器看到的一樣）。</summary>
internal sealed record Node(
    string Id, string Type, string Name, string HelpText, bool KeyboardFocusable, bool Control, bool Content,
    bool Offscreen, double Width, double Height)
{
    public string NameShown => Name.Length == 0 ? "∅" : "\"" + string.Concat(Name.Select(c =>
        c is >= (char)0xE000 and <= (char)0xF8FF || char.IsControl(c) ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";
}

internal sealed record AxeHit(string Rule, string Id, string ControlType);

internal sealed class Shown : IDisposable
{
    public required Window Window { get; init; }
    public required IntPtr Handle { get; init; }

    public FrameworkElement? Find(string name) => Walk(Window, name);

    public static FrameworkElement? Walk(DependencyObject o, string name)
    {
        if (o is FrameworkElement fe && fe.Name == name) return fe;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            if (Walk(VisualTreeHelper.GetChild(o, i), name) is { } found) return found;
        return null;
    }

    public void Dispose() => Window.Close();
}

internal static class Host
{
    public const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
        "xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\" " +
        "xmlns:mah=\"http://metro.mahapps.com/winfx/xaml/controls\" " +
        "xmlns:materialDesign=\"http://materialdesigninxaml.net/winfx/xaml/themes\" " +
        "xmlns:hc=\"https://handyorg.github.io/handycontrol\" " +
        "xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\" " +
        "";

    /// <summary>等 Dispatcher 把排隊的工作跑完（版面、DataGrid 欄寬、繫結）。</summary>
    public static void Pump(DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(priority, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>在另一條執行緒做事（UIA client 不能在 UI 執行緒上問自己），期間 Dispatcher 照常跑。</summary>
    public static T Off<T>(Func<T> work, int timeoutMs = 60_000)
    {
        T result = default!;
        Exception? error = null;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var t = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
            finally { dispatcher.BeginInvoke(() => frame.Continue = false); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(timeoutMs), DispatcherPriority.Send,
            (_, _) => { error ??= new TimeoutException(); frame.Continue = false; }, dispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return error is null ? result : throw new InvalidOperationException("off-thread work failed: " + error.Message, error);
    }

    public static Shown Show(string body, string resources = "", double width = 420, double height = 300)
    {
        var xaml = $"<Window {Ns} Title=\"UiaOracle\"><Window.Resources><ResourceDictionary>{resources}</ResourceDictionary></Window.Resources>" +
                   $"<StackPanel x:Name=\"host\">{body}</StackPanel></Window>";
        return Show((Window)XamlReader.Parse(xaml), width, height);
    }

    public static Shown Show(Window window, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 40; window.Top = 40;
        window.Width = width; window.Height = height;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Pump();
        return new Shown { Window = window, Handle = new WindowInteropHelper(window).Handle };
    }

    /// <summary>整棵 raw view 樹（含 control view 看不到的），文件順序。</summary>
    public static List<Node> Tree(IntPtr handle) => Off(() =>
    {
        var list = new List<Node>();
        void Visit(AutomationElement e)
        {
            AutomationElement.AutomationElementInformation c;
            try { c = e.Current; } catch (ElementNotAvailableException) { return; }
            var r = c.BoundingRectangle;
            list.Add(new Node(c.AutomationId, c.ControlType.ProgrammaticName.Replace("ControlType.", ""), c.Name ?? "", c.HelpText ?? "",
                c.IsKeyboardFocusable, c.IsControlElement, c.IsContentElement, c.IsOffscreen,
                r.IsEmpty ? 0 : r.Width, r.IsEmpty ? 0 : r.Height));
            for (var k = TreeWalker.RawViewWalker.GetFirstChild(e); k is not null; k = TreeWalker.RawViewWalker.GetNextSibling(k)) Visit(k);
        }
        Visit(AutomationElement.FromHandle(handle));
        return list;
    });

    private static System.Diagnostics.Process? _axe;

    /// <summary>
    /// Axe-Windows（Accessibility Insights 與 VS 檢查器的引擎）掃這個行程目前開著的視窗。
    /// Axe 刻意不掃自己的行程，所以開一個子行程（同一支執行檔的 axe-server 模式），這邊照常跑 Dispatcher。
    /// </summary>
    public static List<AxeHit> Axe() => Off(() =>
    {
        _axe ??= System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "axe-server")
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = System.Text.Encoding.UTF8 })!;
        _axe.StandardInput.WriteLine(Environment.ProcessId);
        _axe.StandardInput.Flush();
        var line = _axe.StandardOutput.ReadLine() ?? throw new InvalidOperationException("axe-server died");
        return System.Text.Json.JsonSerializer.Deserialize<List<AxeHit>>(line)!;
    }, 120_000);

    public static void AxeServer()
    {
        for (string? line; (line = Console.ReadLine()) is not null;)
        {
            List<AxeHit> hits;
            try
            {
                var config = global::Axe.Windows.Automation.Config.Builder.ForProcessId(int.Parse(line))
                    .WithOutputFileFormat(global::Axe.Windows.Automation.OutputFileFormat.None).Build();
                var output = global::Axe.Windows.Automation.ScannerFactory.CreateScanner(config).Scan(null);
                hits = output.WindowScanOutputs.SelectMany(w => w.Errors).Select(e => new AxeHit(
                    e.Rule.ID.ToString(),
                    e.Element.Properties is { } p && p.TryGetValue("AutomationId", out var id) ? id : "",
                    e.Element.Properties is { } q && q.TryGetValue("ControlType", out var ct) ? ct : "")).ToList();
            }
            catch (Exception ex) { hits = [new AxeHit("SCAN-FAILED: " + ex.Message, "", "")]; }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(hits));
            Console.Out.Flush();
        }
    }

    /// <summary>從頭一直按 Tab（MoveFocus），停過的元素名稱；沒有名稱的用型別名。</summary>
    public static List<string> TabStops(Shown shown, int max = 60)
    {
        shown.Window.Activate();
        Pump();
        var stops = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        shown.Window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Pump();
        for (var i = 0; i < max; i++)
        {
            if (Keyboard.FocusedElement is not DependencyObject cur || !seen.Add(cur)) break;
            stops.Add(NameOf(cur));
            var moved = cur switch
            {
                UIElement u => u.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                ContentElement ce => ce.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                _ => false,
            };
            Pump();
            if (!moved) break;
        }
        return stops;
    }

    /// <summary>這個元素底下真的畫出來的文字（視覺樹裡看得到的 TextBlock 與 AccessText）。</summary>
    public static List<string> ShownTexts(Shown shown, string name)
    {
        var texts = new List<string>();
        void Visit(DependencyObject d, bool inside)
        {
            if (d is UIElement { IsVisible: false }) return;
            inside |= d is FrameworkElement { Name: { } n } && n == name;
            if (inside && d is System.Windows.Controls.TextBlock { Text.Length: > 0 } tb && tb.FontFamily.Source.Contains("Segoe MDL2", StringComparison.Ordinal) == false) texts.Add(tb.Text);
            if (inside && d is System.Windows.Controls.AccessText { Text.Length: > 0 } at) { texts.Add(at.Text.Replace("_", "", StringComparison.Ordinal)); return; }
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) Visit(System.Windows.Media.VisualTreeHelper.GetChild(d, i), inside);
        }
        Pump();
        Visit(shown.Window, false);
        return [.. texts.Distinct()];
    }

    public static string NameOf(object? o) => o switch
    {
        FrameworkElement fe => fe.Name.Length > 0 ? fe.Name : fe.GetType().Name,
        FrameworkContentElement fce => fce.Name.Length > 0 ? fce.Name : fce.GetType().Name,
        null => "none",
        _ => o.GetType().Name,
    };

    /// <summary>視窗內容目前的像素（96 DPI）。焦點框畫在 AdornerLayer 裡，所以畫的是視窗的第一個視覺子元素。</summary>
    public static (byte[] Pixels, int Width) Render(Window window)
    {
        Pump(DispatcherPriority.ContextIdle);
        var root = (Visual)VisualTreeHelper.GetChild(window, 0);
        var size = ((FrameworkElement)root).RenderSize;
        var bmp = new RenderTargetBitmap(Math.Max(1, (int)size.Width), Math.Max(1, (int)size.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
        return (px, bmp.PixelWidth);
    }

    public static void PressKey(Window window, Key key)
    {
        var source = PresentationSource.FromVisual(window)!;
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyUpEvent });
        Pump();
    }
}
