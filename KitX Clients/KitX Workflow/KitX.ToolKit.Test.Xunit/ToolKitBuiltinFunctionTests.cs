// ─────────────────────────────────────────────────────────────────────────────
// ToolKit builtin function descriptor tests.
//
// Verifies the Ui*/DataStore* builtins are registered into the shared
// BuiltinFunctionRegistry via the public WorkflowV6 registration API when
// AddKitXToolKit() runs, and that each descriptor's spec (name / kind / ports)
// matches the ExecutionGlobals.ToolKit method it dispatches to.
// ─────────────────────────────────────────────────────────────────────────────

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
        services.AddKitXToolKit();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<BuiltinFunctionRegistry>();

        foreach (var name in new[]
        {
            "UiSet", "UiGet", "UiLog", "UiProgress", "UiDialog", "UiOpenPanel",
            "DataStoreSet", "DataStoreGet", "DataStoreWait", "DataStoreWaitAny",
            "DataStoreRemove", "DataStoreKeys", "DataStoreContains",
            "BenchIn", "BenchOut",
        })
        {
            Assert.True(registry.Contains(name), $"Expected builtin '{name}' to be registered.");
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
    public void DataStore_Family_Descriptors_Spec_Correct()
    {
        var set = new DataStoreSetFunction();
        Assert.Equal("DataStoreSet", set.Name);
        Assert.Equal(FunctionKind.SideEffect, set.Kind);
        Assert.Equal(2, set.InputPorts.Count);
        Assert.Empty(set.OutputPorts);

        var get = new DataStoreGetFunction();
        Assert.Equal(FunctionKind.Pure, get.Kind);
        Assert.Single(get.InputPorts);
        Assert.Equal(PinType.Json, get.OutputPorts[0].Type);

        var wait = new DataStoreWaitFunction();
        Assert.Equal(FunctionKind.Pure, wait.Kind);
        Assert.NotNull(wait.InputVariadic);
        Assert.Equal(PinType.String, wait.InputVariadic!.PinType);

        var contains = new DataStoreContainsFunction();
        Assert.Equal(FunctionKind.Pure, contains.Kind);
        Assert.Equal(PinType.Boolean, contains.OutputPorts[0].Type);
    }
}
