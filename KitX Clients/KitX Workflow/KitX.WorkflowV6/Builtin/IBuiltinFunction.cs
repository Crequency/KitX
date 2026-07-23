namespace KitX.WorkflowV6.Builtin;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// Builtin function descriptor system — ISP-style role split, inherited from
// KitX.WorkflowIR (which in turn inherited it from the discussion that retired
// the v4 fat IBuiltinFunctionDefinition interface).
//
// The split:
//   IBuiltinFunction        — always: identity + ports (the spec)
//   IParserHandler          — if it customises KS parse (control-flow forms)
//   ILoweringHandler        — if it customises AST→IR lowering (default = plain call)
//   IBpRenderHandler        — if it customises IR→BP-node template
//   IBpReverseHandler       — if it can build IR from a BP node (cures hardcoded maps)
//
// No function implements more than it needs. The registry discovers each role
// independently and stores them in separate lookup tables — see
// <see cref="BuiltinFunctionRegistry"/>.
//
// v6 considerations:
//   • ControlFlow is no longer a "terminator with Exec arms" (v5). A v6 control-flow
//     keyword (if/switch/forEach/while/break/continue) is *structural*: it owns
//     its body lexically. The renderer/codegen therefore walk into the body. Whether
//     these are even modelled as "builtin functions" or as first-class IR statement
//     kinds is an open design point (discussion notes §10.7). The descriptor system
//     is preserved here so either path is open.
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
