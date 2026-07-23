namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonObjectKeysFunction — gets the key names of a JSON object as a JSON array.
//
// KScript: <c>json > JsonObjectKeys</c>.
// BP node: 1 data input (Any), 1 data output (Json — array of string keys).
// Runtime: G.JsonObjectKeys(json) → JsonElement array of key names, or default
// if the value is not an object.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonObjectKeys builtin — gets key names of a JSON object. Pure.
/// </summary>
public sealed class JsonObjectKeysFunction : IBuiltinFunction
{
    public string Name => "JsonObjectKeys";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Keys", PinType.Json, 50),
    ];
}
