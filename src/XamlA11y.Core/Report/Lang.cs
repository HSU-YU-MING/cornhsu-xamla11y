using XamlA11y.Core.Analysis;

namespace XamlA11y.Core.Report;

/// <summary>報告的語言（主控台、Markdown、HTML）。JSON、SARIF、MSBuild 與錯誤訊息一律英文：給程式讀的不隨設定變。</summary>
public sealed record Lang(string Id)
{
    public static readonly Lang En = new("en");
    public static readonly Lang ZhHant = new("zh-Hant");

    public static Lang? ById(string id) => id switch { "en" => En, "zh-Hant" => ZhHant, _ => null };
    public static string Ids => "en, zh-Hant";

    public bool IsZh => Id == "zh-Hant";

    public string T(string en, string zh) => IsZh ? zh : en;

    public string Title(Kind k) => Words(k).Title;
    public string Help(Kind k) => Words(k).Help;
    public string CheckTitle(string check) => Kinds.Check(check) is var c && IsZh ? c.Zh : c.En;
    public string GroupTitle(GroupInfo g) => IsZh ? g.Zh : g.En;
    public string Criterion(Criterion c) => IsZh ? c.ZhName : c.Name;
    public string Message(string m) => IsZh ? Zh.Message(m) : m;
    public string Reason(string r) => IsZh ? Zh.Reason(r) : r;
    public string Ignore(string s) => IsZh ? Zh.Ignore(s) : s;
    public string Scope => IsZh ? Zh.ScopeText : Summary.Scope;

    private KindText Words(Kind k) => IsZh ? k.Zh : k.En;
}
