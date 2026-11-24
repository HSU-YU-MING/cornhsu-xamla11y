using System.Windows;

namespace UiaOracle;

/// <summary>範本零件與佈景的事實。</summary>
internal static class Parts
{
    private static string FirstLine(Exception ex) => ex.GetBaseException().Message.Split('\n')[0];

    /// <summary>控制項範本裡的零件在 UIA 樹上是什麼（t、PART_*）。</summary>
    public static void RunTemplates()
    {
        Console.WriteLine("\n== template parts");
        foreach (var c in Probe.Load("templates.txt"))
        {
            try
            {
                using var shown = Host.Show(c.Body, c.Resources);
                var parts = Host.Tree(shown.Handle).Where(x => x.Id == "t" || x.Id.StartsWith("PART_", StringComparison.Ordinal)).ToList();
                var hits = string.Join(",", Host.Axe().Select(h => h.Rule + "@" + (h.Id.Length > 0 ? h.Id : h.ControlType)).Distinct());
                var shownParts = parts.Count == 0
                    ? "(no part in the UIA tree)"
                    : string.Join("; ", parts.Select(n => $"{n.Id}:{n.Type} name={n.NameShown} ctl={(n.Control ? 1 : 0)} kbd={(n.KeyboardFocusable ? 1 : 0)} off={(n.Offscreen ? 1 : 0)}"));
                Console.WriteLine($"  {c.Title,-40} {shownParts}  axe[{hits}]");
            }
            catch (Exception ex) { Console.WriteLine($"  {c.Title,-40} !! {FirstLine(ex)}"); }
        }
    }

    internal static readonly Dictionary<string, string> Themes = new()
    {
        ["mahapps"] = "<ResourceDictionary Source='pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml'/><ResourceDictionary Source='pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml'/><ResourceDictionary Source='pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml'/>",
        ["materialdesign"] = "<materialDesign:BundledTheme BaseTheme='Light' PrimaryColor='DeepPurple' SecondaryColor='Lime'/><ResourceDictionary Source='pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml'/>",
        ["hc"] = "<ResourceDictionary Source='pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml'/><ResourceDictionary Source='pack://application:,,,/HandyControl;component/Themes/Theme.xaml'/>",
        ["wpfui"] = "<ui:ThemesDictionary Theme='Light'/><ui:ControlsDictionary/>",
        ["fluent"] = "<ResourceDictionary Source='pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml'/>",
    };

    internal static void UseTheme(string? theme)
    {
        var app = Application.Current;
        app.Resources.MergedDictionaries.Clear();
        if (theme is null) return;
        // XamlReader 只找已經載入的組件：先碰一下型別
        foreach (var assembly in (string[])["MahApps.Metro", "MaterialDesignThemes.Wpf", "HandyControl", "Wpf.Ui", "Wpf.Ui.Abstractions"])
            try { System.Reflection.Assembly.Load(assembly); } catch (System.IO.IOException) { }
        app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
            $"<ResourceDictionary {Host.Ns}><ResourceDictionary.MergedDictionaries>{Themes[theme].Replace('\'', '"')}</ResourceDictionary.MergedDictionaries></ResourceDictionary>"));
    }

    public static void RunThemes()
    {
        foreach (var group in Probe.Load("themes.txt").GroupBy(n => n.Group))
        {
            Console.WriteLine($"\n== {group.Key}");
            try { UseTheme(group.Key); }
            catch (Exception ex) { Console.WriteLine("  theme !! " + FirstLine(ex)); continue; }

            foreach (var c in group)
            {
                try
                {
                    using var shown = Host.Show(c.Body, c.Resources);
                    var tree = Host.Tree(shown.Handle);
                    var i = tree.FindIndex(x => x.Id == "p");
                    var hits = string.Join(",", Host.Axe().Where(h => h.Rule.StartsWith("Name", StringComparison.Ordinal))
                        .Select(h => h.Rule + (h.Id == "p" ? "" : "@" + (h.Id.Length > 0 ? h.Id : h.ControlType))).Distinct());
                    Console.WriteLine(i < 0
                        ? $"  {c.Title,-36} (not in the UIA tree)  axe[{hits}]"
                        : $"  {c.Title,-36} {tree[i].Type,-10} name={tree[i].NameShown,-12} help={tree[i].HelpText,-6} axe[{hits}]");
                }
                catch (Exception ex) { Console.WriteLine($"  {c.Title,-36} !! {FirstLine(ex)}"); }
            }
        }
        UseTheme(null);
    }
}
