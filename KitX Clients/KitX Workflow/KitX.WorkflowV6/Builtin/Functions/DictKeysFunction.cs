namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictKeysFunction — returns all keys of a Dict as a JSON string array.
//
// KScript: <c>dict > DictKeys > allKeys</c>.
// BP node: 1 data input (Dict dict), 1 data output (Json keys).
// Runtime: G.DictKeys(dict) → JSON array of the dict's key names.
// See Package/Dict-Type-Design.md §2.3.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictKeys builtin — all keys as a JSON array. Pure.
/// </summary>
public sealed class DictKeysFunction : IBuiltinFunction
{
    public string Name => "DictKeys";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Keys", PinType.Json, 40),
    ];
}
