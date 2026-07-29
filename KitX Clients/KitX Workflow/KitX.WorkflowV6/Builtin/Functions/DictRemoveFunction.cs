namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictRemoveFunction — removes a key from a Dict, in place (mutable).
//
// KScript: <c>dict, "key" > DictRemove > dict</c>.
// BP node: 2 data inputs (Dict dict, String key), 1 data output (Dict).
// Runtime: G.DictRemove(dict, "key") → dict.Remove("key"); return dict;
//   in-place mutation; the trailing <c>> dict</c> writeback is a redundant self-assign.
// See Package/Dict-Type-Design.md §2.3, §6.3, §6.6.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictRemove builtin — removes a key in place, returns the same Dict. Pure
/// (data transform with an in-place side effect on the input reference).
/// </summary>
public sealed class DictRemoveFunction : IBuiltinFunction
{
    public string Name => "DictRemove";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
        new("Key", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Dict, 50),
    ];
}
