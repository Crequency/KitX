namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// The optional UI panel definition (Bench RFC §8). Declared now; the actual control
/// rendering / <c>UiGet</c>/<c>UiSet</c>/<c>UiLog</c>/<c>UiProgress</c> runtime is deferred
/// to the GUI iteration. This model exists so configs are complete and validatable today.
/// </summary>
/// <remarks>
/// <para><b>Deprecated at S0; removed at S5.5.</b> 面板不是 ToolKit 的一等概念（ADR-0012 D3 / ADR-0013 D4）。保留到这里是因为 Toolkit.UiPanel 属性仍然引用它；S5.5 归档面板宿主后与 Toolkit.UiPanel 一起删除。</para>
/// </remarks>
public sealed class UiPanel
{
    /// <summary>Layout strategy: <c>stack</c> (flow / vertical stacking) or <c>grid</c>. No free coordinates.</summary>
    public string Layout { get; set; } = "stack";

    /// <summary>The fixed-control-set composition in layout order.</summary>
    public List<UiControl> Controls { get; set; } = [];
}
