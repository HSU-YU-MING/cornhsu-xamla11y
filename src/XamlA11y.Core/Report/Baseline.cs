using System.Globalization;
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

    /// <summary>
    /// 只拿掉不存在的，不加新的。<paramref name="keep"/>（種類、檔案、元素）說要留的照留：
    /// 沒看到不等於修好 —— 關掉的種類、讀不懂或排除的檔案、變成「不知道」的。
    /// </summary>
    public Baseline Prune(IReadOnlyList<Finding> findings, DateOnly date, Func<string, string, string, bool> keep)
    {
        var present = findings.Select(f => f.BaselineKey).ToHashSet(StringComparer.Ordinal);
        return new Baseline
        {
            GeneratedOn = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Known = [.. Known.Where(k => present.Contains(k) || keep(Part(k, 0).TrimEnd('~'), Part(k, 1), Part(k, 2)))],
        };
    }

    /// <summary>鍵的第幾段（種類、檔名、元素），跳脫的還原。</summary>
    private static string Part(string key, int index)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0, part = 0; i < key.Length; i++)
        {
            if (key[i] == '|') { if (++part > index) break; continue; }
            if (key[i] == '\\' && i + 1 < key.Length) i++;
            if (part == index) sb.Append(key[i]);
        }
        return sb.ToString();
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
