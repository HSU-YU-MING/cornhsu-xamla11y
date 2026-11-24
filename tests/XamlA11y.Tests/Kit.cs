using XamlA11y.Cli;
using XamlA11y.Core.Analysis;
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

    public static string Verdicts(AuditResult r, string rule) =>
        string.Join(",", r.Judgements.Where(j => j.Rule == rule).Select(j => j.Kind?.Slug ?? j.Verdict.ToString()));
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
