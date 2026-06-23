namespace KitX.Workflow.Abstractions;

/// <summary>
/// Interface for layout service.
/// Internal to the workflow pipeline (consumed by <c>BlockScriptToBlueprintConverter</c>).
/// </summary>
public interface ILayoutService
{
    void LayoutNodes(KitX.Core.Contract.Workflow.Blueprint blueprint);

    /// <summary>
    /// Adjusts node positions when a BlockNode is collapsed or expanded (v5.0).
    /// Pushes sibling nodes away to make room for the expanded state or pulls them
    /// back when collapsed.
    /// </summary>
    void AdjustLayoutForBlockCollapse(KitX.Core.Contract.Workflow.Blueprint blueprint,
        string blockNodeId, bool isCollapsed, IReadOnlyCollection<string> childNodeIds);
}
