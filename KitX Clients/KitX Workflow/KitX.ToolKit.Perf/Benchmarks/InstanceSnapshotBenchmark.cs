using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Perf.Fakes;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Perf.Benchmarks;

/// <summary>
/// Benchmark 4 · F7 实例快照全量物化。真实 manager：合成挂载 40 个 Toolkit（各 1 Manual 触发器 +
/// 1 最小工作流引用），共 Spawn 300 实例并等全部 Completed，再每轮读 <c>manager.Instances</c>
/// 100 次，报告每次 getter 物化耗时。
/// </summary>
public static class InstanceSnapshotBenchmark
{
    private const int ToolkitCount = 40;
    private const int TargetInstances = 300;
    private const int ReadsPerRound = 100;
    private const int Rounds = 10;

    public static List<string> Run()
    {
        var store = new DataStore();
        var executor = new InstantExecutor(store, writeOutput: false);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        try
        {
            for (var t = 0; t < ToolkitCount; t++)
                manager.Mount(BuildToolkit(t));

            // Spawn 7-8 instances per toolkit to reach ~300, then wait for all Completed.
            var completed = new TaskCompletionSource();
            var done = 0;
            manager.BenchEvent += (_, e) =>
            {
                if (e is InstanceCompletedEvent && Interlocked.Increment(ref done) == TargetInstances)
                    completed.TrySetResult();
            };

            var spawnCount = 0;
            for (var t = 0; t < ToolkitCount; t++)
            {
                var per = t % 2 == 0 ? 8 : 7; // 20×8 + 20×7 = 300
                for (var i = 0; i < per && spawnCount < TargetInstances; i++)
                {
                    var id = manager.Spawn($"perf-tk-{t:00}", "manual");
                    if (id is null)
                        throw new InvalidOperationException($"Spawn 失败 toolkit={t}");
                    spawnCount++;
                }
            }

            if (!completed.Task.Wait(TimeSpan.FromSeconds(60)))
                throw new TimeoutException($"{spawnCount} 个实例未在 60s 内全部 Completed");

            long sum = 0;
            var m = Benchmark.Measure(1, Rounds, () =>
            {
                for (var i = 0; i < ReadsPerRound; i++)
                    sum += manager.Instances.Count;
            }, perUnit: ReadsPerRound);
            _ = sum;

            return [m.Row("实例快照物化", $"{TargetInstances} 实例 × {ToolkitCount} Toolkit · 每 getter")];
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static Toolkit BuildToolkit(int t)
    {
        var id = $"perf-tk-{t:00}";
        return new Toolkit
        {
            Id = id,
            Meta = new ToolkitMeta { Name = id },
            Workflows = { new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" } },
            Triggers =
            {
                new Trigger
                {
                    Id = "manual",
                    Type = TriggerType.Manual,
                    Bindings = { new TriggerBinding { Workflow = "wf" } },
                },
            },
        };
    }
}
