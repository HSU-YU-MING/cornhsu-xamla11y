using System.Text.Json;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Tests;

/// <summary>看哪裡：資料夾、專案檔、單一檔案、--only。樣式與資源照樣從整個專案讀。</summary>
public sealed class TargetTests
{
    private static Folder Two() => new Folder().Add("App.csproj", "<Project/>")
        .Add("Styles.xaml", $"<ResourceDictionary {Kit.Ns}><Style x:Key=\"Named\" TargetType=\"Button\"><Setter Property=\"AutomationProperties.Name\" Value=\"n\"/></Style></ResourceDictionary>")
        .Add("App.xaml", $"<Application {Kit.Ns}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"Styles.xaml\"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>")
        .Add("Views/A.xaml", Kit.Window($"<Button Style=\"{{StaticResource Named}}\">{Kit.Icon}</Button><TextBox/>"))
        .Add("Views/B.xaml", Kit.Window("<TextBox/><TextBox/><local:FancyButton/>"));

    private static (int Exit, JsonElement Json) Run(Folder f, params string[] args)
    {
        var exit = f.Run([.. args, "--json", f.At("r.json"), "--min-coverage", "0"]).Exit;
        return (exit, JsonDocument.Parse(File.ReadAllText(f.At("r.json"))).RootElement);
    }

    [Fact]
    public void One_file_reports_only_that_file_and_reads_the_rest()
    {
        using var f = Two();
        var (exit, json) = Run(f, "audit", f.At("Views/A.xaml"));
        Assert.Equal(1, exit);
        Assert.Equal("Views/A.xaml", Assert.Single(json.GetProperty("findings").EnumerateArray()).GetProperty("file").GetString());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("files").GetInt32());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("blocked").GetInt32());
        Assert.Empty(json.GetProperty("summary").GetProperty("unknownControls").EnumerateArray());
    }

    [Theory]
    [InlineData("App.csproj")]
    [InlineData("x.sln")]
    [InlineData("x.slnx")]
    [InlineData("x.vbproj")]
    public void A_project_file_is_its_folder(string file)
    {
        using var f = Two().Add("x.sln", "").Add("x.slnx", "").Add("x.vbproj", "");
        Assert.Equal(3, Run(f, "audit", f.At(file)).Json.GetProperty("findings").GetArrayLength());
    }

    [Theory]
    [InlineData("Views/A.xaml", 1)]
    [InlineData("Views/B.xaml", 2)]
    [InlineData("Views", 3)]
    [InlineData("views/a.XAML", 1)]
    [InlineData("Styles.xaml", 0)]
    [InlineData("", 3)]
    public void Only(string path, int findings)
    {
        using var f = Two();
        Assert.Equal(findings, Run(f, "audit", f.Root, "--only", f.At(path)).Json.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public void Only_can_be_given_several_times()
    {
        using var f = Two();
        var json = Run(f, "audit", f.Root, "--only", f.At("Views/A.xaml"), "--only", f.At("Views/B.xaml")).Json;
        Assert.Equal(3, json.GetProperty("findings").GetArrayLength());
        Assert.Equal("FancyButton", json.GetProperty("summary").GetProperty("unknownControls")[0].GetProperty("element").GetString());
    }

    [Fact]
    public void What_is_not_there_is_an_error()
    {
        using var f = Two().Add("a.txt", "x");
        Assert.Contains("no XAML file", f.Run("audit", f.Root, "--only", f.At("Views/C.xaml")).Err, StringComparison.Ordinal);
        Assert.Contains("no XAML file", f.Run("audit", f.Root, "--only", Path.GetTempPath()).Err, StringComparison.Ordinal);
        Assert.Contains("not a folder, project or XAML file", f.Run("audit", f.At("a.txt")).Err, StringComparison.Ordinal);
        Assert.Contains("not a folder, project or XAML file", f.Run("audit", f.At("nothere")).Err, StringComparison.Ordinal);
        Assert.Equal(2, f.Run("audit", f.Root, "--only").Exit);
    }

    /// <summary>只看一部分的時候不能寫、不能整理整份 baseline；別的檔案的也不算「沒有了」。</summary>
    [Fact]
    public void A_part_does_not_touch_the_baseline()
    {
        using var f = Two();
        Assert.Contains("--only looks at a part", f.Run("audit", f.At("Views/A.xaml"), "--write-baseline", f.At("b.json")).Err, StringComparison.Ordinal);
        f.Run("audit", f.Root, "--write-baseline", f.At("b.json"), "--min-coverage", "0");
        var r = f.Run("audit", f.Root, "--only", f.At("Views/A.xaml"), "--baseline", f.At("b.json"));
        Assert.Equal(0, r.Exit);
        Assert.DoesNotContain("stale", r.Out, StringComparison.Ordinal);
        Assert.Contains("--only looks at a part", f.Run("audit", f.Root, "--only", f.At("Views"), "--baseline", f.At("b.json"), "--prune-baseline").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_without_a_project_is_read_with_its_folder()
    {
        using var f = new Folder().Add("a/M.xaml", Kit.Window("<TextBox/>")).Add("a/N.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal(1, Run(f, "audit", f.At("a/M.xaml")).Json.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public void The_numbers_of_a_part_are_its_own()
    {
        using var f = Two();
        var all = f.Audit();
        var b = all.Only(new HashSet<string> { "Views/B.xaml" });
        Assert.Equal((2, 1), (b.Checks.Single(c => c.Check == Checks.InputNames).Findings, b.Blocked));
        Assert.Equal(3, all.Checks.Single(c => c.Check == Checks.InputNames).Findings);
    }

    /// <summary>shell 補完會加結尾的斜線。</summary>
    [Fact]
    public void A_trailing_slash_is_fine()
    {
        using var f = Two();
        Assert.Equal(3, Run(f, "audit", f.Root, "--only", f.At("Views") + Path.DirectorySeparatorChar).Json.GetProperty("findings").GetArrayLength());
        Assert.Equal(3, Run(f, "audit", f.Root, "--only", f.At("Views") + "/").Json.GetProperty("findings").GetArrayLength());
    }

    /// <summary>自己的類別只報一次、報在別的檔案：用到它的檔案也要看得到，不能說乾淨。</summary>
    [Fact]
    public void A_class_reported_elsewhere_is_still_seen()
    {
        using var f = new Folder().Add("App.csproj", "<Project/>").Add("V1.xaml", Kit.Window("<local:Pad/>")).Add("V2.xaml", Kit.Window("<local:Pad/>")).Add("V3.xaml", Kit.Window("<Button Content=\"x\"/>")).Add("Pad.cs", "namespace App; public class Pad : Control { }");
        Assert.Equal("silent-tab-stop", Assert.Single(Run(f, "audit", f.Root, "--only", f.At("V2.xaml")).Json.GetProperty("findings").EnumerateArray()).GetProperty("kind").GetString());
        Assert.Empty(Run(f, "audit", f.Root, "--only", f.At("V3.xaml")).Json.GetProperty("findings").EnumerateArray());
    }
}
