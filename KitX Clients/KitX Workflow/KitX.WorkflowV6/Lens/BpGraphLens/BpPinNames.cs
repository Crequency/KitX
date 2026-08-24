namespace KitX.WorkflowV6.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// BpPinNames — shared constants for well-known Blueprint pin names.
//
// Pin names are an implicit contract between BpRenderer (creates pins), the
// reverse translator (looks up pins by name), StructuralReducer (classifies
// Exec pins), and the Dashboard front-end (matches pin connections). Defining
// them in one place prevents typo-driven divergence across the four consumers.
//
// Conventions (KScriptGrammarRule.md §14.8):
//   • Exec input pin on every node: "Exec"
//   • Control-flow output pins: "True"/"False" (Branch), "Body"/"End" (Each/While),
//     "0"/"1"/.../"Default" (Switch arms).
//   • Data pins driven by control-flow nodes: "Condition" (Branch/While),
//     "List" (Each), "Selector" (Switch), "Current" (Each element output).
//   • Generic data pins: "Value" (default for Const/Variable/PassThrough).
//   • Named multi-arg function pins: from builtin PortSpec (From/To/Step for Range,
//     Op/A/B for Compare, Left/Right for StringConcat, etc.).
// ─────────────────────────────────────────────────────────────────────────────

internal static class BpPinNames
{
    // Exec flow.
    public const string Exec = "Exec";

    // Branch outputs.
    public const string True = "True";
    public const string False = "False";

    // Loop outputs.
    public const string Body = "Body";
    public const string End = "End";

    // Switch outputs (also "0", "1", ... as integer literals — check via int.TryParse).
    public const string Default = "Default";

    // Control-flow data inputs.
    public const string Condition = "Condition";
    public const string List = "List";
    public const string Selector = "Selector";

    // Loop element output.
    public const string Current = "Current";

    // Generic data pin names.
    public const string Value = "Value";

    // Control-flow node function names (BuiltinFunctionNode.FunctionName / RenderCtrlNode).
    public const string Branch = "Branch";
    public const string Each = "Each";
    public const string While = "While";
    public const string Switch = "Switch";
    public const string Break = "break";
    public const string Continue = "continue";

    /// <summary>
    /// True if <paramref name="name"/> is a control-flow node function name
    /// (Branch/Each/While/Switch) — the functions that expand into structured IR
    /// statements. Loop terminators are classified separately by
    /// <see cref="IsTerminatorName"/>.
    /// </summary>
    public static bool IsControlFlowName(string name)
        => name == Branch || name == Each || name == While || name == Switch;

    /// <summary>True if <paramref name="name"/> is a loop terminator function name (break/continue).</summary>
    public static bool IsTerminatorName(string name)
        => name == Break || name == Continue;
}
