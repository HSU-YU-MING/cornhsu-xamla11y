using Text = XamlA11y.Core.Report.Text;

namespace XamlA11y.Cli;

// 退出碼契約（三件套同一套）：0 通過 ／ 1 找到違規 ／ 2 用法、設定或輸入檔錯誤 ／ 3 結果不可信
public static class Program
{
    public static int Main(string[] args)
    {
        // 報告裡印的是使用者自己的介面文字；.NET 被導向時用系統字碼頁（繁中 Windows 是 Big5）會變亂碼
        try { Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
        catch (IOException) { }
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0) { Usage.Print(stdout); return 2; }
        switch (args[0])
        {
            case "-h" or "--help" or "help": Usage.Print(stdout); return 0;
            case "--version": stdout.WriteLine(Version()); return 0;
            case "audit" when args.Contains("--help") || args.Contains("-h"): Usage.Print(stdout); return 0;
            case "audit": return AuditCommand.Run(args[1..], stdout, stderr);
            case "rules": return Usage.Rules(args[1..], stdout, stderr);
            default:
                stderr.WriteLine($"unknown command: {Text.Safe(args[0])}");
                stderr.WriteLine(Directory.Exists(args[0]) ? "that is a folder: did you mean 'xamla11y audit <folder>'?" : "run 'xamla11y --help' for usage");
                return 2;
        }
    }

    public static string Version() =>
        typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion is { } v
            ? (v.IndexOf('+') > 0 ? v[..v.IndexOf('+')] : v)
            : "0.0.0-dev";
}
