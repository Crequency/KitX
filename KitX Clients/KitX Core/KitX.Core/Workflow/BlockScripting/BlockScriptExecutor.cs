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
    private ScriptState? _scriptState;
    private BlockScript? _currentScript;
    private List<string> _output = new();
    private BlockScriptExecutionGlobals? _globals;

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
        _output = new List<string>();
        var executedBlockCount = 0;

        try
        {
            // Store script reference for use in EvaluateExpressionAsync
            _currentScript = script;

            // Clear previous execution state
            _scopeManager.ClearLocalScopes();
            _output.Clear();  // Clear the executor's output list
            _scriptState = null;

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

            // Initialize CSharpScript session with all variable declarations
            await InitializeScriptSessionAsync(cancellationToken);

            // Execute ConstBlock and PubVarBlock (global variables) FIRST for initialization only
            // They are executed ONCE to establish variables in scope, then we proceed to MainBlock
            // NOTE: We do NOT follow their NextBlockName chains - those are only for sequential linking
            Log.Debug("[BlockScriptExecutor] === Starting Block Execution ===");
            Log.Debug("[BlockScriptExecutor] script.ConstBlock is {IsNull}, script.PubVarBlock is {IsNull2}, script.MainBlock is {IsNull3}",
                script.ConstBlock == null ? "null" : "NOT null",
                script.PubVarBlock == null ? "null" : "NOT null",
                script.MainBlock == null ? "null" : "NOT null");

            // Execute ConstBlock if exists (just for initialization)
            if (script.ConstBlock != null)
            {
                Log.Debug("[BlockScriptExecutor] >>> About to execute ConstBlock (initialization only)");
                executedBlockCount++;
                await ExecuteBlockAsync(script.ConstBlock, cancellationToken);
                Log.Debug("[BlockScriptExecutor] <<< ConstBlock initialization complete");
            }

            // Execute PubVarBlock if exists (just for initialization)
            if (script.PubVarBlock != null)
            {
                Log.Debug("[BlockScriptExecutor] >>> About to execute PubVarBlock (initialization only)");
                executedBlockCount++;
                await ExecuteBlockAsync(script.PubVarBlock, cancellationToken);
                Log.Debug("[BlockScriptExecutor] <<< PubVarBlock initialization complete");
            }

            // MainBlock is the entry point for execution
            if (script.MainBlock == null)
            {
                return new BlockScriptExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = "No MainBlock found in script",
                    ExecutedBlockCount = executedBlockCount,
                    ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                    Output = _output
                };
            }

            Log.Debug("[BlockScriptExecutor] >>> About to execute MainBlock as entry point");
            executedBlockCount++;
            var mainResult = await ExecuteBlockAsync(script.MainBlock, cancellationToken);
            Log.Debug("[BlockScriptExecutor] <<< MainBlock executed, mainResult.NextBlockName = {NextBlock}, ShouldContinue = {ShouldContinue}",
                mainResult.NextBlockName ?? "(null)", mainResult.ShouldContinue);

            // Follow the execution chain using while loop
            while (mainResult.ShouldContinue && !string.IsNullOrEmpty(mainResult.NextBlockName))
            {
                var nextBlockName = mainResult.NextBlockName;

                Log.Debug("[BlockScriptExecutor] Chain continuation: nextBlockName = {NextBlock}",
                    nextBlockName ?? "(null)");

                if (string.IsNullOrEmpty(nextBlockName))
                {
                    Log.Debug("[BlockScriptExecutor] No more blocks in chain, ending execution");
                    break;
                }

                var nextBlock = script.GetBlockByName(nextBlockName);
                Log.Debug("[BlockScriptExecutor] Fetched nextBlock: {BlockName} (type={BlockType})",
                    nextBlock?.Name ?? "null", nextBlock?.Type);
                if (nextBlock == null)
                {
                    Log.Warning("[BlockScriptExecutor] Block '{BlockName}' not found, ending chain", nextBlockName);
                    break;
                }

                var (chainResult, newBlockCount, _) = await ExecuteBlockChainAsync(nextBlock, cancellationToken, executedBlockCount);
                mainResult = chainResult;
                executedBlockCount = newBlockCount;

                Log.Debug("[BlockScriptExecutor] Chain iteration complete: mainResult.NextBlockName = {NextBlock}, ShouldContinue = {ShouldContinue}",
                    mainResult.NextBlockName ?? "(null)", mainResult.ShouldContinue);
            }

            _stopwatch.Stop();
            return new BlockScriptExecutionResult
            {
                IsSuccess = mainResult.ShouldContinue || mainResult.IsReturn,
                ReturnValue = mainResult.ReturnValue,
                ExecutedBlockCount = executedBlockCount,
                ExecutionTimeMs = _stopwatch.ElapsedMilliseconds,
                Output = _output
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
                Output = _output
            };
        }
    }

    /// <summary>
    /// Executes a chain of blocks following NextBlockName
    /// Note: This only follows NextBlockName when a block ends NATURALLY (no flow control).
    /// When flow control returns a NextBlockName (Loop/Branch), caller should handle the跳转.
    /// </summary>
    /// <param name="startBlock">Block to start execution from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="initialBlockCount">Initial block count for tracking</param>
    /// <returns>Tuple of (Result, ExecutedBlockCount, ChainEndedNaturally) where ChainEndedNaturally
    /// is true if chain ended naturally (no flow control jump), false if returned early due to
    /// flow control setting NextBlockName</returns>
    private async Task<(BlockExecutionResult Result, int ExecutedBlockCount, bool ChainEndedNaturally)> ExecuteBlockChainAsync(
        BlockDefinition startBlock,
        CancellationToken cancellationToken,
        int initialBlockCount)
    {
        var chainEndedNaturally = false;
        var currentBlock = startBlock;
        var executedBlockCount = initialBlockCount;

        while (currentBlock != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            executedBlockCount++;
            Log.Debug("[BlockScriptExecutor] Executing block '{BlockName}' (type={BlockType})",
                currentBlock.Name, currentBlock.Type);

            // Execute block and get result
            var result = await ExecuteBlockAsync(currentBlock, cancellationToken);

            // Check if execution should continue
            if (!result.ShouldContinue)
            {
                // Return/break - propagate result up to caller
                return (result, executedBlockCount, chainEndedNaturally);
            }

            // If NextBlockName is set from flow control (Loop/Branch), return to caller
            // DO NOT auto-jump - caller will handle the跳转
            if (!string.IsNullOrEmpty(result.NextBlockName))
            {
                Log.Debug("[BlockScriptExecutor] Block '{BlockName}' has flow control NextBlockName = '{NextBlock}', returning to caller",
                    currentBlock.Name, result.NextBlockName);
                // chainEndedNaturally is already false
                return (result, executedBlockCount, false);
            }

            // Block ended naturally - follow NextBlockName if exists
            if (string.IsNullOrEmpty(currentBlock.NextBlockName))
            {
                // End of execution chain
                Log.Debug("[BlockScriptExecutor] Block '{BlockName}' has no NextBlockName, ending chain",
                    currentBlock.Name);
                chainEndedNaturally = true;
                break;
            }

            currentBlock = _currentScript?.GetBlockByName(currentBlock.NextBlockName);
            if (currentBlock == null)
            {
                Log.Warning("[BlockScriptExecutor] Block '{BlockName}' references non-existent NextBlock '{NextBlock}'",
                    currentBlock?.Name, currentBlock?.NextBlockName);
                chainEndedNaturally = true;
                break;
            }
        }

        // Chain ended naturally
        chainEndedNaturally = true;
        return (BlockExecutionResult.ContinueTo(null), executedBlockCount, true);
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

        await ExecuteBlockAsync(block, cancellationToken);

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
                        // Loop(cond, trueBlock, falseBlock) syntax
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
                    else if (flow.ControlType == FlowControlType.LoopBodyEnd)
                    {
                        // LoopBodyEnd should reference a block that contains a Loop
                        if (!string.IsNullOrEmpty(flow.LoopBodyEndReturnTo) &&
                            !allBlockNames.Contains(flow.LoopBodyEndReturnTo))
                        {
                            result.AddError($"Block '{flow.LoopBodyEndReturnTo}' referenced in LoopBodyEnd at line {flow.LineNumber} does not exist");
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
    /// Executes a single block and returns execution result for state machine
    /// New design: NextBlock is set by Loop/Branch expressions, checked after each statement
    /// </summary>
    private async Task<BlockExecutionResult> ExecuteBlockAsync(
        BlockDefinition block,
        CancellationToken cancellationToken)
    {
        // Reset NextBlock at the start of each block
        _globals?.ResetNextBlock();

        // Execute statements in order
        for (int i = 0; i < block.Statements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var statement = block.Statements[i];
            await ExecuteStatementAsync(block, statement, cancellationToken);

            // Check if NextBlock was set by the statement (Loop/Branch/assignment)
            if (!string.IsNullOrEmpty(_globals?.NextBlock))
            {
                // NextBlock was set - use it for跳转
                var nextBlock = _globals.NextBlock;
                _globals.ResetNextBlock();  // Reset for next block
                return BlockExecutionResult.ContinueTo(nextBlock);
            }
            // Otherwise, continue to next statement
        }

        // Block ended naturally - continue to next block if exists (natural flow)
        return BlockExecutionResult.ContinueTo(block.NextBlockName);
    }

    /// <summary>
    /// Executes a single statement and returns execution result for state machine
    /// </summary>
    private async Task<BlockExecutionResult> ExecuteStatementAsync(
        BlockDefinition currentBlock,
        BlockStatement statement,
        CancellationToken cancellationToken)
    {
        try
        {
            if (statement is ExpressionStatement exprStmt)
            {
                await ExecuteExpressionAsync(exprStmt.Expression, cancellationToken);
                return BlockExecutionResult.ContinueTo(null);  // Continue to next statement
            }
            else if (statement is VariableDeclarationStatement varStmt)
            {
                // Check if DefaultValue is pre-computed (for ConstBlock variables)
                // If so, use it directly instead of evaluating through CSharpScript
                if (varStmt.Declaration.DefaultValue != null)
                {
                    _scopeManager.SetVariable(varStmt.Declaration.Name, varStmt.Declaration.DefaultValue);
                    Serilog.Log.Debug("[BlockScriptExecutor] Declared variable {Name} = {Value} (from DefaultValue)",
                        varStmt.Declaration.Name, varStmt.Declaration.DefaultValue);
                }
                else
                {
                    // For PubVarBlock or variables without pre-computed values,
                    // evaluate the initial value expression
                    var initialValue = varStmt.Declaration.InitialValueExpression;
                    if (!string.IsNullOrEmpty(initialValue))
                    {
                        try
                        {
                            // Evaluate just the initial value expression
                            var valueResult = await EvaluateExpressionAsync(initialValue, cancellationToken);
                            // Store the variable via SetVariable
                            _scopeManager.SetVariable(varStmt.Declaration.Name, valueResult);
                            Serilog.Log.Debug("[BlockScriptExecutor] Declared variable {Name} = {Value}",
                                varStmt.Declaration.Name, valueResult);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "[BlockScriptExecutor] Error evaluating initial value for {Name}",
                                varStmt.Declaration.Name);
                            // Still declare the variable with null/default
                            _scopeManager.SetVariable(varStmt.Declaration.Name, null);
                        }
                    }
                    else
                    {
                        // Declare without initial value
                        _scopeManager.SetVariable(varStmt.Declaration.Name, null);
                    }
                }
                return BlockExecutionResult.ContinueTo(null);  // Continue to next statement
            }
            else if (statement is FlowControlStatement flowStmt)
            {
                // ExecuteFlowControlAsync returns BlockExecutionResult for state machine
                return await ExecuteFlowControlAsync(currentBlock, flowStmt, cancellationToken);
            }
            return BlockExecutionResult.ContinueTo(null);  // Continue to next statement
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;

        // Execute the expression (assignment or function call)
        await EvaluateExpressionAsync(expression, cancellationToken);
    }

    /// <summary>
    /// Evaluates an expression and returns the result
    /// </summary>
    private async Task<object?> EvaluateExpressionAsync(
        string expression,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build a complete statement for execution
            var code = WrapExpressionAsStatement(expression);

            // Determine if we need to prepend helpers:
            // - If _scriptState is null (first evaluation): prepend helpers, they need to be defined
            // - If _scriptState != null (subsequent evaluations via ContinueWithAsync):
            //   DON'T prepend helpers, they were already defined in the initialization script
            //   and ContinueWithAsync shares the same script state
            var fullCode = _scriptState == null
                ? BuildCodeWithHelpers(code)
                : code;

            // Log the full code being evaluated (truncated if too long)
            var codePreview = fullCode.Length > 500 ? fullCode.Substring(0, 500) + "..." : fullCode;
            Log.Debug("[BlockScriptExecutor] Evaluating expression. Code:\n{Code}", codePreview);

            object? result;
            if (_scriptState != null)
            {
                // Use ContinueWithAsync to evaluate in the context of the compiled script
                // Variables and helpers declared in previous evaluations are preserved
                _scriptState = await _scriptState.ContinueWithAsync(
                    fullCode,
                    ScriptOptions.Default
                        .WithReferences(typeof(BlockScriptExecutionGlobals).Assembly)
                        .WithImports(
                            "System",
                            "KitX.Core.Workflow.BlockScripting",
                            "KitX.Core.Workflow"
                        ),
                    cancellationToken: cancellationToken);
                result = _scriptState.ReturnValue;
            }
            else
            {
                // First evaluation: create initial script state using RunAsync to get ScriptState
                _globals = new BlockScriptExecutionGlobals(_scopeManager, _output);
                var globals = _globals;
                _scriptState = await CSharpScript.RunAsync(
                    fullCode,
                    ScriptOptions.Default
                        .WithReferences(typeof(BlockScriptExecutionGlobals).Assembly)
                        .WithImports(
                            "System",
                            "KitX.Core.Workflow.BlockScripting",
                            "KitX.Core.Workflow"
                        ),
                    globals: globals,
                    cancellationToken: cancellationToken);
                result = _scriptState.ReturnValue;
            }

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
    /// Builds helper function code from a list of helper functions
    /// </summary>
    private string BuildHelperFunctionsCode(List<HelperFunction>? helperFunctions)
    {
        if (helperFunctions == null || helperFunctions.Count == 0)
            return string.Empty;

        var code = new System.Text.StringBuilder();

        foreach (var func in helperFunctions)
        {
            // Generate function signature (without static modifier for CSharpScript)
            code.Append(func.ReturnType);
            code.Append(" ");
            code.Append(func.Name);
            code.Append("(");

            // Add parameters
            for (int i = 0; i < func.Parameters.Count; i++)
            {
                if (i > 0) code.Append(", ");
                code.Append(func.Parameters[i].Type);
                code.Append(" ");
                code.Append(func.Parameters[i].Name);
            }

            code.AppendLine(")");
            code.AppendLine("{");

            // Add function body
            if (!string.IsNullOrWhiteSpace(func.Code))
            {
                foreach (var line in func.Code.Split('\n'))
                {
                    code.AppendLine("    " + line);
                }
            }

            code.AppendLine("}");
            code.AppendLine();
        }

        return code.ToString();
    }

    /// <summary>
    /// Builds complete code by prepending helper function definitions
    /// </summary>
    private string BuildCodeWithHelpers(string code)
    {
        var helperCode = BuildHelperFunctionsCode(_currentScript?.HelperFunctions);
        if (string.IsNullOrEmpty(helperCode))
            return code;

        return helperCode + code;
    }

    /// <summary>
    /// Builds a complete initialization script that declares all variables
    /// This should be run once to properly initialize the CSharpScript session
    /// </summary>
    private string BuildInitializationScript(BlockScript script)
    {
        var initCode = new System.Text.StringBuilder();

        // Add helper functions first
        initCode.Append(BuildHelperFunctionsCode(script.HelperFunctions));

        // Add variable declarations from ConstBlock and PubVarBlock
        // IMPORTANT: All variables must be declared in CSharpScript, even without initial values
        //
        // Key distinction:
        // - With DefaultValue: use "var name = value;" (C# can infer the type from the literal value)
        // - Without DefaultValue: use explicit type "type name;" (C# requires explicit type for uninitialized vars)
        //   Note: "var name;" is INVALID in C# because implicitly-typed variables must be initialized
        if (script.ConstBlock != null)
        {
            foreach (var variable in script.ConstBlock.Variables)
            {
                if (variable.DefaultValue != null)
                {
                    initCode.AppendLine($"var {variable.Name} = {variable.DefaultValue};");
                }
                else
                {
                    // Use explicit type since there's no initializer
                    initCode.AppendLine($"{variable.Type} {variable.Name};");
                }
            }
        }

        if (script.PubVarBlock != null)
        {
            foreach (var variable in script.PubVarBlock.Variables)
            {
                if (variable.DefaultValue != null)
                {
                    initCode.AppendLine($"var {variable.Name} = {variable.DefaultValue};");
                }
                else
                {
                    // Use explicit type since there's no initializer
                    initCode.AppendLine($"{variable.Type} {variable.Name};");
                }
            }
        }

        return initCode.ToString();
    }

    /// <summary>
    /// Initializes the CSharpScript session with all variable declarations
    /// </summary>
    private async Task InitializeScriptSessionAsync(CancellationToken cancellationToken)
    {
        if (_currentScript == null) return;

        var initCode = BuildInitializationScript(_currentScript);
        if (string.IsNullOrWhiteSpace(initCode))
        {
            // No initialization needed, just do a simple first evaluation to establish session
            _globals = new BlockScriptExecutionGlobals(_scopeManager, _output);
            _scriptState = await CSharpScript.RunAsync(
                "0",  // Simple expression to establish session
                ScriptOptions.Default
                    .WithReferences(typeof(BlockScriptExecutionGlobals).Assembly)
                    .WithImports("System", "KitX.Core.Workflow.BlockScripting", "KitX.Core.Workflow"),
                globals: _globals,
                cancellationToken: cancellationToken);
            return;
        }

        Log.Debug("[BlockScriptExecutor] Running initialization script:\n{InitCode}", initCode);

        try
        {
            _globals = new BlockScriptExecutionGlobals(_scopeManager, _output);
            _scriptState = await CSharpScript.RunAsync(
                initCode,
                ScriptOptions.Default
                    .WithReferences(typeof(BlockScriptExecutionGlobals).Assembly)
                    .WithImports("System", "KitX.Core.Workflow.BlockScripting", "KitX.Core.Workflow"),
                globals: _globals,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[BlockScriptExecutor] Initialization script failed, will try without it");
            // Fall back to simple session establishment
            _globals = new BlockScriptExecutionGlobals(_scopeManager, _output);
            _scriptState = await CSharpScript.RunAsync(
                "0",
                ScriptOptions.Default
                    .WithReferences(typeof(BlockScriptExecutionGlobals).Assembly)
                    .WithImports("System", "KitX.Core.Workflow.BlockScripting", "KitX.Core.Workflow"),
                globals: _globals,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Executes flow control statement and returns result for state machine
    /// </summary>
    private async Task<BlockExecutionResult> ExecuteFlowControlAsync(
        BlockDefinition currentBlock,
        FlowControlStatement statement,
        CancellationToken cancellationToken)
    {
        switch (statement.ControlType)
        {
            case FlowControlType.Return:
                // Return statement: evaluate the return value expression
                if (!string.IsNullOrEmpty(statement.ConditionExpression))
                {
                    var returnValue = await EvaluateExpressionAsync(statement.ConditionExpression, cancellationToken);
                    return BlockExecutionResult.Return(returnValue);
                }
                return BlockExecutionResult.Return(null);

            case FlowControlType.Break:
                // Break exits the current loop - for now, just end the script
                return BlockExecutionResult.Return(null);

            default:
                // Branch, Loop, LoopBodyEnd, etc.
                // Execute the full statement (e.g., "NextBlock = Loop(...)" or "NextBlock = Branch(...)")
                // The built-in function will set _globals.NextBlock
                await EvaluateExpressionAsync(statement.SourceCode, cancellationToken);

                // Check if NextBlock was set by the built-in function
                var nextBlock = _globals?.NextBlock;
                if (!string.IsNullOrEmpty(nextBlock))
                {
                    return BlockExecutionResult.ContinueTo(nextBlock);
                }

                // Fallback: if NextBlock wasn't set, use the stored target based on control type
                return statement.ControlType switch
                {
                    FlowControlType.Loop => BlockExecutionResult.ContinueTo(statement.TrueBlockName),
                    FlowControlType.Branch => BlockExecutionResult.ContinueTo(statement.TrueBlockName),
                    _ => BlockExecutionResult.ContinueTo(null)
                };
        }
    }
}
