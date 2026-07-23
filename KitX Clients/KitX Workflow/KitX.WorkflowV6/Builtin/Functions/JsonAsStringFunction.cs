namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonAsStringFunction — extracts a string from a JSON value.
//
// KScript: <c>json > JsonAsString</c> or <c>JsonAsString(json)</c>.
// BP node: 1 data input (Any), 1 data output (String).
// Runtime: G.JsonAsString(json) → if JsonElement is string, returns the string;
// otherwise returns the raw JSON text.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonAsString builtin — extracts a string from a JSON value. Pure.
/// </summary>
public sealed class JsonAsStringFunction : IBuiltinFunction
{
    public string Name => "JsonAsString";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.String, 50),
    ];
}
