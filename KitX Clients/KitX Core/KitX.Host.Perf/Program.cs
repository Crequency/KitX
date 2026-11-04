using System.Text;
using KitX.Host.Perf.Benchmarks;

namespace KitX.Host.Perf;

/// <summary>
/// KitX.Host.Perf 微基准入口：顺序跑四项基准（G1 消息解析链 / G2 Log 集合 / G6 活动日志读取 /
/// G5 ConfigValidator），控制台输出 markdown 结果表，并写入
/// <c>results/baseline.md</c>（results 目录不入 git）。
/// </summary>
/// <remarks>
/// G3（RefreshInstances 全量重建）已移除：它测的是 <c>PanelHostViewModel</c>，而面板宿主在
/// ToolKit 重构 S5.5 中已归档，不再参与编译。
/// </remarks>
public static class Program
{
    private sealed record Result(string Name, List<string> Rows);

    public static int Main()
    {
        var results = new List<Result>();
        results.Add(Run("1 · 消息解析链（G1）", MessageParsingBenchmark.Run));
        results.Add(Run("2 · Log 集合追加（G2）", LogCollectionBenchmark.Run));
        results.Add(Run("3 · 活动日志读取（G6）", ActivityLogBenchmark.Run));
        results.Add(Run("4 · ConfigValidator 单次校验（G5）", ConfigValidatorBenchmark.Run));

        var md = BuildMarkdown(results);
        Console.WriteLine();
        Console.WriteLine(md);

        var resultsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "results"));
        Directory.CreateDirectory(resultsDir);
        File.WriteAllText(Path.Combine(resultsDir, "baseline.md"), md, new UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine($"[结果已写入] {Path.Combine(resultsDir, "baseline.md")}");

        return 0;
    }

    private static Result Run(string name, Func<List<string>> body)
    {
        Console.WriteLine($"[{name}] 开始…");
        try
        {
            var rows = body();
            Console.WriteLine($"[{name}] 完成（{rows.Count} 行）");
            return new Result(name, rows);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{name}] BLOCKED: {ex.Message}");
            return new Result(name, [$"| {name} | BLOCKED: {Flatten(ex.Message)} | — | — | — |"]);
        }
    }

    private static string BuildMarkdown(List<Result> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| 基准 | 场景 | mean | P50 | P99 |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in results)
            foreach (var row in r.Rows)
                sb.AppendLine(row);
        return sb.ToString();
    }

    private static string Flatten(string msg) => msg.Replace("|", "/").Replace("\n", " ").Replace("\r", " ");
}
