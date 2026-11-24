using XamlA11y.Cli;
using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;
using XamlA11y.Core.Rules;

namespace XamlA11y.Tests;

internal static class Kit
{
    public const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                             "xmlns:local=\"clr-namespace:App\" xmlns:hc=\"https://handyorg.github.io/handycontrol\" xmlns:md=\"http://materialdesigninxaml.net/winfx/xaml/themes\"";

    public const string Icon = "<Path Data=\"M0,0 L9,9 Z\"/>";

    public static string Window(string body, string resources = "") =>
        $"<Window {Ns} x:Class=\"App.Main\"><Window.Resources>{resources}</Window.Resources><StackPanel>{body}</StackPanel></Window>";

    /// <summary>視窗裡放一段 XAML，跑全部規則。</summary>
    public static AuditResult Audit(string body, string resources = "", IReadOnlyDictionary<string, string>? treatAs = null, AuditOptions? options = null, params (string File, string Xaml)[] more) =>
        Auditor.Run(Project.FromText(Window(body, resources), treatAs, more), options);

    public static List<string> Kinds(string body, string resources = "") => [.. Audit(body, resources).Findings.Select(f => f.Kind.Slug)];

    public static string Verdicts(AuditResult r, string check) =>
        string.Join(",", r.Judgements.Where(j => j.Check == check).Select(j => j.Kind?.Slug ?? j.Verdict.ToString()));

    /// <summary>一段 XAML 在一項檢查裡的判定。</summary>
    public static string Verdict(string check, string body, string resources = "") => Verdicts(Audit(body, resources), check);

    /// <summary>判定，沒判的連原因。</summary>
    public static string Why(AuditResult r, string check) =>
        string.Join(",", r.Judgements.Where(j => j.Check == check).Select(j => j.Kind?.Slug ?? $"{j.Verdict}: {j.Reason}".TrimEnd(' ', ':')));

    public static string[] SlugsOf(AuditResult r, string check) => [.. r.Findings.Where(f => f.Kind.Check == check).Select(f => f.Kind.Slug)];
    public static string[] Slugs(AuditResult r) => [.. r.Findings.Select(f => f.Kind.Slug).Distinct().Order(StringComparer.Ordinal)];

    // ── 另外宣告命名空間的視窗 ──

    public static string WindowNs(string extraNs, string body, string resources = "") =>
        $"<Window {Ns} {extraNs} x:Class=\"App.Main\"><Window.Resources>{resources}</Window.Resources><StackPanel>{body}</StackPanel></Window>";

    public static AuditResult AuditNs(string extraNs, string body, string resources = "") => Auditor.Run(Project.FromText(WindowNs(extraNs, body, resources)));

    public static string VerdictNs(string ns, string check, string body) => Verdicts(Auditor.Run(Project.FromText($"<Window {Ns} {ns}><StackPanel>{body}</StackPanel></Window>")), check);

    // ── 連程式碼一起的專案 ──

    public static Folder Files(string body, string code = "", string resources = "", string ext = ".cs") =>
        new Folder().Add("Main.xaml", Window(body, resources)).Add("Main.xaml" + ext, code);

    public static AuditResult AuditWithCode(string xaml, string code)
    {
        using var f = new Folder().Add("Main.xaml", xaml).Add("Code.cs", code);
        return f.Audit();
    }

    public static AuditResult AuditBody(string body, string code = "", string resources = "") => AuditWithCode(Window(body, resources), code);

    public static string VerdictWithCode(string check, string xaml, string code = "", params (string File, string Text)[] more)
    {
        using var f = new Folder().Add("Main.xaml", Window(xaml)).Add("Code.cs", code);
        foreach (var (file, text) in more) f.Add(file, text);
        return string.Join(",", f.Audit().Judgements.Where(j => j.Check == check).Select(j => j.Verdict));
    }

    public const string NoteList = "<ListBox ItemsSource=\"{Binding Notes}\" DisplayMemberPath=\"Title\"/>";
    public const string Note = "namespace A; public class Note { public string Title { get; set; } = \"\"; } public class Vm { public ObservableCollection<Note> Notes { get; } = new(); }";

    public static string Csproj(params string[] packages) =>
        "<Project><ItemGroup>" + string.Concat(packages.Select(p => $"<PackageReference Include=\"{p.Split(' ')[0]}\" Version=\"{p.Split(' ')[1]}\"/>")) + "</ItemGroup></Project>";

    public static Folder Good() => new Folder().Add("Main.xaml", Window("<Button Content=\"Save\"/>"));
    public static Folder Bad() => new Folder().Add("Main.xaml", Window($"<Button Content=\"ok\"/><Button>{Icon}</Button>"));

    public static string Html(AuditResult r, int exit = 1, Lang? lang = null, string? why = null) => HtmlReport.Create(r, r.Findings, exit, 0, why, "1.0", lang);

    // ── 焦點 ──

    public const string FocusTemplate =
        "<ControlTemplate x:Key=\"T\" TargetType=\"Button\"><Border x:Name=\"b\"/><ControlTemplate.Triggers><Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"b\" Property=\"Background\" Value=\"Red\"/></Trigger></ControlTemplate.Triggers></ControlTemplate>";

    /// <summary>拿掉焦點框、換了範本的按鈕。</summary>
    public static string FocusButton(string triggers) =>
        $"<Button Content=\"x\" FocusVisualStyle=\"{{x:Null}}\"><Button.Template><ControlTemplate TargetType=\"Button\"><Border x:Name=\"b\"><ContentPresenter/></Border>{triggers}</ControlTemplate></Button.Template></Button>";

    // ── 每一種問題都有一個的視窗 ──

    public const string Everything = """
        <Button ToolTip="Delete"><Path Data="M0,0 L9,9 Z"/></Button>
        <Button><StackPanel><TextBlock Text="Save"/></StackPanel></Button>
        <Button FontFamily="Segoe MDL2 Assets" Content="&#xE74D;"/>
        <Button Content="×"/>
        <Label Target="{Binding ElementName=n}" Content="Name"/><TextBox x:Name="n"/>
        <Border MouseLeftButtonUp="OnOpen"/>
        <Button Content="a" IsTabStop="False"/>
        <Button Content="b" FocusVisualStyle="{x:Null}" Template="{StaticResource T}"/>
        <GroupBox Header="Files"><ListBox/></GroupBox>
        <ProgressBar/><ContentControl/>
        <StackPanel><Button Content="Edit" Click="A"/><Button Content="Edit" Click="B"/></StackPanel>
        <StackPanel Orientation="Horizontal"><Button Content="c" Width="18" Height="30"/><Button Content="d" Width="18" Height="30"/></StackPanel>
        """;

    public const string EverythingTemplate = "<ControlTemplate x:Key=\"T\" TargetType=\"Button\"><Border><ContentPresenter/></Border></ControlTemplate>";

    /// <summary>上面那一組有的種類（item-type-name 要有 C#）。</summary>
    public static IEnumerable<string> KindsInEverything => Core.Analysis.Kinds.All.Select(k => k.Slug).Where(s => s != "item-type-name");

    public static AuditResult AuditEverything(Scope? scope = null, Dictionary<string, Severity>? rules = null) =>
        Audit(Everything, EverythingTemplate, options: new AuditOptions { Scope = scope ?? Scope.Default, Severities = rules ?? [] });
}

/// <summary>暫存資料夾裡的專案。</summary>
internal sealed class Folder : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("xamla11y-test-").FullName;

    public Folder Add(string file, string content)
    {
        var path = Path.Combine(Root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return this;
    }

    public string At(string file) => Path.Combine(Root, file);
    public AuditResult Audit(AuditOptions? options = null, IReadOnlyDictionary<string, string>? treatAs = null) => Auditor.Run(Project.Load(Root, [], treatAs), options);

    public (int Exit, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = Program.Run(args, o, e);
        return (exit, o.ToString(), e.ToString());
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}
