using System.Text.Json;
using KitX.ToolKit.Triggers;

namespace KitX.ToolKit.Perf.Benchmarks;

/// <summary>
/// Benchmark 5 · F6 BindingResolver。20 条 param 绑定混合路径（payload/obj/matrix/output），
/// mergedPacket ~2KB 嵌套 JSON。每轮 <c>Resolve</c> 连续 1,000 次（20 param × 1000 = 2 万次解析），
/// 报告每 param 解析成本。
/// </summary>
public static class BindingResolverBenchmark
{
    private const int ResolveCallsPerRound = 1000;
    private const int Rounds = 10;

    public static List<string> Run()
    {
        var packet = BuildPacket();
        var paramBindings = BuildParams();

        var m = Benchmark.Measure(1, Rounds, () =>
        {
            for (var i = 0; i < ResolveCallsPerRound; i++)
                _ = BindingResolver.Resolve(paramBindings, packet);
        }, perUnit: (double)ResolveCallsPerRound * paramBindings.Count);

        return [m.Row("BindingResolver", $"20 param × {ResolveCallsPerRound:n0} Resolve/轮 · ~2KB packet · 每 param")];
    }

    private static Dictionary<string, string?> BuildParams()
    {
        var p = new Dictionary<string, string?>(20)
        {
            ["a"] = "$payload.a",
            ["b"] = "$payload.obj.nested.list[0]",
            ["c"] = "$payload.matrix[2][1]",
            ["d"] = "$output.result.value",
            ["e"] = "$payload.obj.name",
            ["f"] = "$payload.list[1].deep.x",
            ["g"] = "$output.result.tags",
            ["h"] = "$payload.matrix[0][0]",
            ["i"] = "$payload.scalar",
            ["j"] = "$payload.obj.nested.deep[3]",
            ["k"] = "$output.flat",
            ["l"] = "$payload.matrix[1][2]",
            ["m"] = "$payload.empty",
            ["n"] = "$payload.list[0]",
            ["o"] = "$payload.obj.nested",
            ["p"] = "$output.result.count",
            ["q"] = "$payload.zh",
            ["r"] = "literal-value",
            ["s"] = null,
            ["t"] = "$payload.obj.nested.list[2]",
        };
        return p;
    }

    private static JsonElement BuildPacket()
    {
        var doc = JsonSerializer.SerializeToDocument(new
        {
            a = "alpha",
            idx = 7,
            scalar = 3.14,
            flag = true,
            empty = new string(' ', 0),
            zh = "中文字符串值",
            list = new object[]
            {
                new { name = "first", nested = 1 },
                new { name = "second", nested = 2 },
                new { name = "third", nested = 3 },
            },
            matrix = new object[][]
            {
                new object[] { 1, 2, 3 },
                new object[] { 4, 5, 6 },
                new object[] { 7, 8, 9 },
            },
            obj = new
            {
                name = "对象",
                nested = new
                {
                    list = new object[] { "x0", "x1", "x2", "x3", "x4" },
                    deep = new object[] { "d0", "d1", "d2", "d3", "d4" },
                },
            },
            pad = new string('中', 300),
            result = new
            {
                value = "v",
                count = 42,
                tags = "a,b,c",
            },
            flat = "flat-value",
        });
        return doc.RootElement.Clone();
    }
}
