// ─────────────────────────────────────────────────────────────────────────────
// F4 / F5 packet-construction equivalence + key-set + scenario regression tests.
//
// F5 replaced `JsonNode.Parse(GetRawText())` re-parsing with reference wrapping
// (JsonObject.Create / JsonArray.Create / JsonValue.Create dispatched on the
// element's ValueKind), so the old (JsonNode.Parse) BuildOutputPacket / Merge
// logic is copied below as reference implementations and the new implementations
// are asserted to be byte-for-byte identical on `GetRawText()` across a demanding
// payload corpus. This pins the property that the reference wrapper writes through
// the element's original JSON text (numbers, escapes, unicode) instead of
// reformatting it.
//
// F4 replaced the full-key `_dataStore.Keys()` scan with the prefix-scoped
// `KeysByPrefix`, so the key-set test preloads thousands of unrelated keys and
// asserts only the target namespace's local keys survive.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class BenchSchedulerPacketEquivalenceTests
{
    // ── new (F5) implementations under test ──────────────────────────────────
    // Mirrors BenchScheduler.WrapElement: a JsonValue.Create(JsonElement) alone only
    // accepts primitives, so objects/arrays dispatch to their reference factories.
    private static JsonNode WrapElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => JsonObject.Create(element),
        JsonValueKind.Array => JsonArray.Create(element),
        _ => JsonValue.Create(element)!,
    };

    private static JsonElement NewMerge(JsonElement first, JsonElement second)
    {
        var obj = new JsonObject();

        if (first.ValueKind == JsonValueKind.Object)
            foreach (var prop in first.EnumerateObject())
                obj[prop.Name] = WrapElement(prop.Value);

        if (second.ValueKind == JsonValueKind.Object)
            foreach (var prop in second.EnumerateObject())
                obj[prop.Name] = WrapElement(prop.Value);

        return JsonSerializer.SerializeToElement(obj);
    }

    private static JsonElement NewBuildPacket(DataStore store, string prefix, JsonElement inputPacket)
    {
        var obj = new JsonObject();

        if (inputPacket.ValueKind == JsonValueKind.Object)
            foreach (var prop in inputPacket.EnumerateObject())
                obj[prop.Name] = WrapElement(prop.Value);

        foreach (var key in store.KeysByPrefix(prefix))
        {
            if (store.Get(key) is not { } value)
                continue;
            var local = key[prefix.Length..];
            if (!string.IsNullOrEmpty(local))
                obj[local] = WrapElement(value);
        }

        return JsonSerializer.SerializeToElement(obj);
    }

    // ── old (JsonNode.Parse) reference implementations ───────────────────────
    // The old BuildOutputPacket scans `Keys()`; the reference here uses the same
    // `KeysByPrefix` enumeration as NewBuildPacket so the ONLY difference under
    // test is the per-value wrapping (JsonValue.Create vs JsonNode.Parse).
    private static JsonElement OldMerge(JsonElement first, JsonElement second)
    {
        var obj = new JsonObject();

        if (first.ValueKind == JsonValueKind.Object)
            foreach (var prop in first.EnumerateObject())
                obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());

        if (second.ValueKind == JsonValueKind.Object)
            foreach (var prop in second.EnumerateObject())
                obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());

        return JsonSerializer.SerializeToElement(obj);
    }

    private static JsonElement OldBuildPacket(DataStore store, string prefix, JsonElement inputPacket)
    {
        var obj = inputPacket.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(inputPacket.GetRawText())?.AsObject() ?? new JsonObject()
            : new JsonObject();

        foreach (var key in store.KeysByPrefix(prefix))
        {
            if (store.Get(key) is not { } value)
                continue;
            var local = key[prefix.Length..];
            if (!string.IsNullOrEmpty(local))
                obj[local] = JsonNode.Parse(value.GetRawText());
        }

        return JsonSerializer.SerializeToElement(obj);
    }

    private static JsonElement El(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // ── item 1 · byte-for-byte equivalence (new vs old) ──────────────────────

    public static IEnumerable<object[]> MergeCases()
    {
        yield return new object[]
        {
            """{"a":{"b":{"c":{"d":{"e":[1,2,{"f":"g"}]}}}}}""",
            """{}""",
        };
        yield return new object[]
        {
            """{"中文":"值","嵌套":{"数组":[{"id":1,"txt":"中文字符串"},{"id":2,"v":null}],"空":{}}}""",
            """{"顶层":"ok","flag":true}""",
        };
        yield return new object[]
        {
            """{"e":{},"a":[],"n":null}""",
            """{"x":1}""",
        };
        yield return new object[]
        {
            """{"t":true,"f":false}""",
            """{"t":false}""",
        };
        yield return new object[]
        {
            """{"big":9007199254740993}""",
            """{}""",
        };
        yield return new object[]
        {
            """{"d":0.30000000000000004}""",
            """{}""",
        };
        yield return new object[]
        {
            """{"s":1e-7}""",
            """{}""",
        };
        yield return new object[]
        {
            """{"esc":"quote:\" backslash:\\ newline:\n tab:\t uni:\u4e2d"}""",
            """{}""",
        };
        yield return new object[]
        {
            """{"shared":{"a":1,"b":{"deep":[1,2,3]}}}""",
            """{"shared":"replaced"}""",
        };
        yield return new object[]
        {
            """{"k":{"deep":[1,2]}}""",
            """{"k":{"other":true}}""",
        };
        yield return new object[]
        {
            """{"name":"KitX 工作流引擎","meta":{"version":"10.0.0","features":["dataflow","join","fanout"]},"nodes":[{"id":0,"kind":"map","cfg":{"expr":"x*2+1","note":"中文注释"}},{"id":1,"kind":"join","cfg":{"edges":["A","B"]}}],"padding":{"nested":{"array":[{"k":"v"},{"k":null},true,false,3.14159265358979]},"tail":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"}}""",
            """{"name":"KitX","meta":{"version":"9.9.9"},"nodes":[{"id":2,"kind":"reduce","cfg":{}}]}""",
        };
    }

    [Theory]
    [MemberData(nameof(MergeCases))]
    public void Merge_New_Is_Byte_Equal_To_Old(string firstJson, string secondJson)
    {
        var newResult = NewMerge(El(firstJson), El(secondJson)).GetRawText();
        var oldResult = OldMerge(El(firstJson), El(secondJson)).GetRawText();

        Assert.Equal(oldResult, newResult);
    }

    [Fact]
    public void Merge_Second_Overrides_First_With_Deep_Value_Replaced()
    {
        // The overwritten first value is a deep object; after override it must be
        // fully replaced by the second value (no residue of the discarded subtree).
        var merged = NewMerge(
            El("""{"k":{"deep":[1,2],"nested":{"x":true}}}"""),
            El("""{"k":"replaced"}"""));
        Assert.Equal("""{"k":"replaced"}""", merged.GetRawText());
    }

    [Fact]
    public void Merge_Preserves_Number_Raw_Text_Exactly()
    {
        // F5 must write through the element's original number text, not reformat it:
        // a long beyond double precision, a decimal literal, and scientific notation.
        var merged = NewMerge(
            El("""{"big":9007199254740993,"d":0.30000000000000004,"s":1e-7}"""),
            El("{}"));

        var txt = merged.GetRawText();
        Assert.Contains("9007199254740993", txt);
        Assert.Contains("0.30000000000000004", txt);
        Assert.Contains("1e-7", txt);
    }

    [Fact]
    public void BuildOutputPacket_New_Is_Byte_Equal_To_Old()
    {
        var store = new DataStore();
        const string prefix = "tk/inst/wf/wfA/";
        store.Set(prefix + "result", "hello");
        store.Set(prefix + "nested", new { a = 1, b = new[] { 1, 2, 3 } });
        store.Set(prefix + "sci", El("1e-7"));
        store.Set(prefix + "big", El("9007199254740993"));
        store.Set(prefix + "zh", "中文字符串");

        var input = El("""{"incoming":{"deep":[{"k":true}]}}""");

        var newResult = NewBuildPacket(store, prefix, input).GetRawText();
        var oldResult = OldBuildPacket(store, prefix, input).GetRawText();

        Assert.Equal(oldResult, newResult);
        // And the incoming props are expanded first, DataStore keys appended.
        var newObj = NewBuildPacket(store, prefix, input);
        Assert.True(newObj.TryGetProperty("incoming", out _));
        Assert.Equal("hello", newObj.GetProperty("result").GetString());
        Assert.Equal("中文字符串", newObj.GetProperty("zh").GetString());
    }

    [Fact]
    public void BuildOutputPacket_NonObject_Input_Yields_Empty_Object()
    {
        var store = new DataStore();
        // No keys under this prefix → must still produce an empty JSON object, not null.
        var result = NewBuildPacket(store, "tk/none/", El("null"));
        Assert.Equal("{}", result.GetRawText());
    }

    // ── item 2 · key-set correctness under 3k unrelated keys ─────────────────

    [Fact]
    public void BuildOutputPacket_Only_Includes_Target_Namespace_Keys()
    {
        var store = new DataStore();
        const string prefix = "tk/inst/wf/wfA/";

        // Preload 3,000 unrelated keys in other namespaces (mirrors the perf harness).
        var i = 0;
        for (var a = 0; a < 10 && i < 3000; a++)
        for (var b = 0; b < 10 && i < 3000; b++)
        for (var c = 0; c < 30 && i < 3000; c++)
        {
            store.Set($"filler-t{a:000}/inst-{b}/wf/wf-{c}/out", $"{{\"v\":{i}}}");
            i++;
        }

        // Target namespace keys, including one whose local name is empty (must be skipped).
        store.Set(prefix + "result", "hello");
        store.Set(prefix + "obj", new { x = 1 });
        store.Set(prefix + "arr", new[] { 1, 2, 3 });
        store.Set(prefix, "ignored-empty-local");

        var packet = NewBuildPacket(store, prefix, El("{}"));

        // Only the three non-empty-local target keys are present (the empty-local one is skipped).
        var names = packet.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(["arr", "obj", "result"], names);
        Assert.Equal("hello", packet.GetProperty("result").GetString());
        Assert.Equal(1, packet.GetProperty("obj").GetProperty("x").GetInt32());
        Assert.Equal(3, packet.GetProperty("arr").GetArrayLength());
        Assert.Equal(3004, store.Keys().Count()); // 3000 fillers + 4 target-namespace keys
    }

    // ── item 3 · scenario regression: real scheduler, AND-join + fan-out ─────

    private static Toolkit ScenarioToolkit()
    {
        return new Toolkit
        {
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "B", Name = "B", File = "b.kcs" },
                new ToolkitWorkflow { Id = "C", Name = "C", File = "c.kcs" },
                new ToolkitWorkflow { Id = "D", Name = "D", File = "d.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "manual", Type = TriggerType.Manual,
                    Bindings = [new() { Workflow = "A" }, new() { Workflow = "B" }] },
                new Trigger { Id = "eA1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                    Bindings = [new() { Workflow = "C" }] },
                new Trigger { Id = "eB1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "B" },
                    Bindings = [new() { Workflow = "C" }] },
                new Trigger { Id = "eA2", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                    Bindings = [new() { Workflow = "D" }] },
            ],
        };
    }

    /// <summary>A scripted executor that writes a per-node key (<c>{workflowId}</c>) into the
    /// node's own scoped namespace, so the AND-join merge has no duplicate property names and
    /// the expected merged packet is deterministic regardless of predecessor arrival order.</summary>
    private sealed class KeyedExecutor : IWorkflowExecutor
    {
        private readonly DataStore _store;

        public KeyedExecutor(DataStore store) => _store = store;

        public ConcurrentQueue<string> Calls { get; } = new();

        public List<string> Failed { get; } = [];

        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string irData,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
        {
            Calls.Enqueue(workflowId);
            if (overrides is not null &&
                overrides.TryGetValue(DataStoreScope.OutputNamespaceConstant, out var ns) &&
                !string.IsNullOrEmpty(ns))
            {
                _store.Set(DataStoreScope.ScopedKey(ns, workflowId), $"done:{workflowId}");
            }
            return Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
        }
    }

    private static async Task<(BenchRunInstance Instance, KeyedExecutor Executor)> RunScenarioAsync()
    {
        var store = new DataStore();
        var executor = new KeyedExecutor(store);
        var scheduler = new BenchScheduler(ScenarioToolkit(), executor, store,
            new ToolkitFileStore(Path.GetTempPath()));

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, _) => completed.TrySetResult();
        var instance = scheduler.StartRun("manual");

        var done = await Task.WhenAny(completed.Task, Task.Delay(10_000));
        Assert.True(ReferenceEquals(done, completed.Task), "scenario run timed out");

        return (instance!, executor);
    }

    [Fact]
    public async Task Scenario_Delivered_Packets_Match_Hand_Computed_Expectation()
    {
        // A and B are roots (null input) and each write a per-node key into its own
        // namespace. A fans out to C and D; B AND-joins C. So regardless of which of
        // A/B completes first:
        //   Packets[D] = A's output                = { "A": "done:A" }
        //   Packets[C] = Merge(A's output, B's output) = { "A": "done:A", "B": "done:B" }
        var (instance, _) = await RunScenarioAsync();

        lock (instance.Gate)
        {
            var d = instance.Packets["D"];
            Assert.Equal("""{"A":"done:A"}""", d.GetRawText());

            var c = instance.Packets["C"];
            Assert.Equal(2, c.EnumerateObject().Count());
            Assert.Equal("done:A", c.GetProperty("A").GetString());
            Assert.Equal("done:B", c.GetProperty("B").GetString());
        }
    }

    [Fact]
    public async Task Scenario_Every_Node_Completes_With_No_Failures()
    {
        // The chain must run to completion with no failures (A, B roots; C AND-join;
        // D fan-out target), so the F4/F5 packet path is exercised end-to-end.
        var (_, executor) = await RunScenarioAsync();

        var calls = executor.Calls.ToList();
        Assert.Equal(new[] { "A", "B", "C", "D" }.OrderBy(x => x), calls.OrderBy(x => x));
        Assert.Empty(executor.Failed);
    }
}
