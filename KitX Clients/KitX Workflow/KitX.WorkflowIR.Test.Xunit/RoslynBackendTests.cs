using System.Collections.Immutable;
using System.Text.RegularExpressions;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Backend.RoslynBackend;
using KitX.Workflow.Backend.Runtime;
using KitX.Workflow.Builtin;
using KitX.Workflow.Builtin.Functions;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// Roslyn-backend tests: IR → C# codegen, FlattenPipeline, TypeInferer,
// ScriptCompiler (compile + load), and end-to-end execution (Print output).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Codegen + compile + execute tests for the Roslyn execution backend (Phase 7).
/// </summary>
public class RoslynBackendTests
{
    // ── Helpers to build small IR workflows by hand. ──

    private static BuiltinFunctionRegistry NewRegistry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    /// <summary>An entry block whose name matches the IR default ("#MainBlock").</summary>
    private const string MainBlock = "#MainBlock";

    /// <summary>Builds a minimal IR workflow with one entry block carrying the given statements.</summary>
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

    /// <summary>A pipeline statement: <c>Print(literal)</c> — a single FunctionCall segment, no tap.</summary>
    private static IrPipelineStatement PrintCall(string literalArg)
        => new()
        {
            Fingerprint = IrFingerprint.Compute("Print", new[] { literalArg }),
            Sources = ImmutableArray<string>.Empty,
            Segments = ImmutableArray.Create(new IrSegment
            {
                Kind = IrSegmentKind.FunctionCall,
                FunctionName = "Print",
                Arguments = ImmutableArray.Create(IrPipelineArgument.Lit(literalArg)),
            }),
        };

    /// <summary>A pipeline statement: <c>target = Func(args)</c> — FunctionCall + terminal Variable tap.</summary>
    private static IrPipelineStatement AssignmentCall(string funcName, string target, params string[] args)
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

    // ───────────────────────────────────────────────────────────────────────────
    // Codegen: IR → C# source text.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSource_PrintCall_EmitsGPrintInvocation()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        var ir = SingleBlockWorkflow(PrintCall("\"hello\""));

        var source = compiler.GenerateSource(ir);

        // The generated RunAsync must call G.Print(...) for the Print statement.
        Assert.Contains("G.Print(", source);
        // The argument string literal round-trips.
        Assert.Contains("\"hello\"", source);
        // The generated class implements ICompiledBlockScript and lives in the generated namespace.
        Assert.Contains("ICompiledBlockScript", source);
        Assert.Contains("CompiledScript_", source);
    }

    [Fact]
    public void GenerateSource_HasWhileSwitchDispatcher_AndMainBlockCase()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        var ir = SingleBlockWorkflow(PrintCall("\"x\""));

        var source = compiler.GenerateSource(ir);

        Assert.Contains("while (true)", source);
        Assert.Contains("switch (G.NextBlock)", source);
        Assert.Contains($"case \"{MainBlock}\":", source);
        Assert.Contains("G.NextBlock =", source);  // the dispatcher sets NextBlock to MainBlock
        Assert.Contains("ct.ThrowIfCancellationRequested()", source);
    }

    [Fact]
    public void GenerateSource_StringConcatAssignment_EmitsValueAssignmentAndGSet()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        // result = StringConcat("a", "b")
        var ir = SingleBlockWorkflow(AssignmentCall("StringConcat", "result", "\"a\"", "\"b\""));

        var source = compiler.GenerateSource(ir);

        // The StringConcat call is dispatched through its descriptor → G.StringConcat(...)
        Assert.Contains("G.StringConcat(", source);
        // The result PubVar is pre-declared (object) and synced via G.Set("result", result).
        Assert.Contains("G.Set(\"result\", result)", source);
    }

    [Fact]
    public void GenerateSource_BranchTerminator_EmitsNextBlockAssignmentAndBreak()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        var branch = new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("Branch", new[] { "cond" }),
            Op = ControlFlowOp.Branch,
            FunctionName = "Branch",
            Arguments = ImmutableArray.Create("cond"),
            Targets = ImmutableArray.Create(
                new IrControlFlowTarget("True", "TrueBlock"),
                new IrControlFlowTarget("False", "FalseBlock")),
        };
        var ir = SingleBlockWorkflow(branch);

        var source = compiler.GenerateSource(ir);

        Assert.Contains("G.NextBlock = G.Branch(", source);
        Assert.Contains("\"TrueBlock\"", source);
        Assert.Contains("\"FalseBlock\"", source);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // FlattenPipeline: structured pipeline AST → imperative steps.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlattenPipeline_BareCall_NoAssignedVar()
    {
        var pipe = PrintCall("\"hi\"");
        var steps = FlattenPipeline.Flatten(pipe);

        Assert.Single(steps);
        Assert.Equal("Print", steps[0].FunctionName);
        Assert.Null(steps[0].AssignedVar);
        Assert.Equal("\"hi\"", steps[0].Arguments[0]);
    }

    [Fact]
    public void FlattenPipeline_AssignmentCall_TargetFromVariableTap()
    {
        var pipe = AssignmentCall("StringConcat", "out", "\"a\"", "\"b\"");
        var steps = FlattenPipeline.Flatten(pipe);

        Assert.Single(steps);
        Assert.Equal("StringConcat", steps[0].FunctionName);
        Assert.Equal("out", steps[0].AssignedVar);
        Assert.Equal(2, steps[0].Arguments.Count);
    }

    [Fact]
    public void TryGetAssignmentTarget_ReturnsTerminalVariableName()
    {
        var pipe = AssignmentCall("StringConcat", "target", "\"x\"");
        Assert.Equal("target", FlattenPipeline.TryGetAssignmentTarget(pipe));
    }

    [Fact]
    public void TryGetAssignmentTarget_NullForBareCall()
    {
        var pipe = PrintCall("\"x\"");
        Assert.Null(FlattenPipeline.TryGetAssignmentTarget(pipe));
    }

    // ───────────────────────────────────────────────────────────────────────────
    // TypeInferer: two-pass PubVar type inference.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeInferer_BranchCondition_DemandsBool()
    {
        var registry = NewRegistry();
        var branch = new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("Branch", new[] { "cond" }),
            Op = ControlFlowOp.Branch,
            FunctionName = "Branch",
            Arguments = ImmutableArray.Create("cond"),
            Targets = ImmutableArray.Create(
                new IrControlFlowTarget("True", "T"),
                new IrControlFlowTarget("False", "F")),
        };
        var ir = SingleBlockWorkflow(branch);
        var lowering = new LoweringResult
        {
            Ir = ir,
            PubVarTypes = new Dictionary<string, string>(),
            PubVarNames = new HashSet<string> { "cond" },
        };

        var types = TypeInferer.Infer(ir, lowering, registry, null);

        Assert.Equal("bool", types["cond"]);
    }

    [Fact]
    public void TypeInferer_StringConcatResult_InferredFromStringReturnPin()
    {
        var registry = NewRegistry();
        var ir = SingleBlockWorkflow(AssignmentCall("StringConcat", "result", "\"a\"", "\"b\""));

        var types = TypeInferer.Infer(ir, null, registry, null);

        // StringConcat's return pin is PinType.String → "string".
        Assert.Equal("string", types["result"]);
    }

    [Fact]
    public void TypeInferer_DeclaredConstant_KeepsDeclaredType()
    {
        var registry = NewRegistry();
        var ir = SingleBlockWorkflow(PrintCall("\"x\"")) with
        {
            Constants = ImmutableDictionary.CreateRange<string, IrConstant>(
                new[] { new KeyValuePair<string, IrConstant>("MaxRetries", new IrConstant("MaxRetries", "int", "3", 3)) }),
        };

        var types = TypeInferer.Infer(ir, null, registry, null);

        Assert.Equal("int", types["MaxRetries"]);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Compile + load: ScriptCompiler produces a runnable ICompiledBlockScript.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAndRun_PrintStatement_ProducesOutput()
    {
        var backend = new RoslynExecutionBackend(NewRegistry());
        var ir = SingleBlockWorkflow(PrintCall("\"hello world\""));

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Single(result.Output);
        Assert.Equal("hello world", result.Output[0]);
        // The entry block executed at least once.
        Assert.True(result.ExecutedBlockCount >= 1);
    }

    [Fact]
    public async Task ExecuteAsync_TwoPrints_BothAppearInOutput()
    {
        var backend = new RoslynExecutionBackend(NewRegistry());
        var ir = SingleBlockWorkflow(
            PrintCall("\"first\""),
            PrintCall("\"second\""));

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Output.Count);
        Assert.Equal("first", result.Output[0]);
        Assert.Equal("second", result.Output[1]);
    }

    [Fact]
    public async Task ExecuteAsync_FallThroughToSecondBlock_RunsBoth()
    {
        var backend = new RoslynExecutionBackend(NewRegistry());
        var ir = new IrWorkflow
        {
            MainBlockName = MainBlock,
            Blocks = ImmutableArray.Create(
                new IrBlock
                {
                    Name = MainBlock,
                    Kind = IrBlockKind.Entry,
                    Statements = new IrStatement[] { PrintCall("\"from-main\"") }.ToImmutableArray(),
                    Successors = ImmutableArray.Create(new IrEdge(MainBlock, "Next", IrEdgeType.Sequential, null)),
                },
                new IrBlock
                {
                    Name = "Next",
                    Kind = IrBlockKind.Basic,
                    Statements = new IrStatement[] { PrintCall("\"from-next\"") }.ToImmutableArray(),
                }),
        };

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Output.Count);
        Assert.Equal("from-main", result.Output[0]);
        Assert.Equal("from-next", result.Output[1]);
    }

    [Fact]
    public async Task ExecuteAsync_GotoTransfers_AndExitTerminates()
    {
        var backend = new RoslynExecutionBackend(NewRegistry());
        var ir = new IrWorkflow
        {
            MainBlockName = MainBlock,
            Blocks = ImmutableArray.Create(
                new IrBlock
                {
                    Name = MainBlock,
                    Kind = IrBlockKind.Entry,
                    Statements = new IrStatement[]
                    {
                        PrintCall("\"entry\""),
                        new IrControlFlowStatement
                        {
                            Fingerprint = IrFingerprint.Compute("Goto", new[] { "Target" }),
                            Op = ControlFlowOp.Goto,
                            FunctionName = "Goto",
                            Arguments = ImmutableArray<string>.Empty,
                            Targets = ImmutableArray.Create(new IrControlFlowTarget("Exec", "Target")),
                        },
                    }.ToImmutableArray(),
                },
                new IrBlock
                {
                    Name = "Target",
                    Kind = IrBlockKind.Basic,
                    Statements = new IrStatement[]
                    {
                        PrintCall("\"target\""),
                        new IrControlFlowStatement
                        {
                            Fingerprint = IrFingerprint.Compute("Exit", Array.Empty<string>()),
                            Op = ControlFlowOp.Exit,
                            FunctionName = "Exit",
                        },
                    }.ToImmutableArray(),
                }),
        };

        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Output.Count);
        Assert.Equal("entry", result.Output[0]);
        Assert.Equal("target", result.Output[1]);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §2.1 fix: debug checkpoint id == canvas node id ("stmt:" + DeriveStableId).
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSource_DebugCheckpoint_EmitsCanvasNodeId()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        var ir = SingleBlockWorkflow(PrintCall("\"hello\""));

        var source = compiler.GenerateSource(ir, isDebug: true);

        // The expected checkpoint id matches what BpRenderer assigns: the statement
        // at ordinal 0 in "#MainBlock".
        var expectedId = "stmt:" + IrFingerprint.DeriveStableId(
            MainBlock, IrFingerprint.Compute("Print", new[] { "\"hello\"" }), 0);

        Assert.Contains($"\"{expectedId}\"", source);
    }

    [Fact]
    public void GenerateSource_DebugCheckpoint_MatchesNodeIdRegex()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        var ir = SingleBlockWorkflow(
            PrintCall("\"first\""),
            AssignmentCall("StringConcat", "out", "\"a\"", "\"b\""));

        var source = compiler.GenerateSource(ir, isDebug: true);

        // Every debug checkpoint statement id must look like "stmt:" + 12 hex chars.
        var matches = Regex.Matches(source, @"""stmt:([0-9A-F]{12})""");
        Assert.True(matches.Count >= 2, $"Expected >=2 stmt: checkpoint ids, found {matches.Count}");
    }

    [Fact]
    public void GenerateSource_NonDebug_OmitsCheckpoints()
    {
        var compiler = new ScriptCompiler(NewRegistry());
        var ir = SingleBlockWorkflow(PrintCall("\"x\""));

        var source = compiler.GenerateSource(ir, isDebug: false);

        Assert.DoesNotContain("CheckpointAsync", source);
    }
}
