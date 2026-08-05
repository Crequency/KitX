namespace KitX.WorkflowV6.Services;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowSessionManager — lightweight IWorkflowManagementService orchestrator.
//
// The new IR architecture has no "run-by-id" service (IExecutionBackend takes a
// Workflow, not a workflowId). This orchestrator bridges that gap: it loads the
// stored IR (KcsFileFormat.IrData) for a workflow id, deserializes it, and runs it
// through the backend. Run/stop state is tracked by id via a CancellationToken
// per active run.
//
// The manager always dispatches v6 workflows (v5.1 archived — the v6 path is the
// only one, no IrVersion branching): it deserializes via the v6 WorkflowSerializer,
// applies the persisted VariableConstants overrides (the same semantics the editor
// uses at Run-time), and executes through WorkflowRunner — the single shared
// execution path (ApplyConstantOverrides + backend ExecuteAsync). This closes the
// "run-by-id for v6" gap that the ITriggerManager routing path depends on.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs/stops workflows by id, backed by the stored IR (KcsFileFormat) and the
/// v6 execution backend.
/// </summary>
public sealed class WorkflowSessionManager : IWorkflowManagementService
{
    private readonly IWorkflowStorageService _storage;
    private readonly WorkflowRunner _runner;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public WorkflowSessionManager(IWorkflowStorageService storage, WorkflowRunner runner)
    {
        _storage = storage ?? throw new System.ArgumentNullException(nameof(storage));
        _runner = runner ?? throw new System.ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc/>
    public async Task<bool> RunWorkflowAsync(string workflowId)
    {
        var result = await RunWorkflowWithDetailsAsync(workflowId);
        return result.IsSuccess;
    }

    /// <inheritdoc/>
    public async Task<WorkflowRunResult> RunWorkflowWithDetailsAsync(string workflowId)
    {
        var data = await _storage.LoadWorkflowDataAsync(workflowId);
        if (data == null || string.IsNullOrWhiteSpace(data.IrData) || data.IrData == "{}")
            return new WorkflowRunResult(false, $"Workflow '{workflowId}' not found or IR invalid", null);

        // Stop any prior run of this id (single active run per workflow).
        if (_running.TryRemove(workflowId, out var priorCts))
            priorCts.Cancel();

        var cts = new CancellationTokenSource();
        _running[workflowId] = cts;

        try
        {
            var v6Ir = KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(data.IrData);
            if (v6Ir is null)
                return new WorkflowRunResult(false, $"Workflow '{workflowId}' IR invalid", null);

            Log.Information("[WorkflowSessionManager] Running workflow {Id}", workflowId);
            var result = await _runner.ExecuteAsync(
                v6Ir, null, ToStringOverrides(data.VariableConstants), cts.Token);
            return new WorkflowRunResult(result.IsSuccess, result.ErrorMessage, result.Output);
        }
        catch (System.OperationCanceledException)
        {
            Log.Information("[WorkflowSessionManager] Workflow {Id} cancelled", workflowId);
            return new WorkflowRunResult(false, "Cancelled", null);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "[WorkflowSessionManager] Workflow {Id} failed", workflowId);
            return new WorkflowRunResult(false, ex.Message, null);
        }
        finally
        {
            _running.TryRemove(workflowId, out _);
        }
    }

    /// <inheritdoc/>
    public Task<bool> StopWorkflowAsync(string workflowId)
    {
        if (_running.TryRemove(workflowId, out var cts))
        {
            cts.Cancel();
            Log.Information("[WorkflowSessionManager] Stopped workflow {Id}", workflowId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Validates that the stored IR is deserializable as v6 — a cheap compile-readiness
    /// check for the engine.
    /// </remarks>
    public async Task<bool> CompileAndPersistWorkflowAsync(string workflowId)
    {
        var data = await _storage.LoadWorkflowDataAsync(workflowId);
        if (data == null || string.IsNullOrWhiteSpace(data.IrData) || data.IrData == "{}")
            return false;
        return KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(data.IrData) != null;
    }

    /// <summary>Maps persisted <c>VariableConstants</c> (varName → object?) to string overrides.</summary>
    private static Dictionary<string, string?>? ToStringOverrides(Dictionary<string, object?>? variableConstants)
        => variableConstants is null || variableConstants.Count == 0
            ? null
            : variableConstants.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());
}
