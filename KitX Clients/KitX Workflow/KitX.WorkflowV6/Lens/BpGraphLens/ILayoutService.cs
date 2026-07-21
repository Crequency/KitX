namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ILayoutService — assigns canvas coordinates to Blueprint nodes.
// ─────────────────────────────────────────────────────────────────────────────

public interface ILayoutService
{
    void Layout(Blueprint blueprint);
}

/// <summary>
/// Simple auto-layout: vertical stacking. Each node gets a uniform vertical offset.
/// </summary>
public sealed class LayoutService : ILayoutService
{
    private const double HorizontalPadding = 40;
    private const double VerticalSpacing = 80;

    public void Layout(Blueprint blueprint)
    {
        double y = 40;
        foreach (var node in blueprint.Nodes)
        {
            node.X = HorizontalPadding;
            node.Y = y;
            y += VerticalSpacing;
        }
    }
}