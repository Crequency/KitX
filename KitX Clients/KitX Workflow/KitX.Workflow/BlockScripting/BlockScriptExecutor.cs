using System.Diagnostics;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.CFG;

using KitX.Workflow.Conversion;
namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Block script executor using full-script assembly compilation.
/// </summary>
public class BlockScriptExecutor : IBlockScriptExecutor
{
    private readonly BlockScopeManager _scopeManager;
    private readonly Stopwatch _stopwatch = new();
    private List<string> _output = new();
    private BlockScriptExecutionGlobals? _globals;
    private IPluginManager? _pluginManager;
    private string? _workflowId;
    private readonly CSCompiler _assemblyCompiler = new();
    private IBlueprintDebugController? _debugger;

    /// <summary>
    /// Creates a new block script executor
    /// </summary>
    public BlockScriptExecutor()
    {
        _scopeManager = new BlockScopeManager();
    }

    /// <summary>
    /// Creates a new block script executor with existing scope manager
    /// </summary>
    public BlockScriptExecutor(BlockScopeManager scopeManager)
    {
        _scopeManager = scopeManager;
    }

    /// <summary>
    /// Creates a new block script executor with plugin manager support
    /// </summary>
    public BlockScriptExecutor(IPluginManager? pluginManager) : this()
    {
        _pluginManager = pluginManager;
    }

    /// <summary>
    /// Sets the plugin manager for plugin function calls during execution.
    /// </summary>
    public void SetPluginManager(IPluginManager? pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <summary>
    /// Sets the workflow ID for disk persistence of compiled assemblies.
    /// When set, compiled assemblies are saved to and loaded from disk
    /// to enable cross-session reuse.
    /// </summary>
    public void SetWorkflowId(string? workflowId)
    {
        _workflowId = workflowId;
    }

    public void SetDebugger(IBlueprintDebugController? debugger)
    {
        _debugger = debugger;
        CFG2CSGenerator.IsDebugMode = debugger != null;
    }

    /// <summary>
    /// Compiles a BlockScript and persists it to disk (without executing).
    /// Used for pre-compilation at workflow save time.
    /// </summary>
    public bool CompileForPersistence(BlockScript script, string workflowId)
    {
        try
        {
            var compiled = _assemblyCompiler.CompileScript(script, workflowId);
            return compiled != null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[BlockScriptExecutor] CompileForPersistence failed for workflow {WfId}", workflowId);
            return false;
        }
    }

    /// <summary>
    /// Preloads all persisted compiled scripts for a workflow from disk
    /// into the in-memory cache.
    /// </summary>
    public int PreloadFromDisk(string workflowId)
    {
        return _assemblyCompiler.PreloadFromDisk(workflowId);
    }

    /// <summary>
    /// Executes a block script using full-script assembly compilation.
    /// </summary>
    public async Task<BlockScriptExecutionResult> ExecuteAsync(
        BlockScript script,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        _output = new List<string>();

        try
        {
            CFG2CSGenerator.IsDebugMode = _debugger != null;

            // Full-script assembly compilation
            var compiled = _assemblyCompiler.CompileScript(script, _workflowId, out var compileErrors);
            if (compiled == null)
            {
                _stopwatch.Stop();
                return new BlockScriptExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = FormatCompileErrors(compileErrors),
                    ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                    Output = _output
                };
            }

            Log.Debug("[BlockScriptExecutor] Using assembly-compiled execution path");
            _globals = new BlockScriptExecutionGlobals(_scopeManager, _output, _pluginManager);
            _globals.Debugger = _debugger;
            _globals.ResetRunState();
            _scopeManager.InitializeGlobalScope(script);

            // Import parameters
            if (parameters != null)
            {
                foreach (var p in parameters)
                    _globals.Set(p.Key, p.Value);
            }

            await compiled.RunAsync(_globals, cancellationToken);

            _stopwatch.Stop();
            return new BlockScriptExecutionResult
            {
                IsSuccess = true,
                ExecutedBlockCount = _globals.ExecutedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = _output
            };
        }
        catch (OperationCanceledException)
        {
            _stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            Log.Error(ex, "[BlockScriptExecutor] Error executing block script");
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Execution error: {ex.Message}",
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = _output
            };
        }
    }

    /// <summary>
    /// Executes a BlockScript using a pre-built CFG (BP→CFG→CS direct path).
    /// Skips the BS→CFG conversion, preserving StatementIds from the blueprint.
    /// </summary>
    internal async Task<BlockScriptExecutionResult> ExecuteFromCFGAsync(
        BlockScript script,
        ControlFlowGraph cfg,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        _output = new List<string>();

        try
        {
            CFG2CSGenerator.IsDebugMode = _debugger != null;

            var compiled = _assemblyCompiler.CompileFromCFG(cfg, script, _workflowId, out var compileErrors);
            if (compiled == null)
            {
                _stopwatch.Stop();
                return new BlockScriptExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = FormatCompileErrors(compileErrors),
                    ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                    Output = _output
                };
            }

            Log.Debug("[BlockScriptExecutor] Using BP→CFG→CS direct execution path");
            _globals = new BlockScriptExecutionGlobals(_scopeManager, _output, _pluginManager);
            _globals.Debugger = _debugger;
            _globals.ResetRunState();
            _scopeManager.InitializeGlobalScope(script);

            if (parameters != null)
            {
                foreach (var p in parameters)
                    _globals.Set(p.Key, p.Value);
            }

            await compiled.RunAsync(_globals, cancellationToken);

            _stopwatch.Stop();
            return new BlockScriptExecutionResult
            {
                IsSuccess = true,
                ExecutedBlockCount = _globals.ExecutedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = _output
            };
        }
        catch (OperationCanceledException)
        {
            _stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            Log.Error(ex, "[BlockScriptExecutor] Error executing from CFG");
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Execution error: {ex.Message}",
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = _output
            };
        }
    }

    /// <summary>
    /// Validates a block script
    /// </summary>
    public BlockScriptValidationResult Validate(BlockScript script)
    {
        var result = new BlockScriptValidationResult { IsValid = true };

        if (script.MainBlock == null)
        {
            result.AddError("Script must have a MainBlock");
            result.IsValid = false;
        }

        // Check that all branch targets exist
        var allBlockNames = script.NamedBlocks.Keys.ToHashSet();
        if (script.MainBlock != null)
            allBlockNames.Add(script.MainBlock.Name);
        if (script.ConstBlock != null)
            allBlockNames.Add(script.ConstBlock.Name);
        if (script.PubVarBlock != null)
            allBlockNames.Add(script.PubVarBlock.Name);

        // Add LoopBlocks to the set of known blocks
        foreach (var loopBlock in script.LoopBlocks.Values)
        {
            allBlockNames.Add(loopBlock.Name);
        }

        foreach (var block in script.AllBlocks)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is FlowControlStatement flow)
                {
                    // Validate every arm target exists. Works uniformly for Branch (True/False),
                    // Loop (LoopBody/LoopEnd), ToLoopCond (Exec loopback) and Switch (Default/0/1/...).
                    foreach (var arm in flow.Arms)
                    {
                        if (!string.IsNullOrEmpty(arm.TargetBlockName) &&
                            !allBlockNames.Contains(arm.TargetBlockName))
                        {
                            result.AddError($"Block '{arm.TargetBlockName}' referenced in {flow.ControlType} (arm '{arm.PinName}') at line {flow.LineNumber} does not exist");
                        }
                    }
                }
            }
        }

        if (result.Errors.Count > 0)
            result.IsValid = false;

        return result;
    }

    /// <summary>
    /// Formats Roslyn compilation diagnostics into a single multi-line string suitable
    /// for display in the workflow editor's output panel. Capped at 10 entries so a flood
    /// of cascading errors (e.g. one missing type producing dozens) stays readable.
    /// </summary>
    private static string FormatCompileErrors(IReadOnlyList<string>? errors)
    {
        if (errors is null || errors.Count == 0)
            return "Script compilation failed (no diagnostic details were captured).";

        const int maxShown = 10;
        var sb = new System.Text.StringBuilder();
        sb.Append("Script compilation failed with ");
        sb.Append(errors.Count);
        sb.Append(" error");
        if (errors.Count != 1) sb.Append('s');
        sb.Append(':');
        sb.AppendLine();
        var shown = Math.Min(maxShown, errors.Count);
        for (var i = 0; i < shown; i++)
        {
            sb.Append("  • ");
            sb.AppendLine(errors[i]);
        }
        if (errors.Count > maxShown)
        {
            sb.Append("  • …and ");
            sb.Append(errors.Count - maxShown);
            sb.AppendLine(" more (see Log/ for the full list).");
        }
        return sb.ToString().TrimEnd();
    }
}
