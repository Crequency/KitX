// ─────────────────────────────────────────────────────────────────────────────
// Phase 3 acceptance tests for the MVP builtin function subset.
//
// Covers the 5 MVP builtins (Print/Range/StringConcat/Compare/Add):
//   • Reflection discovery finds all 5 by name
//   • Each builtin's FunctionKind / InputPorts / OutputPorts match the spec
//   • StringConcat declares a variadic input spec
//   • Compare lists all 6 operator codes
//   • Codegen handlers are wired (concrete Roslyn emission lands in Phase 4)
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Builtin.Functions;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BuiltinFunctionTests
{
    private static BuiltinFunctionRegistry Discover()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    [Fact]
    public void Registry_Contains_MVP_Functions()
    {
        var registry = Discover();
        Assert.Contains("Print", registry.AllNames);
        Assert.Contains("Range", registry.AllNames);
        Assert.Contains("StringConcat", registry.AllNames);
        Assert.Contains("Compare", registry.AllNames);
        Assert.Contains("Add", registry.AllNames);
    }

    [Fact]
    public void Print_Function_Spec_Correct()
    {
        var registry = Discover();
        var print = registry.Get("Print");
        Assert.NotNull(print);
        Assert.Equal(FunctionKind.SideEffect, print!.Kind);
        Assert.Single(print.InputPorts);
        Assert.Equal(PinType.Any, print.InputPorts[0].Type);
        Assert.Empty(print.OutputPorts);
    }

    [Fact]
    public void Range_Function_Spec_Correct()
    {
        var registry = Discover();
        var range = registry.Get("Range");
        Assert.NotNull(range);
        Assert.Equal(FunctionKind.Pure, range!.Kind);
        Assert.Equal(3, range.InputPorts.Count);
        Assert.All(range.InputPorts, p => Assert.Equal(PinType.Integer, p.Type));
        Assert.Single(range.OutputPorts);
        Assert.Equal(PinType.Json, range.OutputPorts[0].Type);
    }

    [Fact]
    public void StringConcat_Variadic_Spec_Declared()
    {
        var registry = Discover();
        var concat = registry.Get("StringConcat");
        Assert.NotNull(concat);
        var variadic = concat!.InputVariadic;
        Assert.NotNull(variadic);
        Assert.Equal(PinType.String, variadic!.PinType);
        Assert.Equal(3, variadic.StartIndex);
        Assert.Equal("Input ", variadic.BasePinName);
    }

    [Fact]
    public void Compare_Ops_Correct()
    {
        var registry = Discover();
        var compare = registry.Get("Compare");
        Assert.NotNull(compare);
        Assert.Equal(FunctionKind.Pure, compare!.Kind);
        Assert.Equal(3, compare.InputPorts.Count);
        Assert.Equal(PinType.String, compare.InputPorts[0].Type);   // Op
        Assert.Equal(PinType.Any, compare.InputPorts[1].Type);       // A
        Assert.Equal(PinType.Any, compare.InputPorts[2].Type);       // B
        Assert.Single(compare.OutputPorts);
        Assert.Equal(PinType.Boolean, compare.OutputPorts[0].Type);

        // The static SupportedOps set lists all 6 operator codes.
        Assert.Equal(6, CompareFunction.SupportedOps.Count);
        Assert.Contains("BEQ", CompareFunction.SupportedOps);
        Assert.Contains("BNE", CompareFunction.SupportedOps);
        Assert.Contains("BLT", CompareFunction.SupportedOps);
        Assert.Contains("BLE", CompareFunction.SupportedOps);
        Assert.Contains("BGT", CompareFunction.SupportedOps);
        Assert.Contains("BGE", CompareFunction.SupportedOps);
    }

    [Fact]
    public void Add_Function_Spec_Correct()
    {
        var registry = Discover();
        var add = registry.Get("Add");
        Assert.NotNull(add);
        Assert.Equal(FunctionKind.Pure, add!.Kind);
        Assert.Equal(2, add.InputPorts.Count);
        Assert.All(add.InputPorts, p => Assert.Equal(PinType.Integer, p.Type));
        Assert.Single(add.OutputPorts);
        Assert.Equal(PinType.Integer, add.OutputPorts[0].Type);
    }

    [Fact]
    public void CodeGen_Handlers_Registered_For_MVP()
    {
        // Each MVP builtin implements ICodeGenHandler; the registry's per-role lookup
        // must return them so Phase 4 codegen can dispatch.
        var registry = Discover();
        Assert.NotNull(registry.GetCodeGen("Print"));
        Assert.NotNull(registry.GetCodeGen("Range"));
        Assert.NotNull(registry.GetCodeGen("StringConcat"));
        Assert.NotNull(registry.GetCodeGen("Compare"));
        Assert.NotNull(registry.GetCodeGen("Add"));
    }
}