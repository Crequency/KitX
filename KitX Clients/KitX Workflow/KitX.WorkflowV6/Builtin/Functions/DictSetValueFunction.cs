namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictSetValueFunction — sets a key→value pair in a Dict, in place (mutable).
//
// KScript: <c>dict, "key", value > DictSetValue > dict</c>.
// BP node: 3 data inputs (Dict dict, String key, Any value), 1 data output (Dict).
// Runtime: G.DictSetValue(dict, "key", v) → dict["key"] = v; return dict;
//   in-place mutation; the trailing <c>> dict</c> writeback is a redundant self-assign.
// See Package/Dict-Type-Design.md §2.3, §6.3, §6.6.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictSetValue builtin — sets key→value in place, returns the same Dict. Pure
/// (data transform with an in-place side effect on the input reference).
/// </summary>
public sealed class DictSetValueFunction : IBuiltinFunction
{
    public string Name => "DictSetValue";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
        new("Key", PinType.String, 35),
        new("Value", PinType.Any, 50),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Dict, 70),
    ];
}
