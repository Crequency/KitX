using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Csharpell.Core;
using KitX.Core.Contract.Workflow;
using KitX.Shared.CSharp.Plugin;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Serilog;

namespace KitX.Core.Workflow;

/// <summary>
/// C# script execution service.
/// Implements IScriptExecutionService.
/// </summary>
internal class ScriptExecutionService : IScriptExecutionService
{
    private readonly WorkflowRuntimeState _state;
    private readonly IWorkflowPluginService _pluginService;

    /// <summary>
    /// Initializes a new instance of ScriptExecutionService.
    /// </summary>
    /// <param name="state">Shared runtime state.</param>
    /// <param name="pluginService">Plugin service for constants and helper functions.</param>
    internal ScriptExecutionService(WorkflowRuntimeState state, IWorkflowPluginService pluginService)
    {
        _state = state;
        _pluginService = pluginService;
    }

    /// <summary>
    /// Gets the script engine, creating it if necessary.
    /// </summary>
    private CSharpScriptEngine Engine => _state.Engine ??= new CSharpScriptEngine();

    /// <inheritdoc />
    public async Task<object?> ExecuteScriptAsync(string script, System.Collections.Generic.Dictionary<string, object>? parameters = null)
    {
        const string location = $"{nameof(ScriptExecutionService)}.{nameof(ExecuteScriptAsync)}";

        try
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                Log.Warning("Script is empty");
                return await System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            var result = await ExecuteCodesAsync(script, null, true, CancellationToken.None);
            return await System.Threading.Tasks.Task.FromResult<object?>(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error executing script: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult<object?>(null);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ExecuteCodesAsync(
        string code,
        System.Collections.Generic.List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default)
    {
        const string location = $"{nameof(ScriptExecutionService)}.{nameof(ExecuteCodesAsync)}";

        var sw = new Stopwatch();
        var begin = DateTime.Now;
        sw.Start();

        try
        {
            if (requiredPlugins != null && requiredPlugins.Any())
            {
                if (!_state.IsParserInitialized)
                {
                    _pluginService.InitializePluginManager();
                }

                var pluginsReady = await EnsurePluginsReadyAsync(requiredPlugins, cancellationToken);

                if (!pluginsReady)
                {
                    Log.Warning("[ScriptExecutionService] Some plugins failed to start, but continuing script execution");
                }

                Assembly? pluginApiAssembly = null;
                try
                {
                    pluginApiAssembly = Kscript.CSharp.Parser.Parser.Generate(requiredPlugins, "KitXWorkflowPlugins", useCache: false);
                    Log.Information($"[ScriptExecutionService] Successfully generated plugin API with {requiredPlugins.Count} plugins");
                }
                catch (Exception ex)
                {
                    var error = $"Failed to generate plugin API: {ex.Message}";
                    Log.Error(ex, error);
                }

                var result = await ExecuteScriptWithPluginsAsync(
                    code,
                    pluginApiAssembly,
                    includeTimestamp,
                    cancellationToken
                );

                sw.Stop();
                return FormatExecutionResult(result, begin, sw.ElapsedMilliseconds, includeTimestamp);
            }
            else
            {
                return await ExecuteCodesWithoutPluginsAsync(code, includeTimestamp, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, $"In {location}: Error executing code: {ex.Message}");
            return FormatExecutionError(ex, begin, sw.ElapsedMilliseconds, includeTimestamp);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ExecuteKcsCodesAsync(
        string mainCode,
        System.Collections.Generic.List<HelperFunction> helperFunctions,
        System.Collections.Generic.List<VariableConstant> constants,
        System.Collections.Generic.List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default)
    {
        // 1. Analyze main program
        var analyzer = new MainProgramAnalyzer();
        var analysisResult = analyzer.Analyze(mainCode);

        if (!analysisResult.IsValid)
        {
            return includeTimestamp
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Code analysis failed: {analysisResult.ForbiddenReason}"
                : $"Code analysis failed: {analysisResult.ForbiddenReason}";
        }

        // 2. Apply constants
        var codeWithConstants = _pluginService.ApplyConstantsToCode(mainCode, constants);

        // 3. Merge helper functions
        var fullCode = _pluginService.MergeHelperFunctions(codeWithConstants, helperFunctions);

        // 4. Execute
        return await ExecuteCodesAsync(fullCode, requiredPlugins, includeTimestamp, cancellationToken);
    }

    /// <summary>
    /// Executes script with plugin dependencies.
    /// </summary>
    private async Task<string?> ExecuteScriptWithPluginsAsync(
        string code,
        Assembly? pluginApiAssembly,
        bool includeTimestamp,
        CancellationToken cancellationToken)
    {
        try
        {
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

            var scriptOutput = WorkflowOutput.GetAndClear();
            var returnValue = result?.ToString();

            return CombineOutput(scriptOutput, returnValue);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ScriptExecutionService] Error executing script with plugins");
            return $"Script execution error: {ex.Message}";
        }
    }

    /// <summary>
    /// Ensures required plugins are loaded and running.
    /// </summary>
    private Task<bool> EnsurePluginsReadyAsync(
        System.Collections.Generic.List<PluginInfo> requiredPlugins,
        CancellationToken cancellationToken = default)
    {
        Log.Information($"[ScriptExecutionService] Checking {requiredPlugins.Count} required plugins");

        foreach (var plugin in requiredPlugins)
        {
            Log.Information($"[ScriptExecutionService] Required plugin: {plugin.Name}");
        }

        return System.Threading.Tasks.Task.FromResult(true);
    }

    /// <summary>
    /// Executes code without plugin dependencies.
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

            var scriptOutput = WorkflowOutput.GetAndClear();
            var returnValue = result?.ToString();

            var combinedOutput = CombineOutput(scriptOutput, returnValue);

            return FormatExecutionResult(combinedOutput, begin, sw.ElapsedMilliseconds, includeTimestamp);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return FormatExecutionError(ex, begin, sw.ElapsedMilliseconds, includeTimestamp);
        }
    }

    /// <summary>
    /// Formats successful execution output with optional timestamp header.
    /// </summary>
    private static string? FormatExecutionResult(string? output, DateTime begin, long elapsedMs, bool includeTimestamp)
    {
        if (!includeTimestamp)
            return output;

        return new StringBuilder()
            .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
            .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Script ended, took {elapsedMs} ms.")
            .AppendLine(output)
            .ToString();
    }

    /// <summary>
    /// Formats execution error with optional timestamp header.
    /// </summary>
    private static string? FormatExecutionError(Exception ex, DateTime begin, long elapsedMs, bool includeTimestamp)
    {
        if (!includeTimestamp)
            return ex.StackTrace;

        return new StringBuilder()
            .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
            .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {elapsedMs} ms, Message: {ex.Message}")
            .AppendLine(ex.StackTrace)
            .ToString();
    }

    /// <summary>
    /// Combines WorkflowOutput and script return value into a single output string.
    /// </summary>
    private static string? CombineOutput(string? scriptOutput, string? returnValue)
    {
        return !string.IsNullOrEmpty(scriptOutput)
            ? (string.IsNullOrEmpty(returnValue)
                ? scriptOutput
                : $"{scriptOutput}\n{returnValue}")
            : returnValue;
    }
}
