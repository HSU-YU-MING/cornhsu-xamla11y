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
                case "names": Probe.RunNames(args.Contains("--axe")); break;
                case "templates": Parts.RunTemplates(); break;
                case "themes": Parts.RunThemes(); break;
                case "tabs": Probe.RunTabs(); break;
                case "focus": Diag.ShowFocusVisuals(); Probe.RunFocus(); break;
                case "check": return Check.Run();
                case "check-names": return Check.Facts() + Check.Themes() + Check.Generated();
                case "check-keys": return Check.Tabs() + Check.FocusCases("focus.txt", null) + Check.Sizes();
                case "focus-themes": ThemeFocus.Run(); break;
                case "check-theme-focus": return Check.ThemeFocusCases();
                case "check-corpus": return CorpusCheck.Run(args[1..]);
                case "record": Check.Record(args[1]); break;
                case "label": Diag.LabelTarget(); break;
                default: Console.Error.WriteLine("modes: check | check-names | check-keys | names [--axe] | templates | themes | tabs | focus | label"); return 2;
            }
        }
        finally { app.Shutdown(); }
        return 0;
    }
}
