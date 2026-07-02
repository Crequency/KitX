namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrEdge — a typed control-flow edge between two blocks.
//
// The legacy CFGEdge was a mutable class. The legacy CFGBlock exposed Successors
// as a mutable List<CFGEdge>. Here both are immutable records. The block's edges
// are an ImmutableArray; "mutating" a block produces a new block via `with`.
//
// Edge semantics are unchanged (Sequential/BranchTrue/BranchFalse/LoopBody/
// LoopExit/Break/Switch) — that vocabulary is well-tested and drives the BP Exec
// rendering. What changed is mutability only.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Semantic type of a control-flow edge between blocks.</summary>
public enum IrEdgeType
{
    /// <summary>Sequential fall-through (NextBlock assignment / v5.0 Goto).</summary>
    Sequential,

    /// <summary>Branch condition true → BranchTrue output pin.</summary>
    BranchTrue,

    /// <summary>Branch condition false → BranchFalse output pin.</summary>
    BranchFalse,

    /// <summary>ForLoop true → LoopBody output pin.</summary>
    LoopBody,

    /// <summary>ForLoop false → LoopEnd output pin.</summary>
    LoopExit,

    /// <summary>Break from the enclosing loop.</summary>
    Break,

    /// <summary>Switch arm taken by integer selector. PinName carries "Default" or "0".."N-1".</summary>
    Switch,
}

/// <summary>
/// An immutable typed control-flow edge. <see cref="PinName"/> is the Blueprint
/// output pin this edge maps to ("True", "False", "LoopBody", "LoopEnd", "Exec",
/// or "Default"/"0".."N-1" for Switch); null when not applicable.
/// </summary>
public sealed record IrEdge(
    string FromBlockName,
    string ToBlockName,
    IrEdgeType Type,
    string? PinName);
