namespace KitX.WorkflowV6.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// LoweringContext — the controlled channel for lowering-time mutation.
//
// Inherited concept from KitX.WorkflowIR.Ir.Lowering.LoweringContext: lowering a KS
// AST into the structured IR sometimes needs to allocate side resources (runtime-bound
// variable names, PubVar capacitors, ...). Rather than passing the whole mutable
// lowering's state to every builtin, the LoweringContext exposes only the operations
// a builtin legitimately needs.
//
/// The concrete surface is filled in during the implementation phase. The placeholder
/// type exists now so <see cref="Builtin.ILoweringHandler"/> has a stable parameter
/// type to compile against.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Controlled lowering-time mutation channel. Refined during implementation.</summary>
public sealed class LoweringContext
{
    /// <summary>Allocates a unique runtime name (used for forEach element bindings, etc.).</summary>
    public string AllocateRuntimeName(string hint) =>
        throw new NotImplementedException("LoweringContext is a placeholder; filled in during implementation.");
}
