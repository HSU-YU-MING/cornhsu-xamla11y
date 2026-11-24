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
    /// 看不見的：格式字元（雙向控制會改掉別的文字的順序；標籤字元 U+E00xx 在 BMP 外，要一個碼位一個碼位看）、行分隔、沒有字形的諺文填充。
    /// </summary>
    private static bool Hidden(Rune r) =>
        Rune.GetUnicodeCategory(r) == UnicodeCategory.Format || r.Value is 0x0085 or 0x2028 or 0x2029 or 0x115F or 0x1160 or 0x3164 or 0xFFA0;

    private static string Escaped(Rune r) => r.IsBmp ? $"\\u{r.Value:x4}" : $"\\U{r.Value:x8}";

    /// <summary>看不見的字元寫成 \uXXXX。表情用的零寬連字 U+200D 留著。落單的代理（XML 讀不進來，保險）變成 U+FFFD。</summary>
    public static string Visible(string s)
    {
        if (s.All(c => c is >= ' ' and < (char)0x7F)) return s;
        var sb = new StringBuilder(s.Length + 8);
        foreach (var r in s.EnumerateRunes()) sb.Append(Hidden(r) && r.Value != 0x200D ? Escaped(r) : r.ToString());
        return sb.ToString();
    }

    /// <summary>
    /// 印到 console 的字串：控制字元逃掉（MSBuild 與 CI 逐行解析，訊息裡夾一個換行就多一筆假的診斷），
    /// Azure Pipelines／GitHub Actions 的舊指令格式（##vso[、##[）拆開。文字來自使用者的 XAML，是不可信的輸入。
    /// </summary>

    public static string Safe(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var r in s.EnumerateRunes())
            sb.Append(r.Value switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\t",
                // 看不見的格式字元（軟連字號、雙向控制、零寬）：夾在 ## 與 vso[ 之間可以躲過比對，順序也會被改掉
                _ when Rune.IsControl(r) || Hidden(r) => Escaped(r),
                _ => r.ToString(),
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
                off = s.Off,
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
            standard = r.Scope.Standard.Id,
            level = r.Scope.Level,
            wcagVersion = r.Scope.Standard.Wcag,
            scope = Summary.Scope,
        },
        manualChecks = ManualCheck.In(r.Scope).Select(m => new
        {
            criteria = m.Criteria.Select(c => new { number = c.Number, level = c.Level, name = c.Name }),
            check = m.Check.En,
        }),
        findings = reported.Select(f => new
        {
            kind = f.Kind.Slug,
            rule = f.Kind.Rule,
            severity = Text.Slug(f.Severity),
            wcag = string.Join(", ", r.Scope.CriteriaOf(f.Kind)),
            wcagLevels = r.Scope.CriteriaOf(f.Kind).ToDictionary(c => c, Wcag.Level, StringComparer.Ordinal),
            clauses = Clauses(f.Kind, f.NameIsEmpty),
            site = f.Site,
            file = f.File,
            line = f.Line,
            element = f.Element,
            identity = f.Identity,
            message = f.Message,
            help = f.Kind.Help,
            helpUri = f.Kind.Uri(),
            nvda = Nvda.Says(f, Lang.En),
        }),
    }, Json.Options) + "\n";

    /// <summary>每個有自己條號的標準：不管這次選哪一個，寫合規報告時都查得到。</summary>
    internal static Dictionary<string, IReadOnlyList<string>> Clauses(Kind kind, bool nameIsEmpty) =>
        Standard.All.Where(s => s.HasClauses).Select(s => (s.Id, List: s.ClausesOf(new Scope(s, "AA").CriteriaOf(kind), nameIsEmpty))).Where(x => x.List.Count > 0).ToDictionary(x => x.Id, x => x.List, StringComparer.Ordinal);

    private static IEnumerable<object> Reasons(Dictionary<string, int> map) =>
        map.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => new { reason = p.Key, sites = p.Value });
}

public static class Summary
{
    /// <summary>baseline 蓋住的要跟「通過」放在一起講：通過的是「沒有新的」，不是沒有問題。</summary>
    public static string? KnownDebt(IReadOnlyList<Finding>? known, int exitCode, Lang lang)
    {
        if (known is not { Count: > 0 }) return null;
        int Count(Severity s) => known.Count(f => f.Severity == s);
        var inv = CultureInfo.InvariantCulture;
        var pass = exitCode == 0 ? lang.T("Pass means no new findings. ", "通過的意思是沒有新的問題。") : "";
        return pass + lang.T(
            string.Create(inv, $"{known.Count} known problem(s) in the baseline are not fixed: {Count(Severity.Fail)} fail, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info."),
            string.Create(inv, $"baseline 裡還有 {known.Count} 個已知的問題沒修：{Count(Severity.Fail)} fail、{Count(Severity.Warn)} warn、{Count(Severity.Info)} info。"));
    }

    /// <summary>查了多少：幾條準則裡的幾條。</summary>
    public static string Coverage(Scope scope, Lang lang)
    {
        var total = Wcag.Total(scope.Standard.Wcag, scope.Level);
        var here = Kinds.All.SelectMany(scope.CriteriaOf).Distinct(StringComparer.Ordinal).Count();
        return lang.T(
            string.Create(CultureInfo.InvariantCulture, $"This checks {here} of the {total} success criteria of WCAG {scope.Standard.Wcag} {(scope.Level == "A" ? "A" : "A and AA")}, and only as far as the XAML decides them. Passing is not a claim of conformance."),
            string.Create(CultureInfo.InvariantCulture, $"WCAG {scope.Standard.Wcag} 的 {(scope.Level == "A" ? "A" : "A 與 AA")} 共 {total} 條成功準則，這裡查其中 {here} 條，而且只查到 XAML 決定得了的部分。通過不等於符合規範。"));
    }

    public static string Manual(Lang lang) => lang.T(
        "This tool cannot do these. They are the ones that go wrong most in desktop software, not the rest of WCAG.",
        "這支查不到這些。是桌面軟體最常出問題的，不是 WCAG 剩下的全部。");

    /// <summary>每一份報告都要講的：看了什麼、沒看什麼。</summary>
    public const string Scope =
        "Static: the XAML was read, nothing was run; of the code, only type and property declarations and each XAML file's own code-behind (does it set a name, a content or a key handler?). Passing is not a claim of WCAG or EAA conformance.";
}

/// <summary>SARIF 2.1.0。依選的標準標條文。info 不輸出：不是缺陷，灌進 Security 分頁是噪音（JSON 裡照樣有）。</summary>
public static class SarifReport
{
    public static string Create(IReadOnlyList<Finding> reported, string toolVersion, Scope? chosen = null)
    {
        var scope = chosen ?? Scope.Default;
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
                                helpUri = k.Uri(),
                                help = new { text = $"{k.Help} ({scope.Title}: {scope.WcagOf(k)})" },
                                defaultConfiguration = new { level = Level(k.Default) },
                                properties = new { tags = scope.CriteriaOf(k).Select(c => "WCAG " + c).Prepend("accessibility"), clauses = JsonReport.Clauses(k, k.NameIsEmpty) },
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
    private const int ListMax = 20;

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int exitCode, int baselined, string? why, Lang? lang = null, IReadOnlyList<Finding>? known = null)
    {
        lang ??= Lang.En;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int Count(Severity s) => reported.Count(f => f.Severity == s);

        sb.AppendLine(lang.T("## XamlA11y — accessibility audit", "## XamlA11y 無障礙稽核")).AppendLine();
        var verdict = exitCode switch { 0 => lang.T("✅ **PASS**", "✅ **通過**"), 1 => lang.T("❌ **FAIL**", "❌ **沒通過**"), 3 => lang.T("⚠️ **NOT TRUSTWORTHY** (exit 3)", "⚠️ **結果不可信**（exit 3）"), _ => $"exit {exitCode.ToString(inv)}" };
        sb.AppendLine(lang.IsZh
            ? string.Create(inv, $"{verdict} · **{Count(Severity.Fail)} fail**、{Count(Severity.Warn)} warn、{Count(Severity.Info)} info · {r.Files} 個 XAML 檔、判了 {r.Judged} 處 · 可解析率 {r.Resolution:F1}% · {r.Scope.Title}")
            : string.Create(inv, $"{verdict} · **{Count(Severity.Fail)} fail**, {Count(Severity.Warn)} warn, {Count(Severity.Info)} info · {r.Judged} check(s) in {r.Files} XAML file(s) · resolution {r.Resolution:F1}% · {r.Scope.Title}")).AppendLine();

        if (Summary.KnownDebt(known, exitCode, lang) is { } debt) sb.AppendLine($"> {debt}").AppendLine();
        if (exitCode == 3)
            sb.AppendLine($"> ⚠️ **{lang.T("The result can't be trusted.", "結果不可信。")}** {Code(why ?? lang.T("see the console output of the run", "原因看這次執行的輸出"), table: false)}").AppendLine();
        foreach (var f in r.ParseErrors.Take(ListMax))
            sb.AppendLine($"> ⚠️ **{lang.T("Parse failed:", "讀不懂：")}** {Code(f, table: false)} — {lang.T("this file is missing from the audit.", "這個檔案沒有稽核到。")}").AppendLine();
        if (r.ParseErrors.Count > ListMax) sb.AppendLine(string.Create(inv, $"> ⚠️ {lang.T($"… and {r.ParseErrors.Count - ListMax} more file(s) that could not be parsed.", $"… 還有 {r.ParseErrors.Count - ListMax} 個檔案讀不懂。")}")).AppendLine();
        foreach (var i in r.InvalidIgnores.Take(ListMax)) sb.AppendLine($"> ⚠️ {Code(lang.Ignore(i), table: false)}").AppendLine();
        if (r.InvalidIgnores.Count > ListMax) sb.AppendLine(string.Create(inv, $"> ⚠️ {lang.T($"… and {r.InvalidIgnores.Count - ListMax} more xamla11y-ignore that do nothing.", $"… 還有 {r.InvalidIgnores.Count - ListMax} 個沒作用的 xamla11y-ignore。")}")).AppendLine();
        if (r.Blocked > 0)
        {
            sb.AppendLine(string.Create(inv, $"> ⚠️ **{r.Blocked} {lang.T("place(s) could not be judged:", "處判不出來：")}**"));
            var why5 = r.On.SelectMany(x => x.BlockedReasons).GroupBy(p => p.Key).Select(g => (g.Key, N: g.Sum(p => p.Value))).OrderByDescending(x => x.N).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            foreach (var (reason, n) in why5.Take(5))
                sb.AppendLine(string.Create(inv, $"> - {n} × {Code(lang.Reason(reason), table: false)}"));
            if (why5.Count > 5) sb.AppendLine(string.Create(inv, $"> - … {lang.T($"and {why5.Count - 5} more reason(s)", $"還有 {why5.Count - 5} 種原因")}"));
            if (r.UnknownControls.Count > 0)
                sb.AppendLine($"> - {lang.T("controls it does not know (declare them in `treatAs`):", "不認得的控制項（用 `treatAs` 宣告）：")} {string.Join(", ", r.UnknownControls.Take(8).Select(u => Code(string.Create(inv, $"{u.Element} ×{u.Count}"), table: false)))}{(r.UnknownControls.Count > 8 ? " …" : "")}");
            sb.AppendLine();
        }

        foreach (var severity in (ReadOnlySpan<Severity>)[Severity.Fail, Severity.Warn])
        {
            var group = reported.Where(f => f.Severity == severity).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine(string.Create(inv, $"### {(severity == Severity.Fail ? "Fail" : "Warn")} ({group.Count})")).AppendLine();
            Table(sb, group, r.Scope, lang);
        }
        var info = reported.Where(f => f.Severity == Severity.Info).ToList();
        if (info.Count > 0)
        {
            sb.AppendLine(string.Create(inv, $"<details><summary>{info.Count} {lang.T("info finding(s) (do not fail the build)", "筆 info（不會擋）")}</summary>")).AppendLine();
            Table(sb, info, r.Scope, lang);
            sb.AppendLine("</details>").AppendLine();
        }

        var notes = new List<string>();
        if (baselined > 0) notes.Add(string.Create(inv, $"{baselined} {lang.T("in the baseline", "筆在 baseline 裡")}"));
        if (r.NotJudged > 0) notes.Add(string.Create(inv, $"{r.NotJudged} {lang.T("not judged (data-bound names, template parts, list items…)", "處不判（名稱來自資料、範本零件、清單項目…）")}"));
        if (r.Suppressed > 0) notes.Add(string.Create(inv, $"{r.Suppressed} {lang.T("suppressed via xamla11y-ignore", "筆用 xamla11y-ignore 壓掉")}"));
        if (r.Foreign.Count > 0) notes.Add(string.Create(inv, $"{r.Foreign.Count} {lang.T("non-WPF XAML file(s)", "個不是 WPF 的 XAML")}"));
        if (r.Gaps.Excluded > 0) notes.Add(string.Create(inv, $"{r.Gaps.Excluded} {lang.T("file(s) excluded", "個檔案排除")}"));
        if (r.Gaps.Unreadable.Count > 0) notes.Add(string.Create(inv, $"{r.Gaps.Unreadable.Count} {lang.T("unreadable folder(s)", "個資料夾讀不到")}"));
        if (notes.Count > 0) sb.AppendLine($"**{lang.T("Not in the list above:", "上面沒列的：")}** {string.Join(" · ", notes)}").AppendLine();

        // 工具查不到的要跟結果放在一起，不然「通過」看起來像全部查過了
        var manual = ManualCheck.In(r.Scope);
        sb.AppendLine(string.Create(inv, $"<details><summary>{lang.T($"{manual.Count} checks a person has to do (the common ones)", $"要人檢查的 {manual.Count} 項（最常出問題的）")}</summary>")).AppendLine().AppendLine(Summary.Manual(lang)).AppendLine();
        sb.AppendLine($"| WCAG | {lang.T("Check it like this", "怎麼檢查")} |").AppendLine("|---|---|");
        foreach (var (check, criteria) in manual)
            sb.AppendLine($"| {string.Join(lang.T(", ", "、"), criteria.Select(c => $"{c.Number} {lang.Criterion(c)} ({c.Level})"))} | {lang.T(check.En, check.Zh)} |");
        sb.AppendLine().AppendLine("</details>").AppendLine();

        sb.AppendLine($"<sub>{lang.T("Generated by XamlA11y", "XamlA11y 產生")} · {lang.Scope}</sub>");
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void Table(StringBuilder sb, List<Finding> findings, Scope scope, Lang lang)
    {
        sb.AppendLine(lang.T("| Location | Kind | WCAG | Detail |", "| 位置 | 種類 | WCAG | 說明 |")).AppendLine("|---|---|---|---|");
        foreach (var f in findings.Take(RowsPerSection))
        {
            var clauses = scope.ClausesOf(f);
            var says = Nvda.Says(f, lang) is { } s ? $"<br>NVDA: {Code(s)}" : "";
            sb.AppendLine($"| {Code(f.Site)} | {(lang.IsZh ? $"{f.Kind.Slug}<br>{lang.Title(f.Kind)}" : f.Kind.Slug)} | {scope.WcagOf(f.Kind)}{(clauses.Count > 0 ? $"<br>{scope.Standard.Title}: {string.Join(", ", clauses)}" : "")} | {Code(Trunc(lang.Message(f.Message), 220))}{says} |");
        }
        sb.AppendLine();
        if (findings.Count > RowsPerSection)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"<sub>{lang.T($"… and {findings.Count - RowsPerSection} more — see the JSON report artifact.", $"… 還有 {findings.Count - RowsPerSection} 筆，看 JSON 報告。")}</sub>")).AppendLine();
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
