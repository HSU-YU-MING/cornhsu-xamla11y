using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace UiaOracle;

internal static class Diag
{
    /// <summary>真的跑起來的 App：Axe-Windows 掃它的視窗，一行一筆。</summary>
    public static void AxePid(int pid)
    {
        var config = global::Axe.Windows.Automation.Config.Builder.ForProcessId(pid).WithOutputFileFormat(global::Axe.Windows.Automation.OutputFileFormat.None).Build();
        foreach (var w in global::Axe.Windows.Automation.ScannerFactory.CreateScanner(config).Scan(null).WindowScanOutputs)
            foreach (var e in w.Errors)
            {
                string P(string k) => e.Element.Properties is { } p && p.TryGetValue(k, out var v) ? v ?? "" : "";
                string Up() => e.Element.Parent?.Properties is { } p ? $"{(p.TryGetValue("ControlType", out var t) ? t : "")}#{(p.TryGetValue("AutomationId", out var i) ? i : "")}" : "";
                Console.WriteLine($"{e.Rule.ID} | {P("ControlType")} | id={P("AutomationId")} | name={P("Name")} | class={P("ClassName")} | help={P("HelpText")} | parent={Up()} | {P("BoundingRectangle")}");
            }
    }

    /// <summary>Label.Target 有沒有真的設上、peer 怎麼回答（行程內）、UIA client 怎麼回答。</summary>
    public static void LabelTarget()
    {
        using var shown = Host.Show("<Label x:Name=\"lab\" Content=\"_Name:\" Target=\"{Binding ElementName=p}\"/><TextBox x:Name=\"p\"/><TextBox x:Name=\"q\" AutomationProperties.LabeledBy=\"{Binding ElementName=lab}\"/>");
        var lab = (Label)shown.Find("lab")!;
        var p = (TextBox)shown.Find("p")!;
        Console.WriteLine($"Label.Target          = {Host.NameOf(lab.Target)}");
        var peer = UIElementAutomationPeer.CreatePeerForElement(p);
        Console.WriteLine($"peer                  = {peer.GetType().FullName}  name='{peer.GetName()}'  labeledBy={peer.GetLabeledBy()?.GetType().Name ?? "null"}");
        Console.WriteLine($"AutomationProperties.GetLabeledBy(p) = {Host.NameOf(System.Windows.Automation.AutomationProperties.GetLabeledBy(p))}");
        var prop = typeof(Label).GetField("LabeledByProperty", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as DependencyProperty;
        Console.WriteLine($"Label.LabeledByProperty (internal)   = {(prop is null ? "no such field" : Host.NameOf(p.GetValue(prop)))}");
        foreach (var n in Host.Tree(shown.Handle)) Console.WriteLine($"  {n.Type,-10} id={n.Id,-4} name={n.NameShown}");
        Console.WriteLine($"runtime {Environment.Version}  wpf {typeof(Label).Assembly.GetName().Version}  {typeof(Label).Assembly.Location}");
        foreach (var f in typeof(Label).GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)) Console.WriteLine($"  field {f.Name}");
    }

    public static void ShowFocusVisuals()
    {
        var prop = typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                   ?? throw new InvalidOperationException("KeyboardNavigation.AlwaysShowFocusVisual not found");
        prop.SetValue(null, true);
    }
}
