using System.Collections.Immutable;
using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Backend.RoslynBackend;
using KitX.Workflow.Backend.Runtime;
using KitX.Workflow.Builtin;
using KitX.Workflow.Builtin.Functions;
using KitX.Workflow.Conversion;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// End-to-end JSON chain tests (List-Port-And-Json-Functions-Design.md §9).
//
// Verifies that PluginCall's return gains JsonElement type identity, the JSON
// builtin family consumes it, and ConvertTo<JsonElement> normalizes object-typed
// values without throwing.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests the JSON end-to-end pipeline: PluginCall → JSON builtins → Print.
/// Uses a mock IPluginHost that returns a fixed JSON payload.
/// </summary>
public class JsonEndToEndTests
{
    private const string MainBlock = "#MainBlock";

    private static BuiltinFunctionRegistry NewRegistry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    /// <summary>
    /// A mock IPluginHost whose Call returns a JSON array of objects (simulating
    /// kxp-Search's List&lt;SearchResultItem&gt; return shape).
    /// </summary>
    private sealed class MockSearchPluginHost : IPluginHost
    {
        // Simulates: [{ "Title":"Hello", "Url":"http://example.com" }, { "Title":"World", "Url":"http://x.io" }]
        private const string SearchResultsJson =
            """[{"Title":"Hello","Url":"http://example.com"},{"Title":"World","Url":"http://x.io"}]""";

        public object? Call(string pluginName, string methodName, params object[] args)
            => SearchResultsJson;  // raw JSON string — AsJsonElement parses it

        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
            => Call(pluginName, methodName, args);

        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => false;
        public bool StopPlugin(string pluginName) => false;
        public bool StopWorkflow(string workflowId) => false;
        public string CreateWorkflow(string name, string source) => "";
        public bool RunWorkflow(string workflowId) => false;
        public bool InstallPlugin(string kxpPath) => false;
        public string GetPluginInfoByName(string pluginName) => "{}";
        public string ListPluginNames() => "[]";
        public string ListWorkflows() => "[]";
    }

    private static IrWorkflow SingleBlockWorkflow(params IrStatement[] stmts) => new()
    {
        MainBlockName = MainBlock,
        Blocks = ImmutableArray.Create(new IrBlock
        {
            Name = MainBlock,
            Kind = IrBlockKind.Entry,
            Statements = stmts.ToImmutableArray(),
        }),
    };

    /// <summary>PluginCall("plugin","method") assigned to a target PubVar.</summary>
    private static IrPipelineStatement PluginCallAssign(string target, string plugin, string method)
        => new()
        {
            Fingerprint = IrFingerprint.Compute("PluginCall", new[] { $"\"{plugin}\"", $"\"{method}\"" }, target),
            Sources = ImmutableArray<string>.Empty,
            Segments = ImmutableArray.Create(
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = "PluginCall",
                    Arguments = ImmutableArray.Create(
                        IrPipelineArgument.Lit($"\"{plugin}\""),
                        IrPipelineArgument.Lit($"\"{method}\"")),
                },
                new IrSegment { Kind = IrSegmentKind.Variable, VariableName = target }),
        };

    /// <summary>target = FuncName(args) — FunctionCall + terminal Variable tap.</summary>
    private static IrPipelineStatement FuncAssign(string funcName, string target, params string[] args)
        => new()
        {
            Fingerprint = IrFingerprint.Compute(funcName, args, target),
            Sources = ImmutableArray<string>.Empty,
            Segments = ImmutableArray.Create(
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = funcName,
                    Arguments = args.Select(IrPipelineArgument.Lit).ToImmutableArray(),
                },
                new IrSegment { Kind = IrSegmentKind.Variable, VariableName = target }),
        };

    /// <summary>var > Print — variable source + Print call, no assignment.</summary>
    private static IrPipelineStatement VarToPrint(string varName)
        => new()
        {
            Fingerprint = IrFingerprint.Compute("Print", new[] { varName }),
            Sources = ImmutableArray.Create(varName),
            Segments = ImmutableArray.Create(new IrSegment
            {
                Kind = IrSegmentKind.FunctionCall,
                FunctionName = "Print",
                Arguments = ImmutableArray.Create(IrPipelineArgument.Lit(varName)),
            }),
        };

    // ───────────────────────────────────────────────────────────────────────────
    // Type identity: PluginCall return → PubVar typed as JsonElement
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeInferer_PluginCallReturn_InfersJsonElementType()
    {
        var registry = NewRegistry();
        var ir = SingleBlockWorkflow(PluginCallAssign("results", "kxp-Search", "SearxngSearch"));

        var types = TypeInferer.Infer(ir, null, registry, null);

        Assert.Equal("JsonElement", types["results"]);
    }

    [Fact]
    public void PluginCallFunction_ReturnPinIsJson()
    {
        var fn = new PluginCallFunction();
        var returnPin = fn.OutputPorts.FirstOrDefault(p => p.Name == "Return");
        Assert.Equal(PinType.Json, returnPin.Type);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // AsJsonElement normalization (unit-level)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AsJsonElement_ParsesJsonStringIntoArray()
    {
        var el = ((object?)"[1,2,3]").AsJsonElement();
        Assert.Equal(JsonValueKind.Array, el.ValueKind);
        Assert.Equal(3, el.GetArrayLength());
    }

    [Fact]
    public void AsJsonElement_PassesThroughExistingJsonElement()
    {
        var original = JsonDocument.Parse("""{"name":"test"}""").RootElement.Clone();
        var el = ((object?)original).AsJsonElement();
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
        Assert.Equal("test", el.GetProperty("name").GetString());
    }

    [Fact]
    public void AsJsonElement_NullReturnsDefault()
    {
        var el = ((object?)null).AsJsonElement();
        Assert.Equal(JsonValueKind.Undefined, el.ValueKind);
    }

    [Fact]
    public void AsJsonElement_SerializesArbitraryObject()
    {
        var el = ((object?)new { value = 42 }).AsJsonElement();
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
        Assert.Equal(42, el.GetProperty("value").GetInt32());
    }

    // ───────────────────────────────────────────────────────────────────────────
    // End-to-end: PluginCall → JsonArrayAt → JsonGetField → JsonAsString → Print
    // (List-Port-And-Json-Functions-Design.md §9 validation scenario)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EndToEnd_PluginCallToJsonString_PrintsFirstTitle()
    {
        var backend = new RoslynExecutionBackend(NewRegistry(), new MockSearchPluginHost());

        // results = PluginCall("kxp-Search", "SearxngSearch")
        // first = JsonArrayAt(results, 0)
        // title = JsonGetField(first, "Title")
        // titleStr = JsonAsString(title)
        // Print(titleStr)
        var ir = SingleBlockWorkflow(
            PluginCallAssign("results", "kxp-Search", "SearxngSearch"),
            FuncAssign("JsonArrayAt", "first", "results", "0"),
            FuncAssign("JsonGetField", "title", "first", "\"Title\""),
            FuncAssign("JsonAsString", "titleStr", "title"),
            VarToPrint("titleStr"));

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Single(result.Output);
        Assert.Equal("Hello", result.Output[0]);
    }

    [Fact]
    public async Task EndToEnd_JsonArrayLength_PrintsCount()
    {
        var backend = new RoslynExecutionBackend(NewRegistry(), new MockSearchPluginHost());

        // results = PluginCall("kxp-Search", "SearxngSearch")
        // len = JsonArrayLength(results)
        // Print(len)
        var ir = SingleBlockWorkflow(
            PluginCallAssign("results", "kxp-Search", "SearxngSearch"),
            FuncAssign("JsonArrayLength", "len", "results"),
            VarToPrint("len"));

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Single(result.Output);
        Assert.Equal("2", result.Output[0]);
    }

    [Fact]
    public async Task EndToEnd_JsonContains_ChecksPathExistence()
    {
        var backend = new RoslynExecutionBackend(NewRegistry(), new MockSearchPluginHost());

        // results = PluginCall(...)
        // first = JsonArrayAt(results, 0)
        // exists = JsonContains(first, "Title")
        // Print(exists)
        var ir = SingleBlockWorkflow(
            PluginCallAssign("results", "kxp-Search", "SearxngSearch"),
            FuncAssign("JsonArrayAt", "first", "results", "0"),
            FuncAssign("JsonContains", "exists", "first", "\"Title\""),
            VarToPrint("exists"));

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Single(result.Output);
        Assert.Equal("True", result.Output[0]);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // ConvertTo<JsonElement> does not throw for object inputs
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertTo_JsonElementFromObject_DoesNotThrow()
    {
        // This test exercises the generated ConvertTo<JsonElement> branch: a PubVar
        // typed JsonElement receiving an object-typed value (the PluginCall path).
        // If ConvertTo falls through to Convert.ChangeType, it throws for JsonElement.
        var backend = new RoslynExecutionBackend(NewRegistry(), new MockSearchPluginHost());

        // The minimal chain: PluginCall → results (JsonElement-typed via TypeInferer).
        // Compilation + execution success proves ConvertTo<JsonElement> works.
        var ir = SingleBlockWorkflow(PluginCallAssign("results", "kxp-Search", "SearxngSearch"));

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"ConvertTo<JsonElement> failed: {result.ErrorMessage}");
    }
}
