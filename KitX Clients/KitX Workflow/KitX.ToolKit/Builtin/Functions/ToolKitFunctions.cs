namespace KitX.ToolKit.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKit builtin function descriptors — the BP-palette / type-inference metadata
// for the host-side ToolKit services (KitX.UI panel runtime + KitX.DataStore
// blackboard).
//
// These are registered via the public WorkflowV6 registration API
// (AddBuiltinFunction<T> in AddKitXToolKit), so they appear in the BP palette and
// map 1:1 to BP nodes (pins from InputPorts/OutputPorts). The runtime execution
// lives on ExecutionGlobals.ToolKit (WorkflowV6), which routes through IPluginHost
// to the reserved-name bridge in the host.
//
// Dispatch is by method-name convention: the descriptor Name must exactly match an
// ExecutionGlobals method. The UI family is instance-scoped (the owning instance id
// is auto-injected); the DataStore family is not.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>UiSet(controlId, value) → writes a control's main property key.</summary>
public sealed class UiSetFunction : IBuiltinFunction
{
    public string Name => "UiSet";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("ControlId", PinType.String, 20),
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>UiGet(controlId) → reads a control's main property key.</summary>
public sealed class UiGetFunction : IBuiltinFunction
{
    public string Name => "UiGet";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("ControlId", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>UiLog(controlId, entry) → appends to a log control (controlId defaults to "log").</summary>
public sealed class UiLogFunction : IBuiltinFunction
{
    public string Name => "UiLog";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("ControlId", PinType.String, 20),
        new("Entry", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>UiProgress(controlId, value) → sets a progress control's value.</summary>
public sealed class UiProgressFunction : IBuiltinFunction
{
    public string Name => "UiProgress";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("ControlId", PinType.String, 20),
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>UiDialog(controlId, message, buttons...) → writes a dialog request slot.</summary>
public sealed class UiDialogFunction : IBuiltinFunction
{
    public string Name => "UiDialog";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("ControlId", PinType.String, 20),
        new("Message", PinType.String, 35),
    ];

    /// <summary>Extra button labels append as variadic string pins.</summary>
    public VariadicPinSpec? InputVariadic => new("Button ", 3, PinType.String);

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>UiOpenPanel() → requests the host to present the instance's panel.</summary>
public sealed class UiOpenPanelFunction : IBuiltinFunction
{
    public string Name => "UiOpenPanel";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts => [];
    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>DataStoreSet(key, value) → writes a blackboard key.</summary>
public sealed class DataStoreSetFunction : IBuiltinFunction
{
    public string Name => "DataStoreSet";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>DataStoreGet(key) → reads a blackboard key.</summary>
public sealed class DataStoreGetFunction : IBuiltinFunction
{
    public string Name => "DataStoreGet";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>DataStoreWait(keys...) → blocks until all keys are set (AND).</summary>
public sealed class DataStoreWaitFunction : IBuiltinFunction
{
    public string Name => "DataStoreWait";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [];

    /// <summary>Keys append as variadic string pins.</summary>
    public VariadicPinSpec? InputVariadic => new("Key ", 1, PinType.String);

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>DataStoreWaitAny(keys...) → blocks until any key is set (OR).</summary>
public sealed class DataStoreWaitAnyFunction : IBuiltinFunction
{
    public string Name => "DataStoreWaitAny";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [];

    /// <summary>Keys append as variadic string pins.</summary>
    public VariadicPinSpec? InputVariadic => new("Key ", 1, PinType.String);

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>DataStoreRemove(key) → removes a blackboard key.</summary>
public sealed class DataStoreRemoveFunction : IBuiltinFunction
{
    public string Name => "DataStoreRemove";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>DataStoreKeys() → lists all blackboard keys.</summary>
public sealed class DataStoreKeysFunction : IBuiltinFunction
{
    public string Name => "DataStoreKeys";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>DataStoreContains(key) → whether a blackboard key exists.</summary>
public sealed class DataStoreContainsFunction : IBuiltinFunction
{
    public string Name => "DataStoreContains";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Boolean, 50),
    ];
}
