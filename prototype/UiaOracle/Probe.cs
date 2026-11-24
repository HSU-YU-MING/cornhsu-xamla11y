using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace UiaOracle;

/// <summary>M1：規劃書第 3 節每一個 ⚠。只印事實，不判定。案例在 facts/*.txt（「組 | 標題 | XAML | 資源」，引號寫單引號）。</summary>
internal static class Probe
{
    private const string Icon = "<Path Data='M0,0 L10,10 L0,10 Z' Fill='Black' Width='12' Height='12'/>";

    internal sealed record Fact(string Group, string Title, string Body, string Resources);

    internal static List<Fact> Load(string file)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "facts", file);
        var list = new List<Fact>();
        foreach (var raw in System.IO.File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(" | ");
            string Xml(string s) => s.Replace("[ICON]", Icon).Replace('\'', '"');
            list.Add(new Fact(parts[0].Trim(), parts[1].Trim(), Xml(parts[2].Trim()), parts.Length > 3 ? Xml(parts[3].Trim()) : ""));
        }
        return list;
    }

    public static void RunNames(bool axe, string file = "names.txt")
    {
        foreach (var group in Load(file).GroupBy(n => n.Group))
        {
            Console.WriteLine($"\n== {group.Key}");
            foreach (var c in group)
            {
                try
                {
                    using var shown = Host.Show(c.Body, c.Resources);
                    var tree = Host.Tree(shown.Handle);
                    var n = tree.FirstOrDefault(x => x.Id == "p");
                    var hits = axe ? "  axe[" + string.Join(",", Host.Axe().Select(h => $"{h.Rule}{(h.Id == "p" ? "" : "@" + h.ControlType)}").Distinct()) + "]" : "";
                    Console.WriteLine(n is null
                        ? $"  {c.Title,-46} (not in the UIA tree){hits}"
                        : $"  {c.Title,-46} {n.Type,-11} name={n.NameShown,-20} help={(n.HelpText.Length > 0 ? n.HelpText : "∅"),-5} kbd={(n.KeyboardFocusable ? 1 : 0)} ctl={(n.Control ? 1 : 0)} {n.Width:0}x{n.Height:0}{hits}");
                }
                catch (Exception ex) { Console.WriteLine($"  {c.Title,-46} !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
            }
        }
    }

    public static void RunTabs()
    {
        Console.WriteLine("\n== tab stops");
        foreach (var c in Load("tabs.txt"))
        {
            try
            {
                using var shown = Host.Show("<Button x:Name=\"a\"/>" + c.Body + "<Button x:Name=\"z\"/>", c.Resources);
                var uia = Host.Tree(shown.Handle).FirstOrDefault(x => x.Id == "p");
                Console.WriteLine($"  {c.Title,-44} kbd={(uia is null ? "-" : uia.KeyboardFocusable ? "1" : "0")}  {string.Join(" → ", Host.TabStops(shown))}");
            }
            catch (Exception ex) { Console.WriteLine($"  {c.Title,-44} !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
        }
    }

    /// <summary>用鍵盤（Tab）把焦點移到 p，比對前後畫面。</summary>
    public static void RunFocus()
    {
        Console.WriteLine("\n== focus visible (pixels changed when focus arrives by keyboard)");
        foreach (var c in Load("focus.txt"))
        {
            try
            {
                using var shown = Host.Show("<Button x:Name=\"first\" Content=\"first\"/><Border Height=\"10\"/>" + c.Body, c.Resources);
                Console.WriteLine($"  {c.Title,-52} {Measure(shown)}");
            }
            catch (Exception ex) { Console.WriteLine($"  {c.Title,-52} !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
        }
    }

    /// <summary>
    /// 先把焦點放在 first、拍一張，再送 Tab 鍵把焦點移到 p、拍第二張；first 那一塊不比（它的焦點框會消失）。
    /// 焦點要用鍵盤移：FocusVisualStyle 只在最後一個輸入裝置是鍵盤時畫。
    /// </summary>
    /// <summary>最後一次 <see cref="Measure"/>：Tab 有沒有走到 p、改變了幾個像素。</summary>
    internal static bool ReachedTarget { get; private set; }

    /// <summary>拍照之前等多久（佈景的焦點動畫）。</summary>
    internal static int SettleMilliseconds { get; set; }

    private static void Settle()
    {
        if (SettleMilliseconds == 0) return;
        var until = Environment.TickCount64 + SettleMilliseconds;
        while (Environment.TickCount64 < until) { Host.Pump(); Thread.Sleep(15); }
    }
    internal static int ChangedPixels { get; private set; }

    internal static string Measure(Shown shown)
    {
        var window = shown.Window;
        window.Activate();
        Host.Pump();
        var first = shown.Find("first")!;
        Keyboard.Focus(first);
        Host.PressKey(window, Key.LeftShift);   // 讓 InputManager 記得最後的輸入是鍵盤
        Settle();
        var (before, w) = Host.Render(window);
        for (var i = 0; i < 4; i++)
        {
            Host.PressKey(window, Key.Tab);
            if (Reached(Keyboard.FocusedElement)) break;
        }
        var who = Host.NameOf(Keyboard.FocusedElement);
        Settle();
        var (after, _) = Host.Render(window);
        var bottom = first.TransformToAncestor((Visual)VisualTreeHelper.GetChild(window, 0)).TransformBounds(new Rect(first.RenderSize)).Bottom + 5;
        var n = 0;
        for (var i = 0; i < before.Length; i += 4)
            if (i / 4 / w > bottom && (before[i] != after[i] || before[i + 1] != after[i + 1] || before[i + 2] != after[i + 2] || before[i + 3] != after[i + 3])) n++;
        ReachedTarget = Reached(Keyboard.FocusedElement);
        ChangedPixels = n;
        return $"changed={n,-6} focused={who}";
    }

    private static bool Reached(object? o)
    {
        for (var c = o as DependencyObject; c is not null; c = c is Visual ? VisualTreeHelper.GetParent(c) : LogicalTreeHelper.GetParent(c))
            if (c is FrameworkElement { Name: "p" } or FrameworkContentElement { Name: "p" }) return true;
        return false;
    }
}
