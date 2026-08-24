using System.Reflection;
using KitX.Dashboard.ViewModels;
using KitX.Host.Perf.Fakes;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;

namespace KitX.Host.Perf.Benchmarks;

/// <summary>
/// G3 · PanelHost RefreshInstances 全量重建（300 实例 × 100 次）。
///
/// 路径：直构 <see cref="PanelHostViewModel"/>（fake IToolkitService 提供 300 条
/// InstanceSnapshot），绕过 OnBenchEvent 的 Dispatcher.Post，用反射直接同步调私有
/// <c>RefreshInstances()</c> 测全量重建成本。构造不触发 Avalonia Dispatcher（ReactiveUI
/// RaiseAndSetIfChanged 在无 UI 线程下安全），故可直构。
/// </summary>
public static class RefreshInstancesBenchmark
{
    private const int InstanceCount = 300;
    private const int ToolkitCount = 10;

    public static List<string> Run()
    {
        var (toolkits, instances) = BuildData();
        var vm = new PanelHostViewModel(
            new FakeToolkitService(toolkits, instances),
            new FakeBenchService(),
            new FakePanelRuntime(),
            new FakeEventService());

        var refresh = typeof(PanelHostViewModel).GetMethod(
            "RefreshInstances", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RefreshInstances 私有方法未找到");

        var m = Benchmark.Measure(3, 100, () => refresh.Invoke(vm, null));

        return
        [
            m.Row("G3 RefreshInstances 全量重建", $"{InstanceCount} 实例 × {ToolkitCount} 分组，× 100 次"),
        ];
    }

    private static (List<Toolkit> toolkits, List<InstanceSnapshot> instances) BuildData()
    {
        var toolkits = new List<Toolkit>(ToolkitCount);
        var instances = new List<InstanceSnapshot>(InstanceCount);
        for (var t = 0; t < ToolkitCount; t++)
        {
            var tkId = "tk-" + t;
            toolkits.Add(new Toolkit { Id = tkId, Meta = new ToolkitMeta { Name = "Toolkit " + t } });
            for (var i = 0; i < InstanceCount / ToolkitCount; i++)
            {
                instances.Add(new InstanceSnapshot(
                    $"inst-{t}-{i}", tkId, "manual", Initiator.Unknown,
                    InstanceStatus.Running, DateTimeOffset.UtcNow, null,
                    ActiveRuns: 1, CompletedRuns: 2, FailedRuns: 0));
            }
        }
        return (toolkits, instances);
    }
}
