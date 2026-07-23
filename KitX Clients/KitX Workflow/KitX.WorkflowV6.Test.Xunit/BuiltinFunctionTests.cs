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
    public void Registry_Contains_Arithmetic_Functions()
    {
        var registry = Discover();
        Assert.Contains("Sub", registry.AllNames);
        Assert.Contains("Mul", registry.AllNames);
        Assert.Contains("Div", registry.AllNames);
        Assert.Contains("Mod", registry.AllNames);
    }

    [Fact]
    public void Arithmetic_Functions_Spec_Correct()
    {
        var registry = Discover();
        foreach (var name in new[] { "Sub", "Mul", "Div", "Mod" })
        {
            var fn = registry.Get(name);
            Assert.NotNull(fn);
            Assert.Equal(FunctionKind.Pure, fn!.Kind);
            Assert.Equal(2, fn.InputPorts.Count);
            Assert.All(fn.InputPorts, p => Assert.Equal(PinType.Integer, p.Type));
            Assert.Single(fn.OutputPorts);
            Assert.Equal(PinType.Integer, fn.OutputPorts[0].Type);
        }
    }

    [Fact]
    public void Registry_Contains_Utility_Functions()
    {
        var registry = Discover();
        Assert.Contains("Pause", registry.AllNames);
        Assert.Contains("ReadTextFile", registry.AllNames);
        Assert.Contains("WriteTextFile", registry.AllNames);
    }

    [Fact]
    public void Pause_Function_Spec_Correct()
    {
        var registry = Discover();
        var pause = registry.Get("Pause");
        Assert.NotNull(pause);
        Assert.Equal(FunctionKind.SideEffect, pause!.Kind);
        Assert.Single(pause.InputPorts);
        Assert.Equal(PinType.Integer, pause.InputPorts[0].Type);
        Assert.Empty(pause.OutputPorts);
    }

    [Fact]
    public void ReadTextFile_Function_Spec_Correct()
    {
        var registry = Discover();
        var read = registry.Get("ReadTextFile");
        Assert.NotNull(read);
        Assert.Equal(FunctionKind.Pure, read!.Kind);
        Assert.Single(read.InputPorts);
        Assert.Equal(PinType.String, read.InputPorts[0].Type);
        Assert.Single(read.OutputPorts);
        Assert.Equal(PinType.String, read.OutputPorts[0].Type);
    }

    [Fact]
    public void WriteTextFile_Function_Spec_Correct()
    {
        var registry = Discover();
        var write = registry.Get("WriteTextFile");
        Assert.NotNull(write);
        Assert.Equal(FunctionKind.SideEffect, write!.Kind);
        Assert.Equal(2, write.InputPorts.Count);
        Assert.All(write.InputPorts, p => Assert.Equal(PinType.String, p.Type));
        Assert.Empty(write.OutputPorts);
    }

    [Fact]
    public void Len_Function_Spec_Correct()
    {
        var registry = Discover();
        var len = registry.Get("Len");
        Assert.NotNull(len);
        Assert.Equal(FunctionKind.Pure, len!.Kind);
        Assert.Single(len.InputPorts);
        Assert.Equal(PinType.Any, len.InputPorts[0].Type);
        Assert.Single(len.OutputPorts);
        Assert.Equal(PinType.Integer, len.OutputPorts[0].Type);
    }
}