using System.Globalization;

namespace XamlA11y.Core.Report;

/// <summary>報告上的「這是誰的、什麼時候的」。不寫完整路徑：報告會拿給別人看。</summary>
/// <param name="Name">專案（資料夾）的名字，或使用者給的標題。</param>
public sealed record ReportInfo(string Name, DateOnly Date, string? Commit = null)
{
    public string DateText => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static ReportInfo For(string root, string? title)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var name = Path.GetFileName(full);
        // 「src」看不出是誰的：帶上一層
        if (name.ToLowerInvariant() is "src" or "source" or "sources" or "app" or "code" or "wpf" && Path.GetFileName(Path.GetDirectoryName(full)) is { Length: > 0 } parent) name = parent + "/" + name;
        return new ReportInfo(string.IsNullOrWhiteSpace(title) ? name.Length > 0 ? name : full : title.Trim(), DateOnly.FromDateTime(DateTime.Now), CommitOf(full));
    }

    /// <summary>
    /// 目前的 commit（前七碼）。只讀 .git 裡的檔案，不執行 git；讀不到就不寫。
    /// 掃的可能是別人給的資料夾：網路路徑不碰（會替他連線、送出帳號），連結一律不跟（符號連結可以指到網路或外面），
    /// ref 只認 refs/ 底下的名字，只讀一小段。
    /// </summary>
    private static string? CommitOf(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                // 先看是不是連結（不會跟過去），再看有沒有這個檔案
                if (IsLink(git)) return null;
                if (File.Exists(git))
                {
                    if (Read(git) is not { } link || !link.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
                    git = Local(dir.FullName, link["gitdir:".Length..].Trim());
                    if (git.Length == 0) return null;
                }
                if (!Directory.Exists(git)) continue;
                if (Inside(git, "HEAD") is not { } headFile || Read(headFile) is not { } head) return null;
                if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return Short(head);
                var name = head["ref: ".Length..].Trim();
                if (!name.StartsWith("refs/", StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal) || !name.All(c => char.IsLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '+' or '#')) return null;
                // worktree 的分支在主要的那一份裡
                var common = Inside(git, "commondir") is { } file && Read(file) is { } path ? Local(git, path) : git;
                foreach (var at in ((string[])[git, common]).Where(x => x.Length > 0))
                {
                    if (Inside(at, name) is { } loose) return Read(loose) is { } sha ? Short(sha) : null;
                    if (Inside(at, "packed-refs") is { } packed && new FileInfo(packed).Length < 20_000_000 && File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + name, StringComparison.Ordinal)) is { } line) return Short(line);
                }
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    /// <summary>資料夾裡的檔案：一段一段看，中間有連結、沒有這個檔案都回 null。</summary>
    internal static string? Inside(string folder, string relative)
    {
        var path = folder;
        foreach (var part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            if (IsLink(path)) return null;
        }
        return File.Exists(path) ? path : null;
    }

    /// <summary>gitdir、commondir 指到的資料夾。網路路徑、路上有連結的（跟出發點共同的那一段是使用者自己選的，不看）回空字串。</summary>
    internal static string Local(string from, string path)
    {
        if (path.Length == 0 || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return "";
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(from, path)));
        if (full.StartsWith(@"\\", StringComparison.Ordinal) && !from.StartsWith(@"\\", StringComparison.Ordinal)) return "";
        // 從上往下看：上面的是連結，下面的就不能碰
        var parts = new Stack<DirectoryInfo>();
        for (var d = new DirectoryInfo(full); d is not null; d = d.Parent) parts.Push(d);
        foreach (var d in parts)
            if (!IsUnder(from, d.FullName) && d.LinkTarget is not null) return "";
        return full;

        static bool IsUnder(string child, string parent)
        {
            var p = Path.TrimEndingDirectorySeparator(parent);
            return child.StartsWith(p, StringComparison.OrdinalIgnoreCase) && (child.Length == p.Length || p.EndsWith(Path.DirectorySeparatorChar) || child[p.Length] == Path.DirectorySeparatorChar);
        }
    }

    private static string? Read(string file)
    {
        if (IsLink(file)) return null;
        using var stream = File.OpenRead(file);
        var buffer = new byte[4096];
        var n = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, n).Trim();
    }

    private static string? Short(string sha) => sha.Length >= 7 && sha[..7].All(char.IsAsciiHexDigit) ? sha[..7] : null;
}
