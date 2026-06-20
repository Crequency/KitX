using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Abstractions;
using Serilog;

namespace KitX.Workflow;

/// <summary>
/// BlockScript parsing and execution service.
/// Implements the public <c>IBlockScriptService</c> (Dashboard-facing, source-string based
/// methods). The parsed-model based methods (ParseBlockScript / ExecuteBlockScriptAsync(BlockScript)
/// / CompileAndPersistAsync) live on this concrete class and are consumed directly by
/// WorkflowManagementService — the former IBlockScriptPipelineService indirection was removed
/// as it had no interface-type consumers.
/// </summary>
internal class BlockScriptServiceImpl : IBlockScriptService
{
    private readonly WorkflowRuntimeState _state;
    private RealPluginManager? _realPluginManager;

    /// <summary>
    /// Initializes a new instance of BlockScriptServiceImpl.
    /// </summary>
    /// <param name="state">Shared runtime state.</param>
    /// <param name="realPluginManager">RealPluginManager instance from DI container (optional).</param>
    internal BlockScriptServiceImpl(WorkflowRuntimeState state, RealPluginManager? realPluginManager = null)
    {
        _state = state;
        _realPluginManager = realPluginManager;
        TrySetPluginManager();
    }

    /// <summary>
    /// Sets the RealPluginManager after initialization.
    /// This is needed when RealPluginManager is resolved after BlockScriptServiceImpl is created.
    /// </summary>
    internal void SetRealPluginManager(RealPluginManager? realPluginManager)
    {
        _realPluginManager = realPluginManager;
        TrySetPluginManager();
    }

    /// <summary>
    /// Try to set the plugin manager on the BlockScriptExecutor if conditions are met.
    /// </summary>
    private void TrySetPluginManager()
    {
        if (_state.BlockScriptExecutor != null && _state.IsParserInitialized && _realPluginManager != null)
        {
            Log.Information("[BlockScriptServiceImpl] Setting RealPluginManager. HashCode: {HashCode}", _realPluginManager.GetHashCode());
            _state.BlockScriptExecutor.SetPluginManager(_realPluginManager);
        }
    }

    /// <summary>
    /// Gets the BlockScript parser, creating it if necessary.
    /// </summary>
    private BlockScriptParser BlockScriptParser =>
        _state.BlockScriptParser ??= new BlockScriptParser(
            BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));

    /// <summary>
    /// Gets the BlockScript executor, creating it if necessary.
    /// The executor is initialized with the plugin manager if the parser is initialized.
    /// </summary>
    private BlockScriptExecutor BlockScriptExecutor
    {
        get
        {
            if (_state.BlockScriptExecutor == null)
            {
                Log.Information("[BlockScriptServiceImpl] Creating new BlockScriptExecutor, IsParserInitialized = {_IsParserInitialized}",
                    _state.IsParserInitialized);
                _state.BlockScriptExecutor = new BlockScriptExecutor();
                TrySetPluginManager();
            }
            return _state.BlockScriptExecutor;
        }
    }

    /// <inheritdoc />
    public BlockScriptParseResult ParseBlockScript(string sourceCode)
    {
        return BlockScriptParser.Parse(sourceCode);
    }

    /// <inheritdoc />
    public Task<BlockScriptParseResult> ParseBlockScriptAsync(string sourceCode)
    {
        return BlockScriptParser.ParseAsync(sourceCode);
    }

    /// <inheritdoc />
    public BlockScriptValidationResult ValidateBlockScript(string sourceCode)
    {
        return BlockScriptParser.Validate(sourceCode);
    }

    /// <inheritdoc />
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
            Log.Warning(ex, "[BlockScriptServiceImpl] Error parsing constants from BlockScript");
        }

        return result;
    }

    /// <inheritdoc />
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        BlockScript script,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return BlockScriptExecutor.ExecuteAsync(script, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBlockScriptCoreAsync(sourceCode, null, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        List<HelperFunction> helperFunctions,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBlockScriptCoreAsync(sourceCode, helperFunctions, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        string sourceCode,
        List<HelperFunction> helperFunctions,
        Dictionary<string, object?>? constantOverrides,
        CancellationToken cancellationToken = default)
    {
        return ExecuteBlockScriptCoreAsync(sourceCode, helperFunctions, constantOverrides, cancellationToken);
    }

    /// <summary>
    /// Core block script execution logic: parse → validate → execute.
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

        // 3.5. Apply constant overrides from user edits
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

    /// <inheritdoc />
    public Task<bool> CompileAndPersistAsync(BlockScript script, string workflowId)
    {
        BlockScriptExecutor.SetWorkflowId(workflowId);
        var compiled = BlockScriptExecutor.CompileForPersistence(script, workflowId);
        return Task.FromResult(compiled);
    }

    /// <inheritdoc />
    public int PreloadCompiledScripts(string workflowId)
    {
        return BlockScriptExecutor.PreloadFromDisk(workflowId);
    }
}
