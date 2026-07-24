namespace KitX.WorkflowV6.Builtin;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// Builtin function descriptor system.
//
// v6 model: every builtin implements only IBuiltinFunction (identity + ports).
// There is no per-role handler split in v6 — all 32 builtins use the default
// parse / lower / codegen / bp-render paths. Control-flow primitives
// (if/switch/forEach/while/break/continue) are NOT routed through the registry
// at all; they are first-class IR statement kinds (see StatementKind).
//
// History: v5.1 had an ISP-style role split (IParserHandler / ILoweringHandler /
// IBpRenderHandler / IBpReverseHandler). v6 removed these (no builtin needed
// custom behaviour) along with the registry's per-role lookup tables.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The behavioural kind of a builtin — drives BP node colouring and codegen.
/// Inherited from v5; the v6 structured model may collapse some of these distinctions.
/// </summary>
public enum FunctionKind
{
    /// <summary>A pure data transform (e.g. StringConcat, JsonGetField, Range). No control flow.</summary>
    Pure,

    /// <summary>
    /// A control-flow primitive (if/switch/forEach/while/break/continue). In v6
    /// these are structural (own their body); whether they remain in the function
    /// registry or become first-class IR statement kinds is open (discussion notes §10.7).
    /// </summary>
    ControlFlow,

    /// <summary>A side-effecting call (Print/Pause/PluginCall). Has data inputs, no return value read.</summary>
    SideEffect,
}

/// <summary>
/// The non-negotiable spec every builtin must provide: its name, its behavioural kind,
/// and its port layout. This is the only interface a function MUST implement.
/// </summary>
public interface IBuiltinFunction
{
    /// <summary>
    /// The registration key — the function name as it appears in KS source
    /// (e.g. "Print", "Range", "StringConcat").
    /// </summary>
    string Name { get; }

    /// <summary>Behavioural kind (Pure / ControlFlow / SideEffect). See <see cref="FunctionKind"/>.</summary>
    FunctionKind Kind { get; }

    /// <summary>Input port specs (data the function consumes), in source order.</summary>
    IReadOnlyList<PortSpec> InputPorts { get; }

    /// <summary>Output port specs (data the function produces). Empty for SideEffect/ControlFlow.</summary>
    IReadOnlyList<PortSpec> OutputPorts { get; }

    /// <summary>Optional variadic input spec (e.g. StringConcat's N extra string pins).</summary>
    VariadicPinSpec? InputVariadic => null;

    /// <summary>Optional variadic output spec.</summary>
    VariadicPinSpec? OutputVariadic => null;
}

/// <summary>
/// A port (pin) descriptor: name + type + relative vertical position on the BP node.
/// Inherited shape from KitX.WorkflowR6.PortSpec.
/// </summary>
public readonly record struct PortSpec(string Name, PinType Type, double RelativeY);
