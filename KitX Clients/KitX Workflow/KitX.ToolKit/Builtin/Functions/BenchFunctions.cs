namespace KitX.ToolKit.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O builtin descriptors — the author-facing pair for the Bench harness
// edge protocol. Registered via AddBuiltinFunction<T> in AddKitXToolKit, so they
// appear in the BP palette and map 1:1 to BP nodes.
//
// The runtime lives on ExecutionGlobals.Bench (WorkflowV6), fed by the
// ToolKitRunContext the WorkflowRunner extracts from the constant overrides:
//   • BenchIn(name, default) — reads a resolved trigger binding param.
//   • BenchOut(key, value)   — publishes a completion-edge output key.
// Dispatch is by method-name convention (descriptor Name == ExecutionGlobals
// method), same as the Ui*/DataStore* families.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>BenchIn(name, default?) → reads a trigger binding param for this run.</summary>
public sealed class BenchInFunction : IBuiltinFunction
{
    public string Name => "BenchIn";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Name", PinType.String, 20),
        new("Default", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.String, 50),
    ];
}

/// <summary>BenchOut(key, value) → publishes a value on the workflow's completion-edge output packet.</summary>
public sealed class BenchOutFunction : IBuiltinFunction
{
    public string Name => "BenchOut";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Key", PinType.String, 20),
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}
