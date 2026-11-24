using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary><c>--json</c>。summary 與 findings 分兩層：CI 只讀 findings 的話，「哪些看不到」等於不存在。</summary>
public static class JsonReport
{
    public const int SchemaVersion = 1;

    public static string Create(AuditResult r, IReadOnlyList<Finding> reported, int baselined, string toolVersion, int exitCode, ReportInfo? info = null) => JsonSerializer.Serialize(new
    {
        schemaVersion = SchemaVersion,
        toolVersion,
        mode = "audit",
        project = info?.Name,
        date = info?.DateText,
        commit = info?.Commit,
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
            resolutionJudged = r.GateJudged,
            resolutionUnknown = r.GateBlocked,
            checks = r.Checks.Select(s => new
            {
                check = s.Check,
                title = Kinds.Check(s.Check).En,
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
            unusedIgnores = r.UnusedIgnores,
            builtInCode = new { controls = r.CodeBuilt.Controls, mouseHandlers = r.CodeBuilt.MouseHandlers },
            groups = Kinds.Groups.ToDictionary(g => g.Id, g => r.Findings.Count(f => f.Kind.Group == g.Id), StringComparer.Ordinal),
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
        suppressed = r.Suppressions.Select(s => new { kind = s.Kind.Slug, site = $"{s.File}:{s.Line}", file = s.File, line = s.Line, element = s.Element, reason = s.Reason }),
        findings = reported.Select(f => new
        {
            kind = f.Kind.Slug,
            group = f.Kind.Group,
            check = f.Kind.Check,
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
