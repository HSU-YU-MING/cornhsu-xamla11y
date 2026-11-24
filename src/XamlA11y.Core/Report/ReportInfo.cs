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
    /// 掃的可能是別人給的資料夾：網路路徑不碰（會替他連線），ref 只認 refs/ 底下的名字，只讀一小段。
    /// </summary>
    private static string? CommitOf(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (File.Exists(git) && Read(git) is { } link && link.StartsWith("gitdir:", StringComparison.Ordinal)) git = Local(dir.FullName, link["gitdir:".Length..].Trim());
                if (git.Length == 0) return null;
                if (new DirectoryInfo(git).LinkTarget is not null) return null;
                if (!Directory.Exists(git)) continue;
                var head = Read(Path.Combine(git, "HEAD"));
                if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return Short(head);
                var name = head["ref: ".Length..].Trim();
                if (!name.StartsWith("refs/", StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal) || !name.All(c => char.IsLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '+' or '#')) return null;
                // worktree 的分支在主要的那一份裡
                var common = File.Exists(Path.Combine(git, "commondir")) ? Local(git, Read(Path.Combine(git, "commondir"))) : git;
                foreach (var at in ((string[])[git, common]).Where(x => x.Length > 0))
                {
                    if (File.Exists(Path.Combine(at, name))) return Short(Read(Path.Combine(at, name)));
                    if (File.Exists(Path.Combine(at, "packed-refs")) && new FileInfo(Path.Combine(at, "packed-refs")).Length < 20_000_000 && File.ReadLines(Path.Combine(at, "packed-refs")).FirstOrDefault(l => l.EndsWith(" " + name, StringComparison.Ordinal)) is { } line) return Short(line);
                }
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;

        // 網路路徑（\\\\server、//server）回空字串
        static string Local(string from, string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.Length == 0) return "";
            var full = Path.GetFullPath(Path.Combine(from, path));
            return full.StartsWith(@"\\", StringComparison.Ordinal) && !from.StartsWith(@"\\", StringComparison.Ordinal) ? "" : full;
        }

        static string Read(string file)
        {
            using var stream = File.OpenRead(file);
            var buffer = new byte[4096];
            var n = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, n).Trim();
        }

        static string? Short(string sha) => sha.Length >= 7 && sha[..7].All(char.IsAsciiHexDigit) ? sha[..7] : null;
    }
}
