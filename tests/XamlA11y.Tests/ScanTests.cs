using System.Text;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>掃哪些檔案：資料夾、副檔名、連結、編碼、別的 XAML 方言、讀不懂的檔。</summary>
public sealed class ScanTests
{
    /// <summary>剛好到門檻的算過。</summary>
    [Theory]
    [InlineData("50", 0)]
    [InlineData("50.1", 3)]
    public void Half_of_the_files_could_be_parsed(string min, int exit)
    {
        using var f = new Folder().Add("A.xaml", Kit.Window("<Button Content=\"x\"/>")).Add("B.xaml", "<Window");
        Assert.Equal(exit, f.Run("audit", f.Root, "--min-coverage", min).Exit);
    }

    [Fact]
    public void Design_time_attributes_do_not_exist_at_run_time()
    {
        const string d = "xmlns:d=\"http://schemas.microsoft.com/expression/blend/2008\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"d\"";
        var r = Kit.AuditNs(d, $"<Button d:Content=\"Save\">{Kit.Icon}</Button><TextBox d:AutomationProperties.Name=\"x\"/><Button Content=\"x\" d:IsTabStop=\"False\"/>");
        Assert.Equal(["input-no-name", "missing-name"], r.Findings.Select(f => f.Kind.Slug).Order());
    }

    [Fact]
    public void A_source_folder_called_Packages_is_scanned_and_a_NuGet_one_is_not()
    {
        using var p = new Folder()
            .Add("Views/Packages/A.xaml", Kit.Window("<TextBox/>"))
            .Add("packages/Some.Package.1.0.0/Some.Package.1.0.0.nupkg", "")
            .Add("packages/Some.Package.1.0.0/B.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal("Views/Packages/A.xaml", Assert.Single(p.Audit().Findings).File);
    }

    [Theory]
    [InlineData("Views\\Old")]
    [InlineData("./Views/Old")]
    [InlineData("/views/old/")]
    public void Excluded_files_are_counted(string exclude)
    {
        using var p = new Folder().Add("Views/Old/A.xaml", Kit.Window("<TextBox/>")).Add("Main.xaml", Kit.Window("<Button Content=\"ok\"/>"));
        var (exit, output, _) = p.Run("audit", p.Root, "--exclude", exclude);
        Assert.Equal(0, exit);
        Assert.Contains("excluded: 1 XAML file(s)", output);
    }

    [Fact]
    public void A_percent_sign_in_a_path_is_just_a_percent_sign()
    {
        using var p = new Folder().Add("rt%41/A%41.xaml", Kit.Window("<TextBox/>"));
        Assert.Equal("rt%41/A%41.xaml", Assert.Single(p.Audit().Findings).File);
    }

    [Theory]
    [InlineData("windows-1252")]
    [InlineData("gb2312")]
    public void Legacy_encodings_that_the_file_declares_are_read(string encoding)
    {
        using var p = new Folder();
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(p.At("Main.xaml"), System.Text.Encoding.GetEncoding(encoding).GetBytes($"<?xml version=\"1.0\" encoding=\"{encoding}\"?>" + Kit.Window("<Button/>")));
        var r = p.Audit();
        Assert.Empty(r.ParseErrors);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void A_file_that_cannot_be_parsed_says_why()
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>")).Add("Bad.xaml", $"<Window {Kit.Ns} Title=\"a\" Title=\"b\"/>");
        Assert.Contains("'Title'", p.Run("audit", p.Root).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Linked_files_are_not_read()
    {
        using var outside = new Folder().Add("Outside.xaml", Kit.Window("<Button/>"));
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>"));
        try { File.CreateSymbolicLink(p.At("Link.xaml"), outside.At("Outside.xaml")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }      // Windows 沒有開發人員模式就不能建
        var r = p.Audit();
        Assert.Equal(1, r.Files);
        Assert.Empty(r.Findings);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("OBJ")]
    [InlineData("bin")]
    [InlineData("node_modules")]
    [InlineData(".git")]
    public void Build_output_is_not_scanned(string directory)
    {
        using var p = new Folder().Add("Main.xaml", Kit.Window("<Button Content=\"x\"/>")).Add(directory + "/Copy.xaml", Kit.Window("<Button/>"));
        Assert.Equal(1, p.Audit().Files);
    }

    [Fact]
    public void A_file_without_marks_is_what_its_neighbours_are()
    {
        const string wpf = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:l=\"clr-namespace:A\"><Button/></Page>";
        const string winui = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:l=\"using:A\"><Button/></Page>";
        const string plain = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Button/></Page>";
        using var p = new Folder().Add("W/A.xaml", wpf).Add("W/B.xaml", plain).Add("U/A.xaml", winui).Add("U/B.xaml", plain).Add("U/Deep/C.xaml", plain);
        var r = p.Audit();
        Assert.Equal(["U/A.xaml", "U/B.xaml", "U/Deep/C.xaml"], r.Foreign.Select(f => f.File).Order(StringComparer.Ordinal));
        Assert.Equal(2, r.Findings.Count);
    }

    [Theory]
    [InlineData("<Window")]
    [InlineData("")]
    [InlineData("\0\0\0")]
    [InlineData("<!DOCTYPE x [<!ENTITY a SYSTEM \"file:///etc/passwd\">]><x>&a;</x>")]
    public void Unparseable_files_are_listed(string content)
    {
        using var p = Kit.Good().Add("Good2.xaml", Kit.Window("<Button Content=\"ok\"/>")).Add("Broken.xaml", content);
        var (exit, output, _) = p.Run("audit", p.Root);
        Assert.Equal(0, exit);
        Assert.Contains("could not parse Broken.xaml", output);
    }
}
