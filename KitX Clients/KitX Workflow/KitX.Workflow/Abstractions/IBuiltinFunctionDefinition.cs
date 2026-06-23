using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.Models;

namespace KitX.Workflow.BlockScripting;

// ─────────────────────────────────────────────────────────────────────────────
// v5.0 BuiltinFunction registration contract — split into focused role interfaces
// per ISP. IBuiltinFunctionDefinition composes all four. Each of the 25 builtins
// overrides only the members it needs; defaults cover the common cases.
//
// Role breakdown:
//   IBuiltinFunctionSpec      — immutable descriptor (what the function IS)
//   IBuiltinFunctionLowering  — AST→CFG lowering (how the function maps to CFG)
//   IBuiltinFunctionEmitter   — CFG→C# code emission (how it compiles)
//   IBuiltinFunctionExporter  — BP→BS reverse conversion (how it serializes back)
// ─────────────────────────────────────────────────────────────────────────────

// ═════════════════════════════════════════════════════════════════════════════
// Layer A: Immutable spec — "what this function is"
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Immutable descriptor for a builtin function: identity, pins, flow-control shape.
/// Separate from lowering/emission so that metadata queries (e.g. is this a control-flow
/// function?) don't require loading the full definition.
/// </summary>
public interface IBuiltinFunctionSpec
{
    /// <summary>BlockScript source name (e.g. "Print", "Branch").</summary>
    string FunctionName { get; }

    /// <summary>Display name shown in the Blueprint editor.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this function is non-extractable — its call must stay inline
    /// (not hoisted to a PubVar assignment). Typical for side-effecting functions
    /// (Print, Pause) and control-flow functions.
    /// </summary>
    bool IsNonExtractable { get; }

    /// <summary>
    /// The control-flow graph shape. null = value-producing function (default).
    /// Non-null = control-flow function (Branch/ForLoop/Switch/Goto/Break).
    /// Derived: <c>IsFlowControl => FlowControlShape != null</c>.
    /// </summary>
    FlowControlType? FlowControlShape => null;

    /// <summary>Input pin descriptors (includes Exec for control-flow functions).</summary>
    IReadOnlyList<PinDescriptor> InputPins { get; }

    /// <summary>Output pin descriptors (only Execution for control-flow functions per §7).</summary>
    IReadOnlyList<PinDescriptor> OutputPins { get; }

    /// <summary>Variadic input spec (null = fixed). StringConcat overrides this.</summary>
    VariadicPinSpec? InputVariadic => null;

    /// <summary>Variadic output spec (null = fixed). Switch overrides this.</summary>
    VariadicPinSpec? OutputVariadic => null;

    /// <summary>
    /// Per-statement dynamic output pins. Default returns <see cref="OutputPins"/>.
    /// Switch overrides to emit [Default, 0, 1, ..., N-1] based on <see cref="CFGStatement.Arms"/>.
    /// </summary>
    IReadOnlyList<PinDescriptor> GetOutputPinsFor(CFGStatement stmt) => OutputPins;
}

// ═════════════════════════════════════════════════════════════════════════════
// Layer B: AST→CFG lowering — "how this function maps to CFG statements"
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// AST→CFG lowering contract. Each builtin describes how its parsed <see cref="BSCall"/>
/// becomes one or more <see cref="CFGStatement"/>s.
/// </summary>
public interface IBuiltinFunctionLowering
{
    /// <summary>
    /// Extract a typed <see cref="BlockStatement"/> from the parsed <see cref="BSCall"/> AST.
    /// Control-flow functions override this to produce <see cref="FlowControlStatement"/>.
    /// Value-producing functions return null (default) — the parser wraps as ExpressionStatement.
    /// </summary>
    BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

    /// <summary>
    /// Lower the expanded call into CFG statements. The default implementation builds a single
    /// generic <see cref="CFGStatement"/> via <see cref="CfgStatementBuilder"/>.
    /// Override for functions that need custom PubVar synthesis or multi-statement lowering.
    /// </summary>
    /// <param name="invoke">The parsed call AST.</param>
    /// <param name="expandedArgs">Arguments after nested-call expansion (flat string form).</param>
    /// <param name="ctx"><see cref="LowerContext"/> with shared lowering services.</param>
    /// <param name="context">The forward conversion state for the current pass.</param>
    /// <param name="assignedVar">When non-null, the call's result should be written to this PubVar.</param>
    List<CFGStatement> LowerToCFG(
        BSCall invoke,
        IReadOnlyList<string> expandedArgs,
        LowerContext ctx,
        ForwardConversionState context,
        string? assignedVar)
        => new()
        {
            ctx.Build(b =>
            {
                b.FlowControlShape = ((IBuiltinFunctionSpec)this).FlowControlShape;
                b.FunctionName = ((IBuiltinFunctionSpec)this).FunctionName;
                b.Arguments = expandedArgs.ToList();
                b.PubVarTarget = assignedVar;
                b.SourceText = invoke.SourceText;
            })
        };
}

// ═════════════════════════════════════════════════════════════════════════════
// Layer C: CFG→C# emission — "how this function compiles to C#"
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// CFG→C# code emission contract. Generates Roslyn <see cref="StatementSyntax"/> nodes
/// for the compiled script assembly.
/// </summary>
public interface IBuiltinFunctionEmitter
{
    /// <summary>
    /// Emit C# statements for this CFG statement. Default uses the generic
    /// <c>G.{FunctionName}(args)</c> form with optional PubVar assignment wrapper.
    /// Control-flow functions override with custom emission (Branch/ForLoop/etc.).
    /// </summary>
    List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => ctx.EmitDefault(stmt);
}

// ═════════════════════════════════════════════════════════════════════════════
// Layer D: BP→BS reverse export — "how a blueprint node becomes BS text"
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// BP→BS reverse conversion contract. Converts a Blueprint node back to a BlockScript
/// statement for round-trip fidelity.
/// </summary>
public interface IBuiltinFunctionExporter
{
    /// <summary>Convert a Blueprint node to a BlockScript statement (reverse direction).</summary>
    BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper) => null;

    /// <summary>
    /// Output arm descriptors for control-flow edges. Non-control-flow functions return empty.
    /// </summary>
    IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}

// ═════════════════════════════════════════════════════════════════════════════
// Composite interface — the registration key for BuiltinFunctionRegistry
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Composite builtin function registration contract. Composes the four role interfaces
/// (Spec, Lowering, Emitter, Exporter) plus Blueprint-node behaviour.
/// Each of the 25 builtin implementations overrides only the members it needs;
/// defaults cover the common cases.
/// </summary>
public interface IBuiltinFunctionDefinition
    : IBuiltinFunctionSpec, IBuiltinFunctionLowering, IBuiltinFunctionEmitter, IBuiltinFunctionExporter
{
    // ─── Blueprint node behaviour ──────────────────────────────────────────

    /// <summary>
    /// The Blueprint node kind to create for this builtin.
    /// Default <see cref="BuiltinNodeKind.BuiltinFunction"/>.
    /// PluginCallWithTarget overrides to <see cref="BuiltinNodeKind.Call"/>.
    /// </summary>
    BuiltinNodeKind NodeKind => BuiltinNodeKind.BuiltinFunction;

    /// <summary>
    /// Whether to auto-synthesize a PubVar for unconsumed output data edges
    /// (BP→CFG direction, hand-built blueprint scenario). Default false.
    /// </summary>
    bool AutoSynthesizePubVar => false;

    /// <summary>
    /// Node reuse key for CFG2BPConverter dedup. Default null (each statement → own node).
    /// Pure value-producing functions may override to return <see cref="CFGStatement.Fingerprint"/>.
    /// </summary>
    string? GetReuseKey(CFGStatement stmt) => null;

    /// <summary>
    /// Post-creation configuration for the Blueprint node. Default: identity pass-through.
    /// </summary>
    BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

    /// <summary>
    /// Post-creation callback for control-flow nodes. Override to register deferred exec edges
    /// to <see cref="ForwardConversionState.DeferredEdges"/>.
    /// </summary>
    void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context) { }

    /// <summary>
    /// Whether this statement terminates the current block.
    /// True for all control-flow functions (Branch/ForLoop/Switch/Goto/Break).
    /// </summary>
    bool IsBlockTerminator => false;
}

/// <summary>
/// Builtin function's Blueprint node kind. See <see cref="IBuiltinFunctionDefinition.NodeKind"/>.
/// </summary>
public enum BuiltinNodeKind
{
    /// <summary>Standard builtin function node (pins from spec).</summary>
    BuiltinFunction,

    /// <summary>Call node carrying PluginName/TargetDevice (PluginCallWithTarget).</summary>
    Call,
}
