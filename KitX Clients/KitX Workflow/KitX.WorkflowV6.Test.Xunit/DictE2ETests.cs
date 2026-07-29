// ─────────────────────────────────────────────────────────────────────────────
// E2E tests for the Dict function family + dict literal (Package/Dict-Type-Design.md).
// Compiles KS source → IR → C# → runs → asserts on captured OutputLines.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class DictE2ETests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public DictE2ETests(WorkflowTestFixture fixture) => _fixture = fixture;

    private sealed class JsonPluginHost : IPluginHost
    {
        public object? Call(string pluginName, string methodName, params object[] args)
            => "{\"x\":10,\"y\":20}";
        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
            => "{\"x\":10,\"y\":20}";
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool StopWorkflow(string workflowId) => true;
        public string CreateWorkflow(string name, string source) => "wf-001";
        public bool RunWorkflow(string workflowId) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => "{}";
        public string ListPluginNames() => "[]";
        public string ListWorkflows() => "[]";
    }

    [Fact]
    public async Task E2E_Dict_Literal_GetValue_Print()
    {
        var src = """
            var {
                dict colors = {red: 0, green: 1, blue: 2}
            }
            colors, "red" > DictGetValue > Print
            colors, "blue" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "0", "2" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_SetValue_InPlace_Mutable()
    {
        var src = """
            var {
                dict colors = {red: 0}
            }
            colors, "green", 1 > DictSetValue > colors
            colors, "green" > DictGetValue > Print
            colors, "red" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "1", "0" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_ContainsKey()
    {
        var src = """
            var {
                dict colors = {red: 0, blue: 2}
            }
            colors, "blue" > DictContainsKey > Print
            colors, "green" > DictContainsKey > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "True", "False" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_Remove_InPlace()
    {
        var src = """
            var {
                dict colors = {red: 0, green: 1, blue: 2}
            }
            colors, "green" > DictRemove > colors
            colors, "green" > DictContainsKey > Print
            colors, "red" > DictContainsKey > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "False", "True" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_Merge_InPlace()
    {
        var src = """
            var {
                dict cfg = {a: 1, b: 2}
                dict more = {b: 99, c: 3}
            }
            cfg, more > DictMerge > cfg
            cfg, "b" > DictGetValue > Print
            cfg, "c" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "99", "3" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_ToJson()
    {
        var src = """
            var {
                dict d = {a: 1, b: 2}
            }
            d > DictToJson > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.Output);
        Assert.Contains("\"a\":1", result.Output[0]);
        Assert.Contains("\"b\":2", result.Output[0]);
    }

    [Fact]
    public async Task E2E_JsonToDict_GetValue()
    {
        var src = """
            var {
                dict d
            }
            PluginCall("Svc", "get") > JsonToDict > d
            d, "x" > DictGetValue > Print
            d, "y" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend(new JsonPluginHost());
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "10", "20" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_GetValues_Batch_Null_Pad()
    {
        var src = """
            var {
                dict d = {red: 0, blue: 2}
            }
            d, "[\"red\",\"blue\",\"yellow\"]" > DictGetValues > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.Output);
        // JSON array [0, 2, null]
        Assert.Contains("0", result.Output[0]);
        Assert.Contains("2", result.Output[0]);
        Assert.Contains("null", result.Output[0]);
    }

    [Fact]
    public void Parse_Dict_Literal_Structure()
    {
        var src = """
            var {
                dict colors = {red: 0, green: 1}
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.True(ir.GlobalVars.ContainsKey("colors"));
        var g = ir.GlobalVars["colors"];
        Assert.Equal("dict", g.Type);
        Assert.NotNull(g.DictInitializer);
        Assert.Equal(2, g.DictInitializer!.Entries.Length);
    }

    [Fact]
    public async Task E2E_Dict_Empty_Literal()
    {
        var src = """
            var {
                dict empty = {}
            }
            empty, "k" > DictContainsKey > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "False" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_Value_Const_Reference()
    {
        // Dict value referencing a const scalar — the const is inlined into the field initialiser.
        var src = """
            const {
                int MAX = 99
            }
            var {
                dict d = {val: MAX}
            }
            d, "val" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "99" }, result.Output);
    }

    [Fact]
    public async Task E2E_Dict_Keys()
    {
        var src = """
            var {
                dict d = {a: 1, b: 2}
            }
            d > DictKeys > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.Output);
        Assert.Contains("\"a\"", result.Output[0]);
        Assert.Contains("\"b\"", result.Output[0]);
    }

    // ── §8 Parenthesised pipeline source (independent of Dict, same design doc) ──

    [Fact]
    public async Task E2E_Parenthesised_Pipeline_Source_Single()
    {
        // (5 > Add(_, 1)) > Print  →  Add(5,1)=6, Print(6)
        var src = """
            (5 > Add(_, 1)) > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "6" }, result.Output);
    }

    [Fact]
    public async Task E2E_Parenthesised_Pipeline_Source_MultiSource()
    {
        // (5 > Add(_, 1)), 10 > Add(_, _) > Print  →  Add(Add(5,1),10)=16
        var src = """
            (5 > Add(_, 1)), 10 > Add(_, _) > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "16" }, result.Output);
    }

    [Fact]
    public async Task E2E_Parenthesised_Source_With_DictMerge()
    {
        // DictMerge with a parenthesised JsonToDict source (Dict-Type design §8.3 example).
        var src = """
            var {
                dict cfg = {a: 1}
            }
            cfg, (PluginCall("Svc", "get") > JsonToDict) > DictMerge > cfg
            cfg, "x" > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var backend = _fixture.MakeBackend(new JsonPluginHost());
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "10" }, result.Output);
    }

    // ── BP round-trip: dict operations (DictGetValue/DictSetValue/etc. are normal
    //    BuiltinFunctionNodes routed via the registry; they should round-trip cleanly).
    //    NOTE: dict-literal declaration initialisers are not yet rendered to BP
    //    (same limitation as existing `var { int x = 5 }` initialisers) — tracked as a
    //    follow-up alongside the DictNew declaration node + variadic pin-group frontend. ──

    [Fact]
    public void BP_RoundTrip_Dict_Operations_NoLiteralInit()
    {
        // dict operation as a function→function pipeline (d > DictKeys > Print).
        // Exercises the BP function→function data-edge connection (fixed to use ConnectValue).
        var src = """
            var {
                dict d
            }
            d > DictKeys > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff: {diff.StatementChanges.Length} changes: " +
            string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}")));
    }

    [Fact]
    public void BP_RoundTrip_Dict_GetValue_Chain()
    {
        // Multi-segment dict operation chain ending in a var tap.
        var src = """
            var {
                dict d
                string k
            }
            d, k > DictGetValue > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff: {diff.StatementChanges.Length} changes: " +
            string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}")));
    }
}
