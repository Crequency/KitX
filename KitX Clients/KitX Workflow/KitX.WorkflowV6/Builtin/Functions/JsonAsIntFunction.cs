namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonAsIntFunction — extracts an integer from a JSON value.
//
// KScript: <c>json > JsonAsInt</c>.
// BP node: 1 data input (Any), 1 data output (Integer).
// Runtime: G.JsonAsInt(json) → if JsonElement is number, returns int32; else 0.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonAsInt builtin — extracts an integer from a JSON value. Pure.
/// </summary>
public sealed class JsonAsIntFunction : IBuiltinFunction
{
    public string Name => "JsonAsInt";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Integer, 50),
    ];
}
