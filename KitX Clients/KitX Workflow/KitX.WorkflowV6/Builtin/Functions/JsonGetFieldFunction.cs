namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonGetFieldFunction — traverses a JSON object by dotted path and returns the
// value at that field.
//
// KScript: <c>json > JsonGetField(_, "path.to.field")</c>.
// BP node: 2 data inputs (Any json, String fieldPath), 1 data output (Json).
// Runtime: G.JsonGetField(json, "a.b.c") → walks a→b→c; returns default if any
// segment is missing or the value is not an object.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonGetField builtin — gets a field value via dotted path from a JSON object. Pure.
/// </summary>
public sealed class JsonGetFieldFunction : IBuiltinFunction
{
    public string Name => "JsonGetField";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
        new("FieldPath", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Field", PinType.Json, 50),
    ];
}
