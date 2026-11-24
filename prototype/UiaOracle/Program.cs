using System.Windows;

namespace UiaOracle;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args.FirstOrDefault() == "axe-server") { Host.AxeServer(); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var mode = args.FirstOrDefault() ?? "";
        try
        {
            switch (mode)
            {
                case "names": Probe.RunNames(args.Contains("--axe"), args.Skip(1).FirstOrDefault(a => a.EndsWith(".txt", StringComparison.Ordinal)) ?? "names.txt"); break;
                case "templates": Parts.RunTemplates(); break;
                case "themes": Parts.RunThemes(); break;
                case "tabs": Probe.RunTabs(args.Skip(1).FirstOrDefault() ?? "tabs.txt"); break;
                case "items": Items.Run(); break;
                case "silent": Items.Silent(); break;
                case "focus": Diag.ShowFocusVisuals(); Probe.RunFocus(); break;
                case "check": return Check.Run();
                case "check-names" when args.Length > 1: Check.NameFiles = args[1..]; Check.Show = true; return Check.Facts();
                case "check-names": return Check.Facts() + Check.Themes() + Check.Generated();
                case "check-keys": return Check.Tabs() + Check.FocusCases("focus.txt", null) + Check.Sizes();
                case "focus-themes": ThemeFocus.Run(); break;
                case "check-theme-focus": return Check.ThemeFocusCases();
                case "check-corpus": return CorpusCheck.Run(args[1..]);
                case "check-windows": return WindowCheck.Run(args[1..]);
                case "record": Check.Record(args[1]); break;
                case "nvda-window": return NvdaWindow.Run(args[1]);
                case "check-items": return Check.ItemCases() + Check.SilentCases();
                case "check-walk": return Check.Walk() + Check.Labels();
                case "check-hostile": return SafeMarkup.SelfTest();
                case "axe-pid": Diag.AxePid(int.Parse(args[1])); break;
                case "label": Diag.LabelTarget(); break;
                default: Console.Error.WriteLine("modes: check | check-names [facts file] | check-keys | check-corpus <dirs> | check-windows [--axe] <dirs> | check-hostile | axe-pid <pid> | record <json> | names [--axe] | templates | themes | tabs | focus | label"); return 2;
            }
        }
        finally { app.Shutdown(); }
        return 0;
    }
}
