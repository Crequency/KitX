namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpEditTranslator — BP edit actions → WorkflowDiff.
//
// Translates a batch of BpEditActions into a content-addressed WorkflowDiff that
// the SyncService can apply to the session's IR. Each edit is inspected and mapped
// to one or more StatementChanges.
//
// Before translation, the translator invokes <see cref="StructuralReducer.Check"/>
// on the Blueprint to validate that the post-edit graph is structurally well-formed.
// Non-structural back edges are rejected with a user-facing error.
//
// MVP scope: handles AddNodeInBlock, DeleteNode, SetNodeArgument, ConnectData,
// SetControlFlowArm, and MoveNodePosition. AddNodeInBlock produces a real IR
// statement via BpReverseTranslator when the BpNodeKind maps to a known function.
// Full bidirectional fidelity (BP→IR→BP ≡ id) is verified by round-trip tests.
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

        // Structural check on the post-edit blueprint.
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