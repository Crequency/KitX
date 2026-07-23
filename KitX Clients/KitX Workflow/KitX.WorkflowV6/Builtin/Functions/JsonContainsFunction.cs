namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonContainsFunction — checks whether a dotted path exists in a JSON object.
//
// KScript: <c>json > JsonContains(_, "path.to.field")</c>.
// BP node: 2 data inputs (Any json, String path), 1 data output (Boolean).
// Runtime: G.JsonContains(json, "a.b") → true if a.b exists; false otherwise.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonContains builtin — checks if a dotted path exists in a JSON object. Pure.
/// </summary>
public sealed class JsonContainsFunction : IBuiltinFunction
{
    public string Name => "JsonContains";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
        new("Path", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Found", PinType.Boolean, 50),
    ];
}
