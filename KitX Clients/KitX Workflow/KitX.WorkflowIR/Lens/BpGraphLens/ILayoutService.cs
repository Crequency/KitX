namespace KitX.Workflow.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

/// <summary>
/// Interface for the blueprint layout engine. Internal to the BpGraphLens
/// pipeline (consumed by <see cref="BpRenderer"/> after the node/edge phases).
/// </summary>
public interface ILayoutService
{
    /// <summary>
    /// Arranges all nodes in <paramref name="blueprint"/> into a readable layout:
    /// block nodes follow the exec-control-flow graph (linear chains + fork
    /// branches), data nodes (PubVar/Const) go to a left sidebar, and inner
    /// statement nodes are placed inside their owning BlockNode.
    /// </summary>
    void LayoutNodes(Blueprint blueprint);

    /// <summary>
    /// Adjusts node positions when a BlockNode is collapsed or expanded.
    /// Pushes sibling nodes away to make room for the expanded state or pulls
    /// them back when collapsed. Nodes inside the BlockNode's ChildNodeIds are
    /// not affected (they move with the block).
    /// </summary>
    void AdjustLayoutForBlockCollapse(Blueprint blueprint,
        string blockNodeId, bool isCollapsed, IReadOnlyCollection<string> childNodeIds);
}