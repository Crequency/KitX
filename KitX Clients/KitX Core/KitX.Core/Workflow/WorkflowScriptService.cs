using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Core.Device;
using KitX.Shared.CSharp.Plugin;
using Serilog;

namespace KitX.Core.Workflow;

/// <summary>
/// Facade service that delegates to specialized workflow services.
/// Exposes all four workflow interfaces for backward compatibility.
/// </summary>
public class WorkflowScriptService : IWorkflowManagementService, IScriptExecutionService,
    IWorkflowPluginService, IBlockScriptService
{
    private static WorkflowScriptService? _instance;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    internal static WorkflowScriptService Instance => _instance ??= new();

    /// <summary>
    /// Shared runtime state across all workflow services.
    /// </summary>
    private static readonly WorkflowRuntimeState SharedState = new();

    /// <summary>
    /// Service graph — built once at singleton instantiation.
    /// </summary>
    private static readonly IBlockScriptService BlockScriptService;
    private static readonly IWorkflowPluginService PluginService;
    private static readonly IScriptExecutionService ScriptExecutionService;
    private static readonly IWorkflowManagementService ManagementService;

    /// <summary>
    /// Static constructor initializes the service graph in dependency order.
    /// </summary>
    static WorkflowScriptService()
    {
        // 1. BlockScriptService (no cross-service dependencies)
        BlockScriptService = new BlockScriptServiceImpl(SharedState);

        // 2. PluginService (depends only on SharedState)
        PluginService = new WorkflowPluginService(SharedState);

        // 3. ScriptExecutionService (depends on PluginService)
        ScriptExecutionService = new ScriptExecutionService(SharedState, PluginService);

        // 4. ManagementService (depends on BlockScriptService)
        ManagementService = new WorkflowManagementService(SharedState, BlockScriptService);

        // Pre-initialize RealPluginManager at startup
        try
        {
            var pluginsServer = PluginsServer.Instance;
            var realPluginManager = new RealPluginManager(pluginsServer);
            Kscript.CSharp.Parser.Parser.SetPluginManager(realPluginManager);
            SharedState.IsParserInitialized = true;
            Log.Information("[WorkflowScriptService] Real plugin manager pre-initialized at startup");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowScriptService] Failed to pre-initialize RealPluginManager, will retry on first script execution");
        }
    }

    /// <summary>
    /// Exposes the shared runtime state for DI registration.
    /// </summary>
    internal static WorkflowRuntimeState RuntimeState => SharedState;

    /// <summary>
    /// Exposes IBlockScriptService for DI registration.
    /// </summary>
    internal static IBlockScriptService BlockScriptServiceInstance => BlockScriptService;

    /// <summary>
    /// Exposes IWorkflowPluginService for DI registration.
    /// </summary>
    internal static IWorkflowPluginService PluginServiceInstance => PluginService;

    /// <summary>
    /// Exposes IScriptExecutionService for DI registration.
    /// </summary>
    internal static IScriptExecutionService ScriptExecutionServiceInstance => ScriptExecutionService;

    /// <summary>
    /// Exposes IWorkflowManagementService for DI registration.
    /// </summary>
    internal static IWorkflowManagementService ManagementServiceInstance => ManagementService;

    private WorkflowScriptService()
    {
        // Private constructor to enforce singleton usage.
    }

    // --- IWorkflowManagementService ---

    public IReadOnlyList<IWorkflowCase> GetWorkflows() => ManagementService.GetWorkflows();

    public void AddWorkflow(IWorkflowCase workflow) => ManagementService.AddWorkflow(workflow);

    public void RemoveWorkflow(string workflowId) => ManagementService.RemoveWorkflow(workflowId);

    public Task<bool> RunWorkflowAsync(string workflowId) => ManagementService.RunWorkflowAsync(workflowId);

    public Task<bool> StopWorkflowAsync(string workflowId) => ManagementService.StopWorkflowAsync(workflowId);

    public Task<bool> CompileAndPersistWorkflowAsync(string workflowId) =>
        ManagementService.CompileAndPersistWorkflowAsync(workflowId);

    // --- IScriptExecutionService ---

    public Task<object?> ExecuteScriptAsync(string script, Dictionary<string, object>? parameters = null) =>
        ScriptExecutionService.ExecuteScriptAsync(script, parameters);

    public Task<string?> ExecuteCodesAsync(
        string code,
        List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default) =>
        ScriptExecutionService.ExecuteCodesAsync(code, requiredPlugins, includeTimestamp, cancellationToken);

    public Task<string?> ExecuteKcsCodesAsync(
        string mainCode,
        List<HelperFunction> helperFunctions,
        List<VariableConstant> constants,
        List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default) =>
        ScriptExecutionService.ExecuteKcsCodesAsync(mainCode, helperFunctions, constants, requiredPlugins, includeTimestamp, cancellationToken);

    // --- IWorkflowPluginService ---

    public void InitializePluginManager() => PluginService.InitializePluginManager();

    public void UpdateAvailablePlugins(List<PluginInfo> plugins) =>
        PluginService.UpdateAvailablePlugins(plugins);

    public List<VariableConstant> ParseConstantsFromCode(string code) =>
        PluginService.ParseConstantsFromCode(code);

    public string ApplyConstantsToCode(string code, List<VariableConstant> constants) =>
        PluginService.ApplyConstantsToCode(code, constants);

    public string MergeHelperFunctions(string mainCode, List<HelperFunction> helperFunctions) =>
        PluginService.MergeHelperFunctions(mainCode, helperFunctions);

    // --- IBlockScriptService ---

    public BlockScriptParseResult ParseBlockScript(string sourceCode) =>
        BlockScriptService.ParseBlockScript(sourceCode);

    public Task<BlockScriptParseResult> ParseBlockScriptAsync(string sourceCode) =>
        BlockScriptService.ParseBlockScriptAsync(sourceCode);

    public BlockScriptValidationResult ValidateBlockScript(string sourceCode) =>
        BlockScriptService.ValidateBlockScript(sourceCode);

    public List<VariableConstant> ParseConstantsFromBlockScript(string sourceCode) =>
        BlockScriptService.ParseConstantsFromBlockScript(sourceCode);

    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        BlockScript script,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default) =>
        BlockScriptService.ExecuteBlockScriptAsync(script, parameters, cancellationToken);

    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default) =>
        BlockScriptService.ExecuteBlockScriptAsync(sourceCode, parameters, cancellationToken);

    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        List<HelperFunction> helperFunctions,
        CancellationToken cancellationToken = default) =>
        BlockScriptService.ExecuteBlockScriptAsync(sourceCode, helperFunctions, cancellationToken);

    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        List<HelperFunction> helperFunctions,
        Dictionary<string, object?>? constantOverrides,
        CancellationToken cancellationToken = default) =>
        BlockScriptService.ExecuteBlockScriptAsync(sourceCode, helperFunctions, constantOverrides, cancellationToken);

    public Task<bool> CompileAndPersistAsync(BlockScript script, string workflowId) =>
        BlockScriptService.CompileAndPersistAsync(script, workflowId);

    public int PreloadCompiledScripts(string workflowId) =>
        BlockScriptService.PreloadCompiledScripts(workflowId);
}
