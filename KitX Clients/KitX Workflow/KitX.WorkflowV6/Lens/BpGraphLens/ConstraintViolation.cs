namespace KitX.WorkflowV6.Lens.BpGraphLens;

/// <summary>
/// A structural constraint violation detected by <see cref="StructuralReducer"/>,
/// carrying enough detail for the frontend to highlight the offending nodes/connections
/// and present a fix suggestion. See KScript-Blueprint-Correspondence.md §5.5.
/// </summary>
/// <param name="Code">Error code, e.g. "KS102". See the constraint table in §5.5.</param>
/// <param name="Constraint">Constraint identifier, e.g. "E3".</param>
/// <param name="Message">
/// Full user-facing message. Identical to the string formerly returned by
/// <see cref="StructuralReducer.Check"/> (kept stable for test assertions).
/// </param>
/// <param name="NodeIds">IDs of nodes involved in the violation (for frontend highlighting).</param>
/// <param name="ConnectionIds">IDs of connections involved (optional).</param>
/// <param name="FixSuggestion">Suggested fix shown in the error tooltip (optional).</param>
public sealed record ConstraintViolation(
    string Code,
    string Constraint,
    string Message,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string>? ConnectionIds = null,
    string? FixSuggestion = null);
