namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DetachedGraphUtil — snapshot helpers for detached (exec-unreachable) sub-graphs.
//
// Both BpReverseTranslator (snapshot on Reverse) and BpRenderer (re-emit on
// Project) operate on Contract BlueprintNode/BlueprintConnection — mutable
// classes. The IR's DetachedGraph must hold INDEPENDENT copies so that later
// canvas edits (which mutate the working blueprint / the projected blueprint)
// can never corrupt the persisted IR snapshot, and vice versa.
// ─────────────────────────────────────────────────────────────────────────────

internal static class DetachedGraphUtil
{
    /// <summary>Deep-ish clone of a BlueprintNode (new instance, copied pins). Unknown types fall back to the original reference.</summary>
    public static BlueprintNode CloneNode(BlueprintNode src) => src switch
    {
        ConstNode cn => new ConstNode
        {
            Id = cn.Id,
            NodeType = cn.NodeType,
            Name = cn.Name,
            X = cn.X,
            Y = cn.Y,
            Width = cn.Width,
            Height = cn.Height,
            Comment = cn.Comment,
            ConstName = cn.ConstName,
            ConstType = cn.ConstType,
            ConstValue = cn.ConstValue,
            DefaultValue = cn.DefaultValue,
            IsDefinition = cn.IsDefinition,
            InputPins = ClonePins(cn.InputPins),
            OutputPins = ClonePins(cn.OutputPins),
        },
        VariableNode vn => new VariableNode
        {
            Id = vn.Id,
            NodeType = vn.NodeType,
            Name = vn.Name,
            X = vn.X,
            Y = vn.Y,
            Width = vn.Width,
            Height = vn.Height,
            Comment = vn.Comment,
            VarName = vn.VarName,
            VarType = vn.VarType,
            VarKind = vn.VarKind,
            VarInitialValue = vn.VarInitialValue,
            DefaultValue = vn.DefaultValue,
            IsDefinition = vn.IsDefinition,
            InputPins = ClonePins(vn.InputPins),
            OutputPins = ClonePins(vn.OutputPins),
        },
        BuiltinFunctionNode fn => new BuiltinFunctionNode
        {
            Id = fn.Id,
            NodeType = fn.NodeType,
            Name = fn.Name,
            X = fn.X,
            Y = fn.Y,
            Width = fn.Width,
            Height = fn.Height,
            Comment = fn.Comment,
            FunctionName = fn.FunctionName,
            Properties = new Dictionary<string, string>(fn.Properties),
            InputPins = ClonePins(fn.InputPins),
            OutputPins = ClonePins(fn.OutputPins),
        },
        // Entry/PluginTrigger roots can never appear in a detached component (they are
        // the exec-graph roots); unknown types are kept by reference as a safe fallback.
        _ => src,
    };

    /// <summary>Clones a connection (new instance, same ids).</summary>
    public static BlueprintConnection CloneConnection(BlueprintConnection c) => new()
    {
        Id = c.Id,
        SourceNodeId = c.SourceNodeId,
        SourcePinId = c.SourcePinId,
        TargetNodeId = c.TargetNodeId,
        TargetPinId = c.TargetPinId,
        PubVarName = c.PubVarName,
    };

    private static List<BlueprintPin> ClonePins(IEnumerable<BlueprintPin> pins) => pins
        .Select(p => new BlueprintPin
        {
            Id = p.Id,
            Name = p.Name,
            Direction = p.Direction,
            Type = p.Type,
            DefaultValue = p.DefaultValue,
        })
        .ToList();
}
