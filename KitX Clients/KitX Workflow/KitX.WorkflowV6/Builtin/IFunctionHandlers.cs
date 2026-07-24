// NOTE: These role-specific handler interfaces are design placeholders from v5.1.
// In v6, all 32 builtins use the default lowering/codegen/bp-render path; no builtin
// implements any of these interfaces, and the registry's Get* methods are not called.
// TODO: decide whether to wire up dispatch (as v5.1 did) or remove the dead code.

namespace KitX.WorkflowV6.Builtin;

using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// Role handler interfaces — optional capabilities a function implements on top of
// IBuiltinFunction. Inherited from KitX.WorkflowIR's IFunctionHandlers; signatures
// re-typed for the v6 IR (Statement, not IrStatement) and AST (KsNode/KsCall, not
// KsCall). Each handler receives only what its role needs.
//
// The interface surface is fixed here so the registry / lens / backend can be
/// wired against stable types from day one; concrete implementations ship in the
/// implementation phase.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Customises KS parse for control-flow forms (if/switch/forEach/while/break/continue).
/// Pure functions do NOT implement this — the default parser dispatches their
/// call verbatim. Whether v6 control-flow keywords even route through this handler or
/// are special-cased by the indented parser is open (discussion notes §10.8).
/// </summary>
public interface IParserHandler
{
    /// <summary>Parses a control-flow invocation. Signature refined during implementation.</summary>
    object ParseInvocation(KsCall call, int sourceLine);
}

/// <summary>
/// Customises AST→IR lowering. The default lowering builds a PipelineStatement from
/// the call's expanded arguments; functions that need custom lowering (e.g. to inject
/// runtime-bound names or split into multiple statements) implement this.
/// </summary>
public interface ILoweringHandler
{
    /// <summary>Lowers a KS call into IR statement(s). Signature refined during implementation.</summary>
    IReadOnlyList<Statement> LowerToIr(
        KsCall call,
        IReadOnlyList<string> expandedArgs,
        string? assignedVar,
        LoweringContext ctx);
}

/// <summary>
/// Customises IR→BP-node rendering. The default rendering builds a standard
/// BuiltinFunctionNode from the spec's ports; functions with non-standard node
/// shapes implement this.
/// </summary>
public interface IBpRenderHandler
{
    /// <summary>Builds the BP node template for this IR statement.</summary>
    BpNodeTemplate RenderToBp(Statement stmt);
}

/// <summary>
/// Builds an IR statement FROM a BP node — the reverse of IBpRenderHandler. Each
/// function declares its own BP-name → IR translation, so adding a function never
/// requires editing a central map.
/// </summary>
public interface IBpReverseHandler
{
    /// <summary>The BP canvas name(s) this handler reverses (e.g. "Loop" for ForLoop).</summary>
    IReadOnlyCollection<string> BpNames { get; }

    /// <summary>Builds an IR statement from a BP node's information.</summary>
    Statement BuildFromBp(BpNodeInfo node, string lexicalPath);
}
