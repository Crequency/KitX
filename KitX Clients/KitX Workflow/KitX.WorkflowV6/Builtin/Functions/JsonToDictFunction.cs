namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// JsonToDictFunction — converts a JsonElement (e.g. a plugin return) to a mutable
// Dict (Dictionary), the bridge for obtaining a mutable map from JSON data.
//
// KScript: <c>json > JsonToDict > dict</c>.
// BP node: 1 data input (Json json), 1 data output (Dict dict).
// Runtime: G.JsonToDict(json) → deserialize the JsonElement's object properties
//   into a new Dictionary&lt;string, object?&gt;.
// See Package/Dict-Type-Design.md §2.3, §6.7.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The JsonToDict builtin — JsonElement → Dict bridge. Pure.
/// </summary>
public sealed class JsonToDictFunction : IBuiltinFunction
{
    public string Name => "JsonToDict";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Json", PinType.Json, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Dict", PinType.Dict, 40),
    ];
}
