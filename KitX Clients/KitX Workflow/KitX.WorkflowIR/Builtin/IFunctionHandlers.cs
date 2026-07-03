namespace KitX.WorkflowIR.Builtin;

using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Ir.Ast;
using KitX.WorkflowIR.Ir.Lowering;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// Role handler interfaces — optional capabilities a function implements on top of
// IBuiltinFunction. See IBuiltinFunction.cs for the design rationale.
//
// Each handler receives only what its role needs — no ForwardConversionState
// grab-bag, no LowerContext that leaks the whole lowering's mutable set. The
// LoweringContext (Phase 2) is the single controlled channel for lowering-time
// mutation (PubVar capacitor allocation).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Customises BS parse for control-flow forms (Branch/ForLoop/Switch/Goto/Break/Exit).
/// Pure functions do NOT implement this — the default parser dispatches their call
/// verbatim. Control-flow functions use it to validate arm structure and produce a
/// <see cref="FlowControlStatement"/> carrying the parsed arms.
/// </summary>
public interface IParserHandler
{
    /// <summary>
    /// Parses a control-flow invocation into a flow-control statement. Called by the
    /// BS parser when it sees this function's name in statement position.
    /// </summary>
    /// <param name="call">The parsed BSCall AST node.</param>
    /// <param name="sourceLine">1-based source line (for diagnostics).</param>
    FlowControlStatement ParseInvocation(BSCall call, int sourceLine);
}

/// <summary>
/// Customises AST→IR lowering. The default lowering (applied when a function does
/// NOT implement this) builds a single <see cref="IrPipelineStatement"/> from the
    /// call's expanded arguments. Functions that need custom lowering (e.g. to inject
    /// PubVar capacitors or split into multiple statements) implement this.
/// </summary>
public interface ILoweringHandler
{
    /// <summary>
    /// Lowers a BS call into IR statement(s). <paramref name="expandedArgs"/> are the
    /// call's arguments after nested-call expansion (each is a flat PubVar/literal/identifier
    /// string). <paramref name="assignedVar"/> is the PubVar the result should bind to (null
    /// for bare call statements).
    /// </summary>
    /// <returns>One or more IR statements (typically one; ForLoop-like forms may emit more).</returns>
    IReadOnlyList<IrStatement> LowerToIr(
        BSCall call,
        IReadOnlyList<string> expandedArgs,
        string? assignedVar,
        LoweringContext ctx);
}

/// <summary>
/// Customises IR→C# emission. The default emission (applied when a function does NOT
/// implement this) generates <c>G.&lt;Name&gt;(args)</c>. Functions with special codegen
/// needs (control-flow rewriting NextBlock, value assignment, plugin dispatch) implement this.
/// </summary>
public interface ICodeGenHandler
{
    /// <summary>
    /// Emits C# statements for an IR statement. <paramref name="ctx"/> provides the
    /// Roslyn builder helpers (literal boxing, GInvoke, value assignment, NextBlock
    /// assignment) — the same CSEmitContext surface the legacy functions used.
    /// </summary>
    IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx);
}

/// <summary>
/// Customises IR→BP-node rendering. The default rendering (applied when a function
/// does NOT implement this) builds a standard BuiltinFunctionNode from the spec's
/// ports. Functions with non-standard node shapes implement this.
/// </summary>
public interface IBpRenderHandler
{
    /// <summary>
    /// Builds the BP node template for this IR statement. Used by the BpGraphLens when
    /// projecting IR → Blueprint graph.
    /// </summary>
    /// <returns>A description of the node to render (ports, title, colour hint).</returns>
    BpNodeTemplate RenderToBp(IrStatement stmt);
}

/// <summary>
/// Builds an IR statement FROM a BP node — the reverse of <see cref="IBpRenderHandler"/>.
/// This is the cure for the legacy <c>BpEditApplier.DashboardToCfgName</c> hardcoded
/// dictionary: instead of a central string map, each function declares its own BP-name
/// → IR-translation, so adding a function never requires editing a shared map.
/// </summary>
/// <remarks>
/// Implemented by functions whose BP display name differs from their BS/IR name
/// (e.g. ForLoop renders as "Loop" on the canvas). Most functions need not implement
/// this — when BP name == BS name the default identity mapping applies.
/// </remarks>
public interface IBpReverseHandler
{
    /// <summary>The BP canvas name(s) this handler reverses (e.g. "Loop" for ForLoop).</summary>
    IReadOnlyCollection<string> BpNames { get; }

    /// <summary>
    /// Builds an IR statement from a BP node's information. Used by BpEditTranslator
    /// when translating BP edits into IR diffs.
    /// </summary>
    IrStatement BuildFromBp(BpNodeInfo node, string blockName);
}

// ── Lightweight value types used by the render/reverse handlers. ──

/// <summary>
/// A description of a BP node to render: title, kind hint, port layout. Built by
/// <see cref="IBpRenderHandler"/>; consumed by the BpGraphLens (Phase 8).
/// </summary>
public sealed record BpNodeTemplate(
    string Title,
    FunctionKind Kind,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs);

/// <summary>
/// Information about a BP node being reverse-translated to IR: its canvas title,
/// its argument values (port → string), and its control-flow arm targets. Built by
/// the BpEditTranslator; consumed by <see cref="IBpReverseHandler.BuildFromBp"/>.
/// </summary>
public sealed record BpNodeInfo(
    string Title,
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<(string PinName, string TargetBlock)> Arms);
