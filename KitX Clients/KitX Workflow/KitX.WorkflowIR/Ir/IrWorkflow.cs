namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrWorkflow — the top-level immutable container. The single source of truth.
//
// Replaces legacy ControlFlowGraph. Three things were deliberately dropped:
//
//   1. PubVarCounter — a process counter (1, 2, 3, …) that lived on the model.
//      It was a lowering-time allocator concern, not IR state. It now lives in
//      PubVarAllocator (the lowering layer), so two semantically identical IRs
//      are equal regardless of which capacitor numbers the lowering happened to
//      reach.
//
//   2. DebugStatementToNodeId — a debug-pipeline mapping inlined into the model.
//      Debug correlation is recomputed when a session enters debug mode; it does
//      not belong in the IR.
//
//   3. The EntryBlock denormalisation — the entry block is just Blocks[0] with
//      Kind == Entry; no separate field to keep in sync.
//
// Constants/Globals are keyed dictionaries keyed by name (stable identity), so
// re-ordering the source #ConstBlock does not change IR equality. Helper
// functions come from the Contract (HelperFunction) unchanged.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The immutable workflow IR — the canonical representation that BS, BP, and the
/// execution backend all project from / write back to. Equality is structural:
/// two workflows with the same blocks/constants/globals/helpers are equal, and
/// canvas layout (in Annotations) does not affect equality.
/// </summary>
public sealed record IrWorkflow
{
    /// <summary>Name of the entry block (always the first block). Defaults to "#MainBlock".</summary>
    public string MainBlockName { get; init; } = "#MainBlock";

    /// <summary>
    /// All blocks, ordered: the entry block first, then named blocks in definition
    /// order. Order matters for BS rendering but not for equality (equality is
    /// by-name-keyed content).
    /// </summary>
    public ImmutableArray<IrBlock> Blocks { get; init; } = ImmutableArray<IrBlock>.Empty;

    /// <summary>Constants from #ConstBlock, keyed by name.</summary>
    public ImmutableDictionary<string, IrConstant> Constants { get; init; }
        = ImmutableDictionary<string, IrConstant>.Empty;

    /// <summary>Global mutable variables from #PubVarBlock, keyed by name.</summary>
    public ImmutableDictionary<string, IrGlobalVar> GlobalVars { get; init; }
        = ImmutableDictionary<string, IrGlobalVar>.Empty;

    /// <summary>Helper functions available to the script (from Contract, unchanged).</summary>
    public ImmutableArray<HelperFunction> HelperFunctions { get; init; }
        = ImmutableArray<HelperFunction>.Empty;

    /// <summary>The entry block (the first block with Kind == Entry).</summary>
    public IrBlock EntryBlock =>
        Blocks.Length > 0 ? Blocks[0] : throw new InvalidOperationException("IrWorkflow has no blocks.");

    /// <summary>Looks up a block by name.</summary>
    public IrBlock? GetBlock(string name) =>
        Blocks.FirstOrDefault(b => b.Name == name);

    // ── Equality: ImmutableArray/Dictionary default Equals is reference equality,
    // so the record-synthesised Equals would treat two structurally-identical
    // workflows as unequal. Override to compare collections by content.
    //
    // Note: IrBlock.Equals already excludes Annotations, so block comparison here
    // is semantic (canvas positions do not affect workflow equality).

    public bool Equals(IrWorkflow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (MainBlockName != other.MainBlockName) return false;
        if (!Blocks.SequenceEqual(other.Blocks)) return false;
        if (!HelperFunctions.SequenceEqual(other.HelperFunctions)) return false;
        if (Constants.Count != other.Constants.Count) return false;
        foreach (var (k, v) in Constants)
            if (!other.Constants.TryGetValue(k, out var v2) || !v.Equals(v2)) return false;
        if (GlobalVars.Count != other.GlobalVars.Count) return false;
        foreach (var (k, v) in GlobalVars)
            if (!other.GlobalVars.TryGetValue(k, out var v2) || !v.Equals(v2)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MainBlockName);
        foreach (var b in Blocks) hash.Add(b);
        foreach (var h in HelperFunctions) hash.Add(h);
        foreach (var (k, v) in Constants) { hash.Add(k); hash.Add(v); }
        foreach (var (k, v) in GlobalVars) { hash.Add(k); hash.Add(v); }
        return hash.ToHashCode();
    }
}
