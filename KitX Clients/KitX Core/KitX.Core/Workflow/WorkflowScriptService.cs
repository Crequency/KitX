using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Core.Device;
using KitX.Core.DI;
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
    /// <summary>
    /// Gets the singleton facade instance.
    /// WorkflowScriptService is a facade that delegates to its static service graph,
    /// so it's not resolved from DI (IBlockScriptService is registered as BlockScriptServiceImpl).
    /// Use ServiceHost.GetRequiredService&lt;IWorkflowManagementService&gt;() etc. for individual interfaces.
    /// </summary>
    public static WorkflowScriptService Instance { get; } = new();

    /// <summary>
    /// Shared runtime state across all workflow services.
    /// </summary>
    private static readonly WorkflowRuntimeState SharedState = new();

    /// <summary>
    /// Service graph — built once at singleton instantiation.
    /// </summary>
    private static IBlockScriptService? _blockScriptService;
    private static IWorkflowManagementService? _managementService;
    private static readonly IWorkflowPluginService PluginService;
    private static readonly IScriptExecutionService ScriptExecutionService;

    /// <summary>
    /// Static constructor initializes the service graph in dependency order.
    /// Note: BlockScriptService is NOT initialized here to avoid accessing it before
    /// PreResolvedRealPluginManager is set. BlockScriptService is fully lazy-initialized
    /// via its property getter.
    /// </summary>
    static WorkflowScriptService()
    {
        // 1. PluginService (depends only on SharedState)
        PluginService = new WorkflowPluginService(SharedState);

        // 2. ScriptExecutionService (depends on PluginService)
        ScriptExecutionService = new ScriptExecutionService(SharedState, PluginService);

        // 3. ManagementService - will be initialized lazily when first accessed
        // BlockScriptService is also lazy-initialized, so ManagementService should not
        // be created here to avoid using a partially initialized BlockScriptService
    }

    /// <summary>
    /// Gets the BlockScriptService, creating it lazily once RealPluginManager is available.
    /// This ensures the BlockScriptService always has a valid RealPluginManager.
    /// </summary>
    private static IBlockScriptService BlockScriptService
    {
        get
        {
            if (_blockScriptService == null)
            {
                RealPluginManager? rpm = null;
                try
                {
                    rpm = ServiceHost.GetRequiredService<RealPluginManager>();
                    if (rpm != null)
                    {
                        Kscript.CSharp.Parser.Parser.SetPluginManager(rpm);
                        SharedState.IsParserInitialized = true;
                        Log.Information("[WorkflowScriptService] Real plugin manager obtained. HashCode: {HashCode}", rpm.GetHashCode());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[WorkflowScriptService] Failed to get RealPluginManager");
                }

                _blockScriptService = new BlockScriptServiceImpl(SharedState, rpm);
                Log.Information("[WorkflowScriptService] BlockScriptService created with RealPluginManager. HashCode: {HashCode}", rpm?.GetHashCode());
            }
            return _blockScriptService;
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
    /// Gets the ManagementService, creating it lazily once BlockScriptService is available.
    /// </summary>
    private static IWorkflowManagementService ManagementService =>
        _managementService ??= new WorkflowManagementService(SharedState, BlockScriptService);

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
