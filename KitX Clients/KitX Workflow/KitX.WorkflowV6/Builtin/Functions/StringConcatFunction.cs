namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// StringConcatFunction — the Pure string concatenation builtin (discussion notes
// §十二-B: arithmetic operators disabled, replaced by builtins).
//
// KScript: <c>StringConcat(a, b, c, ...)</c>.
// BP node: 2 fixed data inputs (string, string) + a variadic input group ("Input 3",
// "Input 4", ... — uses VariadicPinSpec, the v5.1 frontend auto-expansion mechanism
// discussed in §十二-N). 1 data output (string).
// Codegen: <c>string.Concat(args)</c>.
//
// Per §十二-N, variadic pin expansion reuses the v5.1 frontend
// BlueprintEditorViewModel.TryExpandVariadicPins — no backend work needed; the
// variadic spec here drives both the BP node template and the auto-growth behaviour.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The StringConcat builtin — concatenates N string arguments into one. Pure: returns
/// a value. Declares a variadic input spec so the BP editor auto-grows new string pins
/// when the last one is connected.
/// </summary>
public sealed class StringConcatFunction : IBuiltinFunction
{
    public string Name => "StringConcat";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("A", PinType.String, 20),
        new("B", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Concat", PinType.String, 50),
    ];

    public VariadicPinSpec? InputVariadic => new("Input ", 3, PinType.String);
}