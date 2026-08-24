using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.ToolKit.Perf.Fakes;

namespace KitX.ToolKit.Perf.Benchmarks;

/// <summary>
/// Benchmark 2 · F4/F5 节点完成开销。内存合成 100 节点链 A1→A2→…→A100，根为 Manual，
/// payload ~1KB，共享 DataStore 预置 3,000 个无关命名空间键。每轮重建 DataStore + 调度器，
/// 整轮计时后按节点数平均，报告每节点完成开销。
/// </summary>
public static class SchedulerNodeOverheadBenchmark
{
    private const int NodeCount = 100;
    private const int FillerKeyCount = 3000;
    private const int Rounds = 10;

    public static List<string> Run()
    {
        var toolkit = BuildChainToolkit();

        var rows = new List<string>();
        var measurement = Benchmark.Measure(2, Rounds, () =>
        {
            var store = new DataStore();
            Preload(store, FillerKeyCount);
            var executor = new InstantExecutor(store);
            using var scheduler = new BenchScheduler(toolkit, executor, store,
                new ToolkitFileStore(Path.GetTempPath()));

            var tcs = new TaskCompletionSource();
            scheduler.RunCompleted += (_, _) => tcs.TrySetResult();
            scheduler.StartRun("manual", BuildPayload(), Initiator.Unknown, null, null);

            if (!tcs.Task.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("调度器 100 节点链未在 30s 内完成");
        }, perUnit: NodeCount);

        rows.Add(measurement.Row("节点完成开销", "100 节点链 A1→A100 · 3k 键 · 每节点 5×1KB 输出 · 每节点"));
        return rows;
    }

    private static Toolkit BuildChainToolkit()
    {
        var workflows = new List<ToolkitWorkflow>(NodeCount);
        for (var i = 1; i <= NodeCount; i++)
            workflows.Add(new ToolkitWorkflow { Id = $"A{i}", Name = $"A{i}", File = $"a{i}.kcs" });

        var triggers = new List<Trigger>
        {
            new() { Id = "manual", Type = TriggerType.Manual, Bindings = { new() { Workflow = "A1" } } },
        };
        for (var i = 1; i < NodeCount; i++)
        {
            triggers.Add(new Trigger
            {
                Id = $"e{i}",
                Type = TriggerType.WorkflowCompletion,
                Config = new TriggerConfig { From = $"A{i}" },
                Bindings = { new() { Workflow = $"A{i + 1}" } },
            });
        }

        return new Toolkit
        {
            Id = "chain-perf",
            Meta = new ToolkitMeta { Name = "chain-perf" },
            Workflows = workflows,
            Triggers = triggers,
        };
    }

    private static object BuildPayload()
    {
        // ~1KB nested JSON object.
        return new
        {
            a = new string('x', 200),
            b = Enumerable.Range(0, 5).Select(i => new { id = i, v = new string('y', 40) }).ToArray(),
            c = new { n = 42, s = "中文字符串" },
            pad = new string('z', 400),
        };
    }

    private static void Preload(DataStore store, int count)
    {
        var i = 0;
        for (var a = 0; a < 10 && i < count; a++)
        for (var b = 0; b < 10 && i < count; b++)
        for (var c = 0; c < 30 && i < count; c++)
        {
            store.Set($"filler-toolkit-{a:000}/inst-{b}/wf/wf-{c}/out", "{\"v\":1}");
            i++;
        }
    }
}
