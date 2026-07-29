namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DictGetValuesFunction — batch lookup by a JSON array of keys; missing keys → null.
//
// KScript: <c>dict, "[\"a\",\"b\",\"c\"]" > DictGetValues > values</c>.
// BP node: 2 data inputs (Dict dict, Json keys), 1 data output (Json values).
// Runtime: G.DictGetValues(dict, keysJson) → JSON array of values in key order;
//   absent keys are filled with null.
// See Package/Dict-Type-Design.md §2.3, §6.5.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The DictGetValues builtin — batch lookup, missing keys padded with null. Pure.
/// </summary>
public sealed class DictGetValuesFunction : IBuiltinFunction
{
    public string Name => "DictGetValues";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Dict", PinType.Dict, 20),
        new("Keys", PinType.Json, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Values", PinType.Json, 55),
    ];
}
