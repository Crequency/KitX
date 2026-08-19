using System.Text;
using KitX.ToolKit.Perf.Benchmarks;

namespace KitX.ToolKit.Perf;

/// <summary>
/// KitX.ToolKit.Perf 微基准入口：顺序跑五项基准，控制台输出 markdown 结果表，并写入
/// <c>results/baseline.md</c>（results 目录不入 git）。
/// </summary>
public static class Program
{
    private sealed record Result(string Name, List<string> Rows);

    public static int Main()
    {
        var results = new List<Result>();
        results.Add(Run("1 · PluginEvent 路由", PluginEventRoutingBenchmark.Run));
        results.Add(Run("2 · 节点完成开销", SchedulerNodeOverheadBenchmark.Run));
        results.Add(Run("3 · DataStore Set/Append/Wait", DataStoreBenchmark.Run));
        results.Add(Run("4 · 实例快照物化", InstanceSnapshotBenchmark.Run));
        results.Add(Run("5 · BindingResolver", BindingResolverBenchmark.Run));

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
