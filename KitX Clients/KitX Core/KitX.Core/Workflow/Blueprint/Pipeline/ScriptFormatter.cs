using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Functions;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Phase 2: Takes a parsed BlockScript AST and produces a FormattedBlockScript
/// where all nested function calls have been expanded into PubVar assignments.
/// Also duplicates Loop condition evaluations before LoopBodyEnd statements.
/// </summary>
public class ScriptFormatter
{
    private readonly List<HelperFunction> _helperFunctions;

    public ScriptFormatter(List<HelperFunction> helperFunctions)
    {
        _helperFunctions = helperFunctions;
    }

    public FormattedBlockScript Format(BlockScript script, PipelineContext context)
    {
        var result = new FormattedBlockScript();

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

        // Format LoopBlocks
        foreach (var kvp in script.LoopBlocks)
        {
            if (!result.Blocks.Any(b => b.Name == kvp.Value.Name))
            {
                result.Blocks.Add(FormatBlock(kvp.Value, context));
            }
        }

        // Insert Loop condition duplications before LoopBodyEnd
        InsertLoopConditionDuplications(result, context);

        Log.Debug("[ScriptFormatter] Done: {BlockCount} blocks, {StmtCount} statements",
            result.Blocks.Count, result.Blocks.Sum(b => b.Statements.Count));

        return result;
    }

    // ──────────────────────────────────────────────
    // Block-level formatting
    // ──────────────────────────────────────────────

    private FormattedBlock FormatBlock(BlockDefinition blockDef, PipelineContext context)
    {
        var result = new FormattedBlock { Name = blockDef.Name, NextBlockName = blockDef.NextBlockName };
        foreach (var stmt in blockDef.Statements)
        {
            var formatted = FormatStatement(stmt, blockDef.Name, context);
            result.Statements.AddRange(formatted);
        }
        return result;
    }

    /// <summary>Returns a list of FormattedStatements (may be multiple when expansion occurs).</summary>
    private List<FormattedStatement> FormatStatement(BlockStatement stmt, string blockName, PipelineContext context)
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

    private List<FormattedStatement> FormatFlowControl(FlowControlStatement flowCtrl, string blockName, PipelineContext context)
    {
        var result = new List<FormattedStatement>();

        switch (flowCtrl.ControlType)
        {
            case FlowControlType.Branch:
                {
                    var (condStmts, condPubVar) = ExpandCondition(flowCtrl.ConditionExpression, blockName, context);
                    result.AddRange(condStmts);
                    result.Add(new FormattedStatement
                    {
                        BlockName = blockName,
                        Kind = FormattedStatementKind.Branch,
                        ConditionPubVar = condPubVar,
                        ConditionExpression = flowCtrl.ConditionExpression,
                        TrueBlockName = flowCtrl.TrueBlockName,
                        FalseBlockName = flowCtrl.FalseBlockName,
                        OriginalExpression = flowCtrl.SourceCode,
                        SourceLine = flowCtrl.LineNumber
                    });
                }
                break;

            case FlowControlType.Loop:
                {
                    var (condStmts, condPubVar) = ExpandCondition(flowCtrl.ConditionExpression, blockName, context);
                    result.AddRange(condStmts);

                    var loopStmt = new FormattedStatement
                    {
                        BlockName = blockName,
                        Kind = FormattedStatementKind.Loop,
                        ConditionPubVar = condPubVar,
                        ConditionExpression = flowCtrl.ConditionExpression,
                        TrueBlockName = flowCtrl.TrueBlockName,
                        FalseBlockName = flowCtrl.FalseBlockName,
                        OriginalExpression = flowCtrl.SourceCode,
                        SourceLine = flowCtrl.LineNumber
                    };
                    result.Add(loopStmt);

                    // Store condition for duplication before LoopBodyEnd
                    if (!string.IsNullOrEmpty(condPubVar))
                    {
                        context.LoopConditions[blockName] = new ConditionInfo
                        {
                            ConditionPubVar = condPubVar,
                            RawExpression = flowCtrl.ConditionExpression,
                            ExpansionStatements = condStmts.ToList()
                        };
                    }
                }
                break;

            case FlowControlType.LoopBodyEnd:
                result.Add(new FormattedStatement
                {
                    BlockName = blockName,
                    Kind = FormattedStatementKind.LoopBodyEnd,
                    LoopBodyEndReturnTo = flowCtrl.LoopBodyEndReturnTo,
                    OriginalExpression = flowCtrl.SourceCode,
                    SourceLine = flowCtrl.LineNumber
                });
                break;

            case FlowControlType.Break:
                result.Add(new FormattedStatement
                {
                    BlockName = blockName,
                    Kind = FormattedStatementKind.Break,
                    OriginalExpression = flowCtrl.SourceCode,
                    SourceLine = flowCtrl.LineNumber
                });
                break;
        }
        return result;
    }

    // ──────────────────────────────────────────────
    // Expression statement formatting
    // ──────────────────────────────────────────────

    private List<FormattedStatement> FormatExpressionStatement(ExpressionStatement exprStmt, string blockName, PipelineContext context)
    {
        var result = new List<FormattedStatement>();
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
            if (ExprUtils.FlowControlFunctions.Contains(funcName)) return result;

            var fullFuncName = ExprUtils.GetFullMethodName(invoke);
            result.AddRange(FormatInvocation(invoke, funcName, blockName, context, assignedVar, fullFuncName));
            return result;
        }

        // If RHS is just an identifier (variable reference), skip
        return result;
    }

    /// <summary>
    /// Formats a function invocation, expanding nested calls in arguments.
    /// </summary>
    private List<FormattedStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string funcName, string blockName,
        PipelineContext context, string? assignedVar, string? fullFuncName = null)
    {
        var result = new List<FormattedStatement>();

        // Expand nested calls in arguments first
        var (expansionStmts, currentArgExprs) = ExpandArguments(invoke, blockName, context);
        result.AddRange(expansionStmts);

        // Determine statement kind and extract info
        FormattedStatementKind kind;
        string? pubVarTarget = null;
        string? setVarName = null;
        string? getVarName = null;

        switch (funcName)
        {
            case Print:
                kind = FormattedStatementKind.Print;
                break;
            case Pause:
                kind = FormattedStatementKind.Pause;
                break;
            case Set:
                kind = FormattedStatementKind.Set;
                // First arg is varName (string literal)
                if (currentArgExprs.Count > 0)
                {
                    var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
                    var varNameLiteral = ExprUtils.GetStringLiteralValue(firstArgExpr);
                    setVarName = varNameLiteral ?? currentArgExprs[0];
                    currentArgExprs.RemoveAt(0);
                }
                break;
            case Get:
                kind = FormattedStatementKind.Assignment;
                if (currentArgExprs.Count > 0)
                {
                    var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
                    getVarName = ExprUtils.GetStringLiteralValue(firstArgExpr) ?? currentArgExprs[0];
                }
                // Auto-generate PubVar if not already assigned per §4.3 (pre-expanded format may already assign PubVars)
                if (string.IsNullOrEmpty(assignedVar) || !context.PubVarNames.Contains(assignedVar))
                {
                    pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
                    if (!context.PubVarNames.Contains(pubVarTarget))
                        context.PubVarNames.Add(pubVarTarget);
                }
                else
                {
                    pubVarTarget = assignedVar;
                }
                break;
            default:
                // Helper or regular function call
                if (!string.IsNullOrEmpty(assignedVar) && assignedVar != "_")
                {
                    kind = FormattedStatementKind.Assignment;
                    // PubVarTarget is set only if the assigned variable is a declared PubVar
                    pubVarTarget = context.PubVarNames.Contains(assignedVar) ? assignedVar : null;
                }
                else
                {
                    kind = FormattedStatementKind.Expression;
                }
                break;
        }

        var fingerprint = kind is FormattedStatementKind.Assignment or FormattedStatementKind.Expression
            ? ExprUtils.ComputeFingerprint(funcName, currentArgExprs)
            : null;

        result.Add(new FormattedStatement
        {
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
    private (List<FormattedStatement> stmts, List<string> argExprs) ExpandArguments(
        InvocationExpressionSyntax invoke, string blockName, PipelineContext context)
    {
        var stmts = new List<FormattedStatement>();
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
    private (List<FormattedStatement> stmts, string finalExpr) ExpandExpression(
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

            // Built-in functions (Get/Set/Print/Pause) stay inline
            if (ExprUtils.NonExtractableFunctions.Contains(funcName))
                return (new(), invoke.ToString());

            // Flow control functions → should not appear as arguments
            if (ExprUtils.FlowControlFunctions.Contains(funcName))
                return (new(), invoke.ToString());

            // Get(varName) → extract as a proper Get statement with GetVarName set.
            // This is critical for PubVar reuse detection in NodeBuilder:
            // cloned Get statements (from Loop condition duplication) must have
            // GetVarName set so the reuse check (PubVarTarget + GetVarName) can
            // match them to the original Get node instead of creating duplicates.
            if (funcName == Get)
            {
                var varName = invoke.ArgumentList.Arguments.Count > 0
                    ? ExprUtils.GetStringLiteralValue(invoke.ArgumentList.Arguments[0].Expression)
                      ?? invoke.ArgumentList.Arguments[0].Expression.ToString().Trim('"')
                    : "";

                var getPubVar = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
                if (!context.PubVarNames.Contains(getPubVar))
                    context.PubVarNames.Add(getPubVar);

                return (new List<FormattedStatement>
                {
                    new()
                    {
                        BlockName = blockName,
                        Kind = FormattedStatementKind.Assignment,
                        FunctionName = Get,
                        PubVarTarget = getPubVar,
                        GetVarName = varName,
                        Arguments = new List<string> { $"\"{varName}\"" },
                        OriginalExpression = $"{getPubVar} = Get(\"{varName}\")",
                    }
                }, getPubVar);
            }

            // This is a helper/regular function call that needs extraction
            // First, recursively expand ITS arguments
            var allStmts = new List<FormattedStatement>();
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

            allStmts.Add(new FormattedStatement
            {
                BlockName = blockName,
                Kind = FormattedStatementKind.Assignment,
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

    private (List<FormattedStatement> stmts, string? pubVar) ExpandCondition(
        string conditionExpression, string blockName, PipelineContext context)
    {
        var result = new List<FormattedStatement>();
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

    // ──────────────────────────────────────────────
    // Loop condition duplication before LoopBodyEnd
    // ──────────────────────────────────────────────

    private void InsertLoopConditionDuplications(FormattedBlockScript script, PipelineContext context)
    {
        foreach (var block in script.Blocks)
        {
            var insertions = new List<(int index, List<FormattedStatement> stmts)>();

            for (int i = 0; i < block.Statements.Count; i++)
            {
                var stmt = block.Statements[i];
                if (stmt.Kind == FormattedStatementKind.LoopBodyEnd
                    && !string.IsNullOrEmpty(stmt.LoopBodyEndReturnTo))
                {
                    // Try both the original block name and the Loop sub-block name
                    // (parser moves Loop stmts into {ParentBlock}_Loop sub-blocks)
                    var lookupKeys = new[] { stmt.LoopBodyEndReturnTo, $"{stmt.LoopBodyEndReturnTo}_Loop" };
                    ConditionInfo? condInfo = null;
                    foreach (var key in lookupKeys)
                    {
                        if (context.LoopConditions.TryGetValue(key, out var ci) && ci.ExpansionStatements.Count > 0)
                        {
                            condInfo = ci;
                            break;
                        }
                    }

                    if (condInfo != null)
                    {
                        var dupStmts = condInfo.ExpansionStatements.Select(CloneStatement).ToList();
                        dupStmts.ForEach(s => s.IsLoopConditionDuplication = true);
                        insertions.Add((i, dupStmts));
                    }
                }
            }

            // Apply in reverse order to preserve indices
            foreach (var (index, stmts) in insertions.OrderByDescending(x => x.index))
                block.Statements.InsertRange(index, stmts);
        }
    }

    private FormattedStatement CloneStatement(FormattedStatement source) => new()
    {
        StatementId = Guid.NewGuid().ToString(),
        BlockName = source.BlockName,
        OriginalExpression = source.OriginalExpression,
        Kind = source.Kind,
        PubVarTarget = source.PubVarTarget,
        FunctionName = source.FunctionName,
        FullFunctionName = source.FullFunctionName,
        Arguments = new List<string>(source.Arguments),
        ConditionExpression = source.ConditionExpression,
        ConditionPubVar = source.ConditionPubVar,
        TrueBlockName = source.TrueBlockName,
        FalseBlockName = source.FalseBlockName,
        LoopBodyEndReturnTo = source.LoopBodyEndReturnTo,
        Fingerprint = source.Fingerprint,
        SetVarName = source.SetVarName,
        GetVarName = source.GetVarName,
        IsLoopConditionDuplication = source.IsLoopConditionDuplication,
        SourceLine = source.SourceLine
    };
}
