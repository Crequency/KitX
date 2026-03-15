using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Csharpell.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Kscript.CSharp.Parser;
using KitX.Core.Contract.Workflow;
using KitX.Core.Device;
using KitX.Shared.CSharp.Plugin;
using Serilog;
using CTask = System.Threading.Tasks.Task;

namespace KitX.Core.Workflow;

/// <summary>
/// Workflow script service for executing workflows
/// </summary>
public class WorkflowScriptService : IWorkflowService
{
    private static WorkflowScriptService? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static WorkflowScriptService Instance => _instance ??= new();

    private readonly List<IWorkflowCase> _workflows = new();

    /// <summary>
    /// CSharpScriptEngine instance for script execution
    /// </summary>
    private CSharpScriptEngine? _engine;

    /// <summary>
    /// Whether the plugin manager is initialized
    /// </summary>
    private bool _isParserInitialized = false;

    /// <summary>
    /// Stores available plugins for workflow execution
    /// </summary>
    private List<PluginInfo> _availablePlugins { get; set; } = new();

    /// <summary>
    /// Private constructor - initializes RealPluginManager immediately
    /// </summary>
    private WorkflowScriptService()
    {
        // Pre-initialize RealPluginManager to ensure it subscribes to plugin events
        // This must be done at startup, not when first script is executed
        try
        {
            var pluginsServer = PluginsServer.Instance;
            var realPluginManager = new RealPluginManager(pluginsServer);
            Parser.SetPluginManager(realPluginManager);
            _isParserInitialized = true;
            Log.Information("[WorkflowScriptService] Real plugin manager pre-initialized at startup");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowScriptService] Failed to pre-initialize RealPluginManager, will retry on first script execution");
        }
    }

    /// <summary>
    /// Gets the script engine, creating it if necessary
    /// </summary>
    private CSharpScriptEngine Engine => _engine ??= new CSharpScriptEngine();

    /// <summary>
    /// Gets the workflow list
    /// </summary>
    /// <returns>List of workflow cases</returns>
    public IReadOnlyList<IWorkflowCase> GetWorkflows()
    {
        return _workflows.ToList();
    }

    /// <summary>
    /// Runs a workflow
    /// </summary>
    /// <param name="workflowId">The workflow ID</param>
    /// <returns>True if run was successful</returns>
    public async Task<bool> RunWorkflowAsync(string workflowId)
    {
        const string location = $"{nameof(WorkflowScriptService)}.{nameof(RunWorkflowAsync)}";

        try
        {
            var workflow = _workflows.FirstOrDefault(w => w.Id == workflowId);

            if (workflow == null)
            {
                Log.Warning($"Workflow not found: {workflowId}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            if (workflow.IsRunning)
            {
                Log.Warning($"Workflow is already running: {workflow.Name}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            workflow.IsRunning = true;

            // TODO: Implement actual workflow execution logic
            // This would involve parsing the workflow script and executing it

            Log.Information($"Started workflow: {workflow.Name}");

            return await System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error running workflow {workflowId}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult(false);
        }
    }

    /// <summary>
    /// Stops a workflow
    /// </summary>
    /// <param name="workflowId">The workflow ID</param>
    /// <returns>True if stop was successful</returns>
    public async Task<bool> StopWorkflowAsync(string workflowId)
    {
        const string location = $"{nameof(WorkflowScriptService)}.{nameof(StopWorkflowAsync)}";

        try
        {
            var workflow = _workflows.FirstOrDefault(w => w.Id == workflowId);

            if (workflow == null)
            {
                Log.Warning($"Workflow not found: {workflowId}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            if (!workflow.IsRunning)
            {
                Log.Warning($"Workflow is not running: {workflow.Name}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            workflow.IsRunning = false;

            // TODO: Implement actual workflow stopping logic

            Log.Information($"Stopped workflow: {workflow.Name}");

            return await System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error stopping workflow {workflowId}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult(false);
        }
    }

    /// <summary>
    /// Executes a workflow script
    /// </summary>
    /// <param name="script">The script content</param>
    /// <param name="parameters">Optional parameters</param>
    /// <returns>The execution result</returns>
    public async Task<object?> ExecuteScriptAsync(string script, Dictionary<string, object>? parameters = null)
    {
        const string location = $"{nameof(WorkflowScriptService)}.{nameof(ExecuteScriptAsync)}";

        try
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                Log.Warning("Script is empty");
                return await System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            // Execute using the full ExecuteCodesAsync method
            var result = await ExecuteCodesAsync(script, null, true, CancellationToken.None);

            return await System.Threading.Tasks.Task.FromResult<object?>(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error executing script: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult<object?>(null);
        }
    }

    /// <summary>
    /// Adds a workflow
    /// </summary>
    /// <param name="workflow">The workflow to add</param>
    public void AddWorkflow(IWorkflowCase workflow)
    {
        if (workflow != null && !_workflows.Any(w => w.Id == workflow.Id))
        {
            _workflows.Add(workflow);
        }
    }

    /// <summary>
    /// Removes a workflow
    /// </summary>
    /// <param name="workflowId">The workflow ID</param>
    public void RemoveWorkflow(string workflowId)
    {
        var workflow = _workflows.FirstOrDefault(w => w.Id == workflowId);
        if (workflow != null)
        {
            _workflows.Remove(workflow);
        }
    }

    /// <summary>
    /// Executes workflow script codes with plugin dependencies
    /// </summary>
    public async Task<string?> ExecuteCodesAsync(
        string code,
        List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default)
    {
        const string location = $"{nameof(WorkflowScriptService)}.{nameof(ExecuteCodesAsync)}";

        var sw = new Stopwatch();
        var begin = DateTime.Now;
        sw.Start();

        try
        {
            // If plugins are required, ensure they are loaded
            if (requiredPlugins != null && requiredPlugins.Any())
            {
                // Ensure plugin manager is initialized
                if (!_isParserInitialized)
                {
                    InitializePluginManager();
                }

                // Ensure plugins are ready
                var pluginsReady = await EnsurePluginsReadyAsync(requiredPlugins, cancellationToken);

                if (!pluginsReady)
                {
                    Log.Warning("[WorkflowScriptService] Some plugins failed to start, but continuing script execution");
                }

                // Generate plugin API assembly
                Assembly? pluginApiAssembly = null;
                try
                {
                    // Disable cache during development to ensure RealPluginManager is used
                    pluginApiAssembly = Parser.Generate(requiredPlugins, "KitXWorkflowPlugins", useCache: false);
                    Log.Information($"[WorkflowScriptService] Successfully generated plugin API with {requiredPlugins.Count} plugins");
                }
                catch (Exception ex)
                {
                    var error = $"Failed to generate plugin API: {ex.Message}";
                    Log.Error(ex, error);
                }

                // Execute script with plugins
                var result = await ExecuteScriptWithPluginsAsync(
                    code,
                    pluginApiAssembly,
                    includeTimestamp,
                    cancellationToken
                );

                sw.Stop();

                return includeTimestamp
                    ? new StringBuilder()
                        .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                        .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Script ended, took {sw.ElapsedMilliseconds} ms.")
                        .AppendLine(result)
                        .ToString()
                    : result;
            }
            else
            {
                // No plugin dependencies, execute directly
                return await ExecuteCodesWithoutPluginsAsync(code, includeTimestamp, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();

            Log.Error(ex, $"In {location}: Error executing code: {ex.Message}");

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                    .AppendLine(
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {sw.ElapsedMilliseconds} ms, Message: {ex.Message}"
                    )
                    .AppendLine(ex.StackTrace)
                    .ToString()
                : ex.StackTrace;
        }
    }

    /// <summary>
    /// Executes script with plugin dependencies
    /// </summary>
    private async Task<string?> ExecuteScriptWithPluginsAsync(
        string code,
        Assembly? pluginApiAssembly,
        bool includeTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            // Clear previous output before execution
            WorkflowOutput.GetAndClear();

            var result = await Engine.ExecuteAsync(
                code,
                options =>
                {
                    options = options
                        .WithReferences(Assembly.GetExecutingAssembly())
                        .WithImports(
                            "KitX",
                            "KitX.Core",
                            "KitX.Core.Workflow",
                            "KitX.Shared.CSharp.Plugin",
                            "System",
                            "System.Collections.Generic",
                            "System.Threading.Tasks"
                        )
                        .WithLanguageVersion(LanguageVersion.Preview);

                    // Add plugin API assembly if available
                    if (pluginApiAssembly != null)
                    {
                        options = options.WithReferences(pluginApiAssembly);
                    }

                    return options;
                },
                addDefaultImports: true,
                runInReplMode: false,
                cancellationToken: cancellationToken
            );

            // Get output from WorkflowOutput and combine with return value
            var scriptOutput = WorkflowOutput.GetAndClear();
            var returnValue = result?.ToString();

            if (!string.IsNullOrEmpty(scriptOutput))
            {
                return string.IsNullOrEmpty(returnValue)
                    ? scriptOutput
                    : $"{scriptOutput}\n{returnValue}";
            }

            return returnValue;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WorkflowScriptService] Error executing script with plugins");
            return $"Script execution error: {ex.Message}";
        }
    }

    /// <summary>
    /// Ensures required plugins are loaded and running
    /// </summary>
    private async Task<bool> EnsurePluginsReadyAsync(
        List<PluginInfo> requiredPlugins,
        CancellationToken cancellationToken = default)
    {
        // Note: This requires IPluginService to check if plugins are running
        // For now, we log the required plugins
        Log.Information($"[WorkflowScriptService] Checking {requiredPlugins.Count} required plugins");

        foreach (var plugin in requiredPlugins)
        {
            Log.Information($"[WorkflowScriptService] Required plugin: {plugin.Name}");
        }

        // Simplified: assume all plugins are ready
        // Full implementation would use IPluginService to check status
        return await System.Threading.Tasks.Task.FromResult(true);
    }

    /// <summary>
    /// Executes code without plugin dependencies
    /// </summary>
    private async Task<string?> ExecuteCodesWithoutPluginsAsync(
        string code,
        bool includeTimestamp,
        CancellationToken cancellationToken)
    {
        var sw = new Stopwatch();
        var begin = DateTime.Now;
        sw.Start();

        try
        {
            // Clear previous output before execution
            WorkflowOutput.GetAndClear();

            var result = await Engine.ExecuteAsync(
                code,
                options => options
                    .WithReferences(Assembly.GetExecutingAssembly())
                    .WithImports(
                        "System",
                        "System.Collections.Generic",
                        "System.Threading.Tasks",
                        "KitX.Core.Workflow"
                    )
                    .WithLanguageVersion(LanguageVersion.Preview),
                addDefaultImports: true,
                runInReplMode: false,
                cancellationToken: cancellationToken
            );

            sw.Stop();

            // Get output from WorkflowOutput and combine with return value
            var scriptOutput = WorkflowOutput.GetAndClear();
            var returnValue = result?.ToString();

            var combinedOutput = !string.IsNullOrEmpty(scriptOutput)
                ? (string.IsNullOrEmpty(returnValue)
                    ? scriptOutput
                    : $"{scriptOutput}\n{returnValue}")
                : returnValue;

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Script ended, took {sw.ElapsedMilliseconds} ms.")
                    .AppendLine(combinedOutput)
                    .ToString()
                : combinedOutput;
        }
        catch (Exception ex)
        {
            sw.Stop();

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                    .AppendLine(
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {sw.ElapsedMilliseconds} ms, Message: {ex.Message}"
                    )
                    .AppendLine(ex.StackTrace)
                    .ToString()
                : ex.StackTrace;
        }
    }

    /// <summary>
    /// Initializes the plugin manager
    /// </summary>
    public void InitializePluginManager()
    {
        if (_isParserInitialized) return;

        try
        {
            // Use the real plugin manager that communicates via WebSocket
            var pluginsServer = PluginsServer.Instance;
            var realPluginManager = new RealPluginManager(pluginsServer);

            Parser.SetPluginManager(realPluginManager);

            _isParserInitialized = true;
            Log.Information("[WorkflowScriptService] Real plugin manager initialized");
        }
        catch (Exception ex)
        {
            Log.Error($"[WorkflowScriptService] Failed to initialize real plugin manager: {ex.Message}, falling back to mock");
            // Use mock manager as fallback
            Parser.SetPluginManager(new Kscript.CSharp.Parser.Core.MockPluginManager());
            _isParserInitialized = true;
        }
    }

    /// <summary>
    /// Updates the available plugins list
    /// </summary>
    public void UpdateAvailablePlugins(List<PluginInfo> plugins)
    {
        _availablePlugins = plugins ?? new List<PluginInfo>();
        Log.Information($"[WorkflowScriptService] Updated available plugins: {_availablePlugins.Count} plugins");
    }
}

/// <summary>
/// Workflow case implementation
/// </summary>
public class WorkflowCase : IWorkflowCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Untitled Workflow";
    public string Description { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public string? ScriptPath { get; set; }
}

/// <summary>
/// Plugin service provider implementation for workflow integration
/// </summary>
public class PluginServiceProvider : IPluginServiceProvider
{
    private readonly List<PluginInfo> _runningPlugins = new();
    private readonly object? _pluginsServer;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="pluginsServer">Plugins server instance (can be null)</param>
    public PluginServiceProvider(object? pluginsServer)
    {
        _pluginsServer = pluginsServer;
    }

    /// <summary>
    /// Generates a plugin ID from plugin info
    /// </summary>
    private Guid GeneratePluginId(PluginInfo pluginInfo)
    {
        // Generate deterministic GUID from: PublisherName_AuthorName_Name_Version
        var input = $"{pluginInfo.PublisherName}_{pluginInfo.AuthorName}_{pluginInfo.Name}_{pluginInfo.Version}";

        // Use MD5 hash to create a deterministic GUID
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));

        // Convert first 16 bytes to GUID
        return new Guid(hash.Take(16).ToArray());
    }

    /// <summary>
    /// Gets running plugins
    /// </summary>
    public IEnumerable<PluginInfo> GetRunningPlugins()
    {
        return _runningPlugins.ToList();
    }

    /// <summary>
    /// Finds a plugin by name
    /// </summary>
    public PluginInfo? FindPlugin(string pluginName)
    {
        return _runningPlugins.FirstOrDefault(p => p.Name == pluginName);
    }

    /// <summary>
    /// Finds a connector for a plugin
    /// </summary>
    public object? FindConnector(PluginInfo pluginInfo)
    {
        // TODO: Implement connector lookup using plugins server
        return null;
    }

    /// <summary>
    /// Sends a request asynchronously
    /// </summary>
    public CTask SendRequestAsync(object connector, object request)
    {
        // TODO: Implement request sending
        return CTask.CompletedTask;
    }

    /// <summary>
    /// Subscribes to plugin responses
    /// </summary>
    public void SubscribeToResponses(Action<string, string> responseHandler)
    {
        // TODO: Implement response subscription
    }

    /// <summary>
    /// Adds a running plugin
    /// </summary>
    public void AddRunningPlugin(PluginInfo pluginInfo)
    {
        if (!_runningPlugins.Any(p => GeneratePluginId(p) == GeneratePluginId(pluginInfo)))
        {
            _runningPlugins.Add(pluginInfo);
        }
    }

    /// <summary>
    /// Removes a running plugin
    /// </summary>
    public void RemoveRunningPlugin(Guid pluginId)
    {
        var plugin = _runningPlugins.FirstOrDefault(p => GeneratePluginId(p) == pluginId);
        if (plugin != null)
        {
            _runningPlugins.Remove(plugin);
        }
    }
}
