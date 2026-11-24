using XamlA11y.Core.Analysis;
using XamlA11y.Core.Xaml;

namespace XamlA11y.Cli;

/// <summary>
/// 要看哪裡：資料夾、專案檔（看它的資料夾）、單一 .xaml（看它的專案，只報這個檔 —— 樣式與資源在別的檔案裡，只讀一個檔會判錯）。
/// </summary>
internal static class Target
{
    /// <summary>把 Root 換成資料夾；不對回傳錯在哪。</summary>
    public static string? Resolve(Options o)
    {
        if (Directory.Exists(o.Root)) return null;
        if (!File.Exists(o.Root)) return $"not a folder, project or XAML file: {o.Root}";
        var full = Path.GetFullPath(o.Root);
        switch (Path.GetExtension(full).ToLowerInvariant())
        {
            case ".csproj" or ".vbproj" or ".sln" or ".slnx":
                o.Root = Path.GetDirectoryName(full)!;
                return null;
            case ".xaml":
                o.Only.Add(full);
                o.Root = ProjectFolder(Path.GetDirectoryName(full)!);
                return null;
            default: return $"not a folder, project or XAML file: {o.Root}";
        }
    }

    /// <summary>往上最近一個有專案檔的資料夾；沒有就是它自己。</summary>
    private static string ProjectFolder(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
                if (dir.EnumerateFiles("*.csproj").Any() || dir.EnumerateFiles("*.vbproj").Any()) return dir.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return folder;
    }

    /// <summary>--only 對到哪些檔案（專案裡的相對路徑）。</summary>
    public static (IReadOnlySet<string> Files, string? Missing) Files(Options o, Project project)
    {
        var all = project.Docs.Select(d => d.File).Concat(project.ParseErrors).Concat(project.Foreign.Select(f => f.File)).ToList();
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var only in o.Only)
        {
            var rel = XamlFiles.Relative(o.Root!, Path.TrimEndingDirectorySeparator(Path.GetFullPath(only)));
            var here = all.Where(f => string.Equals(f, rel, StringComparison.OrdinalIgnoreCase) || rel == "." || f.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase)).ToList();
            if (here.Count == 0) return (found, $"--only {only}: no XAML file of {o.Root} is there");
            found.UnionWith(here);
        }
        return (found, null);
    }
}
