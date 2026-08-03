// ─────────────────────────────────────────────────────────────────────────────
// Phase 10 acceptance tests for interactive debugging.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.Debugging;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.KsTextLens;
using System.Text.RegularExpressions;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class DebugTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public DebugTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private Workflow Parse(string src) => _fixture.KsLens.Parse(src, []);

    [Fact]
    public async Task Debug_No_Debugger_Fast_Path()
    {
        var ir = _fixture.KsLens.Parse("Print(\"hello\")\n", []);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public void Debug_Codegen_Inserts_Checkpoint_When_HasDebugger()
    {
        var ir = _fixture.KsLens.Parse("Print(\"hello\")\n", []);
        var codegen = new DebugCodegen(_fixture.Registry);
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("Checkpoint", source);
        Assert.Contains("this.Checkpoint(", source);
    }

    [Fact]
    public void Debug_Codegen_No_Checkpoint_When_No_Debugger()
    {
        var ir = _fixture.KsLens.Parse("Print(\"hello\")\n", []);
        var codegen = new DebugCodegen(_fixture.Registry);
        var source = codegen.Generate(ir, null, hasDebugger: false);
        Assert.DoesNotContain("Checkpoint", source);
    }

    [Fact]
    public void Debug_Codegen_Handles_Multi_Source_Pipeline()
    {
        var ir = _fixture.KsLens.Parse("guessNum, targetNum > Compare(\"BEQ\")\n", []);
        var codegen = new DebugCodegen(_fixture.Registry);
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("this.Compare(\"BEQ\"", source);
        Assert.DoesNotContain("/* pipeline */", source);
    }

    [Fact]
    public void Debug_Codegen_Handles_Placeholder_Pipeline()
    {
        var ir = _fixture.KsLens.Parse("loopMax > Range(0, _, 1)\n", []);
        var codegen = new DebugCodegen(_fixture.Registry);
        var source = codegen.Generate(ir, null, hasDebugger: true);
        // E3: assert the stub is gone (semantic contract); don't freeze exact parameter format.
        Assert.DoesNotContain("/* pipeline */", source);
        Assert.Contains("this.Range(", source);
    }

    [Fact]
    public void Debug_Codegen_Handles_Variable_Tap()
    {
        var ir = _fixture.KsLens.Parse("counter > Add(_, 1) > counter\n", []);
        var codegen = new DebugCodegen(_fixture.Registry);
        var source = codegen.Generate(ir, null, hasDebugger: true);
        // E3: assert write-back happens (semantic); don't freeze exact method format.
        Assert.Contains("this.counter", source);
    }

    [Fact]
    public void Debug_Codegen_Generates_Unique_Pipe_Variables_For_Multiple_Pipelines()
    {
        var src = """
            var {
                int a
                int b
            }
            a > Add(_, 1)
            b > Add(_, 1)
            """;
        var ir = Parse(src);
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains("__pipe_0", code);
        Assert.Contains("__pipe_1", code);
        Assert.DoesNotContain("__pipe_0_0", code);
    }

    [Fact]
    public void Debug_Codegen_Void_Segment_Is_Not_Bound_To_Pipe_Variable()
    {
        // Regression: `5 > Print` — Print has no output ports. Debug codegen must emit
        // a bare call statement, NOT `var __pipe_N = this.Print(5);` which fails to
        // compile with CS0815 (cannot assign void to an implicitly-typed variable).
        // The SEGMENT publishes no wire value (nothing to publish); the source node's
        // own wire publication is unaffected.
        var ir = Parse("5 > Print\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains("this.Print(5);", code);
        Assert.DoesNotContain("__pipe_0", code);
        Assert.DoesNotContain($"this.OnWireValue(\"w:{NodeId.Of("/top/stmt/0/seg/0")}\"", code);
    }

    [Fact]
    public async Task Debug_Void_Segment_Compiles_And_Runs()
    {
        // End-to-end: `a > Print` under a debugger must COMPILE (the CS0815 regression)
        // and produce the expected output.
        var ir = Parse("""
            var {
                int a
            }
            a > Print
            """);
        var backend = _fixture.MakeBackend();
        var debugger = new MockDebugController();

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None, debugger);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(0, result.Output.Select(o => o.Trim()).Select(int.Parse));
    }

    [Fact]
    public void Debug_Codegen_Recognizes_Helper_Functions()
    {
        var ir = new Workflow
        {
            Body = [new PipelineStatement
            {
                Sources = [new KsCall { MethodName = "MyHelper", Args = [] }],
                Segments = [],
                Fingerprint = Fingerprint.Compute("test-helper"),
            }],
            HelperFunctions = [new HelperFunction { Name = "MyHelper" }],
        };
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);
        Assert.Contains("this.MyHelper()", code);
        Assert.DoesNotContain("this.MyHelper = ", code);
    }

    [Fact]
    public void Debug_Codegen_Renders_ForEach_Item_As_Local_Variable()
    {
        var ir = Parse("forEach Range(0, 3, 1) as i:\n    i > Print\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);
        Assert.DoesNotContain("this.i)", code);
        Assert.Contains("i)", code);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Data-tooltip + variable-notification tests (discussion notes §十二-M).
    //
    // These cover the three codegen插桩 paths added for the wire-value tooltip
    // and the real-time variable panel:
    //   1. Function-call segment → OnWireValue("w:{nodeId}", __pipe_N)
    //   2. Variable-tap segment  → OnVarChanged("name", this.name)
    //                               + OnWireValue("w:{varNodeId}", __pipe_N)
    //   3. Control-flow condition/selector → OnWireValue("w:{ctrlNodeId}:{pin}", __cond_N)
    //
    // Plus the foundational ID-unification test (statementId == BP nodeId), and
    // an end-to-end test asserting the wire value reaches IBlueprintDebugController.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Debug_Codegen_Emits_OnWireValue_After_Each_Segment()
    {
        // Pipeline: 5, 7 > Compare("BEQ") — one function-call segment.
        // Expected codegen: var __pipe_0 = this.Compare(...); this.OnWireValue("w:n_xxx", __pipe_0);
        var ir = Parse("5, 7 > Compare(\"BEQ\")\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        // The wire's source-node path is /top/stmt/0/seg/0 (mirrors BpRenderer).
        var expectedNodeId = NodeId.Of("/top/stmt/0/seg/0");
        Assert.Contains($"this.OnWireValue(\"w:{expectedNodeId}\", __pipe_0);", code);
    }

    [Fact]
    public void Debug_Codegen_Emits_OnVarChanged_On_PubVar_Write()
    {
        // Variable tap: 0 > counter — writes 0 to PubVar counter.
        var ir = Parse("var {\n    int counter\n}\n0 > counter\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains("this.counter = ", code);
        Assert.Contains("this.OnVarChanged(\"counter\", this.counter);", code);
    }

    [Fact]
    public void Debug_Codegen_StatementId_Equals_Bp_Node_Id()
    {
        // Foundational ID-unification test: the statementId passed to Checkpoint
        // must equal the nodeId of the BP node for the same statement. Otherwise
        // breakpoints set on a BP node would never fire at Checkpoint time.
        var src = """
            const {
                int guessNum = 5
            }
            var {
                int counter
            }
            Print("start")
            0 > counter
            counter > Add(_, 1) > counter
            if counter:
                Print("yes")
            Print("end")
            """;
        var ir = Parse(src);

        // Generate debug C#; harvest every `Checkpoint("...", "...")` call.
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);
        var checkpointIds = Regex.Matches(code, @"this\.Checkpoint\(""(n_[0-9A-F]{8})""")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(checkpointIds);

        // Project the IR to BP and harvest every node id.
        var bp = _fixture.BpLens.Project(ir);
        var bpNodeIds = bp.Nodes.Select(n => n.Id).ToHashSet();
        Assert.NotEmpty(bpNodeIds);

        // Every statement id must appear as a BP node id.
        var missing = checkpointIds.Where(id => !bpNodeIds.Contains(id)).ToList();
        Assert.True(missing.Count == 0,
            $"statementIds not found in BP node ids: {string.Join(", ", missing)}\n" +
            $"BP ids: {string.Join(", ", bpNodeIds.OrderBy(x => x))}");
    }

    [Fact]
    public async Task Debug_WireValue_Forwarded_To_VariableChanged_Event()
    {
        // End-to-end: execute a pipeline under a mock debugger and verify that
        // OnWireValue/OnVarChanged reach the IBlueprintDebugController via the
        // VariableChanged event.
        var ir = Parse("var {\n    int counter\n}\n0 > counter\n");
        var backend = _fixture.MakeBackend();
        var debugger = new MockDebugController();

        await backend.ExecuteAsync(ir, null, CancellationToken.None, debugger);

        // The PubVar write `0 > counter` should publish ("counter", 0).
        Assert.Contains(debugger.ValueChanges,
            kv => kv.name == "counter" && kv.value is int i && i == 0);
    }

    [Fact]
    public void Debug_ControlFlow_Condition_WireId_Matches_Bp_Node()
    {
        // ForEach: the List input wire id is w:{eachNodeId}:List.
        // The eachNodeId is derived from /top/stmt/i (the statement's own path),
        // which is also the Each node's id in BP.
        var ir = Parse("forEach Range(0, 3, 1) as i:\n    i > Print\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        var bp = _fixture.BpLens.Project(ir);
        var eachNode = bp.Nodes.Single(n => n.Name == "Each");
        Assert.Equal($"n_", eachNode.Id.Substring(0, 2));

        // The generated code must publish w:{eachNodeId}:List with the source value.
        Assert.Contains($"this.OnWireValue(\"w:{eachNode.Id}:List\",", code);

        // Similar for If: Branch node's Condition input.
        var ir2 = Parse("var {\n    bool c\n}\nif c:\n    Print(\"yes\")\n");
        var cg2 = new DebugCodegen(_fixture.Registry);
        var code2 = cg2.Generate(ir2, null, hasDebugger: true);
        var bp2 = _fixture.BpLens.Project(ir2);
        var branchNode = bp2.Nodes.Single(n => n.Name == "Branch");
        Assert.Contains($"this.OnWireValue(\"w:{branchNode.Id}:Condition\",", code2);
    }

    [Fact]
    public void Debug_Codegen_Checkpoints_At_Node_Granularity()
    {
        // `5 > Print` — the source node AND the Print segment node each get a stop
        // point (the BP user's mental model is node-by-node stepping).
        var ir = Parse("5 > Print\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains($"this.Checkpoint(\"{NodeId.Of("/top/stmt/0/src/0")}\"", code);
        Assert.Contains($"this.Checkpoint(\"{NodeId.Of("/top/stmt/0/seg/0")}\"", code);
    }

    [Fact]
    public void Debug_Codegen_Emits_Execution_End_Checkpoint()
    {
        var ir = Parse("Print(\"hello\")\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        // The execution-complete stop point lets the user step once more to formally
        // finish the debug session.
        Assert.Contains("this.Checkpoint(\"end\", \"end\");", code);
    }

    [Fact]
    public void Debug_Codegen_Checkpoint_Ids_All_Exist_In_Bp_Nodes()
    {
        // Node-granularity checkpoints (src/seg paths) must ALL resolve to BP node ids —
        // otherwise the frontend cannot highlight them and breakpoints cannot fire.
        var ir = Parse("""
            var {
                int a
                int b
            }
            a, b > Compare("BEQ", _, _) > Print
            """);
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);
        var checkpointIds = Regex.Matches(code, @"this\.Checkpoint\(""(n_[0-9A-F]{8})""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var bp = _fixture.BpLens.Project(ir);
        var bpNodeIds = bp.Nodes.Select(n => n.Id).ToHashSet();

        var missing = checkpointIds.Where(id => !bpNodeIds.Contains(id)).ToList();
        Assert.True(missing.Count == 0,
            $"checkpoint ids not found in BP node ids: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Debug_Codegen_Publishes_Source_Node_Wire_Value()
    {
        // `5 > Print` — the source node must publish its value on w:{src/0} so the BP
        // source node's data port tooltip shows it (previously only segment outputs
        // published, leaving source ports empty).
        var ir = Parse("5 > Print\n");
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains($"this.OnWireValue(\"w:{NodeId.Of("/top/stmt/0/src/0")}\", 5);", code);
    }

    [Fact]
    public async Task Debug_Print_Output_Streams_Live_To_Debugger()
    {
        // ExecutionGlobals.Print must forward each line to the debug controller with
        // the "print:" prefix so the frontend can stream it into the Output panel
        // during the session (not only after completion).
        var ir = Parse("Print(\"hello\")\nPrint(\"world\")\n");
        var backend = _fixture.MakeBackend();
        var debugger = new MockDebugController();

        await backend.ExecuteAsync(ir, null, CancellationToken.None, debugger);

        Assert.Contains(debugger.ValueChanges, kv => kv.name == "print:hello");
        Assert.Contains(debugger.ValueChanges, kv => kv.name == "print:world");
    }

    [Fact]
    public async Task Debug_Run_Without_Debugger_Does_Not_Stream_Print()
    {
        // Non-debug runs must NOT go through the debug controller (no NotifyValueChanged).
        var ir = Parse("Print(\"hello\")\n");
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public void Debug_Codegen_Emits_Helper_Functions()
    {
        // Regression: Debug codegen must emit helper methods on G — Run and Debug
        // share the same G surface, otherwise every helper call fails with CS1061
        // in debug mode (BP workflow with user helpers).
        var ir = new Workflow
        {
            Body = [new PipelineStatement
            {
                Sources = [new KsCall { MethodName = "CreateMemory", Args = [new KsLiteral { Kind = KsLiteralKind.Integer, Value = 100, SourceText = "100" }] }],
                Segments = [],
                Fingerprint = Fingerprint.Compute("test-helper-debug"),
            }],
            HelperFunctions = [new HelperFunction
            {
                Name = "CreateMemory",
                ReturnType = "string",
                Parameters = [new HelperFunctionParameter { Name = "size", Type = "int" }],
                Code = "return new string(' ', size);",
            }],
        };
        var cg = new DebugCodegen(_fixture.Registry);
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains("public string CreateMemory(int size)", code);
        Assert.Contains("return new string(' ', size);", code);
    }

    [Fact]
    public async Task Debug_With_Helper_Function_Compiles_And_Runs()
    {
        // End-to-end: a BP-style workflow calling a user helper must COMPILE in debug
        // mode (the CS1061 regression) and produce the expected output.
        var ir = new Workflow
        {
            Body = [new PipelineStatement
            {
                Sources = [new KsCall
                {
                    MethodName = "HelperFn",
                    Args = [new KsLiteral { Kind = KsLiteralKind.Integer, Value = 3, SourceText = "3" }],
                }],
                Segments = [new Segment { Target = "Print" }],
                Fingerprint = Fingerprint.Compute("test-helper-e2e"),
            }],
            HelperFunctions = [new HelperFunction
            {
                Name = "HelperFn",
                ReturnType = "int",
                Parameters = [new HelperFunctionParameter { Name = "x", Type = "int" }],
                Code = "return x * 2;",
            }],
        };
        var backend = _fixture.MakeBackend();
        var debugger = new MockDebugController();

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None, debugger);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("6", result.Output.Select(o => o.Trim()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MockDebugController — minimal IBlueprintDebugController for E2E tests.
    // Records every NotifyValueChanged call so tests can assert on the wire/variable
    // events without depending on RealBlueprintDebugger (which lives in Dashboard).
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class MockDebugController : IBlueprintDebugController
    {
        public List<(string name, object? value)> ValueChanges { get; } = new();

#pragma warning disable CS0067 // Events required by interface; not raised by this mock.
        public event Action<string>? NodeExecuting;
        public event Action<string>? NodeExecuted;
        public event Action<string>? BlockEntered;
        public event Action<string, object?>? VariableChanged;
        public event Action? ExecutionPaused;
        public event Action? ExecutionResumed;
#pragma warning restore CS0067

        public void SetBreakpoint(string nodeId) { }
        public void RemoveBreakpoint(string nodeId) { }
        public void ClearBreakpoints() { }
        public bool HasBreakpoint(string nodeId) => false;

        public void Pause() { }
        public void StepNext() { }
        public void Continue() { }

        public void SetSpeed(ExecutionSpeed speed) { }
        public ExecutionSpeed Speed => ExecutionSpeed.RealTime;
        public bool IsPaused => false;

        public IReadOnlyDictionary<string, object?> CurrentVariableSnapshot
            => new Dictionary<string, object?>();

        public void UpdateVariableSnapshot(Dictionary<string, object?> variables) { }

        public void NotifyValueChanged(string name, object? value)
        {
            ValueChanges.Add((name, value));
            VariableChanged?.Invoke(name, value);
        }

        public Task CheckpointAsync(string statementId, string? blockName, CancellationToken ct)
            => Task.CompletedTask;
    }
}