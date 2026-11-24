using System.Windows;

namespace UiaOracle;

/// <summary>M3：每個佈景裡，FocusVisualStyle 關掉之後控制項自己的範本看不看得到焦點。輸出可以貼進 data/known-controls.json 的 themeFocus。</summary>
internal static class ThemeFocus
{
    private static readonly (string Control, string Xaml)[] Controls =
    [
        ("Button", "<Button x:Name=\"p\" Content=\"x\" {0}/>"),
        ("ToggleButton", "<ToggleButton x:Name=\"p\" Content=\"x\" {0}/>"),
        ("RepeatButton", "<RepeatButton x:Name=\"p\" Content=\"x\" {0}/>"),
        ("CheckBox", "<CheckBox x:Name=\"p\" Content=\"x\" {0}/>"),
        ("RadioButton", "<RadioButton x:Name=\"p\" Content=\"x\" {0}/>"),
        ("ComboBox", "<ComboBox x:Name=\"p\" {0}><ComboBoxItem Content=\"x\"/></ComboBox>"),
        ("Slider", "<Slider x:Name=\"p\" {0}/>"),
    ];

    /// <summary>工具在佈景原始碼裡報的樣式：開真的看。</summary>
    private static readonly (string Theme, string Title, string Xaml)[] Keyed =
    [
        ("mahapps", "CheckBox.Win10", "<CheckBox x:Name=\"p\" Content=\"x\" Style=\"{DynamicResource MahApps.Styles.CheckBox.Win10}\"/>"),
        ("mahapps", "RadioButton.Win10", "<RadioButton x:Name=\"p\" Content=\"x\" Style=\"{DynamicResource MahApps.Styles.RadioButton.Win10}\"/>"),
        ("materialdesign", "SwitchToggleButton", "<ToggleButton x:Name=\"p\" Style=\"{DynamicResource MaterialDesignSwitchToggleButton}\"/>"),
        ("materialdesign", "Slider (MD3 default)", "<Slider x:Name=\"p\"/>"),
        ("materialdesign", "Button default", "<Button x:Name=\"p\" Content=\"x\"/>"),
        ("hc", "ButtonIconCircular", "<Button x:Name=\"p\" Style=\"{DynamicResource ButtonIconCircular}\"/>"),
        ("wpfui", "ComboBox default", "<ComboBox x:Name=\"p\"><ComboBoxItem Content=\"x\"/></ComboBox>"),
        ("wpfui", "ui:Button default", "<ui:Button x:Name=\"p\" Content=\"x\"/>"),
    ];

    private static (bool Reached, int Changed) Measure(string body)
    {
        using var shown = Host.Show("<Button x:Name=\"first\" Content=\"first\"/><Border Height=\"14\"/>" + body);
        // 佈景的焦點常是動畫：等它跑完再拍
        Probe.SettleMilliseconds = 600;
        try { Probe.Measure(shown); }
        finally { Probe.SettleMilliseconds = 0; }
        return (Probe.ReachedTarget, Probe.ChangedPixels);
    }

    public static void Run()
    {
        Diag.ShowFocusVisuals();
        foreach (var theme in (string[])["mahapps", "materialdesign", "hc", "wpfui", "fluent"])
        {
            try { Parts.UseTheme(theme); }
            catch (Exception ex) { Console.WriteLine($"{theme}: {ex.GetBaseException().Message}"); continue; }
            Console.WriteLine($"== {theme}");
            foreach (var (control, xaml) in Controls)
            {
                try
                {
                    var on = Measure(string.Format(xaml, ""));
                    var off = Measure(string.Format(xaml, "FocusVisualStyle=\"{x:Null}\""));
                    Console.WriteLine($"  {control,-14} default changed={on.Changed,-6} FVS null changed={off.Changed,-6} reached={on.Reached && off.Reached}");
                }
                catch (Exception ex) { Console.WriteLine($"  {control,-14} !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
            }
            foreach (var k in Keyed.Where(k => k.Theme == theme))
            {
                try
                {
                    var r = Measure(k.Xaml);
                    Console.WriteLine($"  [{k.Title}] changed={r.Changed} reached={r.Reached}");
                }
                catch (Exception ex) { Console.WriteLine($"  [{k.Title}] !! {ex.GetBaseException().Message.Split('\n')[0]}"); }
            }
        }
        Parts.UseTheme(null);
    }
}
