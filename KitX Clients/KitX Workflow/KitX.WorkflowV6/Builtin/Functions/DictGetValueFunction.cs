namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictGetValueFunction — gets a value by key from a Dict (mutable Dictionary).
//
// KScript: <c>dict, "key" > DictGetValue > value</c>.
// BP node: 2 data inputs (Dict dict, String key), 1 data output (Any value).
// Runtime: G.DictGetValue(dict, "key") → dict[key], or null if absent.
// See Package/Dict-Type-Design.md §2.3.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictGetValue builtin — gets a value by key from a Dict. Pure.
/// </summary>
public sealed class DictGetValueFunction : IBuiltinFunction
{
    public string Name => "DictGetValue";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
        new("Key", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Value", PinType.Any, 50),
    ];
}
