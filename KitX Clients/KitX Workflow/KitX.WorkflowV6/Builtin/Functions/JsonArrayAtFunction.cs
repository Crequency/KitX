namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonArrayAtFunction — gets the element at a zero-based index from a JSON array.
//
// KScript: <c>json > JsonArrayAt(_, index)</c>.
// BP node: 2 data inputs (Any json, Integer index), 1 data output (Json).
// Runtime: G.JsonArrayAt(json, index) → JsonElement at position, or default if
// the value is not an array or index is out of range.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonArrayAt builtin — gets an element at an index from a JSON array. Pure.
/// </summary>
public sealed class JsonArrayAtFunction : IBuiltinFunction
{
    public string Name => "JsonArrayAt";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
        new("Index", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Element", PinType.Json, 50),
    ];
}
