using KitX.Workflow.Models;

namespace KitX.Workflow.CFG;

/// <summary>
/// First-class CFG citizen for the v5.0 pipeline (<c>&gt;</c>) syntax. Carries the structured
/// <see cref="BSPipeline"/> AST verbatim — the pipeline is the data-flow primitive of v5.0's
/// functional core (replaces <c>=</c> assignment, nested calls, <c>vaaa####</c> one-shot temp
/// capacitors, and NextBlock-driven jumps). Not sugar.
/// </summary>
/// <remarks>
/// <para>Consumers that need the imperative flat form (CFG2BP / CFG2CS / executor) call
/// <see cref="FlattenedStatements"/>, which lazily expands the AST into a sequence of plain
/// <see cref="CFGStatement"/>s via <see cref="PipelineFlattener"/>. The flattening is cached.</para>
/// <para>Round-trip (CFG2BS) reads the AST directly — no more verbatim-text stamping on segment 0's
/// <see cref="CFGStatement.OriginalExpression"/> (the silent-drop failure mode at the old
/// CFG2BSConverter.ConvertPipelineGroup:149 is eliminated).</para>
/// </remarks>
public class PipelineStatement : CFGStatement
{
    /// <summary>The verbatim <c>&gt;</c> AST — Sources (comma-separated LHS) and Targets (segments).</summary>
    public required BSPipeline Pipeline { get; set; }

    /// <summary>
    /// Internal holder for the flattener delegate. Set by BS2CFGConverter when the statement is
    /// constructed (the converter owns the registry / context needed to expand nested calls and
    /// synthesize PubVar capacitors). Lazy + cached on first access.
    /// </summary>
    public Func<PipelineStatement, IReadOnlyList<CFGStatement>>? Flattener { internal get; set; }

    private IReadOnlyList<CFGStatement>? _flattened;

    /// <summary>
    /// The imperative flat view: pipeline chain expanded into a sequence of plain CFGStatements
    /// (nested-call hoisting + placeholder substitution + PubVar capacitor synthesis where needed).
    /// Cached after first computation. Throws if <see cref="Flattener"/> was not set.
    /// </summary>
    public IReadOnlyList<CFGStatement> FlattenedStatements =>
        _flattened ??= (Flattener ?? throw new InvalidOperationException(
            $"{nameof(PipelineStatement)} has no flattener set (BS2CFGConverter must assign it)."))(this);
}
