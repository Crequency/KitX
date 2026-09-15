namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpEditTranslator — BP edit actions → WorkflowDiff.
//
// CURRENT STATUS: stub. Produces placeholder StatementChanges with NewValue=null
// (only the Kind/LexicalPath/Fingerprint are filled). This is intentional — the
// full V6-native implementation is deferred to the project's P2 milestone
// (dual-pane live highlight feature).
//
// Why deferred: the v5.1-era BpEditAction hierarchy (AddNodeInBlock/DeleteNode/
// SetNodeArgument/ConnectData/SetControlFlowArm/MoveNodePosition) carries Block-
// centric concepts that have no V6 equivalent. V6 retired the "Block" notion
// entirely (KScriptGrammarRule §0/§16) in favour of structured AST + lexical path.
// A proper V6 redesign is required (proposed "replay model":
// edits → BpEditApplier.Apply → new Blueprint → BpReverseTranslator.Reverse → new IR
// → WorkflowDiffer.Compute — fully reusing existing tested components).
//
// Why it is OK to defer:
//   • SyncService.ApplyKsEdit (KS→IR) is fully functional and independent.
//   • BP→KS round-trip uses BpReverseTranslator + KsRenderer (wholesale replacement).
//   • The actual consumer (dual-pane live highlight) is itself in P2 priority.
//
// See Package/Archive/Docs/V6-BpEditAction-Future-Design-ADR.md for the design.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Translates a batch of <see cref="BpEditAction"/>s into a <see cref="WorkflowDiff"/>.
/// </summary>
internal sealed class BpEditTranslator
{
    private readonly BuiltinFunctionRegistry _registry;

    public BpEditTranslator(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Translates <paramref name="edits"/> against <paramref name="blueprint"/> into a
    /// <see cref="WorkflowDiff"/>. Returns a tuple of (diff, error). When error is
    /// non-null, the diff may be null or partial, and the caller must surface the error.
    /// </summary>
    public (WorkflowDiff? Diff, string? Error) Translate(Blueprint blueprint, IReadOnlyList<BpEditAction> edits)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(edits);

        // Structural check on the passed-in (baseline) blueprint. NOTE: the edits are
        // NOT applied before this check — this translator is a stub pending the P2
        // replay-model redesign (see Package/Archive/Docs/V6-BpEditAction-Future-Design-ADR.md);
        // it exists to keep the edit protocol surface stable.
        var structuralError = StructuralReducer.Check(blueprint);
        if (structuralError is not null)
            return (null, structuralError);

        var changes = new List<StatementChange>();
        foreach (var edit in edits)
        {
            try
            {
                TranslateOne(edit, blueprint, changes);
            }
            catch (Exception ex)
            {
                return (null, $"Error translating edit {edit.GetType().Name}: {ex.Message}");
            }
        }

        if (changes.Count == 0)
            return (new WorkflowDiff(), null);

        return (new WorkflowDiff { StatementChanges = changes.ToImmutableArray() }, null);
    }

    private void TranslateOne(BpEditAction edit, Blueprint bp, List<StatementChange> changes)
    {
        switch (edit)
        {
            case AddNodeInBlock add:
                changes.Add(new StatementChange
                {
                    LexicalPath = $"/new-{add.BpNodeKind}",
                    Fingerprint = Fingerprint.Compute($"bp-add:{add.BpNodeKind}"),
                    Kind = DiffKind.Added,
                    NewValue = null, // IR statement not created — frontend handles BP-first edits
                    Index = add.Position ?? -1,
                });
                break;

            case DeleteNode del:
                changes.Add(new StatementChange
                {
                    LexicalPath = ToLexicalPath(del.NodeId),
                    Fingerprint = Fingerprint.Compute($"bp-del:{del.NodeId}"),
                    Kind = DiffKind.Removed,
                    NewValue = null,
                });
                break;

            case SetNodeArgument setArg:
                changes.Add(new StatementChange
                {
                    LexicalPath = ToLexicalPath(setArg.NodeId),
                    Fingerprint = Fingerprint.Compute($"bp-arg:{setArg.NodeId}:{setArg.ArgIndex}"),
                    Kind = DiffKind.Modified,
                    NewValue = null,
                });
                break;

            case ConnectData cd:
                changes.Add(new StatementChange
                {
                    LexicalPath = ToLexicalPath(cd.TargetNodeId),
                    Fingerprint = Fingerprint.Compute($"bp-connect:{cd.SourceNodeId}:{cd.TargetNodeId}"),
                    Kind = DiffKind.Modified,
                    NewValue = null,
                });
                break;

            case SetControlFlowArm arm:
                changes.Add(new StatementChange
                {
                    LexicalPath = ToLexicalPath(arm.NodeId),
                    Fingerprint = Fingerprint.Compute($"bp-cf-arm:{arm.NodeId}:{arm.ArmPinName}"),
                    Kind = DiffKind.Modified,
                    NewValue = null,
                });
                break;

            case MoveNodePosition pos:
                // Position-only changes don't produce structural diffs.
                break;

            case AddBlock:
            case DeleteBlock:
            case RenameBlock:
            case Disconnect:
            case MoveNodeToBlock:
            default:
                // Unsupported or no-op for MVP.
                break;
        }
    }

    private static string ToLexicalPath(string nodeId) => $"/bp/{nodeId}";
}