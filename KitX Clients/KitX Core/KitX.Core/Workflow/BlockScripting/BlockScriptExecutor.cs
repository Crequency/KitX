using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Workflow;
using KitX.Core.Workflow.Blueprint.CFG;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

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
            var compiled = _assemblyCompiler.CompileScript(script, _workflowId);
            if (compiled == null)
            {
                _stopwatch.Stop();
                return new BlockScriptExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Script compilation failed",
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

            var compiled = _assemblyCompiler.CompileFromCFG(cfg, script, _workflowId);
            if (compiled == null)
            {
                _stopwatch.Stop();
                return new BlockScriptExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Script compilation failed",
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
                    if (flow.ControlType == FlowControlType.Branch)
                    {
                        if (!string.IsNullOrEmpty(flow.TrueBlockName) &&
                            !allBlockNames.Contains(flow.TrueBlockName))
                        {
                            result.AddError($"Block '{flow.TrueBlockName}' referenced in Branch at line {flow.LineNumber} does not exist");
                        }
                        if (!string.IsNullOrEmpty(flow.FalseBlockName) &&
                            !allBlockNames.Contains(flow.FalseBlockName))
                        {
                            result.AddError($"Block '{flow.FalseBlockName}' referenced in Branch at line {flow.LineNumber} does not exist");
                        }
                    }
                    else if (flow.ControlType == FlowControlType.Loop)
                    {
                        if (!string.IsNullOrEmpty(flow.TrueBlockName) &&
                            !allBlockNames.Contains(flow.TrueBlockName))
                        {
                            result.AddError($"Block '{flow.TrueBlockName}' referenced in Loop at line {flow.LineNumber} does not exist");
                        }
                        if (!string.IsNullOrEmpty(flow.FalseBlockName) &&
                            !allBlockNames.Contains(flow.FalseBlockName))
                        {
                            result.AddError($"Block '{flow.FalseBlockName}' referenced in Loop at line {flow.LineNumber} does not exist");
                        }
                    }
                    else if (flow.ControlType == FlowControlType.ToLoopCond)
                    {
                        if (!string.IsNullOrEmpty(flow.ToLoopCondReturnTo) &&
                            !allBlockNames.Contains(flow.ToLoopCondReturnTo))
                        {
                            result.AddError($"Block '{flow.ToLoopCondReturnTo}' referenced in ToLoopCond at line {flow.LineNumber} does not exist");
                        }
                    }
                }
            }
        }

        if (result.Errors.Count > 0)
            result.IsValid = false;

        return result;
    }
}
