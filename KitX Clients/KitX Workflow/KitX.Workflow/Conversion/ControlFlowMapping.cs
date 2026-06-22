using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Mapping helpers between the BlockScript layer's <see cref="FlowControlType"/> (the
/// authoritative control-flow shape, now carried on <see cref="CFGStatement.FlowControlShape"/>)
/// and derived labels: the <see cref="CFGStatementKind"/> (diagnostic only) and the canonical
/// function-name string for each control-flow shape.
///
/// <see cref="ToFunctionName"/> is the sole remaining string provider — control-flow consumers
/// should query <c>stmt.FlowControlShape</c> or <c>def.FlowControlShape</c> directly rather than
/// switching on Kind. <see cref="ToKind"/> / <see cref="ToControlType"/> are retained as thin
/// bridges for the few sites that still read the derived Kind label (Dump output,
/// IBuiltinFunctionDefinition.StatementKind default).
/// </summary>
internal static class ControlFlowMapping
{
    /// <summary>FlowControlType → CFGStatementKind. Unknown for unmapped.</summary>
    public static CFGStatementKind ToKind(FlowControlType type) => type switch
    {
        FlowControlType.ConditionalJump => CFGStatementKind.Branch,
        FlowControlType.IterativeJump => CFGStatementKind.Loop,
        FlowControlType.IterativeCounted => CFGStatementKind.ForLoop,
        FlowControlType.UnconditionalJump => CFGStatementKind.Goto,
        FlowControlType.IndexedDispatch => CFGStatementKind.Switch,
        FlowControlType.LoopBackedge => CFGStatementKind.ToLoopCond,
        FlowControlType.LoopExit => CFGStatementKind.Break,
        _ => CFGStatementKind.Unknown
    };

    /// <summary>FlowControlType → canonical function-name string (e.g. "Branch").</summary>
    public static string ToFunctionName(FlowControlType type) => type switch
    {
        FlowControlType.ConditionalJump => "Branch",
        FlowControlType.IterativeJump => "Loop",
        FlowControlType.IterativeCounted => "ForLoop",
        FlowControlType.UnconditionalJump => "Goto",
        FlowControlType.IndexedDispatch => "Switch",
        FlowControlType.LoopBackedge => "ToLoopCond",
        FlowControlType.LoopExit => "Break",
        _ => string.Empty
    };

    /// <summary>CFGStatementKind → FlowControlType, or null for non-control-flow kinds.</summary>
    public static FlowControlType? ToControlType(CFGStatementKind kind) => kind switch
    {
        CFGStatementKind.Branch => FlowControlType.ConditionalJump,
        CFGStatementKind.Loop => FlowControlType.IterativeJump,
        CFGStatementKind.ForLoop => FlowControlType.IterativeCounted,
        CFGStatementKind.Goto => FlowControlType.UnconditionalJump,
        CFGStatementKind.Switch => FlowControlType.IndexedDispatch,
        CFGStatementKind.ToLoopCond => FlowControlType.LoopBackedge,
        CFGStatementKind.Break => FlowControlType.LoopExit,
        _ => null
    };
}