using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.Services;
using Serilog;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// BlockScript parsing and execution service.
/// Implements the public <c>IBlockScriptService</c> (Dashboard-facing, source-string based
/// methods). The parsed-model based methods (ParseBlockScript / ExecuteBlockScriptAsync(BlockScript)
/// / CompileAndPersistAsync) live on this concrete class and are consumed directly by
/// WorkflowManagementService — the former IBlockScriptPipelineService indirection was removed
/// as it had no interface-type consumers.
/// </summary>
internal class BlockScriptService : IBlockScriptService
{
    private readonly WorkflowRuntimeState _state;
    private readonly BlockScriptExecutor _executor;

    /// <summary>
    /// Initializes a new instance of BlockScriptService.
    /// </summary>
    /// <param name="state">Shared runtime state.</param>
    /// <param name="executor">
    /// The DI-registered, plugin-manager-wired <see cref="IBlockScriptExecutor"/> singleton.
    /// Must be the same instance registered in <c>AddKitXWorkflow</c> (where its
    /// <c>SetPluginManager</c> is called with the <see cref="RealPluginManager"/>), so that
    /// <c>G.PluginCall(...)</c> dispatches to live plugins during execution. Injecting it here
    /// (instead of lazily constructing a bare <c>new BlockScriptExecutor()</c>) eliminates the
    /// prior dual-instance bug where the trigger execution path ran on an unwired executor.
    /// </param>
    internal BlockScriptService(WorkflowRuntimeState state, IBlockScriptExecutor executor)
    {
        _state = state;
        // The contract guarantees a wired executor; cast once to the concrete type the
        // pipeline (ExecuteAsync / CompileForPersistence / PreloadFromDisk / Validate) needs.
        _executor = (BlockScriptExecutor)executor;
    }

    /// <summary>
    /// Gets the BlockScript parser, creating it if necessary.
    /// </summary>
    private BlockScriptParser BlockScriptParser =>
        _state.BlockScriptParser ??= new BlockScriptParser(
            BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));

    /// <summary>
    /// Gets the DI-injected BlockScript executor (single, plugin-manager-wired instance).
    /// </summary>
    private BlockScriptExecutor BlockScriptExecutor => _executor;

    /// <inheritdoc />
    public BlockScriptParseResult ParseBlockScript(string sourceCode)
    {
        return BlockScriptParser.Parse(sourceCode);
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
            Log.Warning(ex, "[BlockScriptService] Error parsing constants from BlockScript");
        }

        return result;
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
