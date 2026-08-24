namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ILayoutService — assigns canvas coordinates to Blueprint nodes.
//
// The default implementation (LayoutService) builds a recursive region tree
// from the exec-control-flow graph and arranges nodes with smart wrapping
// and symmetric fork branch separation. See LayoutService.cs for details.
// ─────────────────────────────────────────────────────────────────────────────

public interface ILayoutService
{
    void Layout(Blueprint blueprint);
}
