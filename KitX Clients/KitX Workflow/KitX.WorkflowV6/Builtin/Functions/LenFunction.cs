namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// LenFunction — the universal length builtin (inspired by Python's len()).
//
// KScript: <c>Len(value)</c>.
// BP node: 1 data input (Any), 1 data output (Integer).
// Codegen: <c>this.Len(value)</c> → runtime polymorphic dispatch on the actual
// runtime type: string.Length, JsonElement (array/object/string), Array.Length,
// ICollection.Count. Returns 0 for null or scalar types.
//
// Replaces v5.1's specialised JsonArrayLength — one polymorphic function covers
// string length, JSON array length, JSON object key count, and array length.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Len builtin — returns the length/count of a value. Pure: returns an Integer.
/// Accepts strings, JSON arrays/objects, and .NET arrays/collections.
/// </summary>
public sealed class LenFunction : IBuiltinFunction
{
    public string Name => "Len";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Length", PinType.Integer, 50),
    ];
}
