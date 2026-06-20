using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Centralised bidirectional mapping between the BlockScript layer's
/// <see cref="FlowControlType"/> and the CFG layer's <see cref="CFGStatementKind"/>,
/// plus the canonical function-name string for each control-flow kind.
///
/// Replaces three duplicated mapping tables that had drifted across the converters:
/// BS2CFGConverter.MapControlTypeToKind / MapControlTypeToFunctionName,
/// BP2CFGConverter's inline switch inside ConvertBlockStatementToCfgStatement, and
/// CFG2BSConverter.KindToControlType. All five control-flow kinds
/// (Branch / Loop / Switch / ToLoopCond / Break) map 1:1; non-control-flow
/// CFGStatementKind values have no FlowControlType counterpart (null).
/// </summary>
internal static class ControlFlowMapping
{
    /// <summary>FlowControlType → CFGStatementKind. Unknown for unmapped.</summary>
    public static CFGStatementKind ToKind(FlowControlType type) => type switch
    {
        FlowControlType.Branch => CFGStatementKind.Branch,
        FlowControlType.Loop => CFGStatementKind.Loop,
        FlowControlType.Switch => CFGStatementKind.Switch,
        FlowControlType.ToLoopCond => CFGStatementKind.ToLoopCond,
        FlowControlType.Break => CFGStatementKind.Break,
        _ => CFGStatementKind.Unknown
    };

    /// <summary>FlowControlType → canonical function-name string (e.g. "Branch").</summary>
    public static string ToFunctionName(FlowControlType type) => type switch
    {
        FlowControlType.Branch => "Branch",
        FlowControlType.Loop => "Loop",
        FlowControlType.Switch => "Switch",
        FlowControlType.ToLoopCond => "ToLoopCond",
        FlowControlType.Break => "Break",
        _ => string.Empty
    };

    /// <summary>CFGStatementKind → FlowControlType, or null for non-control-flow kinds.</summary>
    public static FlowControlType? ToControlType(CFGStatementKind kind) => kind switch
    {
        CFGStatementKind.Branch => FlowControlType.Branch,
        CFGStatementKind.Loop => FlowControlType.Loop,
        CFGStatementKind.Switch => FlowControlType.Switch,
        CFGStatementKind.ToLoopCond => FlowControlType.ToLoopCond,
        CFGStatementKind.Break => FlowControlType.Break,
        _ => null
    };
}