namespace KitX.ToolKit.Models;

/// <summary>
/// The optional UI panel definition (Bench RFC §8). Declared now; the actual control
/// rendering / <c>UiGet</c>/<c>UiSet</c>/<c>UiLog</c>/<c>UiProgress</c> runtime is deferred
/// to the GUI iteration. This model exists so configs are complete and validatable today.
/// </summary>
public sealed class UiPanel
{
    /// <summary>Layout strategy: <c>stack</c> (flow / vertical stacking) or <c>grid</c>. No free coordinates.</summary>
    public string Layout { get; set; } = "stack";

    /// <summary>The fixed-control-set composition in layout order.</summary>
    public List<UiControl> Controls { get; set; } = [];
}
