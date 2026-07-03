namespace KitX.Workflow.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpEditTranslator — translates a stream of BP canvas edits (BpEditAction) into
// an immutable IrDiff, against the baseline IR the canvas is projecting from.
//
// This is the greenfield successor to the legacy BpEditApplier. Three things
// changed vs the legacy design:
//
//   1. Edits become an IrDiff, NOT a direct mutation. The legacy applier mutated
//      a live ControlFlowGraph in place (the central hazard of the old arch).
//      Here, Translate returns a value IrDiff; the caller (SyncService, Phase 9)
//      is responsible for IrDiffApply.Apply(baseline, diff). Purity + testability.
//
//   2. BP-name → IR-name mapping goes through the registry, NOT a hardcoded
//      dictionary. The legacy BpEditApplier carried:
//          private static readonly Dictionary<string,string> DashboardToCfgName
//              = { ["Loop"] = "ForLoop" };
//      Every new BP alias needed a hand-edit to that shared map. Here, each
//      function declares its own alias via IBpReverseHandler.BpNames, registered
//      once; ResolveBpName(bpName) queries registry.GetBpReverseByBpName(bpName)
//      and falls back to identity when no handler claims the alias. There is NO
//      DashboardToCfgName field anywhere in this file (a grep test asserts it).
//
//   3. Statement lookup is by DeriveStableId (content-derived), not by random
//      Guid. A DeleteNode/SetNodeArgument arriving from the canvas carries the
//      node id the renderer assigned; we correlate it back to the IR statement
//      via the same BpNodeIds scheme, so the correlation survives a re-parse.
//
// Data edges (ConnectData/Disconnect): the current IrDiff is purely semantic
// (block + statement changes); it has no edge-level operations. These two
// actions are accepted and produce an EMPTY diff with a TODO marker — BP data
// wires are a pure function of which statements write which variables, so once
// the statement diff is applied and re-rendered, the wires re-derive. Encoding
// them as separate diff ops is Phase 9+ work.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Translates <see cref="BpEditAction"/>s into an <see cref="IrDiff"/> against a
/// baseline <see cref="IrWorkflow"/>. Produces a value diff (never mutates the
/// baseline). BP-name → IR-name resolution goes through the injected registry.
/// </summary>
public sealed class BpEditTranslator
{
    private readonly BuiltinFunctionRegistry _registry;

    public BpEditTranslator(BuiltinFunctionRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Convenience: translate a single action.</summary>
    public IrDiff Translate(IrWorkflow baseline, BpEditAction action) =>
        Translate(baseline, new[] { action });

    /// <summary>
    /// Translates a batch of BP edits into one merged <see cref="IrDiff"/>. The
    /// caller (SyncService, Phase 9) applies the diff to <paramref name="baseline"/>
    /// via <see cref="IrDiffApply"/>(...). The baseline is never mutated.
    /// </summary>
    public IrDiff Translate(IrWorkflow baseline, IReadOnlyList<BpEditAction> actions)
    {
        var blockChanges = ImmutableArray.CreateBuilder<BlockChange>();
        var statementChanges = ImmutableArray.CreateBuilder<StatementChange>();
        bool positionsChanged = false;

        // Index statement locations by node id, so DeleteNode/SetNodeArgument/
        // SetControlFlowArm/MoveNodePosition can resolve a canvas node id back to
        // its (block, ordinal, statement) without re-walking the IR each time.
        var stmtLocById = IndexStatementLocations(baseline);

        foreach (var action in actions)
        {
            switch (action)
            {
                case AddNodeInBlock a:
                    TranslateAddNode(a, statementChanges);
                    break;
                case DeleteNode d:
                    TranslateDeleteNode(stmtLocById, d, statementChanges);
                    break;
                case MoveNodeToBlock m:
                    TranslateMoveNode(stmtLocById, baseline, m, statementChanges);
                    break;
                case SetNodeArgument sa:
                    TranslateSetArgument(stmtLocById, sa, statementChanges);
                    break;
                case ConnectData:
                case Disconnect:
                    // TODO(Phase 9+): IrDiff currently has no edge-level ops. BP data
                    // wires re-derive from the statement diff on re-render, so an empty
                    // contribution here is correct for now; a future IrDiff extension
                    // would carry an explicit EdgeChange for these.
                    break;
                case SetControlFlowArm scf:
                    TranslateSetControlFlowArm(stmtLocById, scf, statementChanges);
                    break;
                case AddBlock ab:
                    TranslateAddBlock(ab, blockChanges);
                    break;
                case RenameBlock rb:
                    TranslateRenameBlock(rb, blockChanges);
                    break;
                case DeleteBlock db:
                    TranslateDeleteBlock(db, blockChanges);
                    break;
                case MoveNodePosition:
                    // Pure view state — IrDiff deliberately carries no view state (see
                    // its header). Record that a re-layout is needed; coordinates are
                    // persisted separately via ApplyPosition.
                    positionsChanged = true;
                    break;
            }
        }

        // If only positions changed (no semantic changes), return an empty diff —
        // the renderer re-derives coordinates from annotations, and the caller
        // persists the new annotation out-of-band via ApplyPosition.
        var diff = new IrDiff
        {
            BlockChanges = blockChanges.ToImmutable(),
            StatementChanges = statementChanges.ToImmutable(),
        };

        // positionsChanged is reflected indirectly via StatementChange.Moved; for a
        // pure MoveNodePosition batch (no semantic changes) the diff is empty and the
        // caller drives the coordinate update via ApplyPosition.
        _ = positionsChanged;
        return diff;
    }

    // ───────────────────────────────────────────────────────────────────────
    // AddNodeInBlock → StatementChange(Added).
    // The BP node kind is a BP canvas name; resolve it to an IR function name via
    // the registry (NOT a hardcoded dictionary). Then build a default IR statement.
    // ───────────────────────────────────────────────────────────────────────
    private void TranslateAddNode(AddNodeInBlock a, ImmutableArray<StatementChange>.Builder statementChanges)
    {
        var funcName = ResolveBpName(a.BpNodeKind);
        var stmt = BuildDefaultStatement(funcName, a.BlockName);
        statementChanges.Add(new StatementChange
        {
            BlockName = a.BlockName,
            Fingerprint = stmt.Fingerprint,
            Kind = DiffKind.Added,
            NewValue = stmt,
            NewIndex = a.Position,
        });
    }

    private static void TranslateDeleteNode(
        Dictionary<string, (string Block, int Ordinal, IrStatement Stmt)> locs,
        DeleteNode d,
        ImmutableArray<StatementChange>.Builder statementChanges)
    {
        if (!locs.TryGetValue(d.NodeId, out var loc)) return;
        statementChanges.Add(new StatementChange
        {
            BlockName = loc.Block,
            Fingerprint = loc.Stmt.Fingerprint,
            Kind = DiffKind.Removed,
        });
    }

    private static void TranslateMoveNode(
        Dictionary<string, (string Block, int Ordinal, IrStatement Stmt)> locs,
        IrWorkflow baseline,
        MoveNodeToBlock m,
        ImmutableArray<StatementChange>.Builder statementChanges)
    {
        if (!locs.TryGetValue(m.NodeId, out var loc)) return;
        if (loc.Block == m.TargetBlock)
        {
            // Intra-block reorder: FromIndex → NewIndex.
            statementChanges.Add(new StatementChange
            {
                BlockName = loc.Block,
                Fingerprint = loc.Stmt.Fingerprint,
                Kind = DiffKind.Moved,
                FromIndex = loc.Ordinal,
                NewIndex = m.Position ?? EndOf(baseline, m.TargetBlock),
                ToBlock = m.TargetBlock,
            });
        }
        else
        {
            // Cross-block move: report under the destination block (so IrDiffApply
            // inserts it there), carrying the statement value (cross-block moves
            // need NewValue populated — see IrDiffApply.ApplyStatementChanges).
            statementChanges.Add(new StatementChange
            {
                BlockName = loc.Block,
                Fingerprint = loc.Stmt.Fingerprint,
                Kind = DiffKind.Moved,
                FromIndex = loc.Ordinal,
                NewIndex = m.Position ?? EndOf(baseline, m.TargetBlock),
                ToBlock = m.TargetBlock,
                NewValue = loc.Stmt,
            });
        }
    }

    private static int EndOf(IrWorkflow baseline, string blockName)
    {
        var block = baseline.GetBlock(blockName);
        return block?.Statements.Length ?? 0;
    }

    // ───────────────────────────────────────────────────────────────────────
    // SetNodeArgument → StatementChange(Modified).
    // The argument is edited positionally; the statement's fingerprint changes
    // (it is content-derived from the args), so this is a Modify, not a Moved.
    // ───────────────────────────────────────────────────────────────────────
    private void TranslateSetArgument(
        Dictionary<string, (string Block, int Ordinal, IrStatement Stmt)> locs,
        SetNodeArgument sa,
        ImmutableArray<StatementChange>.Builder statementChanges)
    {
        if (!locs.TryGetValue(sa.NodeId, out var loc)) return;

        var modified = WithArgument(loc.Stmt, sa.ArgIndex, sa.Value);
        statementChanges.Add(new StatementChange
        {
            BlockName = loc.Block,
            Fingerprint = modified.Fingerprint,
            Kind = DiffKind.Modified,
            NewValue = modified,
            FromIndex = loc.Ordinal,
        });
    }

    // ───────────────────────────────────────────────────────────────────────
    // SetControlFlowArm → StatementChange(Modified, Targets change).
    // The arm's TargetBlockName is updated on the control-flow terminator.
    // ───────────────────────────────────────────────────────────────────────
    private static void TranslateSetControlFlowArm(
        Dictionary<string, (string Block, int Ordinal, IrStatement Stmt)> locs,
        SetControlFlowArm scf,
        ImmutableArray<StatementChange>.Builder statementChanges)
    {
        if (!locs.TryGetValue(scf.NodeId, out var loc)) return;
        if (loc.Stmt is not IrControlFlowStatement cf) return;

        // Replace (or append) the arm matching the pin name.
        var targets = cf.Targets.ToList();
        var idx = targets.FindIndex(t => t.PinName == scf.ArmPinName);
        if (idx >= 0)
            targets[idx] = targets[idx] with { TargetBlockName = scf.TargetBlockName };
        else
            targets.Add(new IrControlFlowTarget(scf.ArmPinName, scf.TargetBlockName));

        var modified = cf with { Targets = targets.ToImmutableArray() };
        statementChanges.Add(new StatementChange
        {
            BlockName = loc.Block,
            Fingerprint = modified.Fingerprint,
            Kind = DiffKind.Modified,
            NewValue = modified,
            FromIndex = loc.Ordinal,
        });
    }

    private static void TranslateAddBlock(AddBlock ab, ImmutableArray<BlockChange>.Builder blockChanges)
    {
        blockChanges.Add(new BlockChange
        {
            Name = ab.BlockName,
            Kind = BlockChangeKind.Added,
            NewBlock = new IrBlock { Name = ab.BlockName, Kind = IrBlockKind.Basic },
        });
    }

    // RenameBlock = remove old name + add new (block identity IS the name). The
    // added block carries the renamed block's content; downstream successors in
    // OTHER blocks are reconciled by IrDiffApply when the new IR is rebuilt.
    private static void TranslateRenameBlock(
        RenameBlock rb, ImmutableArray<BlockChange>.Builder blockChanges)
    {
        blockChanges.Add(new BlockChange { Name = rb.OldName, Kind = BlockChangeKind.Removed });
        // The new block is minimal — the SyncService re-derives the full renamed
        // block from the baseline before applying (it has both names). Here we
        // declare the intent: a new block named rb.NewName exists.
        blockChanges.Add(new BlockChange
        {
            Name = rb.NewName,
            Kind = BlockChangeKind.Added,
            NewBlock = new IrBlock { Name = rb.NewName, Kind = IrBlockKind.Basic },
        });
    }

    private static void TranslateDeleteBlock(
        DeleteBlock db, ImmutableArray<BlockChange>.Builder blockChanges)
    {
        blockChanges.Add(new BlockChange { Name = db.BlockName, Kind = BlockChangeKind.Removed });
    }

    // ───────────────────────────────────────────────────────────────────────
    // BP-name → IR-name resolution. This is the cure for the legacy
    // DashboardToCfgName hardcoded dictionary: each function declares its own
    // BP alias via IBpReverseHandler.BpNames, registered once at startup.
    //
    //   resolve("Loop")  → registry.GetBpReverseByBpName("Loop") → ForLoop
    //   resolve("Print") → no handler → identity ("Print")
    //
    // There is NO DashboardToCfgName field in this file (a unit test asserts it).
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a BP canvas name to its IR function name. Queries the registry's
    /// IBpReverseHandler table (the per-function alias declaration); falls back to
    /// identity when no handler claims the alias. Replaces the legacy
    /// DashboardToCfgName dictionary.
    /// </summary>
    public string ResolveBpName(string bpName)
    {
        var handler = _registry.GetBpReverseByBpName(bpName);
        // The handler IS the function (ForLoopFunction implements IBpReverseHandler);
        // its IR name is the function's IBuiltinFunction.Name. We reach it by casting,
        // since the registry indexes the same instance under both interfaces.
        if (handler is IBuiltinFunction fn)
            return fn.Name;
        // Unknown alias → identity mapping (BP name == IR name).
        return bpName;
    }

    /// <summary>
    /// Builds the IR statement a freshly-added BP node should produce. If the
    /// resolved function implements IBpReverseHandler, use its BuildFromBp for a
    /// faithful reverse translation; otherwise emit a default bare call.
    /// </summary>
    private IrStatement BuildDefaultStatement(string funcName, string blockName)
    {
        var handler = _registry.Get(funcName);
        if (handler is IBpReverseHandler reverse)
        {
            return reverse.BuildFromBp(new BpNodeInfo(funcName, new Dictionary<string, string>(), new List<(string, string)>()), blockName);
        }
        // Default: a bare pipeline call `FuncName()`.
        var fp = IrFingerprint.Compute(funcName, []);
        return new IrPipelineStatement
        {
            Fingerprint = fp,
            Sources = [$"{funcName}()"],
            Segments =
            [
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = funcName,
                    Arguments = [],
                },
            ],
        };
    }

    /// <summary>
    /// Returns a copy of <paramref name="stmt"/> with argument at
    /// <paramref name="index"/> replaced by <paramref name="value"/>. The
    /// fingerprint is recomputed so the diff sees this as a Modify. For an
    /// IrPipelineStatement, the head FunctionCall segment's literal argument at
    /// <paramref name="index"/> is replaced (the slot is grown with "null" if
    /// needed); for an IrControlFlowStatement, Arguments[index] is replaced.
    /// </summary>
    private static IrStatement WithArgument(IrStatement stmt, int index, string value)
    {
        if (stmt is IrPipelineStatement pipe)
        {
            var segs = pipe.Segments;
            if (segs.Length == 0) return pipe;
            var head = segs[0];
            if (head.Kind != IrSegmentKind.FunctionCall) return pipe;

            var args = head.Arguments.ToList();
            while (args.Count <= index)
                args.Add(IrPipelineArgument.Lit("null"));
            args[index] = IrPipelineArgument.Lit(value);

            var newHead = head with { Arguments = args.ToImmutableArray() };
            var newSegs = pipe.Segments.ToArray();
            newSegs[0] = newHead;

            // Recompute fingerprint from the new literal arguments.
            var funcName = head.FunctionName ?? "Pipeline";
            var literals = args.Select(a => a.Literal ?? "").ToList();
            return pipe with
            {
                Segments = newSegs.ToImmutableArray(),
                Fingerprint = IrFingerprint.Compute(funcName, literals),
            };
        }

        if (stmt is IrControlFlowStatement cf)
        {
            var args = cf.Arguments.ToList();
            while (args.Count <= index)
                args.Add("null");
            args[index] = value;
            return cf with
            {
                Arguments = args.ToImmutableArray(),
                Fingerprint = IrFingerprint.Compute(cf.FunctionName, args),
            };
        }

        return stmt;
    }

    /// <summary>
    /// Builds a node-id → (block, ordinal, statement) index over the baseline,
    /// using the SAME BpNodeIds.Statement scheme the renderer assigns. This is
    /// how a DeleteNode/SetNodeArgument from the canvas finds its IR statement.
    /// </summary>
    private static Dictionary<string, (string Block, int Ordinal, IrStatement Stmt)> IndexStatementLocations(IrWorkflow baseline)
    {
        var map = new Dictionary<string, (string Block, int Ordinal, IrStatement Stmt)>();
        foreach (var block in baseline.Blocks)
        {
            int ordinal = 0;
            foreach (var stmt in block.Statements)
            {
                // Note: ordinal counts EVERY statement (including control-flow
                // terminators) so the index matches the IR's positional order. The
                // renderer assigns statement-node ids only to non-control-flow
                // statements, but a SetControlFlowArm targets the terminator — which
                // the renderer renders as Exec pins on the BLOCK node, not a statement
                // node. So we ALSO index the terminator under the BLOCK node id, so
                // SetControlFlowArm can locate it.
                if (stmt is IrControlFlowStatement)
                {
                    var blockId = BpNodeIds.Block(block.Name);
                    map[blockId] = (block.Name, ordinal, stmt);
                }
                else
                {
                    var id = BpNodeIds.Statement(block.Name, stmt.Fingerprint, ordinal);
                    map[id] = (block.Name, ordinal, stmt);
                }
                ordinal++;
            }
        }
        return map;
    }

    // ───────────────────────────────────────────────────────────────────────
    // MoveNodePosition persistence helper.
    //
    // IrDiff deliberately carries NO view state (its header documents this), so a
    // pure coordinate move produces an empty semantic diff. The caller persists
    // the new coordinate into the baseline's Annotations out-of-band, via this
    // pure helper. It returns a NEW IrWorkflow with the updated Layout annotation
    // (structural sharing; baseline untouched).
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new <see cref="IrWorkflow"/> with the canvas position of the node
    /// identified by <paramref name="nodeId"/> updated to
    /// (<paramref name="x"/>, <paramref name="y"/>). Pure: the baseline is not
    /// mutated; the result shares all unchanged structure.
    /// </summary>
    /// <remarks>
    /// The node id is interpreted per <see cref="BpNodeIds"/>: a "block:" prefix
    /// updates the block-anchor ("BlockPos") annotation; a "stmt:" prefix updates
    /// the per-statement annotation keyed by the statement's fingerprint string.
    /// Unknown prefixes / ids leave the baseline unchanged (returned as-is).
    /// </remarks>
    public static IrWorkflow ApplyPosition(IrWorkflow baseline, string nodeId, double x, double y)
    {
        var layout = new IrLayout(x, y);
        var blocks = baseline.Blocks.ToArray();
        bool anyChanged = false;

        if (nodeId.StartsWith("block:", StringComparison.Ordinal))
        {
            var blockName = nodeId["block:".Length..];
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Name != blockName) continue;
                blocks[i] = WithLayoutAnnotation(blocks[i], "BlockPos", layout);
                anyChanged = true;
                break;
            }
        }
        else if (nodeId.StartsWith("stmt:", StringComparison.Ordinal))
        {
            // The statement id encodes DeriveStableId(block, fp, ordinal). We don't
            // decode it; instead, find the statement whose DeriveStableId matches the
            // suffix and update the annotation keyed by its fingerprint.
            var suffix = nodeId["stmt:".Length..];
            for (int i = 0; i < blocks.Length; i++)
            {
                var block = blocks[i];
                int ordinal = 0;
                int stmtIdx = -1;
                IrFingerprint foundFp = default;
                foreach (var stmt in block.Statements)
                {
                    if (stmt is IrControlFlowStatement) { ordinal++; continue; }
                    var id = IrFingerprint.DeriveStableId(block.Name, stmt.Fingerprint, ordinal);
                    if (id == suffix) { stmtIdx = ordinal; foundFp = stmt.Fingerprint; break; }
                    ordinal++;
                }
                if (stmtIdx >= 0)
                {
                    blocks[i] = WithLayoutAnnotation(block, foundFp.Value, layout);
                    anyChanged = true;
                    break;
                }
            }
        }

        return anyChanged ? baseline with { Blocks = blocks.ToImmutableArray() } : baseline;
    }

    /// <summary>
    /// Returns a copy of <paramref name="block"/> with the Layout annotation
    /// keyed <paramref name="key"/> replaced by (or augmented with)
    /// <paramref name="layout"/>. Other annotations are preserved.
    /// </summary>
    private static IrBlock WithLayoutAnnotation(IrBlock block, string key, IrLayout layout)
    {
        var rebuilt = ImmutableArray.CreateBuilder<IrAnnotation>(block.Annotations.Length + 1);
        bool replaced = false;
        foreach (var ann in block.Annotations)
        {
            if (ann.IsLayout && ann.Key == key)
            {
                rebuilt.Add(new IrAnnotation(AnnotationKind.Layout, key, layout));
                replaced = true;
            }
            else
            {
                rebuilt.Add(ann);
            }
        }
        if (!replaced)
            rebuilt.Add(new IrAnnotation(AnnotationKind.Layout, key, layout));
        return block with { Annotations = rebuilt.ToImmutable() };
    }
}
