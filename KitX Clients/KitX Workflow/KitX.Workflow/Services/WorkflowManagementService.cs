using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using Serilog;

namespace KitX.Workflow.Services;

/// <summary>
/// Workflow lifecycle management service.
/// Implements IWorkflowManagementService.
/// </summary>
internal class WorkflowManagementService : IWorkflowManagementService
{
    private readonly WorkflowRuntimeState _state;
    // Holds the concrete impl so both the public IBlockScriptService (source-string methods)
    // and the internal IBlockScriptPipelineService (parsed-model methods) are reachable.
    private readonly BlockScriptServiceImpl _blockScriptService;

    /// <summary>
    /// Initializes a new instance of WorkflowManagementService.
    /// </summary>
    /// <param name="state">Shared runtime state.</param>
    /// <param name="blockScriptService">Block script execution service.</param>
    internal WorkflowManagementService(WorkflowRuntimeState state, BlockScriptServiceImpl blockScriptService)
    {
        _state = state;
        _blockScriptService = blockScriptService;
    }

    /// <inheritdoc />
    public IReadOnlyList<IWorkflowCase> GetWorkflows()
    {
        return _state.Workflows.ToList();
    }

    /// <inheritdoc />
    public void AddWorkflow(IWorkflowCase workflow)
    {
        if (workflow != null && !_state.Workflows.Any(w => w.Id == workflow.Id))
        {
            _state.Workflows.Add(workflow);
        }
    }

    /// <inheritdoc />
    public void RemoveWorkflow(string workflowId)
    {
        var workflow = _state.Workflows.FirstOrDefault(w => w.Id == workflowId);
        if (workflow != null)
        {
            _state.Workflows.Remove(workflow);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RunWorkflowAsync(string workflowId)
    {
        var result = await RunWorkflowWithDetailsAsync(workflowId);
        return result.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<WorkflowRunResult> RunWorkflowWithDetailsAsync(string workflowId)
    {
        const string location = $"{nameof(WorkflowManagementService)}.{nameof(RunWorkflowWithDetailsAsync)}";

        try
        {
            var storageService = WorkflowStorageService.Instance;
            var data = await storageService.LoadWorkflowDataAsync(workflowId);

            if (data == null)
            {
                Log.Warning("[{Location}] Workflow data not found in storage for ID: {WorkflowId}. " +
                    "Expected file path: {Path}", location, workflowId,
                    storageService.GetWorkflowFilePath(workflowId));
                return new WorkflowRunResult(false, "Workflow data not found", null);
            }

            Log.Information("[{Location}] Loaded workflow '{Name}' (ID: {Id}), " +
                "UseBlockMode: {UseBlockMode}, BlockScriptSource length: {BsLen}, " +
                "MainProgram length: {MpLen}, Helpers: {HelperCount}",
                location, data.Name, workflowId, data.UseBlockMode,
                data.BlockScriptSource?.Length ?? 0,
                data.MainProgram?.Length ?? 0,
                data.HelperFunctions?.Count ?? 0);

            string sourceCode;
            List<HelperFunction>? helpers = data.HelperFunctions;

            if (!string.IsNullOrWhiteSpace(data.BlockScriptSource))
            {
                sourceCode = data.BlockScriptSource;
            }
            else
            {
                Log.Warning("[{Location}] Workflow '{Name}' (ID: {Id}) has no executable source code",
                    location, data.Name, workflowId);
                return new WorkflowRunResult(false, "No executable source code", null);
            }

            Log.Information("[{Location}] Executing workflow '{Name}' ({SourceLength} chars)...",
                location, data.Name, sourceCode.Length);

            var result = await _blockScriptService.ExecuteBlockScriptAsync(
                sourceCode,
                helpers ?? new List<HelperFunction>(),
                CancellationToken.None);

            if (result.IsSuccess)
            {
                var output = result.Output != null && result.Output.Count > 0
                    ? string.Join("\n", result.Output)
                    : "(no output)";
                Log.Information("[{Location}] Workflow '{Name}' executed successfully. " +
                    "Blocks: {Blocks}, Time: {Time}ms\nOutput:\n{Output}",
                    location, data.Name, result.ExecutedBlockCount, result.ExecutionTimeMs, output);
            }
            else
            {
                Log.Error("[{Location}] Workflow '{Name}' execution failed: {Error}",
                    location, data.Name, result.ErrorMessage);
            }

            return new WorkflowRunResult(result.IsSuccess, result.ErrorMessage, result.Output);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Location}] Error running workflow {WorkflowId}: {Message}",
                location, workflowId, ex.Message);
            return new WorkflowRunResult(false, ex.Message, null);
        }
    }

    /// <inheritdoc />
    public async Task<bool> StopWorkflowAsync(string workflowId)
    {
        const string location = $"{nameof(WorkflowManagementService)}.{nameof(StopWorkflowAsync)}";

        try
        {
            var storageService = WorkflowStorageService.Instance;
            var data = await storageService.LoadWorkflowDataAsync(workflowId);

            if (data == null)
            {
                Log.Warning("[{Location}] Workflow data not found in storage for ID: {WorkflowId}",
                    location, workflowId);
                return false;
            }

            Log.Information("[{Location}] Stop requested for workflow '{Name}' (ID: {WorkflowId}) — " +
                "cancellation not yet implemented, workflow will complete current execution",
                location, data.Name, workflowId);

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Location}] Error stopping workflow {WorkflowId}: {Message}",
                location, workflowId, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompileAndPersistWorkflowAsync(string workflowId)
    {
        const string location = $"{nameof(WorkflowManagementService)}.{nameof(CompileAndPersistWorkflowAsync)}";

        try
        {
            var storageService = WorkflowStorageService.Instance;
            var data = await storageService.LoadWorkflowDataAsync(workflowId);

            if (data == null)
            {
                Log.Warning("[{Location}] Workflow data not found for ID: {WorkflowId}", location, workflowId);
                return false;
            }

            if (!data.UseBlockMode || string.IsNullOrWhiteSpace(data.BlockScriptSource))
            {
                Log.Warning("[{Location}] Workflow '{Name}' has no BlockScript source to compile",
                    location, data.Name);
                return false;
            }

            // Parse the BlockScript
            var parseResult = _blockScriptService.ParseBlockScript(data.BlockScriptSource);
            if (!parseResult.IsSuccess || parseResult.Script == null)
            {
                Log.Warning("[{Location}] Failed to parse BlockScript for workflow '{Name}': {Error}",
                    location, data.Name, parseResult.ErrorMessage);
                return false;
            }

            // Attach helper functions
            if (data.HelperFunctions != null)
                parseResult.Script.HelperFunctions = data.HelperFunctions;

            // Use BlockScriptService for compilation (it owns BlockScriptExecutor)
            var compiled = await _blockScriptService.CompileAndPersistAsync(parseResult.Script, workflowId);

            if (compiled)
            {
                Log.Information("[{Location}] Compiled and persisted workflow '{Name}' (ID: {WorkflowId})",
                    location, data.Name, workflowId);
            }
            else
            {
                Log.Warning("[{Location}] Compilation failed for workflow '{Name}' (ID: {WorkflowId})",
                    location, data.Name, workflowId);
            }

            return compiled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Location}] Error compiling workflow {WorkflowId}", location, workflowId);
            return false;
        }
    }
}
