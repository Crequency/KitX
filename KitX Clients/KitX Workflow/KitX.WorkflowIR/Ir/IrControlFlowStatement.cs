namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrControlFlowStatement — unified control-flow statement with a single Targets list.
//
// The legacy CFGStatement expressed control flow through a quartet of overlapping
// fields: Arms (a List<BranchArm>), TrueBlockName, FalseBlockName, and the hidden
// IsLoopback flag. Each control-flow builtin kind (Branch/ForLoop/Switch/Goto/
// Break/Exit) used a different combination, and consumers had to know the shape.
//
// The new model is a single sealed record with:
//   • Op — which control-flow builtin this is (the discriminant).
//   • Arguments — the call's flat arguments (e.g. Branch's condition source).
//   • Targets — a uniform list of (pinName, targetBlock) pairs. Branch has two
//     (True/False), ForLoop has two (LoopBody/LoopEnd), Goto has one (Exec),
//     Switch has N (Default + 0..N-1), Break/Exit have zero.
//
// IsLoopback is gone — v5.0 already removed LoopbackToCondition; loop re-entry is
// a plain Sequential edge.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which control-flow builtin this statement represents.</summary>
public enum ControlFlowOp
{
    /// <summary>Branch(cond) — transfers to Targets[True] or Targets[False].</summary>
    Branch,

    /// <summary>ForLoop(...) — iterates Targets[LoopBody], exits Targets[LoopEnd].</summary>
    ForLoop,

    /// <summary>Switch(selector) — indexed dispatch over Targets (Default + 0..N-1).</summary>
    Switch,

    /// <summary>Goto("blockName") — unconditional transfer; Targets has one (Exec) entry.</summary>
    Goto,

    /// <summary>Break — exits the enclosing loop; Targets is empty (handled by the loop).</summary>
    Break,

    /// <summary>Exit — terminates the script; Targets is empty.</summary>
    Exit,
}

/// <summary>
/// One outgoing control-flow target: a (pin name, target block) pair. The pin name
/// is the Blueprint output pin this arm maps to ("True", "False", "LoopBody",
/// "LoopEnd", "Exec", "Default", or "0".."N-1" for Switch).
/// </summary>
public sealed record IrControlFlowTarget(string PinName, string TargetBlockName);

/// <summary>
/// A control-flow terminator statement. Unified across all six control-flow builtins
/// via <see cref="Op"/> + <see cref="Targets"/>; no overlapping positional fields.
/// </summary>
public sealed record IrControlFlowStatement : IrStatement
{
    /// <summary>Which control-flow builtin this statement is.</summary>
    public required ControlFlowOp Op { get; init; }

    /// <summary>Function name as it appears in BS (e.g. "Branch", "ForLoop", "Goto").</summary>
    public required string FunctionName { get; init; }

    /// <summary>Full dotted method path (only for plugin-qualified calls; usually null here).</summary>
    public string? FullFunctionName { get; init; }

    /// <summary>
    /// Flattened argument strings (no nested calls remain). For Branch this is the
    /// condition source PubVar; for ForLoop the loop bounds; for Switch the integer
    /// selector; for Goto the target block name; etc.
    /// </summary>
    public ImmutableArray<string> Arguments { get; init; } = [];

    /// <summary>
    /// The outgoing control-flow targets. See <see cref="ControlFlowOp"/> for the per-op
    /// layout. Empty for Break/Exit.
    /// </summary>
    public ImmutableArray<IrControlFlowTarget> Targets { get; init; } = [];

    // ── Equality: ImmutableArray defaults to reference equality, so override to
    // compare Arguments/Targets by content.

    public bool Equals(IrControlFlowStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Fingerprint.Equals(other.Fingerprint)
            && Comment == other.Comment
            && SourceLine == other.SourceLine
            && Op == other.Op
            && FunctionName == other.FunctionName
            && FullFunctionName == other.FullFunctionName
            && Arguments.SequenceEqual(other.Arguments)
            && Targets.SequenceEqual(other.Targets);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
        hash.Add(SourceLine);
        hash.Add(Op);
        hash.Add(FunctionName);
        hash.Add(FullFunctionName);
        foreach (var a in Arguments) hash.Add(a);
        foreach (var t in Targets) hash.Add(t);
        return hash.ToHashCode();
    }
}
