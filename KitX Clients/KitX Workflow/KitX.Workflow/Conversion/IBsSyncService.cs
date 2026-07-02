namespace KitX.Workflow.Conversion;

/// <summary>
/// BS edit → CFG sync. Full re-parse of the new BS text, semantic diff against live CFG,
/// diff-apply back to live CFG (Decision 1).
/// </summary>
public interface IBsSyncService
{
    CfgChangeSet ApplyBsEdit(IWorkflowSession session, string newBsSource);
}
