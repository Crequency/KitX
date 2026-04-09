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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

                return FormatExecutionResult(result, begin, sw.ElapsedMilliseconds, includeTimestamp);
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

            return FormatExecutionError(ex, begin, sw.ElapsedMilliseconds, includeTimestamp);
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

            return CombineOutput(scriptOutput, returnValue);
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

    #region Execution Result Formatting Helpers

    /// <summary>
    /// Formats successful execution output with optional timestamp header
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
    /// Formats execution error with optional timestamp header
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
    /// Combines WorkflowOutput and script return value into a single output string
    /// </summary>
    private static string? CombineOutput(string? scriptOutput, string? returnValue)
    {
        return !string.IsNullOrEmpty(scriptOutput)
            ? (string.IsNullOrEmpty(returnValue)
                ? scriptOutput
                : $"{scriptOutput}\n{returnValue}")
            : returnValue;
    }

    #endregion

    #region KCS Script Processing Methods

    /// <summary>
    /// 从代码中解析常量
    /// </summary>
    public List<VariableConstant> ParseConstantsFromCode(string code)
    {
        var result = new List<VariableConstant>();

        if (string.IsNullOrWhiteSpace(code))
            return result;

        // 简单的const解析 - 匹配 "const 类型 变量名 = 值;" 模式
        var lines = code.Split('\n');
        var newConstants = new Dictionary<string, VariableConstant>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("const "))
            {
                // 解析 const 类型 名称 = 值;
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] == "const")
                {
                    var type = parts[1];
                    var name = parts[2];
                    var valueStr = string.Join(" ", parts.Skip(3)).TrimStart('=').Trim().TrimEnd(';');

                    if (!newConstants.ContainsKey(name))
                    {
                        var defaultValue = ParseValue(valueStr, type);

                        newConstants[name] = new VariableConstant
                        {
                            Name = name,
                            DefaultValue = defaultValue,
                            UserValue = defaultValue,
                            Type = type
                        };
                    }
                }
            }
        }

        return newConstants.Values.ToList();
    }

    /// <summary>
    /// 解析常量值
    /// </summary>
    private object? ParseValue(string valueStr, string type)
    {
        if (string.IsNullOrEmpty(valueStr)) return null;

        try
        {
            return type switch
            {
                "int" => int.TryParse(valueStr, out var i) ? i : 0,
                "double" => double.TryParse(valueStr, out var d) ? d : 0.0,
                "float" => float.TryParse(valueStr, out var f) ? f : 0.0f,
                "bool" => bool.TryParse(valueStr, out var b) && b,
                "string" => valueStr.Trim('"').Trim('\''),
                _ => valueStr
            };
        }
        catch
        {
            return valueStr;
        }
    }

    /// <summary>
    /// 应用常量到代码
    /// </summary>
    /// <remarks>
    /// 使用 CSharpSyntaxRewriter 进行语法树级别的值注入，比正则表达式更准确可靠，
    /// 不会误匹配注释或字符串中的内容。
    /// </remarks>
    public string ApplyConstantsToCode(string code, List<VariableConstant> constants)
    {
        if (string.IsNullOrWhiteSpace(code) || constants == null || !constants.Any())
            return code;

        // 构建常量名称到值的字典
        var constantValues = new Dictionary<string, object?>();
        foreach (var constant in constants)
        {
            // 根据类型转换 UserValue
            var typedValue = constant.UserValue;
            if (typedValue != null && constant.Type != null)
            {
                typedValue = ConvertToTypedValue(typedValue, constant.Type);
            }
            constantValues[constant.Name] = typedValue;
        }

        // 解析代码为语法树
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // 使用语法重写器注入常量值
        var rewriter = new ConstantValueRewriter(constantValues);
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    /// <summary>
    /// 将值转换为指定类型
    /// </summary>
    private object? ConvertToTypedValue(object? value, string type)
    {
        if (value == null) return null;

        return type.ToLowerInvariant() switch
        {
            "int" => Convert.ToInt32(value),
            "long" => Convert.ToInt64(value),
            "double" => Convert.ToDouble(value),
            "float" => Convert.ToSingle(value),
            "decimal" => Convert.ToDecimal(value),
            "bool" or "boolean" => Convert.ToBoolean(value),
            "string" => value.ToString(),
            "char" => Convert.ToChar(value),
            _ => value
        };
    }

    /// <summary>
    /// 合并辅助函数到代码
    /// </summary>
    public string MergeHelperFunctions(string mainCode, List<HelperFunction> helperFunctions)
    {
        return BlockScripting.HelperFunctionCodeGenerator.MergeWithMainProgram(mainCode, helperFunctions);
    }

    /// <summary>
    /// 执行KCS代码 - 包含代码分析、常量应用、辅助函数合并
    /// </summary>
    public async Task<string?> ExecuteKcsCodesAsync(
        string mainCode,
        List<HelperFunction> helperFunctions,
        List<VariableConstant> constants,
        List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default)
    {
        // 1. 分析主程序代码
        var analyzer = new MainProgramAnalyzer();
        var analysisResult = analyzer.Analyze(mainCode);

        if (!analysisResult.IsValid)
        {
            return includeTimestamp
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Code analysis failed: {analysisResult.ForbiddenReason}"
                : $"Code analysis failed: {analysisResult.ForbiddenReason}";
        }

        // 2. 应用常量
        var codeWithConstants = ApplyConstantsToCode(mainCode, constants);

        // 3. 合并辅助函数
        var fullCode = MergeHelperFunctions(codeWithConstants, helperFunctions);

        // 4. 执行代码
        return await ExecuteCodesAsync(fullCode, requiredPlugins, includeTimestamp, cancellationToken);
    }

    #region Block Script Methods

    // Block script parser instance
    private BlockScripting.BlockScriptParser? _blockScriptParser;

    // Block script executor instance
    private BlockScripting.BlockScriptExecutor? _blockScriptExecutor;

    /// <summary>
    /// Gets the block script parser
    /// </summary>
    private BlockScripting.BlockScriptParser BlockScriptParser =>
        _blockScriptParser ??= new BlockScripting.BlockScriptParser();

    /// <summary>
    /// Gets the block script executor (initialized with plugin manager)
    /// </summary>
    private BlockScripting.BlockScriptExecutor BlockScriptExecutor
    {
        get
        {
            if (_blockScriptExecutor == null)
            {
                _blockScriptExecutor = new BlockScripting.BlockScriptExecutor();
                if (_isParserInitialized)
                {
                    var realPluginManager = new RealPluginManager(PluginsServer.Instance);
                    _blockScriptExecutor.SetPluginManager(realPluginManager);
                }
            }
            return _blockScriptExecutor;
        }
    }

    /// <summary>
    /// 解析块脚本
    /// </summary>
    public BlockScriptParseResult ParseBlockScript(string sourceCode)
    {
        return BlockScriptParser.Parse(sourceCode);
    }

    /// <summary>
    /// 异步解析块脚本
    /// </summary>
    public Task<BlockScriptParseResult> ParseBlockScriptAsync(string sourceCode)
    {
        return BlockScriptParser.ParseAsync(sourceCode);
    }

    /// <summary>
    /// 验证块脚本
    /// </summary>
    public BlockScriptValidationResult ValidateBlockScript(string sourceCode)
    {
        return BlockScriptParser.Validate(sourceCode);
    }

    /// <summary>
    /// 从 BlockScript 源码的 #ConstBlock 中解析有初始值的常量
    /// </summary>
    public List<VariableConstant> ParseConstantsFromBlockScript(string sourceCode)
    {
        var result = new List<VariableConstant>();

        if (string.IsNullOrWhiteSpace(sourceCode))
            return result;

        try
        {
            var parseResult = BlockScriptParser.Parse(sourceCode);

            if (!parseResult.IsSuccess || parseResult.Script?.ConstBlock == null)
                return result;

            foreach (var variable in parseResult.Script.ConstBlock.Variables)
            {
                if (variable.DefaultValue != null)
                {
                    result.Add(new VariableConstant
                    {
                        Name = variable.Name,
                        DefaultValue = variable.DefaultValue,
                        UserValue = variable.DefaultValue,
                        Type = variable.Type
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowScriptService] Error parsing constants from BlockScript");
        }

        return result;
    }

    /// <summary>
    /// 执行块脚本（从已解析的 BlockScript 对象）
    /// </summary>
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        BlockScript script,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return BlockScriptExecutor.ExecuteAsync(script, parameters, cancellationToken);
    }

    /// <summary>
    /// 从块脚本源代码执行
    /// </summary>
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBlockScriptCoreAsync(sourceCode, null, parameters, cancellationToken);
    }

    /// <summary>
    /// 从块脚本源代码执行（带辅助函数）
    /// </summary>
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        List<HelperFunction> helperFunctions,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBlockScriptCoreAsync(sourceCode, helperFunctions, null, cancellationToken);
    }

    /// <summary>
    /// 从块脚本源代码执行（带辅助函数和常量覆盖）
    /// </summary>
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        List<HelperFunction> helperFunctions,
        Dictionary<string, object?>? constantOverrides,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBlockScriptCoreAsync(sourceCode, helperFunctions, constantOverrides, cancellationToken);
    }

    /// <summary>
    /// 核心块脚本执行逻辑：解析 → 验证 → 执行
    /// </summary>
    private async Task<BlockScriptExecutionResult> ExecuteBlockScriptCoreAsync(
        string sourceCode,
        List<HelperFunction>? helperFunctions,
        Dictionary<string, object?>? constantOverrides,
        CancellationToken cancellationToken)
    {
        // 1. Parse
        var parseResult = BlockScriptParser.Parse(sourceCode);

        if (!parseResult.IsSuccess || parseResult.Script == null)
        {
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = parseResult.ErrorMessage ?? "Failed to parse block script"
            };
        }

        // 2. Validate
        var validationResult = BlockScriptExecutor.Validate(parseResult.Script);
        if (!validationResult.IsValid)
        {
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = string.Join("; ", validationResult.Errors)
            };
        }

        // 3. Attach helper functions if provided
        if (helperFunctions != null)
            parseResult.Script.HelperFunctions = helperFunctions;

        // 3.5. Apply constant overrides from user edits (replaces DefaultValue before execution)
        if (constantOverrides != null && parseResult.Script.ConstBlock != null)
        {
            foreach (var variable in parseResult.Script.ConstBlock.Variables)
            {
                if (constantOverrides.TryGetValue(variable.Name, out var userValue))
                {
                    variable.DefaultValue = userValue;
                }
            }
        }

        // 4. Execute
        return await BlockScriptExecutor.ExecuteAsync(parseResult.Script, constantOverrides, cancellationToken);
    }

    #endregion

    #endregion
}

/// <summary>
/// Rewriter for injecting constant values into variable declarations using CSharpSyntaxRewriter
/// </summary>
/// <remarks>
/// This approach is more reliable than regex-based matching as it understands the actual
/// syntax structure and won't accidentally match content in comments or strings.
/// </remarks>
internal class ConstantValueRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, object?> _constantValues;

    public ConstantValueRewriter(Dictionary<string, object?> constantValues)
    {
        _constantValues = constantValues;
    }

    /// <summary>
    /// Visits variable declarators to replace their initializer values
    /// </summary>
    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        // Check if this variable declarator has an initializer and matches a constant name
        if (node.Initializer != null && _constantValues.TryGetValue(node.Identifier.Text, out var newValue))
        {
            var newInitializer = CreateNewInitializer(node.Initializer, newValue);
            if (newInitializer != null)
            {
                return node.WithInitializer(newInitializer);
            }
        }

        return base.VisitVariableDeclarator(node);
    }

    /// <summary>
    /// Creates a new EqualsValueClauseSyntax with the specified value
    /// </summary>
    private EqualsValueClauseSyntax? CreateNewInitializer(EqualsValueClauseSyntax oldInitializer, object? value)
    {
        ExpressionSyntax? newExpression = null;

        if (value == null)
        {
            newExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (value is int intVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(intVal));
        }
        else if (value is long longVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(longVal));
        }
        else if (value is double doubleVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(doubleVal));
        }
        else if (value is float floatVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(floatVal));
        }
        else if (value is decimal decimalVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(decimalVal));
        }
        else if (value is bool boolVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                boolVal ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
        }
        else if (value is string stringVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(stringVal));
        }
        else if (value is char charVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                SyntaxFactory.Literal(charVal));
        }

        if (newExpression != null)
        {
            return SyntaxFactory.EqualsValueClause(newExpression)
                .WithLeadingTrivia(oldInitializer.GetLeadingTrivia())
                .WithTrailingTrivia(oldInitializer.GetTrailingTrivia());
        }

        return null;
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
