using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Services;
using Serilog;

namespace KitX.ToolKit.Bench;

/// <summary>
/// The real <see cref="IWorkflowExecutor"/>: reads the resolved <c>.kcs</c>, deserializes
/// the v6 IR, applies constant overrides and executes through the shared
/// <see cref="WorkflowRunner"/>. It deliberately bypasses <see cref="WorkflowSessionManager"/>'s
/// "one active run per id" constraint: each Bench trigger path gets its own
/// <see cref="CancellationTokenSource"/>, enabling the multi-instance concurrency the Bench
/// requires (RFC §4.5) without modifying v6.
/// </summary>
public sealed class BenchWorkflowRunner : IWorkflowExecutor
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private const long MaxKcsFileBytes = 10 * 1024 * 1024;

    private readonly WorkflowRunner _runner;

    public BenchWorkflowRunner(WorkflowRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc/>
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        string workflowId,
        string filePath,
        IReadOnlyDictionary<string, string?>? overrides,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workflowId);

        try
        {
            var kcs = await LoadKcsAsync(filePath);
            if (kcs is null || string.IsNullOrWhiteSpace(kcs.IrData) || kcs.IrData == "{}")
                return new WorkflowExecutionResult(workflowId, false, $"Workflow '{workflowId}' not found", null);

            var ir = KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(kcs.IrData);
            if (ir is null)
                return new WorkflowExecutionResult(workflowId, false, $"Workflow '{workflowId}' IR invalid", null);

            var result = await _runner.ExecuteAsync(ir, null, overrides, ct);
            Log.Information("[BenchWorkflowRunner] Workflow {Id} finished: succeeded={Succeeded} error={Error}",
                workflowId, result.IsSuccess, result.ErrorMessage);
            return new WorkflowExecutionResult(workflowId, result.IsSuccess, result.ErrorMessage, null);
        }
        catch (OperationCanceledException)
        {
            Log.Information("[BenchWorkflowRunner] Workflow {Id} cancelled", workflowId);
            return new WorkflowExecutionResult(workflowId, false, "Cancelled", null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BenchWorkflowRunner] Workflow {Id} failed", workflowId);
            return new WorkflowExecutionResult(workflowId, false, ex.Message, null);
        }
    }

    /// <summary>
    /// Reads + deserializes a <c>.kcs</c>. <b>Threat model:</b> a <c>.kcs</c> is executable
    /// code (its IrData compiles and runs) — only load from trusted ToolKit packages.
    /// </summary>
    private static async Task<KcsFileFormat?> LoadKcsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var info = new FileInfo(filePath);
        if (info.Length > MaxKcsFileBytes)
            return null;

        var json = await File.ReadAllTextAsync(filePath);
        try
        {
            return JsonSerializer.Deserialize<KcsFileFormat>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
