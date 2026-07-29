namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictToJsonFunction — converts a Dict (mutable Dictionary) to a JsonElement, the
// bridge for passing Dict values to plugins (which speak JSON).
//
// KScript: <c>dict > DictToJson > json</c>.
// BP node: 1 data input (Dict dict), 1 data output (Json json).
// Runtime: G.DictToJson(dict) → JsonSerializer.SerializeToElement(dict).
// See Package/Dict-Type-Design.md §2.3, §6.7.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictToJson builtin — Dict → JsonElement bridge. Pure.
/// </summary>
public sealed class DictToJsonFunction : IBuiltinFunction
{
    public string Name => "DictToJson";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Json", PinType.Json, 40),
    ];
}
