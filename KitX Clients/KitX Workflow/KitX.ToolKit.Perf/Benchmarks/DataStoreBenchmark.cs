using KitX.ToolKit.Data;

namespace KitX.ToolKit.Perf.Benchmarks;

/// <summary>
/// Benchmark 3 · F1/F2 DataStore Set/Append/Wait。Set：K∈{1k,3k} 预置键 × W∈{10,50} 永不满足
/// 等待者，对 1 热键连 Set 10,000 次，测每 Set 成本。Append：上限 1000，单键 10,000 条 entry，
/// 预热到上限后测每条成本（含 K=3k 背景）。Wait：3k 键下 fast path + 10ms 唤醒。
/// </summary>
public static class DataStoreBenchmark
{
    private const int SetCount = 10_000;
    private const int AppendCount = 10_000;
    private const int Rounds = 10;

    private static readonly string HotValue = "{\"v\":\"" + new string('a', 40) + "\"}";
    private static readonly string AppendEntry = "{\"log\":\"" + new string('b', 80) + "\"}";

    public static List<string> Run()
    {
        var rows = new List<string>();

        foreach (var k in new[] { 1000, 3000 })
        foreach (var w in new[] { 10, 50 })
            rows.Add(SetScenario(k, w));

        rows.Add(AppendScenario("Append 每条 (单键, 无背景)", 0));
        rows.Add(AppendScenario("Append 每条 (K=3k 背景)", 3000));

        rows.AddRange(WaitScenarios());
        return rows;
    }

    private static string SetScenario(int k, int w)
    {
        // W persistent, never-satisfied waiters block the whole measurement window; the
        // hot key is Set 10,000x per round against a store with K preset keys.
        var store = new DataStore();
        for (var i = 0; i < k; i++)
            store.Set($"preset-{i}", "{\"v\":1}");

        var waiterKeys = Enumerable.Range(0, w).Select(i => $"never-{i}").ToArray();
        var waiterTasks = waiterKeys.Select(key => Task.Run(() => store.Wait([key]))).ToArray();

        var m = Benchmark.Measure(1, Rounds, () =>
        {
            for (var i = 0; i < AppendCount; i++)
                store.Set("hot", HotValue);
        }, perUnit: SetCount);

        foreach (var key in waiterKeys)
            store.Set(key, "x");
        Task.WaitAll(waiterTasks);

        return m.Row("DataStore Set", $"K={k} · W={w} · {SetCount:n0} Set · ~64B · 每 Set");
    }

    private static string AppendScenario(string scenario, int backgroundKeys)
    {
        var store = new DataStore(new DataStoreOptions { AppendLimit = 1000 });
        for (var i = 0; i < backgroundKeys; i++)
            store.Set($"bg-{i}", "{\"v\":1}");

        // Warm up to the ring-buffer limit so every measured append drops the oldest entry.
        for (var i = 0; i < 1000; i++)
            store.Append("log", AppendEntry);

        var m = Benchmark.Measure(1, Rounds, () =>
        {
            for (var i = 0; i < AppendCount; i++)
                store.Append("log", AppendEntry);
        }, perUnit: AppendCount);

        return m.Row("DataStore Append", $"{scenario} · 上限 1000 · ~100B · 每 Append");
    }

    private static List<string> WaitScenarios()
    {
        var rows = new List<string>();

        // Fast path: the awaited key already exists → returns immediately.
        var fast = new DataStore();
        for (var i = 0; i < 3000; i++)
            fast.Set($"k{i}", "{\"v\":1}");
        fast.Set("present", "{\"v\":1}");
        var fastM = Benchmark.Measure(1, Rounds, () =>
        {
            for (var i = 0; i < 100; i++)
                fast.Wait(["present"]);
        }, perUnit: 100);
        rows.Add(fastM.Row("DataStore Wait", "fast path · K=3k · 键已存在即返回 · 每 Wait"));

        // Slow path: the awaited key is Set ~10ms after the waiter registers (wake-up path).
        var slow = new DataStore();
        for (var i = 0; i < 3000; i++)
            slow.Set($"k{i}", "{\"v\":1}");

        var round = 0;
        var slowM = Benchmark.Measure(1, Rounds, () =>
        {
            var baseKey = $"r{round++}";
            var keys = Enumerable.Range(0, 100).Select(i => $"{baseKey}-later-{i}").ToArray();
            var setters = keys
                .Select(key => Task.Run(async () => { await Task.Delay(10); slow.Set(key, "{\"v\":1}"); }))
                .ToArray();
            foreach (var key in keys)
                slow.Wait([key]);
            Task.WaitAll(setters);
        }, perUnit: 100);
        rows.Add(slowM.Row("DataStore Wait", "K=3k · 10ms 后 Set 唤醒 · 每 Wait"));

        return rows;
    }
}
