using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlA11y.Core.Analysis;

/// <summary>
/// <c>xamla11y.config.json</c>。⚠ 寫錯要 exit 2 並指出是哪一個欄位，不能靜默忽略：
/// 打錯字的設定被跳過，使用者會以為它生效了（三件套同一條規矩）。
/// </summary>
public sealed class A11yConfig
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultFileName = "xamla11y.config.json";

    /// <summary>編輯器用的 JSON schema 位址，工具不看。</summary>
    [JsonPropertyName("$schema")] public string? Schema { get; set; }

    [JsonPropertyName("schemaVersion")] public int? SchemaVersion { get; set; }

    /// <summary>相對於掃描根的路徑片段；比對不分大小寫。</summary>
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }

    [JsonPropertyName("minCoverage")] public double? MinCoverage { get; set; }

    /// <summary>
    /// 「把 A 當成 WPF 的 B」：<c>{ "ExtendedButton": "Button" }</c>。跟 XamlFit 的 treatAs 同一個意思，
    /// 但這裡只影響「名稱怎麼來、是不是互動元件」。鍵與值都是區域名稱（不含命名空間前綴）。
    /// </summary>
    [JsonPropertyName("treatAs")] public Dictionary<string, string>? TreatAs { get; set; }

    /// <summary>每一種 finding 的嚴重度：<c>{ "not-in-tab-order": "off", "symbol-name": "warn" }</c>。</summary>
    [JsonPropertyName("rules")] public Dictionary<string, string>? Rules { get; set; }

    /// <summary>外部佈景在 FocusVisualStyle 關掉之後自己畫焦點（你在畫面上確認過）。</summary>
    [JsonPropertyName("themeShowsFocus")] public bool? ThemeShowsFocus { get; set; }

    public sealed record Problem(string Field, string Message)
    {
        public override string ToString() => $"{Field}: {Message}";
    }

    public static (A11yConfig? Config, IReadOnlyList<Problem> Problems) Load(string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, [new Problem(path, $"cannot be read: {ex.Message}")]); }
        return Parse(text, path);
    }

    public static (A11yConfig? Config, IReadOnlyList<Problem> Problems) Parse(string text, string path = DefaultFileName)
    {
        A11yConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<A11yConfig>(text, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            });
        }
        catch (JsonException ex) { return (null, [new Problem(path, ex.Message)]); }
        if (config is null) return (null, [new Problem(path, "the file is empty")]);

        var problems = new List<Problem>();
        if (config.SchemaVersion is { } v && (v > CurrentSchemaVersion || v < 1))
            problems.Add(new Problem("schemaVersion", v > CurrentSchemaVersion
                ? $"{v} is newer than this tool understands ({CurrentSchemaVersion}); upgrade xamla11y"
                : "must be 1"));

        // NaN 對任何比較都是 false，範圍檢查擋不住它
        if (config.MinCoverage is { } cov && (!double.IsFinite(cov) || cov < 0 || cov > 100))
            problems.Add(new Problem("minCoverage", "must be a number between 0 and 100"));

        for (var i = 0; i < (config.Exclude?.Count ?? 0); i++)
            if (string.IsNullOrWhiteSpace(config.Exclude![i])) problems.Add(new Problem($"exclude[{i}]", "must not be empty"));

        foreach (var (from, to) in config.TreatAs ?? [])
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from.Contains(':') || to.Contains(':'))
                problems.Add(new Problem($"treatAs[\"{from}\"]", "both names are local element names, without a namespace prefix"));
            else if (!Wpf.IsKnownType(to))
                problems.Add(new Problem($"treatAs[\"{from}\"]", $"'{to}' is not a WPF control this tool knows"));
        }

        foreach (var (slug, severity) in config.Rules ?? [])
        {
            if (Kinds.BySlug(slug) is null)
                problems.Add(new Problem($"rules[\"{slug}\"]", $"is not a rule; the rules are {string.Join(", ", Kinds.All.Select(k => k.Slug))}"));
            if (ParseSeverity(severity) is null)
                problems.Add(new Problem($"rules[\"{slug}\"]", "must be off, info, warn or fail"));
        }
        return (config, problems);
    }

    public static Severity? ParseSeverity(string? s) => s switch
    {
        "off" => Severity.Off,
        "info" => Severity.Info,
        "warn" => Severity.Warn,
        "fail" => Severity.Fail,
        _ => null,
    };

    public IReadOnlyDictionary<string, Severity> Severities =>
        (Rules ?? []).Where(r => ParseSeverity(r.Value) is not null).ToDictionary(r => r.Key, r => ParseSeverity(r.Value)!.Value, StringComparer.Ordinal);
}
