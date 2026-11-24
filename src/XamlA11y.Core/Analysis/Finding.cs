using XamlA11y.Core.Xaml;

namespace XamlA11y.Core.Analysis;

public sealed record Finding(Kind Kind, Severity Severity, string File, int Line, string Element, string Message)
{
    /// <summary>認得出是哪一個元素的東西（x:Name、命令、處理器…），不含行號。</summary>
    public string Identity { get; init; } = "";

    /// <summary>同一個檔案裡同樣的鍵第幾個（1 起算）。</summary>
    public int Ordinal { get; set; } = 1;

    /// <summary>baseline 的鍵：不含行號 —— 行號會隨無關的編輯漂移，一次格式化就讓整份 baseline 失效。</summary>
    /// <remarks>降過級的（不能聚焦、停用）記「~」：之後變成能聚焦、升回 fail，就是新的一筆，不能被舊的蓋住。</remarks>
    public string BaselineKey => $"{Kind.Slug}{(Lowered ? "~" : "")}|{Escape(KeyFile ?? File)}|{(KeyFile is null ? Element : "")}|{Escape(Identity)}{(Ordinal > 1 ? "|#" + Ordinal : "")}";

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>名稱規則的：當成哪一個 WPF 控制項（NVDA 的念法要用）。別的規則是 null。</summary>
    public string? Control { get; init; }

    /// <summary>HelpText 或 ToolTip：NVDA 當描述念在型別後面。沒有是 ""；不確定（綁定、物件）是 null。</summary>
    public string? Description { get; init; }

    /// <summary>NVDA 會念的名稱（只有確定的：項目容器的型別名）；空名稱是 null。</summary>
    public string? SpokenName { get; init; }

    /// <summary>baseline 的鍵用這個、不用檔名：報在類別上的，多一個用它的檔案不能變成新的一筆。</summary>
    public string? KeyFile { get; init; }

    /// <summary>因為元素不能聚焦而降了一級。</summary>
    public bool Lowered { get; init; }

    /// <summary>名稱是空的。清單的亂碼名稱也報 list-no-name，但它不是空的。</summary>
    public bool NameIsEmpty => Kind.NameIsEmpty && !Unreadable;

    /// <summary>名稱有，只是念不出意思（清單與進度條沒有另外一種）。</summary>
    public bool Unreadable { get; init; }

    public string Site => $"{File}:{Line}";
}

/// <summary>候選的判定結果。</summary>
public enum Verdict
{
    Pass,
    Finding,
    /// <summary>該判得出來卻卡住了（第三方控制項、專案外的樣式）：進可解析率的分母。</summary>
    Blocked,
    /// <summary>靜態不可能知道，或不該判（綁定資料、範本零件）：不進分母，但要數出來。</summary>
    NotJudged,
    Suppressed,
}

/// <summary>一項檢查的數字。</summary>
public sealed record CheckStats(string Check)
{
    /// <summary>會算在這項檢查裡的每一種都關掉了（設定檔，或選的標準沒有這一條）。它的數字不進總數與可解析率。</summary>
    public bool Off { get; init; }

    public int Pass { get; set; }
    public int Findings { get; set; }
    public int Blocked { get; set; }
    public int NotJudged { get; set; }
    public int Suppressed { get; set; }
    public Dictionary<string, int> BlockedReasons { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> NotJudgedReasons { get; } = new(StringComparer.Ordinal);

    public int Candidates => Pass + Findings + Blocked + NotJudged + Suppressed;
    public double Resolution => Pass + Findings + Suppressed + Blocked == 0 ? 100 : 100.0 * (Pass + Findings + Suppressed) / (Pass + Findings + Suppressed + Blocked);
}

public sealed class AuditResult
{
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required IReadOnlyList<CheckStats> Checks { get; init; }
    public required int Files { get; init; }

    /// <summary>依哪個標準、哪個等級。</summary>
    public Scope Scope { get; init; } = Scope.Default;

    public required IReadOnlyList<string> ParseErrors { get; init; }
    public IReadOnlyDictionary<string, string> ParseWhy { get; init; } = new Dictionary<string, string>();
    public required IReadOnlyList<(string File, string Dialect)> Foreign { get; init; }
    public required IReadOnlyList<ThemeUse> Themes { get; init; }
    /// <summary>不認得的控制項 → 卡住幾個位置。</summary>
    public required IReadOnlyList<(string Element, int Count)> UnknownControls { get; init; }
    /// <summary>沒作用的 xamla11y-ignore 與原因（「檔:行: 為什麼」）：沒寫理由、或停在被註解掉的元素前面。</summary>
    public required IReadOnlyList<string> InvalidIgnores { get; init; }

    /// <summary>沒關掉的檢查。</summary>
    public IEnumerable<CheckStats> On => Checks.Where(r => !r.Off);

    public int Pass => On.Sum(r => r.Pass);
    public int Blocked => On.Sum(r => r.Blocked);
    public int NotJudged => On.Sum(r => r.NotJudged);
    public int Suppressed => On.Sum(r => r.Suppressed);
    public int Judged => On.Sum(r => r.Pass + r.Findings + r.Suppressed);
    public int Candidates => On.Sum(r => r.Candidates);

    /// <summary>
    /// 可解析率：判得出來的 ÷（判得出來的 + 卡住的）。「靜態不可能知道」的不在分母。
    /// 只看預設會擋的四項（findings.json 的 gate）：其他的預設不擋，也幾乎不會卡住，算進來會把名稱整批看不到的專案墊高過關。
    /// 關掉的檢查不算：不看的東西看不到，不是結果不可信。
    /// </summary>
    public double Resolution
    {
        get
        {
            var gate = On.Where(r => Kinds.IsGate(r.Check)).ToList();
            double judged = gate.Sum(r => r.Pass + r.Findings + r.Suppressed), blocked = gate.Sum(r => r.Blocked);
            return judged + blocked == 0 ? (Judged > 0 || (Blocked == 0 && NotJudged > 0) ? 100 : 0) : 100.0 * judged / (judged + blocked);
        }
    }

    public ScanGaps Gaps { get; init; } = new();

    /// <summary>每個候選的判定（Oracle 比對用；--json 不含）。</summary>
    public IReadOnlyList<Judgement> Judgements { get; init; } = [];
}

/// <param name="Element">XAML 元素。</param>
public sealed record Judgement(string Check, Verdict Verdict, System.Xml.Linq.XElement Element, string File, int Line, string? Reason, Kind? Kind);
