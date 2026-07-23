namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonAsBoolFunction — extracts a boolean from a JSON value.
//
// KScript: <c>json > JsonAsBool</c>.
// BP node: 1 data input (Any), 1 data output (Boolean).
// Runtime: G.JsonAsBool(json) → if JsonElement is true/false, returns it; else false.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonAsBool builtin — extracts a boolean from a JSON value. Pure.
/// </summary>
public sealed class JsonAsBoolFunction : IBuiltinFunction
{
    public string Name => "JsonAsBool";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];
}
