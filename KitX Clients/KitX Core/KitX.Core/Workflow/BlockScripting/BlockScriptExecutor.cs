using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Block script executor - executes parsed block scripts using a call stack model
/// </summary>
public class BlockScriptExecutor : IBlockScriptExecutor
{
    private readonly BlockScopeManager _scopeManager;
    private readonly Stopwatch _stopwatch = new();

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
    /// Executes a block script
    /// </summary>
    public async Task<BlockScriptExecutionResult> ExecuteAsync(
        BlockScript script,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var output = new List<string>();
        var executedBlockCount = 0;

        try
        {
            // Clear previous execution state
            _scopeManager.ClearLocalScopes();
            BuiltInFunctions.ClearOutput();

            // Initialize global scope with ConstBlock and PubVarBlock
            _scopeManager.InitializeGlobalScope(script);

            // Import parameters into global scope
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    _scopeManager.SetVariable(param.Key, param.Value, global: true);
                }
            }

            // Build script globals with all required members
            var globals = new BlockScriptExecutionGlobals(_scopeManager, output);

            // 1. Execute ConstBlock and PubVarBlock (global variables)
            if (script.ConstBlock != null)
            {
                executedBlockCount++;
                await ExecuteBlockAsync(script.ConstBlock, globals, cancellationToken);
            }

            if (script.PubVarBlock != null)
            {
                executedBlockCount++;
                await ExecuteBlockAsync(script.PubVarBlock, globals, cancellationToken);
            }

            // 2. Execute MainBlock
            if (script.MainBlock == null)
            {
                return new BlockScriptExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = "No MainBlock found in script",
                    ExecutedBlockCount = executedBlockCount,
                    ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                    Output = output
                };
            }

            executedBlockCount++;
            await ExecuteBlockAsync(script.MainBlock, globals, cancellationToken);

            _stopwatch.Stop();

            return new BlockScriptExecutionResult
            {
                IsSuccess = true,
                ExecutedBlockCount = executedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = output
            };
        }
        catch (ReturnException returnEx)
        {
            _stopwatch.Stop();
            return new BlockScriptExecutionResult
            {
                IsSuccess = true,
                ReturnValue = returnEx.Value,
                ExecutedBlockCount = executedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = output
            };
        }
        catch (BranchException branchEx)
        {
            _stopwatch.Stop();
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Unresolved branch to '{branchEx.TargetBlock}' at end of block execution",
                ExecutedBlockCount = executedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = output
            };
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            Log.Error(ex, "[BlockScriptExecutor] Error executing block script");
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Execution error: {ex.Message}",
                ExecutedBlockCount = executedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = output
            };
        }
    }

    /// <summary>
    /// Executes a specific block by name
    /// </summary>
    public async Task<BlockScriptExecutionResult> ExecuteBlockAsync(
        BlockScript script,
        string blockName,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (!script.NamedBlocks.TryGetValue(blockName, out var block))
        {
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Block '{blockName}' not found"
            };
        }

        var globals = new BlockScriptExecutionGlobals(_scopeManager, new List<string>());
        await ExecuteBlockAsync(block, globals, cancellationToken);

        return new BlockScriptExecutionResult
        {
            IsSuccess = true,
            ExecutedBlockCount = 1
        };
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
                        if (!string.IsNullOrEmpty(flow.LoopBlockName) &&
                            !allBlockNames.Contains(flow.LoopBlockName))
                        {
                            result.AddError($"Block '{flow.LoopBlockName}' referenced in Loop at line {flow.LineNumber} does not exist");
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
    /// Executes a single block
    /// </summary>
    private async Task ExecuteBlockAsync(
        BlockDefinition block,
        BlockScriptExecutionGlobals globals,
        CancellationToken cancellationToken)
    {
        // Create local scope for this block
        var localScope = _scopeManager.CreateLocalScope(block.Name);

        // Declare local variables in scope
        foreach (var varDecl in block.Variables)
        {
            localScope.SetVariable(varDecl.Name, varDecl.DefaultValue);
        }

        // Execute statements in order
        for (int i = 0; i < block.Statements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var statement = block.Statements[i];
            await ExecuteStatementAsync(statement, globals, cancellationToken);
        }
    }

    /// <summary>
    /// Executes a single statement
    /// </summary>
    private async Task ExecuteStatementAsync(
        BlockStatement statement,
        BlockScriptExecutionGlobals globals,
        CancellationToken cancellationToken)
    {
        try
        {
            if (statement is ExpressionStatement exprStmt)
            {
                await ExecuteExpressionAsync(exprStmt.Expression, globals, cancellationToken);
            }
            else if (statement is VariableDeclarationStatement varStmt)
            {
                // Variable declaration is handled in ExecuteBlockAsync
                // For now, we re-evaluate to set the value
                if (!string.IsNullOrEmpty(varStmt.Declaration.InitialValueExpression))
                {
                    await EvaluateExpressionAsync(varStmt.Declaration.InitialValueExpression, globals, cancellationToken);
                }
            }
            else if (statement is FlowControlStatement flowStmt)
            {
                await ExecuteFlowControlAsync(flowStmt, globals, cancellationToken);
            }
        }
        catch (BranchException ex)
        {
            // Rethrow to be handled by execution engine
            throw;
        }
        catch (ReturnException ex)
        {
            // Rethrow to be handled by execution engine
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BlockScriptExecutor] Error executing statement at line {Line}", statement.LineNumber);
            throw;
        }
    }

    /// <summary>
    /// Executes an expression statement
    /// </summary>
    private async Task ExecuteExpressionAsync(
        string expression,
        BlockScriptExecutionGlobals globals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;

        // Check if it's an assignment
        if (expression.Contains('=') && !expression.Contains("==") && !expression.Contains("!="))
        {
            // This is an assignment - execute as script
            await EvaluateExpressionAsync(expression, globals, cancellationToken);
        }
        else
        {
            // This is a function call or other expression
            await EvaluateExpressionAsync(expression, globals, cancellationToken);
        }
    }

    /// <summary>
    /// Evaluates an expression and returns the result
    /// </summary>
    private async Task<object?> EvaluateExpressionAsync(
        string expression,
        BlockScriptExecutionGlobals globals,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build a complete statement for execution
            var code = WrapExpressionAsStatement(expression);

            var result = await CSharpScript.EvaluateAsync(
                code,
                ScriptOptions.Default
                    .WithReferences(typeof(BuiltInFunctions).Assembly)
                    .WithImports(
                        "System",
                        "KitX.Core.Workflow.BlockScripting"
                    ),
                globals: globals,
                cancellationToken: cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[BlockScriptExecutor] Error evaluating expression: {Expression}", expression);
            throw;
        }
    }

    /// <summary>
    /// Wraps an expression as a statement for evaluation
    /// </summary>
    private string WrapExpressionAsStatement(string expression)
    {
        expression = expression.Trim();
        if (expression.EndsWith(';'))
            return expression;
        return expression + ";";
    }

    /// <summary>
    /// Executes flow control statement
    /// </summary>
    private async Task ExecuteFlowControlAsync(
        FlowControlStatement statement,
        BlockScriptExecutionGlobals globals,
        CancellationToken cancellationToken)
    {
        switch (statement.ControlType)
        {
            case FlowControlType.Branch:
                // Evaluate condition and throw BranchException
                if (!string.IsNullOrEmpty(statement.ConditionExpression))
                {
                    var condition = await EvaluateExpressionAsync(statement.ConditionExpression, globals, cancellationToken);
                    if (condition is bool boolCondition)
                    {
                        var targetBlock = boolCondition ? statement.TrueBlockName : statement.FalseBlockName;
                        if (!string.IsNullOrEmpty(targetBlock))
                        {
                            throw new BranchException(targetBlock);
                        }
                    }
                }
                break;

            case FlowControlType.Loop:
                // Check condition and throw BranchException if true
                if (!string.IsNullOrEmpty(statement.ConditionExpression))
                {
                    var condition = await EvaluateExpressionAsync(statement.ConditionExpression, globals, cancellationToken);
                    if (condition is bool boolCondition && boolCondition)
                    {
                        if (!string.IsNullOrEmpty(statement.LoopBlockName))
                        {
                            throw new BranchException(statement.LoopBlockName);
                        }
                    }
                }
                break;

            case FlowControlType.Return:
                if (!string.IsNullOrEmpty(statement.ConditionExpression))
                {
                    var returnValue = await EvaluateExpressionAsync(statement.ConditionExpression, globals, cancellationToken);
                    throw new ReturnException(returnValue);
                }
                throw new ReturnException(null);

            case FlowControlType.Break:
                throw new BreakException();
        }
    }
}

/// <summary>
/// Script globals for block script execution - provides access to built-in functions and collects output
/// </summary>
public class BlockScriptExecutionGlobals
{
    private readonly BlockScopeManager _scopeManager;
    private readonly List<string> _output;

    /// <summary>
    /// Creates script globals
    /// </summary>
    public BlockScriptExecutionGlobals(BlockScopeManager scopeManager, List<string> output)
    {
        _scopeManager = scopeManager;
        _output = output;
    }

    /// <summary>
    /// Gets a variable value
    /// </summary>
    public object? GetVariable(string name)
    {
        return _scopeManager.ResolveVariable(name);
    }

    /// <summary>
    /// Sets a variable value
    /// </summary>
    public void SetVariable(string name, object? value)
    {
        _scopeManager.SetVariable(name, value, global: false);
    }

    /// <summary>
    /// Condition branch
    /// </summary>
    public void Branch(bool condition, string trueBlock, string falseBlock)
    {
        BuiltInFunctions.Branch(condition, trueBlock, falseBlock);
    }

    /// <summary>
    /// Loop while condition is true
    /// </summary>
    public void Loop(bool condition, string loopBlock)
    {
        BuiltInFunctions.Loop(condition, loopBlock);
    }

    /// <summary>
    /// Print a value
    /// </summary>
    public void Print(object? value)
    {
        var str = value?.ToString() ?? "null";
        _output.Add(str);
        WorkflowOutput.WriteLine(value);
    }

    /// <summary>
    /// Pause execution
    /// </summary>
    public void Pause(int milliseconds)
    {
        BuiltInFunctions.Pause(milliseconds);
    }
}
