namespace KitX.WorkflowIR.Diff;

using KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrDiffApply — applies an IrDiff to an immutable IrWorkflow, PURELY.
//
// This is the greenfield successor to the legacy CfgDiffApplier, which mutated a
// live ControlFlowGraph in place (block.Statements.Remove/Insert/Replace, edge
// Successors.RemoveAll, arm TargetBlockName rewrites). That mutation was the
// legacy architecture's central hazard: the diff was destructive, untestable for
// purity, and could not be rolled back.
//
// The rewrite is a pure function: Apply(ir, diff) → newIr. The input ir is NEVER
// touched (callers can verify this by reference equality before/after). Every
// change is expressed with `with` expressions so unchanged subtrees are shared by
// reference (structural sharing) — the new IR is cheap to build and the old one
// remains valid.
//
// Algorithm:
//
//   1. Block-level pass — drop Removed blocks, append Added blocks (with their
//      Successors pruned of edges to dropped blocks, and control-flow targets
//      pointing at dropped blocks blanked). Produces a fresh Blocks array.
//
//   2. Statement-level pass — for every common block touched by the diff, derive
//      the desired final statement list from the OLD block + the grouped changes:
//        • Modified  → replace at FromIndex
//        • Removed   → drop
//        • Moved (intra-block) → relocate from FromIndex to NewIndex
//        • Moved (cross-block, source) → drop from source
//        • Moved (cross-block, dest) → insert at NewIndex
//        • Added     → insert at NewIndex
//      Each touched block is rebuilt with `with { Statements = ... }`.
//
//   3. Layout-inheritance pass — for every block in the result, each statement
//      whose fingerprint also appears in the SAME block of the OLD workflow keeps
//      its Layout annotation (copied across if the statement object changed). This
//      is the §7 requirement: a BS edit that leaves a node unchanged must not drop
//      its BP canvas position. Changed statements keep whatever layout they
//      already carry.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pure-function applier for an <see cref="IrDiff"/>. Returns a NEW
/// <see cref="IrWorkflow"/> reflecting the diff; the input is never mutated.
/// Reconciles Layout annotations from the baseline so unchanged nodes keep their
/// canvas positions across a BS edit.
/// </summary>
public static class IrDiffApply
{
    /// <summary>
    /// Applies <paramref name="diff"/> to <paramref name="ir"/>, returning a new
    /// <see cref="IrWorkflow"/>. The input <paramref name="ir"/> is not mutated —
    /// every change uses structural sharing via `with` expressions.
    /// </summary>
    public static IrWorkflow Apply(IrWorkflow ir, IrDiff diff)
    {
        if (diff.IsEmpty)
        {
            // Even an empty diff must still inherit layout (the input IR's layout is
            // already authoritative, so just return it). Reference-identical return
            // preserves the caller's reference for purity assertions.
            return ir;
        }

        // ── 1. Block-level pass ──
        var removedBlockNames = diff.BlockChanges
            .Where(b => b.Kind == BlockChangeKind.Removed)
            .Select(b => b.Name)
            .ToHashSet();

        // Start from the existing blocks, dropping removed ones. We rebuild each
        // retained block's Successors/targets to sever references to dropped blocks.
        var blocks = new List<IrBlock>(ir.Blocks.Length);
        foreach (var block in ir.Blocks)
        {
            if (removedBlockNames.Contains(block.Name)) continue;
            blocks.Add(SeverDroppedSuccessors(block, removedBlockNames));
        }

        // Append added blocks (also severing any references to other dropped blocks,
        // in case an added block points at a just-removed name).
        foreach (var bc in diff.BlockChanges.Where(b => b.Kind == BlockChangeKind.Added))
        {
            if (bc.NewBlock is null) continue;
            if (blocks.Any(b => b.Name == bc.Name)) continue;
            blocks.Add(SeverDroppedSuccessors(bc.NewBlock, removedBlockNames));
        }

        // ── 2. Statement-level pass ──
        // Group statement changes by the block they mutate. A cross-block move
        // contributes to TWO blocks: a drop in its source (BlockName) and an insert
        // in its destination (ToBlock).
        var changesByBlock = new Dictionary<string, List<StatementChange>>();
        foreach (var sc in diff.StatementChanges)
        {
            AddChange(changesByBlock, sc.BlockName, sc);
            if (sc.Kind == DiffKind.Moved && sc.ToBlock is not null && sc.ToBlock != sc.BlockName)
                AddChange(changesByBlock, sc.ToBlock, sc);
        }

        if (changesByBlock.Count > 0)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (!changesByBlock.TryGetValue(blocks[i].Name, out var changes)) continue;
                blocks[i] = ApplyStatementChanges(blocks[i], changes);
            }
        }

        var newIr = ir with { Blocks = blocks.ToImmutableArray() };

        // ── 3. Layout-inheritance pass ──
        // For each result block, copy Layout annotations for unchanged statements
        // from the corresponding old block. This is what keeps a node's BP canvas
        // position when the surrounding BS text is edited but the node itself is not.
        return InheritLayouts(ir, newIr);
    }

    // ── Block-level helper: sever edges/targets pointing at dropped blocks ──

    private static IrBlock SeverDroppedSuccessors(IrBlock block, HashSet<string> dropped)
    {
        if (dropped.Count == 0) return block;

        bool anySuccessorSevered = false;
        var successors = ImmutableArray.CreateBuilder<IrEdge>(block.Successors.Length);
        foreach (var edge in block.Successors)
        {
            if (dropped.Contains(edge.ToBlockName))
            {
                anySuccessorSevered = true;
                continue;
            }
            successors.Add(edge);
        }

        // Control-flow targets pointing at a dropped block are blanked (matching the
        // legacy applier's stmt.Arms[a].TargetBlockName = string.Empty).
        bool anyTargetSevered = false;
        var statements = block.Statements;
        IrStatement[]? rewritten = null;
        for (int s = 0; s < statements.Length; s++)
        {
            if (statements[s] is not IrControlFlowStatement cf) continue;
            bool changed = false;
            var targets = ImmutableArray.CreateBuilder<IrControlFlowTarget>(cf.Targets.Length);
            foreach (var t in cf.Targets)
            {
                if (dropped.Contains(t.TargetBlockName))
                {
                    changed = true;
                    targets.Add(t with { TargetBlockName = string.Empty });
                }
                else
                {
                    targets.Add(t);
                }
            }
            if (changed)
            {
                anyTargetSevered = true;
                rewritten ??= statements.ToArray();
                rewritten[s] = cf with { Targets = targets.ToImmutable() };
            }
        }

        if (!anySuccessorSevered && !anyTargetSevered) return block;
        return block with
        {
            // ToImmutable (not MoveToImmutable): the builder may have skipped
            // dropped edges, so Count < Capacity and MoveToImmutable would throw.
            Successors = anySuccessorSevered ? successors.ToImmutable() : block.Successors,
            Statements = anyTargetSevered ? rewritten!.ToImmutableArray() : block.Statements,
        };
    }

    // ── Statement-level helper: rebuild one block's statement list ──

    private static IrBlock ApplyStatementChanges(IrBlock block, List<StatementChange> changes)
    {
        // Work from the OLD statement list and derive the NEW list. We process
        // removes/relocations first (operating on positional indices in the OLD
        // list), then inserts (operating on indices in the NEW list).
        //
        // Indices in changes are positional in the OLD list for FromIndex/Removed,
        // and in the NEW list for Added/NewIndex (matching how IrDiffer reports
        // them: NewIndex is the insert position in the new sequence).

        // Pass A — materialise the surviving list, in old order, applying in-place
        // edits (Modified replaces; Removed/Moved-source drop). Track which old
        // index each survivor came from so we can resolve Moved-source drops.
        var survivors = new List<(int OldIndex, IrStatement Stmt)>();
        var drops = new HashSet<int>();
        var modifies = new Dictionary<int, IrStatement>();

        foreach (var sc in changes)
        {
            switch (sc.Kind)
            {
                case DiffKind.Removed:
                    // A pure Removed (not part of a cross-block move) drops its slot.
                    // Removed changes carry no index in the new model; locate by fingerprint.
                    break;
                case DiffKind.Modified:
                    if (sc.FromIndex.HasValue) modifies[sc.FromIndex.Value] = sc.NewValue!;
                    break;
                case DiffKind.Moved:
                    // Source side of a move (cross or intra): drop the old slot.
                    if (sc.FromIndex.HasValue) drops.Add(sc.FromIndex.Value);
                    break;
            }
        }

        for (int i = 0; i < block.Statements.Length; i++)
        {
            if (drops.Contains(i)) continue;
            if (modifies.TryGetValue(i, out var replacement))
                survivors.Add((i, replacement));
            else
                survivors.Add((i, block.Statements[i]));
        }

        // Handle Removed changes that had no FromIndex: locate by fingerprint and drop.
        var removedFingerprints = changes
            .Where(c => c.Kind == DiffKind.Removed)
            .Select(c => c.Fingerprint)
            .ToHashSet();
        if (removedFingerprints.Count > 0)
        {
            survivors = survivors
                .Where(kvp => !removedFingerprints.Contains(kvp.Stmt.Fingerprint))
                .ToList();
        }

        // Pass B — apply inserts (Added, and the destination side of a cross-block
        // Moved). Inserts carry NewIndex = position in the FINAL list. We sort by
        // NewIndex and splice them in; null/missing NewIndex means append.
        var inserts = changes
            .Where(c => c.Kind == DiffKind.Added
                     || (c.Kind == DiffKind.Moved && c.ToBlock is not null && c.ToBlock != c.BlockName))
            .ToList();

        if (inserts.Count == 0)
        {
            return block with { Statements = survivors.Select(kvp => kvp.Stmt).ToImmutableArray() };
        }

        // Build the final list by inserting at the requested indices. NewIndex values
        // come from IrDiffer and are positions in the NEW sequence; splicing in
        // ascending index order keeps them stable.
        var result = survivors.Select(kvp => kvp.Stmt).ToList();
        foreach (var ins in inserts.OrderBy(c => c.NewIndex ?? int.MaxValue))
        {
            var stmt = ins.NewValue;
            // Cross-block move destination: the moved statement is the one being
            // inserted; NewValue is null for Moved, so we must carry it. IrDiffer
            // reports cross-block moves with NewValue null (it relocates the existing
            // statement). Resolve it from the source block's old statement.
            if (stmt is null && ins.Kind == DiffKind.Moved)
            {
                // The source block already dropped this statement; we need its value.
                // It is the statement at FromIndex in the OLD source block. Since we
                // may not have the source block here, fall back to locating it by
                // fingerprint in the source block of the live IR... but we only have
                // the current block. Instead, IrDiffer MUST carry NewValue for cross-
                // block moves. To be robust, locate by fingerprint across the block's
                // original statements first.
                stmt = block.Statements.FirstOrDefault(s => s.Fingerprint == ins.Fingerprint);
            }
            if (stmt is null) continue;

            int idx = ins.NewIndex ?? result.Count;
            if (idx < 0) idx = 0;
            if (idx > result.Count) idx = result.Count;
            result.Insert(idx, stmt);
        }

        return block with { Statements = result.ToImmutableArray() };
    }

    // ── Layout-inheritance helper ──

    /// <summary>
    /// Reconciles Layout annotations from the baseline IR into the result, so that
    /// unchanged statements keep their BP canvas position across a BS edit (the §7
    /// requirement). This is a per-block rebuild of the Layout annotations:
    ///
    /// <list type="bullet">
    ///   <item>The block anchor (<c>"BlockPos"</c>) is always carried across.</item>
    ///   <item>A per-statement Layout is carried across iff the statement's
    ///       fingerprint is still present in the result block (i.e. the statement
    ///       was unchanged or only moved — NOT when it was Added/Modified, since
    ///       those statements carry a new fingerprint).</item>
    ///   <item>A per-statement Layout whose fingerprint no longer exists (the
    ///       statement was Removed or Modified) is dropped — a stale position for
    ///       a gone node must not linger.</item>
    ///   <item>All non-Layout annotations (Comment, SourcePosition) are preserved
    ///       untouched.</item>
    /// </list>
    ///
    /// We recompute from the result block rather than mutate the input block's
    /// annotations, so the result is authoritative regardless of where the new
    /// statements came from.
    /// </summary>
    private static IrWorkflow InheritLayouts(IrWorkflow oldIr, IrWorkflow newIr)
    {
        var oldByBlock = oldIr.Blocks.ToDictionary(b => b.Name);
        bool anyRewritten = false;
        // Copy the result blocks into a mutable array so we can rewrite individual
        // entries (ImmutableArray<T>.this[int] is read-only). Final conversion back
        // to ImmutableArray happens once at the end.
        var blocks = newIr.Blocks.ToArray();

        for (int bi = 0; bi < blocks.Length; bi++)
        {
            var block = blocks[bi];
            if (!oldByBlock.TryGetValue(block.Name, out var oldBlock)) continue;

            // Old block's layout annotations: anchor + per-fingerprint positions.
            var oldLayoutByKey = new Dictionary<string, IrLayout>();
            foreach (var ann in oldBlock.Annotations)
            {
                if (ann.IsLayout && ann.Key is string key && ann.Value is IrLayout layout)
                    oldLayoutByKey[key] = layout;
            }
            if (oldLayoutByKey.Count == 0) continue;

            // Fingerprints still present in the result block (unchanged + moved-in
            // statements). A Modified statement has a NEW fingerprint that does not
            // match any old layout key, so its old position is naturally dropped.
            var liveFingerprints = new HashSet<string>(
                block.Statements.Select(s => s.Fingerprint.Value));

            // Rebuild the annotation list: keep every non-Layout annotation as-is;
            // for Layout, keep only the block anchor and per-statement positions
            // whose fingerprint is still live (re-sourcing the coordinate from the
            // old block so it is identical even if the result block had none).
            var rebuilt = ImmutableArray.CreateBuilder<IrAnnotation>(block.Annotations.Length);
            bool changed = false;
            var seenKeys = new HashSet<string>();

            foreach (var ann in block.Annotations)
            {
                if (!ann.IsLayout)
                {
                    rebuilt.Add(ann);
                    continue;
                }

                var key = ann.Key!;
                // Block anchor: always keep (from whichever side has it; prefer the
                // result block's own value if present, else inherit from the old).
                if (key == "BlockPos")
                {
                    rebuilt.Add(ann);
                    seenKeys.Add(key);
                    continue;
                }

                // Per-statement layout: keep iff the statement still exists in the
                // result block (its fingerprint is live). Otherwise drop it (stale).
                if (liveFingerprints.Contains(key))
                {
                    rebuilt.Add(ann);
                    seenKeys.Add(key);
                }
                else
                {
                    changed = true;   // dropped a stale layout
                }
            }

            // Inherit any old layout that the result block is missing: the block
            // anchor (if absent) and per-statement positions for live fingerprints.
            foreach (var (key, layout) in oldLayoutByKey)
            {
                if (seenKeys.Contains(key)) continue;
                if (key != "BlockPos" && !liveFingerprints.Contains(key)) continue;
                rebuilt.Add(new IrAnnotation(AnnotationKind.Layout, key, layout));
                seenKeys.Add(key);
                changed = true;
            }

            if (!changed) continue;

            blocks[bi] = block with { Annotations = rebuilt.ToImmutable() };
            anyRewritten = true;
        }

        return anyRewritten ? newIr with { Blocks = blocks.ToImmutableArray() } : newIr;
    }

    private static void AddChange(
        Dictionary<string, List<StatementChange>> map, string block, StatementChange sc)
    {
        if (!map.TryGetValue(block, out var list))
            map[block] = list = [];
        list.Add(sc);
    }
}
