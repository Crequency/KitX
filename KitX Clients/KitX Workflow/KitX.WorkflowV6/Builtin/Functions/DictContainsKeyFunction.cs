namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictContainsKeyFunction — checks whether a key exists in a Dict.
//
// KScript: <c>dict, "key" > DictContainsKey > found</c>.
// BP node: 2 data inputs (Dict dict, String key), 1 data output (Boolean found).
// Runtime: G.DictContainsKey(dict, "key") → dict.ContainsKey("key").
// See Package/Dict-Type-Design.md §2.3.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictContainsKey builtin — key existence check. Pure.
/// </summary>
public sealed class DictContainsKeyFunction : IBuiltinFunction
{
    public string Name => "DictContainsKey";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
        new("Key", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Found", PinType.Boolean, 50),
    ];
}
