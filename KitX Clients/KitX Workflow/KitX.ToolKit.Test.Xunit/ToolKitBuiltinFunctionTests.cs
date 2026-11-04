// ─────────────────────────────────────────────────────────────────────────────
// ToolKit builtin function descriptor tests.
//
// Verifies the Ui*/Toolkit* builtins are registered into the shared
// BuiltinFunctionRegistry via the public WorkflowV6 registration API when
// AddKitXToolKit() runs, and that each descriptor's spec (name / kind / ports)
// matches the ExecutionGlobals method it dispatches to.
//
// ADR-0011 requires the descriptor set and the implementation type to agree: every
// registered name must have a same-named method. That check lives in
// BuiltinResolutionCompileTests, because it needs the implementation type; here we
// pin the descriptors' own shape.
// ─────────────────────────────────────────────────────────────────────────────

using System.IO;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Builtin.Functions;
using KitX.ToolKit.Hosting;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Spec")]
public class ToolKitBuiltinFunctionTests
{
    [Fact]
    public void AddKitXToolKit_Registers_All_ToolKit_Builtins()
    {
        var services = new ServiceCollection();
        services.AddKitXWorkflowV6();
        services.AddKitXToolKit(Path.GetTempPath());
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<BuiltinFunctionRegistry>();

        foreach (var name in new[]
        {
            "UiSet", "UiGet", "UiLog", "UiProgress", "UiDialog", "UiOpenPanel",
            "ToolkitSet", "ToolkitGet", "ToolkitWait", "ToolkitWaitAny",
            "ToolkitRemove", "ToolkitKeys", "ToolkitContains", "ToolkitAppend",
            "BenchIn", "BenchOut",
        })
        {
            Assert.True(registry.Contains(name), $"Expected builtin '{name}' to be registered.");
        }
    }

    [Fact]
    public void DataStore_family_is_gone_from_the_catalogue()
    {
        // ADR-0004 D2: the scope lives in the function name, so the old unscoped family must not
        // merely be deprecated — a leftover registration would still be callable and would
        // bypass the key table entirely.
        var services = new ServiceCollection();
        services.AddKitXWorkflowV6();
        services.AddKitXToolKit(Path.GetTempPath());
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<BuiltinFunctionRegistry>();

        foreach (var name in new[]
        {
            "DataStoreSet", "DataStoreGet", "DataStoreWait", "DataStoreWaitAny",
            "DataStoreRemove", "DataStoreKeys", "DataStoreContains",
        })
        {
            Assert.False(registry.Contains(name), $"'{name}' must not be registered any more.");
        }
    }

    [Fact]
    public void Ui_Family_Descriptors_Spec_Correct()
    {
        var set = new UiSetFunction();
        Assert.Equal("UiSet", set.Name);
        Assert.Equal(FunctionKind.SideEffect, set.Kind);
        Assert.Equal(2, set.InputPorts.Count);
        Assert.Equal(PinType.String, set.InputPorts[0].Type);   // ControlId
        Assert.Equal(PinType.Any, set.InputPorts[1].Type);       // Value
        Assert.Empty(set.OutputPorts);

        var get = new UiGetFunction();
        Assert.Equal(FunctionKind.Pure, get.Kind);
        Assert.Single(get.InputPorts);
        Assert.Equal(PinType.Json, get.OutputPorts[0].Type);

        var dialog = new UiDialogFunction();
        Assert.Equal(FunctionKind.SideEffect, dialog.Kind);
        Assert.NotNull(dialog.InputVariadic);
        Assert.Equal(PinType.String, dialog.InputVariadic!.PinType);
    }

    [Fact]
    public void Toolkit_Family_Descriptors_Spec_Correct()
    {
        var set = new ToolkitSetFunction();
        Assert.Equal("ToolkitSet", set.Name);
        Assert.Equal(FunctionKind.SideEffect, set.Kind);
        Assert.Equal(2, set.InputPorts.Count);
        Assert.Empty(set.OutputPorts);

        var get = new ToolkitGetFunction();
        Assert.Equal(FunctionKind.Pure, get.Kind);
        Assert.Single(get.InputPorts);
        Assert.Equal(PinType.Json, get.OutputPorts[0].Type);

        var wait = new ToolkitWaitFunction();
        Assert.Equal(FunctionKind.ControlFlow, wait.Kind);
        Assert.NotNull(wait.InputVariadic);
        Assert.Equal(PinType.String, wait.InputVariadic!.PinType);

        var contains = new ToolkitContainsFunction();
        Assert.Equal(FunctionKind.Pure, contains.Kind);
        Assert.Equal(PinType.Boolean, contains.OutputPorts[0].Type);

        var append = new ToolkitAppendFunction();
        Assert.Equal(FunctionKind.SideEffect, append.Kind);
        Assert.Equal(2, append.InputPorts.Count);
    }
}
