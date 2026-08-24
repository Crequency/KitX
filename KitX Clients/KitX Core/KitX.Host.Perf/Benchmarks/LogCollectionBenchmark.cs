using System.Collections.ObjectModel;
using System.Text;

namespace KitX.Host.Perf.Benchmarks;

/// <summary>
/// G2 · Log 集合追加（数据层对比）：无界 <see cref="ObservableCollection{T}"/> 追加 10 万条
/// vs 环形（List 超 1000 移除头部）追加 10 万条。指标：ms/万条 + 分配量级
/// （<see cref="GC.GetAllocatedBytesForCurrentThread"/> 粗估）。UI Post/渲染不进本基准
/// （由功能测试覆盖）。
/// </summary>
public static class LogCollectionBenchmark
{
    private const int AppendCount = 100_000;
    private const int RingCap = 1000;

    public static List<string> Run()
    {
        // 预热 + 各 10 轮计时（每轮 10 万条）。
        var unbounded = Benchmark.Measure(2, 10, () => UnboundedAppend(), perUnit: 10_000);
        var ring = Benchmark.Measure(2, 10, () => RingAppend(), perUnit: 10_000);

        var unboundedAlloc = MeasureAllocation(UnboundedAppend);
        var ringAlloc = MeasureAllocation(RingAppend);

        return
        [
            unbounded.Row("G2 Log 无界集合追加", $"{AppendCount} 条（~100B/条），× 10 轮"),
            ring.Row($"G2 Log 环形集合追加（上限 {RingCap}）", $"{AppendCount} 条，× 10 轮"),
            $"| G2 Log 分配粗估 | 无界 {AppendCount} 条 | {FormatBytes(unboundedAlloc)} | — | — |",
            $"| G2 Log 分配粗估 | 环形 {AppendCount} 条 | {FormatBytes(ringAlloc)} | — | — |",
        ];
    }

    private static void UnboundedAppend()
    {
        var col = new ObservableCollection<string>();
        for (var i = 0; i < AppendCount; i++)
            col.Add(Line(i));
    }

    private static void RingAppend()
    {
        var list = new List<string>(RingCap + 1);
        for (var i = 0; i < AppendCount; i++)
        {
            if (list.Count >= RingCap)
                list.RemoveAt(0);
            list.Add(Line(i));
        }
    }

    private static long MeasureAllocation(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Line(int i) => $"2026-08-19 12:00:00.000 [INFO] log line number {i} with some payload text ~100 bytes";

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:0.##} MB";
        if (bytes >= 1000)
            return $"{bytes / 1000.0:0.##} KB";
        return $"{bytes} B";
    }
}
