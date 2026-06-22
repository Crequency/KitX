using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Mapping helpers between the BlockScript layer's <see cref="FlowControlType"/> (the
/// authoritative control-flow shape, now carried on <see cref="CFGStatement.FlowControlShape"/>)
/// and derived labels: the <see cref="CFGStatementKind"/> (diagnostic only) and the canonical
/// function-name string for each control-flow shape.
/// </summary>
/// <remarks>
/// v5.0: the v4.0 <c>IterativeJump</c> (Loop) / <c>LoopBackedge</c> (ToLoopCond) shapes and
/// their <c>Loop</c>/<c>ToLoopCond</c> Kind/function-name entries are removed — ForLoop
/// (<see cref="FlowControlType.IterativeCounted"/>) and Goto
/// (<see cref="FlowControlType.UnconditionalJump"/>) replace them.
/// </remarks>
internal static class ControlFlowMapping
{
    /// <summary>FlowControlType → CFGStatementKind. Unknown for unmapped.</summary>
    public static CFGStatementKind ToKind(FlowControlType type) => type switch
    {
        FlowControlType.ConditionalJump => CFGStatementKind.Branch,
        FlowControlType.IterativeCounted => CFGStatementKind.ForLoop,
        FlowControlType.UnconditionalJump => CFGStatementKind.Goto,
        FlowControlType.IndexedDispatch => CFGStatementKind.Switch,
        FlowControlType.LoopExit => CFGStatementKind.Break,
        _ => CFGStatementKind.Unknown
    };

    /// <summary>FlowControlType → canonical function-name string (e.g. "Branch").</summary>
    public static string ToFunctionName(FlowControlType type) => type switch
    {
        FlowControlType.ConditionalJump => "Branch",
        FlowControlType.IterativeCounted => "ForLoop",
        FlowControlType.UnconditionalJump => "Goto",
        FlowControlType.IndexedDispatch => "Switch",
        FlowControlType.LoopExit => "Break",
        _ => string.Empty
    };

    /// <summary>CFGStatementKind → FlowControlType, or null for non-control-flow kinds.</summary>
    public static FlowControlType? ToControlType(CFGStatementKind kind) => kind switch
    {
        CFGStatementKind.Branch => FlowControlType.ConditionalJump,
        CFGStatementKind.ForLoop => FlowControlType.IterativeCounted,
        CFGStatementKind.Goto => FlowControlType.UnconditionalJump,
        CFGStatementKind.Switch => FlowControlType.IndexedDispatch,
        CFGStatementKind.Break => FlowControlType.LoopExit,
        _ => null
    };
}
