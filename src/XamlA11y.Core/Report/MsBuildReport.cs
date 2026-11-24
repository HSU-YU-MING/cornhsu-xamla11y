using System.Globalization;
using System.Text;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>
/// MSBuild 診斷（<c>--msbuild</c>）：XA0001 = 這次擋下的、XA0002 = 有問題但這次不擋、XA0003 = 結果不可信。
/// Exec 會把這種行列進 Visual Studio 的錯誤清單。
/// </summary>
public static class MsBuildReport
{
    public static string Create(IReadOnlyList<Finding> reported, string root, Severity gate, int exitCode)
    {
        var sb = new StringBuilder();
        foreach (var f in reported.Where(f => f.Severity >= Severity.Warn))
            sb.Append(Text.Safe(FullPath(root, f.File)))
              .Append('(').Append(Math.Max(f.Line, 1).ToString(CultureInfo.InvariantCulture)).Append(",1): ")
              .Append(f.Severity >= gate ? "error XA0001: " : "warning XA0002: ")
              .Append(Text.Safe($"{f.Kind.Slug}: {f.Message}")).Append('\n');
        if (exitCode == 3) sb.Append("XamlA11y : error XA0003: the result can't be trusted (exit 3) — see the messages above\n");
        return sb.ToString();
    }

    private static string FullPath(string root, string file)
    {
        try { return Path.GetFullPath(Path.Combine(root, file)); }
        catch (Exception) { return file; }
    }
}
