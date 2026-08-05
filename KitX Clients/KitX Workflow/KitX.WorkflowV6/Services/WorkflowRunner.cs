namespace KitX.WorkflowV6.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowRunner — the single shared workflow execution path.
//
// Both the in-editor Run/DebugRun (WorkflowEditorViewModelV6) and the run-by-id
// path (WorkflowSessionManager) previously applied constant overrides and then
// executed the IR through the backend themselves. This class owns that shared
// sequence — ApplyConstantOverrides + IExecutionBackend.ExecuteAsync — so the
// execution semantics are defined exactly once.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Executes workflow IRs with constant overrides applied, through the default
/// execution backend.
/// </summary>
public sealed class WorkflowRunner
{
    private readonly IExecutionBackend _backend;

    public WorkflowRunner(IExecutionBackend backend)
    {
        _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// Executes an IR with constant overrides applied, through the default backend.
    /// </summary>
    /// <param name="ir">The workflow IR to execute (not modified in place).</param>
    /// <param name="lowering">Optional lowering-time artefacts for the backend.</param>
    /// <param name="constantOverrides">User constant/global overrides (varName → text).</param>
    /// <param name="ct">Cancellation token for the execution.</param>
    /// <param name="debugger">Optional debug controller attached to the execution.</param>
    public Task<BlockScriptExecutionResult> ExecuteAsync(
        Workflow ir,
        LoweringResult? lowering,
        IReadOnlyDictionary<string, string?>? constantOverrides,
        CancellationToken ct,
        IBlueprintDebugController? debugger = null)
    {
        ArgumentNullException.ThrowIfNull(ir);

        var applied = WorkflowOverrides.ApplyConstantOverrides(ir, constantOverrides);
        return _backend.ExecuteAsync(applied, lowering, ct, debugger);
    }
}
