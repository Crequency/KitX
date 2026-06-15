namespace KitX.Workflow.Contract;

/// <summary>
/// Interface for layout service.
/// Internal to the workflow pipeline (consumed by <c>BlockScriptToBlueprintConverter</c>).
/// </summary>
public interface ILayoutService
{
    void LayoutNodes(KitX.Core.Contract.Workflow.Blueprint blueprint);
}
