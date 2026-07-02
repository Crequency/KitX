namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// Ir declarations — constants, globals (PubVars), and block-local variables.
//
// These mirror the legacy ConstDeclaration / the PubVarDeclarations+PubVarTypes
// pair / VariableDeclaration, but as immutable records and with the type map
// unified into the declaration itself (no parallel lists to keep in sync).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A constant from #ConstBlock. Preserves both the raw C# initialiser expression
/// (for lossless BS round-trip, keeping quoting/escaping) and the evaluated value
/// (for execution). Replaces legacy ConstDeclaration.
/// </summary>
public sealed record IrConstant(
    string Name,
    string Type,
    string? InitialValueExpression,
    object? DefaultValue)
{
    /// <summary>True when the constant has any kind of initial value.</summary>
    public bool HasInitialValue => DefaultValue is not null || !string.IsNullOrEmpty(InitialValueExpression);
}

/// <summary>
/// A global mutable variable declared in #PubVarBlock. Carries its declared type
/// so the C# backend can emit strong types (replaces the parallel PubVarTypes
/// dictionary on the legacy ControlFlowGraph).
/// </summary>
public sealed record IrGlobalVar(
    string Name,
    string Type,
    string? InitialValueExpression,
    object? DefaultValue);

/// <summary>
/// A block-local variable (##BlockVars, v5.0 §3.3). Lifetime = one block
/// activation; reset on each entry. Replaces legacy VariableDeclaration as used
/// in BlockDefinition.BlockVars.
/// </summary>
public sealed record IrBlockVar(
    string Name,
    string Type,
    string? InitialValueExpression);
