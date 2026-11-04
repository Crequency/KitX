namespace KitX.ToolKit.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// Toolkit data-plane builtin descriptors — the BP-palette / type-inference metadata
// for the host-side key store (ADR-0004, ADR-0011).
//
// Every descriptor here takes a LOCAL key name. There is deliberately no function
// that accepts a full key or a prefix: the toolkit scope is applied by KeyTable, so
// "read another toolkit's data" is not expressible rather than merely rejected
// (ADR-0004 D2).
//
// The old DataStore* family (Set/Get/Wait/WaitAny/Remove/Keys/Contains/Append) is
// replaced by this one. Wait/WaitAny stay reachable from workflows — only their
// implementation moved out of the store (ADR-0004 D1).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>ToolkitSet(key, value) → writes a declared key the workflow owns.</summary>
internal sealed class ToolkitSetFunction : IBuiltinFunction
{
    public string Name => "ToolkitSet";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>ToolkitGet(key) → reads a declared key.</summary>
internal sealed class ToolkitGetFunction : IBuiltinFunction
{
    public string Name => "ToolkitGet";
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

/// <summary>ToolkitWait(keys...) → blocks until all named keys are present (AND).</summary>
internal sealed class ToolkitWaitFunction : IBuiltinFunction
{
    public string Name => "ToolkitWait";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts => [];

    /// <summary>Keys append as variadic string pins.</summary>
    public VariadicPinSpec? InputVariadic => new("Key ", 1, PinType.String);

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>ToolkitWaitAny(keys...) → blocks until any named key is present (OR).</summary>
internal sealed class ToolkitWaitAnyFunction : IBuiltinFunction
{
    public string Name => "ToolkitWaitAny";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts => [];

    /// <summary>Keys append as variadic string pins.</summary>
    public VariadicPinSpec? InputVariadic => new("Key ", 1, PinType.String);

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>ToolkitRemove(key) → removes a declared key the workflow owns.</summary>
internal sealed class ToolkitRemoveFunction : IBuiltinFunction
{
    public string Name => "ToolkitRemove";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>ToolkitKeys() → the local names of every declared key.</summary>
internal sealed class ToolkitKeysFunction : IBuiltinFunction
{
    public string Name => "ToolkitKeys";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>ToolkitContains(key) → whether the declared key currently holds a value.</summary>
internal sealed class ToolkitContainsFunction : IBuiltinFunction
{
    public string Name => "ToolkitContains";
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

/// <summary>ToolkitAppend(key, entry) → appends to a key whose role is a stream.</summary>
internal sealed class ToolkitAppendFunction : IBuiltinFunction
{
    public string Name => "ToolkitAppend";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
        new("Entry", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}
