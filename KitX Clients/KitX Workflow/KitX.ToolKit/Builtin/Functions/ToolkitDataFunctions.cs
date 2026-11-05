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

/// <summary>
/// ToolkitClearRegion(region) → removes every key declared in that region (design Q65 §5.5.2).
/// </summary>
/// <remarks>
/// <para><b>Why regions and not key lists.</b> The three-tier cleanup table has one grouped form,
/// and a region is it: after Q64 removed the control list, a region is the only collection the
/// server still recognises. Passing key names would put key names into the script, which is the
/// opposite of "declaration is the field".</para>
///
/// <para>The motivating case is a real one: "scan one book, then drop this transaction but keep the
/// running totals" could not be written before — <c>ToolkitRemove</c> takes one key and
/// <c>ToolkitClear</c> takes everything.</para>
/// </remarks>
internal sealed class ToolkitClearRegionFunction : IBuiltinFunction
{
    public string Name => "ToolkitClearRegion";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Region", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Removed", PinType.Integer, 50),
    ];
}

/// <summary>
/// ToolkitClear() → removes every key this toolkit declares (design Q37 §5.5.2).
/// </summary>
/// <remarks>
/// The whole-table tier, for the user's "start over" rather than for orchestration. It takes no
/// arguments on purpose: a parameterised clear would be "key names in source", which Q65 rejected.
/// </remarks>
internal sealed class ToolkitClearFunction : IBuiltinFunction
{
    public string Name => "ToolkitClear";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts => [];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Removed", PinType.Integer, 50),
    ];
}
