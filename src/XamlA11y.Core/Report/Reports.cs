using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>已知債清單。第一次跑產生，之後只擋新增的。鍵不含行號（見 <see cref="Finding.BaselineKey"/>）。</summary>
public sealed class Baseline
{
    public const int CurrentKeyVersion = 1;

    [JsonPropertyName("keyVersion")] public int KeyVersion { get; set; } = CurrentKeyVersion;
    [JsonPropertyName("generatedOn")] public string GeneratedOn { get; set; } = "";
    [JsonPropertyName("known")] public List<string> Known { get; set; } = [];

    public static Baseline From(IEnumerable<Finding> findings, DateOnly date) => new()
    {
        GeneratedOn = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Known = [.. findings.Select(f => f.BaselineKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json.Options) + "\n";

    public static Baseline Parse(string json)
    {
        // 新版多了欄位的檔案：先說「太新了」，不要丟「不認得的欄位」
        using (var probe = JsonDocument.Parse(json))
            if (probe.RootElement.ValueKind == JsonValueKind.Object && probe.RootElement.TryGetProperty("keyVersion", out var kv) && kv.ValueKind == JsonValueKind.Number
                && kv.TryGetInt32(out var version) && version > CurrentKeyVersion)
                throw new InvalidDataException($"baseline keyVersion {version} is newer than this tool understands ({CurrentKeyVersion}); upgrade xamla11y");
        var b = JsonSerializer.Deserialize<Baseline>(json, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidDataException("the baseline file is empty");
        if (b.Known is null || b.Known.Any(k => k is null)) throw new InvalidDataException("\"known\" must be a list of keys");
        if (b.KeyVersion < 1) throw new InvalidDataException($"baseline keyVersion {b.KeyVersion} is not valid");
        return b;
    }

    /// <summary>(新的, 已知的, baseline 裡已經不存在的)。</summary>
    public (IReadOnlyList<Finding> New, int Known, int Stale) Split(IReadOnlyList<Finding> findings)
    {
        var known = new HashSet<string>(Known, StringComparer.Ordinal);
        var fresh = findings.Where(f => !known.Contains(f.BaselineKey)).ToList();
        var present = findings.Select(f => f.BaselineKey).ToHashSet(StringComparer.Ordinal);
        return (fresh, findings.Count - fresh.Count, known.Count(k => !present.Contains(k)));
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public static class Text
{
    public static string Slug(Severity s) => s switch { Severity.Fail => "fail", Severity.Warn => "warn", Severity.Info => "info", _ => "off" };

    /// <summary>
    /// 印到 console 的字串：控制字元逃掉（MSBuild 與 CI 逐行解析，訊息裡夾一個換行就多一筆假的診斷），
    /// Azure Pipelines／GitHub Actions 的舊指令格式（##vso[、##[）拆開。文字來自使用者的 XAML，是不可信的輸入。
    /// </summary>
    /// <summary>雙向控制與看不見的格式字元寫成 \uXXXX（會把別的文字的順序改掉）。表情用的零寬連字 U+200D 留著。</summary>
    public static string Visible(string s)
    {
        if (!s.Any(Hidden)) return s;
        return string.Concat(s.Select(c => Hidden(c) ? $"\\u{(int)c:x4}" : c.ToString()));
        static bool Hidden(char c) => c != (char)0x200D && char.GetUnicodeCategory(c) == UnicodeCategory.Format;
    }

    public static string Safe(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
            sb.Append(c switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\t",
                // 看不見的格式字元（軟連字號、雙向控制、零寬）：夾在 ## 與 vso[ 之間可以躲過比對，順序也會被改掉
                _ when char.IsControl(c) || c is (char)0x2028 or (char)0x2029 || char.GetUnicodeCategory(c) == UnicodeCategory.Format => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        return sb.ToString().Replace("##", "#\\u0023", StringComparison.Ordinal);
    }
}

/// <summary><c>--json</c>。summary 與 findings 分兩層：CI 只讀 findings 的話，「哪些看不到」等於不存在。</summary>
public static class JsonReport
{
    public const int SchemaVersion = 1;

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int baselined, string toolVersion, int exitCode) => JsonSerializer.Serialize(new
    {
        schemaVersion = SchemaVersion,
        toolVersion,
        mode = "audit",
        exitCode,
        summary = new
        {
            files = r.Files,
            parseErrors = r.ParseErrors.Count,
            parseErrorFiles = r.ParseErrors,
            foreignFiles = r.Foreign.GroupBy(f => f.Dialect).Select(g => new { dialect = g.Key, files = g.Count() }),
            candidates = r.Candidates,
            judged = r.Judged,
            blocked = r.Blocked,
            notJudged = r.NotJudged,
            suppressed = r.Suppressed,
            resolution = Math.Round(r.Resolution, 1),
            rules = r.Rules.Select(s => new
            {
                rule = s.Rule,
                title = Kinds.RuleTitle(s.Rule),
                candidates = s.Candidates,
                pass = s.Pass,
                findings = s.Findings,
                suppressed = s.Suppressed,
                blocked = s.Blocked,
                notJudged = s.NotJudged,
                resolution = Math.Round(s.Resolution, 1),
                blockedReasons = Reasons(s.BlockedReasons),
                notJudgedReasons = Reasons(s.NotJudgedReasons),
            }),
            unknownControls = r.UnknownControls.Select(u => new { element = u.Element, sites = u.Count }),
            externalThemes = r.Themes.Select(t => new { theme = t.Name, declaredIn = t.DeclaredIn, scope = t.Scope.Length == 0 ? "(everything scanned)" : t.Scope, known = t.LibraryId }),
            excludedFiles = r.Gaps.Excluded,
            unreadableFolders = r.Gaps.Unreadable,
            invalidIgnores = r.InvalidIgnores,
            counts = Kinds.All.ToDictionary(k => k.Slug, k => r.Findings.Count(f => f.Kind == k), StringComparer.Ordinal),
            severities = new
            {
                fail = reported.Count(f => f.Severity == Severity.Fail),
                warn = reported.Count(f => f.Severity == Severity.Warn),
                info = reported.Count(f => f.Severity == Severity.Info),
            },
            inBaseline = baselined,
            reportedAfterBaseline = reported.Count,
            scope = Summary.Scope,
        },
        findings = reported.Select(f => new
        {
            kind = f.Kind.Slug,
            rule = f.Kind.Rule,
            severity = Text.Slug(f.Severity),
            wcag = f.Kind.Wcag,
            site = f.Site,
            file = f.File,
            line = f.Line,
            element = f.Element,
            identity = f.Identity,
            message = f.Message,
            help = f.Kind.Help,
        }),
    }, Json.Options) + "\n";

    private static IEnumerable<object> Reasons(Dictionary<string, int> map) =>
        map.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => new { reason = p.Key, sites = p.Value });
}

public static class Summary
{
    /// <summary>每一份報告都要講的：看了什麼、沒看什麼。</summary>
    public const string Scope =
        "Static: the XAML was read, nothing was run; of the code, only class declarations and each XAML file's own code-behind (does it set a name, a content or a key handler?). Passing is not a claim of WCAG or EAA conformance.";
}

/// <summary>SARIF 2.1.0。info 不輸出：不是缺陷，灌進 Security 分頁是噪音（JSON 裡照樣有）。</summary>
public static class SarifReport
{
    public static string Create(IReadOnlyList<Finding> reported, string toolVersion)
    {
        var text = JsonSerializer.Serialize(new
        {
            version = "2.1.0",
            schema__ = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "XamlA11y",
                            informationUri = "https://cornhsu.com/xamla11y",
                            version = toolVersion,
                            rules = Kinds.All.Select(k => new
                            {
                                id = k.Slug,
                                name = string.Concat(k.Slug.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..])),
                                shortDescription = new { text = k.Title },
                                fullDescription = new { text = k.Help },
                                help = new { text = $"{k.Help} (WCAG {k.Wcag})" },
                                defaultConfiguration = new { level = Level(k.Default) },
                                properties = new { tags = new[] { "accessibility", "WCAG " + k.Wcag } },
                            }),
                        },
                    },
                    results = reported.Where(f => f.Severity >= Severity.Warn).Select(f => new
                    {
                        ruleId = f.Kind.Slug,
                        level = Level(f.Severity),
                        // SARIF 的純文字訊息裡 [文字](網址) 是連結，訊息夾了 repo 的字：方括號跳脫
                        message = new { text = f.Message.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal) },
                        locations = new[]
                        {
                            new
                            {
                                physicalLocation = new
                                {
                                    artifactLocation = new { uri = Uri(f.File) },
                                    region = new { startLine = Math.Max(f.Line, 1) },
                                },
                            },
                        },
                        partialFingerprints = new Dictionary<string, string> { ["xamla11yKey/v1"] = f.BaselineKey },
                    }),
                },
            },
        }, Json.Options);
        // C# 的匿名型別不能有 $schema 這個名字
        return text.Replace("\"schema__\"", "\"$schema\"", StringComparison.Ordinal) + "\n";
    }

    /// <summary>SARIF 的 uri 要是合法的 URI：資料夾名有空白或中文時逐段編碼（斜線留著）。</summary>
    private static string Uri(string file) => string.Join('/', file.Split('/').Select(System.Uri.EscapeDataString));

    private static string Level(Severity s) => s switch { Severity.Fail => "error", Severity.Warn => "warning", _ => "note" };
}

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

/// <summary>
/// Markdown（<c>--md</c>）：給 PR 留言用。「看不到的東西」放最前面。
/// ⚠ 路徑、名字、文字都來自 repo（不可信的輸入）：不是工具自己寫的字串一律走 <see cref="Code"/> ——
///   GitHub 只有在 code span 裡不處理連結、圖片、@ 提及與 www. 自動連結。訊息裡夾了使用者的文字，所以整句也進 code span。
/// </summary>
public static class MarkdownReport
{
    private const int RowsPerSection = 50;

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int exitCode, int baselined, string? why)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int Count(Severity s) => reported.Count(f => f.Severity == s);

        sb.AppendLine("## XamlA11y — accessibility audit").AppendLine();
        var verdict = exitCode switch { 0 => "✅ **PASS**", 1 => "❌ **FAIL**", 3 => "⚠️ **NOT TRUSTWORTHY** (exit 3)", _ => $"exit {exitCode.ToString(inv)}" };
        sb.AppendLine(string.Create(inv,
            $"{verdict} · **{Count(Severity.Fail)} fail**, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info · {r.Judged} check(s) in {r.Files} XAML file(s) · resolution {r.Resolution:F1}%")).AppendLine();

        if (exitCode == 3)
            sb.AppendLine($"> ⚠️ **The result can't be trusted.** {Code(why ?? "see the console output of the run", table: false)}").AppendLine();
        foreach (var f in r.ParseErrors.Take(20))
            sb.AppendLine($"> ⚠️ **Parse failed:** {Code(f, table: false)} — this file is missing from the audit.").AppendLine();
        if (r.Blocked > 0)
        {
            sb.AppendLine(string.Create(inv, $"> ⚠️ **{r.Blocked} place(s) could not be judged:**"));
            foreach (var (reason, n) in r.Rules.SelectMany(x => x.BlockedReasons).GroupBy(p => p.Key).Select(g => (g.Key, N: g.Sum(p => p.Value))).OrderByDescending(x => x.N).ThenBy(x => x.Key, StringComparer.Ordinal).Take(5))
                sb.AppendLine(string.Create(inv, $"> - {n} × {Code(reason, table: false)}"));
            sb.AppendLine();
        }

        foreach (var severity in (ReadOnlySpan<Severity>)[Severity.Fail, Severity.Warn])
        {
            var group = reported.Where(f => f.Severity == severity).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine(string.Create(inv, $"### {(severity == Severity.Fail ? "Fail" : "Warn")} ({group.Count})")).AppendLine();
            Table(sb, group);
        }
        var info = reported.Where(f => f.Severity == Severity.Info).ToList();
        if (info.Count > 0)
        {
            sb.AppendLine(string.Create(inv, $"<details><summary>{info.Count} info finding(s) (do not fail the build)</summary>")).AppendLine();
            Table(sb, info);
            sb.AppendLine("</details>").AppendLine();
        }

        var notes = new List<string>();
        if (baselined > 0) notes.Add(string.Create(inv, $"{baselined} in the baseline"));
        if (r.NotJudged > 0) notes.Add(string.Create(inv, $"{r.NotJudged} not judged (data-bound names, template parts, list items…)"));
        if (r.Suppressed > 0) notes.Add(string.Create(inv, $"{r.Suppressed} suppressed via xamla11y-ignore"));
        if (r.Foreign.Count > 0) notes.Add(string.Create(inv, $"{r.Foreign.Count} non-WPF XAML file(s)"));
        if (r.Gaps.Excluded > 0) notes.Add(string.Create(inv, $"{r.Gaps.Excluded} file(s) excluded"));
        if (r.Gaps.Unreadable.Count > 0) notes.Add(string.Create(inv, $"{r.Gaps.Unreadable.Count} unreadable folder(s)"));
        if (notes.Count > 0) sb.AppendLine($"**Not in the list above:** {string.Join(" · ", notes)}").AppendLine();

        sb.AppendLine($"<sub>Generated by XamlA11y · {Summary.Scope}</sub>");
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void Table(StringBuilder sb, List<Finding> findings)
    {
        sb.AppendLine("| Location | Kind | WCAG | Detail |").AppendLine("|---|---|---|---|");
        foreach (var f in findings.Take(RowsPerSection))
            sb.AppendLine($"| {Code(f.Site)} | {f.Kind.Slug} | {f.Kind.Wcag} | {Code(Trunc(f.Message, 220))} |");
        sb.AppendLine();
        if (findings.Count > RowsPerSection)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<sub>… and {findings.Count - RowsPerSection} more — see the JSON report artifact.</sub>")).AppendLine();
    }

    private static string Trunc(string s, int n)
    {
        if (s.Length <= n) return s;
        var cut = n - 1;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut] + "…";
    }

    /// <summary>code span：圍欄比內容裡最長的反引號串多一個，換行收成空白，表格裡的 | 跳脫。</summary>
    internal static string Code(string s, bool table = true)
    {
        var body = Text.Visible((table ? s.Replace("|", "\\|") : s).Replace("\r", " ").Replace("\n", " "));
        if (body.Trim().Length == 0) body = "(empty)";
        int longest = 0, run = 0;
        foreach (var c in body)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', longest + 1);
        var pad = longest > 0 ? " " : "";
        return fence + pad + body + pad + fence;
    }
}
