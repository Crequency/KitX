using System.Reflection;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Builtin.Functions;
using Xunit;

namespace KitX.WorkflowIR.Test.Xunit;

/// <summary>
/// Verifies the builtin function registry: reflection discovery of all migrated
/// functions, name lookup, the control-flow set, the ForLoop BP-reverse mapping
/// (the cure for the legacy DashboardToCfgName hardcoded dictionary), and that each
/// function advertises exactly the role interfaces it needs (the ISP split: a
/// function implements only its real roles, never all of them).
/// </summary>
public class RegistryTests
{
    // Discover once for the whole fixture. The descriptors live in the main IR
    // assembly, which the test project references.
    private static BuiltinFunctionRegistry BuildRegistry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    // The 32 migrated function names (the v5.0 builtin surface, plus Break whose
    // distinct loop-exit semantics the new IR's ControlFlowOp keeps separate from Exit).
    private static readonly HashSet<string> ExpectedNames =
    [
        // A-level: simple value / side-effect / control-flow
        "Print", "Pause", "ReadTextFile", "WriteTextFile",
        "Branch", "Goto", "Exit",
        // B-level: algorithmic (variadic / index injection / BP-reverse)
        "ForLoop", "Switch", "Flip", "Break",
        "StringConcat",
        "JsonAsString", "JsonAsInt", "JsonAsBool", "JsonArrayLength",
        "JsonArrayAt", "JsonObjectKeys", "JsonGetField", "JsonContains",
        // C-level: plugin / device / service
        "PluginCall", "PluginCallWithTarget", "TryGetDevice",
        "StartPlugin", "StopPlugin", "StopWorkflow",
        "CreateWorkflow", "RunWorkflow", "InstallPlugin",
        "GetPluginInfoByName", "ListPluginNames", "ListWorkflows",
    ];

    // ── Discovery: all 32 functions register, no duplicates, no misses. ──

    [Fact]
    public void Discover_FindsAllBuiltinFunctions()
    {
        var registry = BuildRegistry();
        // Exactly the expected surface — nothing extra registered by accident.
        Assert.Equal(32, registry.AllNames.Count);
    }

    [Fact]
    public void Discover_AllExpectedNamesPresent()
    {
        var registry = BuildRegistry();
        var actual = new HashSet<string>(registry.AllNames);
        var missing = ExpectedNames.Where(n => !actual.Contains(n)).ToList();
        Assert.Empty(missing);
        // And no unexpected extras.
        var extras = actual.Where(n => !ExpectedNames.Contains(n)).ToList();
        Assert.Empty(extras);
    }

    // ── Per-name lookup: Get returns the function, Contains is true. ──

    [Theory]
    [InlineData("Print")]
    [InlineData("Branch")]
    [InlineData("ForLoop")]
    [InlineData("Switch")]
    [InlineData("StringConcat")]
    [InlineData("TryGetDevice")]
    [InlineData("PluginCall")]
    [InlineData("JsonGetField")]
    [InlineData("Exit")]
    public void Get_ReturnsNonNull_ForEachFunction(string name)
    {
        var registry = BuildRegistry();
        var fn = registry.Get(name);
        Assert.NotNull(fn);
        Assert.Equal(name, fn!.Name);
        Assert.True(registry.Contains(name));
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownName()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.Get("DoesNotExist"));
        Assert.False(registry.Contains("DoesNotExist"));
    }

    // ── Control-flow set: the seven control-flow terminators. ──
    // Keyed off FunctionKind.ControlFlow instead of the legacy IsFlowControl/ArgLayout pair.

    [Fact]
    public void ControlFlowNames_AreExactlyTheSevenTerminators()
    {
        var registry = BuildRegistry();
        var cf = new HashSet<string>(registry.ControlFlowNames);
        var expected = new HashSet<string>
        {
            "Branch", "ForLoop", "Switch", "Goto", "Break", "Exit", "Flip",
        };
        Assert.True(cf.SetEquals(expected),
            $"Control-flow set was {{{string.Join(", ", cf.OrderBy(x => x))}}}, expected {{{string.Join(", ", expected.OrderBy(x => x))}}}");
    }

    [Fact]
    public void ControlFlowFunctions_HaveNoDataOutputPins()
    {
        // v5.0 §7: control-flow nodes terminate the block — no data output pins.
        // Enforced at Register time; assert it holds for every discovered CF function.
        var registry = BuildRegistry();
        foreach (var name in registry.ControlFlowNames)
        {
            var fn = registry.Get(name)!;
            Assert.Empty(fn.OutputPorts);
        }
    }

    // ── ForLoop BP-reverse: the "Loop" alias replaces the hardcoded dictionary entry. ──

    [Fact]
    public void GetBpReverseByBpName_Loop_ReturnsForLoopHandler()
    {
        var registry = BuildRegistry();
        var handler = registry.GetBpReverseByBpName("Loop");
        Assert.NotNull(handler);
        Assert.IsType<ForLoopFunction>(handler);
        Assert.Contains("Loop", handler!.BpNames);
    }

    [Fact]
    public void GetBpReverseByBpName_UnknownAlias_ReturnsNull()
    {
        // Functions whose BP name == BS name do not register a reverse handler; the
        // caller falls back to identity mapping. "Loop" is the only alias today.
        var registry = BuildRegistry();
        Assert.Null(registry.GetBpReverseByBpName("Print"));
        Assert.Null(registry.GetBpReverseByBpName("NonexistentAlias"));
    }

    // ── ISP split: each function advertises ONLY the roles it needs. ──

    [Fact]
    public void Print_ImplementsOnlyCodeGenHandler()
    {
        var registry = BuildRegistry();
        var print = registry.Get("Print")!;
        Assert.Null(registry.GetParser("Print"));
        Assert.Null(registry.GetLowerer("Print"));
        Assert.NotNull(registry.GetCodeGen("Print"));
        Assert.Null(registry.GetBpRenderer("Print"));
        // Print is a SideEffect (output to console, no consumed return).
        Assert.Equal(FunctionKind.SideEffect, print.Kind);
    }

    [Fact]
    public void Branch_ImplementsParserAndCodeGen()
    {
        var registry = BuildRegistry();
        Assert.NotNull(registry.GetParser("Branch"));
        Assert.NotNull(registry.GetCodeGen("Branch"));
        Assert.Null(registry.GetLowerer("Branch"));      // default lowering applies
        Assert.Null(registry.GetBpRenderer("Branch"));
        Assert.Equal(FunctionKind.ControlFlow, registry.Get("Branch")!.Kind);
    }

    [Fact]
    public void ForLoop_ImplementsParserCodeGenLoweringAndBpReverse()
    {
        var registry = BuildRegistry();
        Assert.NotNull(registry.GetParser("ForLoop"));
        Assert.NotNull(registry.GetCodeGen("ForLoop"));
        // ForLoop customises lowering so it can register the indexName injected variable.
        Assert.NotNull(registry.GetLowerer("ForLoop"));
        Assert.Null(registry.GetBpRenderer("ForLoop"));
        Assert.Equal(FunctionKind.ControlFlow, registry.Get("ForLoop")!.Kind);
    }

    [Fact]
    public void TryGetDevice_ImplementsLoweringAndCodeGen()
    {
        // The only function besides ForLoop with a custom lowering (mints its own PubVar).
        var registry = BuildRegistry();
        Assert.Null(registry.GetParser("TryGetDevice"));
        Assert.NotNull(registry.GetLowerer("TryGetDevice"));
        Assert.NotNull(registry.GetCodeGen("TryGetDevice"));
        Assert.Null(registry.GetBpRenderer("TryGetDevice"));
    }

    [Fact]
    public void Exit_ImplementsParserAndCodeGen_NoTargets()
    {
        var registry = BuildRegistry();
        Assert.NotNull(registry.GetParser("Exit"));
        Assert.NotNull(registry.GetCodeGen("Exit"));
        var exit = registry.Get("Exit")!;
        Assert.Empty(exit.OutputPorts);   // Exit terminates the script — no arms, no data out
    }

    // ── Variadic specs round-trip through the descriptor. ──

    [Fact]
    public void StringConcat_DeclaresInputVariadic()
    {
        var registry = BuildRegistry();
        var sc = registry.Get("StringConcat")!;
        Assert.NotNull(sc.InputVariadic);
        Assert.Null(sc.OutputVariadic);
        Assert.Equal("Input ", sc.InputVariadic!.BasePinName);
    }

    [Fact]
    public void Switch_DeclaresOutputVariadic()
    {
        var registry = BuildRegistry();
        var sw = registry.Get("Switch")!;
        Assert.NotNull(sw.OutputVariadic);
        Assert.Null(sw.InputVariadic);
        Assert.Equal(1, sw.OutputVariadic!.StartIndex);
    }

    [Fact]
    public void PureFunctions_DeclareNoVariadicSpec()
    {
        var registry = BuildRegistry();
        foreach (var name in new[] { "Print", "JsonAsString", "JsonArrayAt", "ReadTextFile" })
        {
            var fn = registry.Get(name)!;
            Assert.Null(fn.InputVariadic);
            Assert.Null(fn.OutputVariadic);
        }
    }

    // ── Duplicate registration is rejected (the registry is the single source of truth). ──

    [Fact]
    public void Register_Duplicate_Throws()
    {
        var registry = BuildRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Register(new PrintFunction()));
    }

    [Fact]
    public void Register_ControlFlowWithDataOutput_Throws()
    {
        // The v5.0 §7 invariant is enforced at Register time.
        var registry = new BuiltinFunctionRegistry();
        var bad = new BadControlFlowWithDataOutput();
        Assert.Throws<InvalidOperationException>(() => registry.Register(bad));
    }

    /// <summary>A control-flow function that (incorrectly) declares a data output pin.</summary>
    private sealed class BadControlFlowWithDataOutput : IBuiltinFunction
    {
        public string Name => "Bad";
        public FunctionKind Kind => FunctionKind.ControlFlow;
        public IReadOnlyList<PortSpec> InputPorts => [];
        public IReadOnlyList<PortSpec> OutputPorts => [new("Return", PinType.String, 40)];
    }
}
