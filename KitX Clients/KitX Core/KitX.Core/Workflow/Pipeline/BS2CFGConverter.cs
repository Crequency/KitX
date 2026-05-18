using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Functions;
using KitX.Core.Workflow.CFG;

namespace KitX.Core.Workflow.Pipeline;

/// <summary>
/// Phase 2: Takes a parsed BlockScript AST and produces a ControlFlowGraph
/// where all nested function calls have been expanded into PubVar assignments.
/// Also duplicates Loop condition evaluations before ToLoopCond statements.
/// </summary>
public class BS2CFGConverter
{
    private readonly List<HelperFunction> _helperFunctions;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public BS2CFGConverter(List<HelperFunction> helperFunctions, BuiltinFunctionRegistry? functionRegistry = null)
    {
        _helperFunctions = helperFunctions;
        _functionRegistry = functionRegistry;
    }

    public ControlFlowGraph Format(BlockScript script, PipelineContext context)
    {
        var result = new ControlFlowGraph();

        // Initialize counter: find max existing PubVar counter to avoid conflicts
        context.NextPubVarCounter = 1;
        foreach (var name in context.PubVarNames)
        {
            var existingCounter = ExprUtils.TryExtractPubVarCounter(name);
            if (existingCounter.HasValue)
                context.NextPubVarCounter = Math.Max(context.NextPubVarCounter, existingCounter.Value + 1);
        }

        // Format MainBlock
        if (script.MainBlock != null)
        {
            var mainBlock = FormatBlock(script.MainBlock, context);
            result.Blocks.Add(mainBlock);
            result.MainBlockName = mainBlock.Name;
        }

        // Format NamedBlocks
        foreach (var kvp in script.NamedBlocks)
        {
            if (!result.Blocks.Any(b => b.Name == kvp.Value.Name))
            {
                result.Blocks.Add(FormatBlock(kvp.Value, context));
            }
        }

        Log.Debug("[BS2CFGConverter] Done: {BlockCount} blocks, {StmtCount} statements",
            result.Blocks.Count, result.Blocks.Sum(b => b.Statements.Count));

        return result;
    }

    // ──────────────────────────────────────────────
    // Block-level formatting
    // ──────────────────────────────────────────────

    private CFGBlock FormatBlock(BlockDefinition blockDef, PipelineContext context)
    {
        var result = new CFGBlock { Name = blockDef.Name, NextBlockName = blockDef.NextBlockName };
        foreach (var stmt in blockDef.Statements)
        {
            var formatted = FormatStatement(stmt, blockDef.Name, context);
            result.Statements.AddRange(formatted);
        }
        return result;
    }

    /// <summary>Returns a list of FormattedStatements (may be multiple when expansion occurs).</summary>
    private List<CFGStatement> FormatStatement(BlockStatement stmt, string blockName, PipelineContext context)
    {
        switch (stmt)
        {
            case FlowControlStatement flowCtrl:
                return FormatFlowControl(flowCtrl, blockName, context);
            case ExpressionStatement expr:
                return FormatExpressionStatement(expr, blockName, context);
            default:
                return new();
        }
    }

    // ──────────────────────────────────────────────
    // Flow control formatting
    // ──────────────────────────────────────────────

    private List<CFGStatement> FormatFlowControl(FlowControlStatement flowCtrl, string blockName, PipelineContext context)
    {
        var result = new List<CFGStatement>();

        // Determine the function name from the source code or control type
        var functionName = GetFunctionNameFromFlowControl(flowCtrl);
        var kind = _functionRegistry?.Get(functionName)?.StatementKind ?? MapControlTypeToKind(flowCtrl.ControlType);

        // Expand condition for Branch/Loop
        var hasCondition = !string.IsNullOrEmpty(flowCtrl.ConditionExpression);
        string? condPubVar = null;

        if (hasCondition)
        {
            var (condStmts, pubVar) = ExpandCondition(flowCtrl.ConditionExpression, blockName, context);
            result.AddRange(condStmts);
            condPubVar = pubVar;
        }

        var stmt = new CFGStatement
        {
            StatementId = !string.IsNullOrEmpty(flowCtrl.StatementId) ? flowCtrl.StatementId : Guid.NewGuid().ToString(),
            BlockName = blockName,
            Kind = kind,
            FunctionName = functionName,
            ConditionPubVar = condPubVar,
            ConditionExpression = flowCtrl.ConditionExpression,
            TrueBlockName = flowCtrl.TrueBlockName,
            FalseBlockName = flowCtrl.FalseBlockName,
            ToLoopCondReturnTo = flowCtrl.ToLoopCondReturnTo,
            OriginalExpression = flowCtrl.SourceCode,
            SourceLine = flowCtrl.LineNumber
        };
        result.Add(stmt);

        // Store loop condition for duplication before ToLoopCond
        if (kind == CFGStatementKind.Loop && !string.IsNullOrEmpty(condPubVar))
        {
            context.LoopConditions[blockName] = new ConditionInfo
            {
                ConditionPubVar = condPubVar,
                RawExpression = flowCtrl.ConditionExpression,
                ExpansionStatements = result.Where(s => s != stmt).ToList()
            };
        }

        return result;
    }

    private static string GetFunctionNameFromFlowControl(FlowControlStatement flowCtrl)
    {
        var src = flowCtrl.SourceCode;
        // Parse function name from source like "NextBlock = Branch(...)", "Break()", etc.
        var parsed = ExprUtils.ParseStatement(src);
        if (parsed?.rightExpr is InvocationExpressionSyntax invoke)
            return ExprUtils.GetMethodName(invoke);
        if (parsed?.rightExpr is IdentifierNameSyntax id)
            return id.Identifier.Text;
        // Fallback: derive from ControlType
        return MapControlTypeToFunctionName(flowCtrl.ControlType);
    }

    private static string MapControlTypeToFunctionName(FlowControlType type) => type switch
    {
        FlowControlType.Branch => Branch,
        FlowControlType.Loop => Loop,
        FlowControlType.ToLoopCond => ToLoopCond,
        FlowControlType.Break => Break,
        _ => string.Empty
    };

    private static CFGStatementKind MapControlTypeToKind(FlowControlType type) => type switch
    {
        FlowControlType.Branch => CFGStatementKind.Branch,
        FlowControlType.Loop => CFGStatementKind.Loop,
        FlowControlType.ToLoopCond => CFGStatementKind.ToLoopCond,
        FlowControlType.Break => CFGStatementKind.Break,
        _ => CFGStatementKind.Unknown
    };

    // ──────────────────────────────────────────────
    // Expression statement formatting
    // ──────────────────────────────────────────────

    private List<CFGStatement> FormatExpressionStatement(ExpressionStatement exprStmt, string blockName, PipelineContext context)
    {
        var result = new List<CFGStatement>();
        var expression = exprStmt.Expression;

        // Try to parse the expression
        var parsed = ExprUtils.ParseStatement(expression);
        if (parsed == null) return result;

        var (rightExpr, assignedVar) = parsed.Value;

        if (rightExpr == null) return result;

        // Handle "NextBlock = ..." assignments (should be handled as FlowControl by parser)
        if (assignedVar != null && assignedVar == "NextBlock")
            return result;

        // If RHS is a function invocation, process it
        if (rightExpr is InvocationExpressionSyntax invoke)
        {
            var funcName = ExprUtils.GetMethodName(invoke);
            if (string.IsNullOrEmpty(funcName)) return result;

            // Skip flow control functions (handled by FlowControlStatement)
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } fcDef && fcDef.IsFlowControl)
                return result;

            var fullFuncName = ExprUtils.GetFullMethodName(invoke);
            result.AddRange(FormatInvocation(invoke, funcName, blockName, context, assignedVar, fullFuncName,
                statementId: exprStmt.StatementId));
            return result;
        }

        // If RHS is just an identifier (variable reference), skip
        return result;
    }

    /// <summary>
    /// Formats a function invocation, expanding nested calls in arguments.
    /// </summary>
    private List<CFGStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string funcName, string blockName,
        PipelineContext context, string? assignedVar, string? fullFuncName = null,
        string? statementId = null)
    {
        var result = new List<CFGStatement>();

        // Expand nested calls in arguments first
        var (expansionStmts, currentArgExprs) = ExpandArguments(invoke, blockName, context);
        result.AddRange(expansionStmts);

        // Determine statement kind and extract info
        CFGStatementKind kind;
        string? pubVarTarget = null;
        string? setVarName = null;
        string? getVarName = null;

        switch (funcName)
        {
            default:
                // All registered functions (Set/Get/Print/Pause/etc.) go through registry
                if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } funcDef)
                {
                    kind = funcDef.StatementKind;
                    var (sn, gn, pv) = funcDef.ExtractStatementFields(invoke, currentArgExprs, assignedVar, context);
                    setVarName = sn;
                    getVarName = gn;
                    pubVarTarget = pv;
                    // If ExtractStatementFields didn't set PubVarTarget but we have an assignment context
                    // and the function produces a value (non-void return), preserve the assignment
                    if (pubVarTarget == null && !string.IsNullOrEmpty(assignedVar) && assignedVar != "_")
                    {
                        pubVarTarget = assignedVar;
                        if (!context.PubVarNames.Contains(assignedVar))
                            context.PubVarNames.Add(assignedVar);
                    }
                }
                else
                {
                    // Helper or regular function call
                    if (!string.IsNullOrEmpty(assignedVar) && assignedVar != "_")
                    {
                        kind = CFGStatementKind.Assignment;
                        pubVarTarget = assignedVar;
                        // Ensure the assigned variable is tracked as a PubVar
                        if (!context.PubVarNames.Contains(assignedVar))
                            context.PubVarNames.Add(assignedVar);
                    }
                    else
                    {
                        kind = CFGStatementKind.Expression;
                    }
                }
                break;
        }

        var fingerprint = kind is CFGStatementKind.Assignment or CFGStatementKind.Expression
            ? ExprUtils.ComputeFingerprint(funcName, currentArgExprs)
            : null;

        result.Add(new CFGStatement
        {
            StatementId = !string.IsNullOrEmpty(statementId) ? statementId
                : Guid.NewGuid().ToString(),
            BlockName = blockName,
            Kind = kind,
            FunctionName = funcName,
            FullFunctionName = fullFuncName,
            PubVarTarget = pubVarTarget,
            SetVarName = setVarName,
            GetVarName = getVarName,
            Arguments = currentArgExprs,
            OriginalExpression = invoke.ToString(),
            SourceLine = 0,
            Fingerprint = fingerprint
        });

        return result;
    }

    // ──────────────────────────────────────────────
    // Argument expansion (nested call extraction)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Expands nested function calls in arguments.
    /// Returns (expansionStatements, currentArgStrings).
    /// </summary>
    private (List<CFGStatement> stmts, List<string> argExprs) ExpandArguments(
        InvocationExpressionSyntax invoke, string blockName, PipelineContext context)
    {
        var stmts = new List<CFGStatement>();
        var argExprs = new List<string>();

        foreach (var arg in invoke.ArgumentList.Arguments)
        {
            var (expanded, finalExpr) = ExpandExpression(arg.Expression, blockName, context);
            stmts.AddRange(expanded);
            argExprs.Add(finalExpr);
        }

        return (stmts, argExprs);
    }

    /// <summary>
    /// Recursively expands nested calls within a single expression.
    /// Returns (expansionStatements, finalExpressionString).
    /// </summary>
    private (List<CFGStatement> stmts, string finalExpr) ExpandExpression(
        ExpressionSyntax expr, string blockName, PipelineContext context)
    {
        // Literal → return as-is
        if (expr is LiteralExpressionSyntax)
            return (new(), expr.ToString());

        // Simple identifier → return as-is
        if (expr is IdentifierNameSyntax)
            return (new(), expr.ToString());

        // Invocation → may need expansion
        if (expr is InvocationExpressionSyntax invoke)
        {
            var funcName = ExprUtils.GetMethodName(invoke);
            var fullFuncName = ExprUtils.GetFullMethodName(invoke);

            // Check registry for function classification
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } inlineDef)
            {
                // Non-extractable functions (Set/Print/Pause) stay inline
                if (inlineDef.IsNonExtractable)
                    return (new(), invoke.ToString());

                // Flow control functions → should not appear as arguments
                if (inlineDef.IsFlowControl)
                    return (new(), invoke.ToString());
            }

            // Check registry for functions that need extraction (e.g., Get)
            // Registered functions use their FormatInvocation to create proper statements.
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } regFuncDef)
            {
                var formatted = regFuncDef.FormatInvocation(invoke, blockName, context, null);
                var lastStmt = formatted.LastOrDefault();
                if (lastStmt?.PubVarTarget != null)
                    return (formatted, lastStmt.PubVarTarget);
            }

            // This is a helper/regular function call that needs extraction
            // First, recursively expand ITS arguments
            var allStmts = new List<CFGStatement>();
            var currentArgs = new List<string>();
            foreach (var arg in invoke.ArgumentList.Arguments)
            {
                var (expanded, finalExpr) = ExpandExpression(arg.Expression, blockName, context);
                allStmts.AddRange(expanded);
                currentArgs.Add(finalExpr);
            }

            // Generate PubVar for this call
            var pubVarName = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarName))
                context.PubVarNames.Add(pubVarName);

            var fingerprint = ExprUtils.ComputeFingerprint(funcName, currentArgs);

            allStmts.Add(new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Assignment,
                PubVarTarget = pubVarName,
                FunctionName = funcName,
                FullFunctionName = fullFuncName,
                Arguments = currentArgs,
                OriginalExpression = $"{pubVarName} = {invoke}",
                Fingerprint = fingerprint
            });

            return (allStmts, pubVarName);
        }

        // Parenthesized expression
        if (expr is ParenthesizedExpressionSyntax paren)
            return ExpandExpression(paren.Expression, blockName, context);

        // Default: return as-is
        return (new(), expr.ToString());
    }

    // ──────────────────────────────────────────────
    // Condition expansion (for Branch/Loop)
    // ──────────────────────────────────────────────

    private (List<CFGStatement> stmts, string? pubVar) ExpandCondition(
        string conditionExpression, string blockName, PipelineContext context)
    {
        var result = new List<CFGStatement>();
        if (string.IsNullOrWhiteSpace(conditionExpression))
            return (result, null);

        var trimmed = conditionExpression.Trim();

        // Already a PubVar reference — skip expansion per BlockScript spec §4.3:
        // pre-expanded format (where nested calls have already been flattened into PubVar assignments)
        // is a valid input format and should not be re-expanded.
        if (context.PubVarNames.Contains(trimmed))
            return (result, trimmed);

        // ConstBlock variable or VariableNode
        if (context.ConstNodes.ContainsKey(trimmed) || context.VariableNodes.ContainsKey(trimmed))
            return (result, null);

        // Parse the expression
        var expr = ExprUtils.ParseExpression(trimmed);
        if (expr == null)
            return (result, null);

        // If it's a simple identifier, no expansion needed
        if (expr is IdentifierNameSyntax)
            return (result, null);

        // If it's an invocation, expand it
        if (expr is InvocationExpressionSyntax invoke)
        {
            var funcName = ExprUtils.GetMethodName(invoke);
            if (string.IsNullOrEmpty(funcName)) return (result, null);

            var fullFuncName = ExprUtils.GetFullMethodName(invoke);
            var formatted = FormatInvocation(invoke, funcName, blockName, context, null, fullFuncName);

            // The last statement should be the main call
            // If it already has a PubVarTarget, use it
            var lastStmt = formatted.LastOrDefault();
            if (lastStmt?.PubVarTarget != null)
            {
                result.AddRange(formatted);
                return (result, lastStmt.PubVarTarget);
            }

            // If no PubVar was assigned, generate one
            var pubVarName = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarName))
                context.PubVarNames.Add(pubVarName);
            if (lastStmt != null) lastStmt.PubVarTarget = pubVarName;
            result.AddRange(formatted);
            return (result, pubVarName);
        }

        return (result, null);
    }
}
