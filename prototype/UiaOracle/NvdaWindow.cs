using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Rules;
using XamlMarkup = XamlA11y.Core.Xaml.Markup;

namespace UiaOracle;

/// <summary>
/// 給真的 NVDA 走的視窗：每一案一塊，Tab 走過去。焦點到哪一案、視窗還是不是前景，一行一行印到 stdout，
/// 由 nvda.mjs 對照 NVDA 念了什麼。視窗一失去前景就印 lost，驅動程式要立刻停手（按鍵是真的送到前景視窗）。
/// </summary>
internal static class NvdaWindow
{
    public static int Run(string file)
    {
        var cases = Probe.Load(file);
        var panel = new StackPanel { Margin = new Thickness(8) };
        var first = new Button { Content = "Start" };
        panel.Children.Add(first);
        var hosts = new List<FrameworkElement>();
        var markers = new List<Border>();
        for (var i = 0; i < cases.Count; i++)
        {
            var host = (FrameworkElement)XamlReader.Parse($"<StackPanel {Host.Ns} Margin=\"0,4\">{cases[i].Body}</StackPanel>");
            hosts.Add(host);
            var marker = new Border { Focusable = true, Height = 1 };
            KeyboardNavigation.SetIsTabStop(marker, false);
            markers.Add(marker);
            panel.Children.Add(marker);
            panel.Children.Add(host);
            // 工具對同一段的判定
            var audit = Auditor.Run(Project.FromText($"<Window {Host.Ns}><StackPanel>{cases[i].Body}</StackPanel></Window>"));
            var verdicts = audit.Judgements.Where(j => j.Rule is "R1" or "R2" or "R7" && XamlMarkup.NameOf(j.Element) == "p").Select(j => j.Kind?.Slug ?? j.Verdict.ToString());
            Console.WriteLine($"case\t{i}\t{cases[i].Title}\t{string.Join(",", verdicts)}\t{cases[i].Body}");
        }
        panel.Children.Add(new Button { Content = "End", Tag = "end" });
        var window = new Window { Title = "XamlA11y NVDA", Width = 700, Height = 700, Left = 60, Top = 40, Topmost = true, Content = new ScrollViewer { Content = panel, Focusable = false } };

        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(Focus), true);
        EventManager.RegisterClassHandler(typeof(ContentElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(Focus), true);
        window.Deactivated += (_, _) => Say("lost");
        window.Activated += (_, _) => Say("active");
        window.Closed += (_, _) => window.Dispatcher.InvokeShutdown();
        // 指令：start = 搶前景並把焦點放到第一顆按鈕；quit
        new Thread(() =>
        {
            while (Console.ReadLine() is { } line)
                window.Dispatcher.Invoke(() =>
                {
                    if (line == "start") { window.Activate(); first.Focus(); Say(window.IsActive ? "active" : "lost"); }
                    // 焦點放在前一案後面的標記上，下一個 Tab 就走進第 i 案
                    if (line.StartsWith("goto ", StringComparison.Ordinal)) markers[int.Parse(line[5..])].Focus();
                    if (line == "quit") window.Close();
                });
            window.Dispatcher.Invoke(window.Close);
        }) { IsBackground = true }.Start();
        window.Show();
        Say("ready");
        System.Windows.Threading.Dispatcher.Run();
        return 0;

        void Focus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.OriginalSource)) return;
            var at = -1;
            for (DependencyObject? d = e.OriginalSource as DependencyObject; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is FrameworkElement fe && hosts.IndexOf(fe) is >= 0 and var i) { at = i; break; }
            var end = e.OriginalSource is FrameworkElement { Tag: "end" };
            Say($"focus\t{(end ? "end" : at)}\t{e.OriginalSource.GetType().Name}");
        }

        static void Say(string s) { Console.WriteLine(s); Console.Out.Flush(); }
    }
}
