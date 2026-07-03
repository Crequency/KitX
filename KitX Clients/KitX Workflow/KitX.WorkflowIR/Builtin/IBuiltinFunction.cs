namespace KitX.WorkflowIR.Builtin;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// Builtin function descriptor system — the ISP replacement for the legacy
// IBuiltinFunctionDefinition fat interface.
//
// The legacy design had ONE compound interface (IBuiltinFunctionDefinition) that
// combined three roles (Spec + Lowering + Emitter) plus a v5.1 control-flow
// bolt-on, and required every function to implement it even if it only needed
// one role. Default interface Methods papered over the gaps, but the result was
// a fat contract where a Print function had to "implement" lowering/emitter/
// flow-control fields it never used, and reading a function's true capabilities
// meant inspecting default overrides at runtime.
//
// The new design splits capabilities into focused role interfaces. A function
// implements ONLY the roles it needs:
//
//   IBuiltinFunction        — always: identity + ports (the spec)
//   IParserHandler          — if it customises BS parse (control-flow forms)
//   ILoweringHandler        — if it customises AST→IR lowering (default = plain call)
//   ICodeGenHandler         — if it customises IR→C# emission (default = GInvoke)
//   IBpRenderHandler        — if it customises IR→BP-node template
//   IBpReverseHandler       — if it can build IR from a BP node (cures hardcoded maps)
//
// No function implements more than it needs. The registry discovers each role
// independently and stores them in separate lookup tables.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The behavioural kind of a builtin — drives BP node colouring and codegen.</summary>
public enum FunctionKind
{
    /// <summary>
    /// A pure data transform (e.g. StringConcat, JsonGetField). No control-flow side,
    /// returns a value. Maps to a BP node with data input/output pins.
    /// </summary>
    Pure,

    /// <summary>
    /// A control-flow terminator (Branch/ForLoop/Switch/Goto/Break/Exit). Ends the
    /// current block; carries Exec arms. Per the v5.0 §7 rule, control-flow nodes have
    /// NO data output pins (they terminate the block, nothing consumes a return value).
    /// </summary>
    ControlFlow,

    /// <summary>
    /// A side-effecting call (Print/Pause/PluginCall). Has data inputs but no return
    /// value the program reads (Print returns void). Maps to an Exec-in / Exec-out node.
    /// </summary>
    SideEffect,
}

/// <summary>
/// The non-negotiable spec every builtin must provide: its name, its behavioural kind,
/// and its port layout. This is the only interface a function MUST implement.
/// </summary>
public interface IBuiltinFunction
{
    /// <summary>
    /// The registration key — the function name as it appears in BS source
    /// (e.g. "Print", "Branch", "ForLoop", "StringConcat").
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

    /// <summary>Optional variadic output spec (e.g. Switch's N arms).</summary>
    VariadicPinSpec? OutputVariadic => null;
}

/// <summary>
/// A port (pin) descriptor: name + type + relative vertical position on the BP node.
/// Replaces the legacy PinDescriptor with a leaner value type (the legacy one carried
/// direction redundantly — direction is implied by Input/OutputPorts membership).
/// </summary>
public readonly record struct PortSpec(string Name, PinType Type, double RelativeY);
