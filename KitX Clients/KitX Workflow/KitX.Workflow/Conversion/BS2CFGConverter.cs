using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using Serilog;

using KitX.Workflow.CFG;

using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

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
        var result = new CFGBlock { Name = blockDef.Name };
        // BlockScript §6: a flow-control statement (Branch/Loop/ToLoopCond/Break) terminates
        // the block; any statement after it is unreachable dead code. Track the terminator and
        // warn (non-fatal) on subsequent statements instead of silently formatting them.
        bool seenTerminator = false;
        foreach (var stmt in blockDef.Statements)
        {
            if (seenTerminator)
            {
                context.Diagnostics.AddWarning("BS_DEAD_CODE",
                    $"Unreachable statement after flow-control in block '{blockDef.Name}' is ignored",
                    stmt.LineNumber > 0 ? stmt.LineNumber : null);
                continue;
            }

            var formatted = FormatStatement(stmt, blockDef.Name, context);
            result.Statements.AddRange(formatted);

            if (stmt is FlowControlStatement)
                seenTerminator = true;
        }

        // Sequential fall-through: represent BlockDefinition.NextBlockName as a Sequential edge
        // in Successors (single source of truth) instead of a parallel CFGBlock.NextBlockName
        // field. Only non-control-flow blocks fall through; control-flow blocks already carry
        // their Branch/Loop/Switch/ToLoopCond/Break targets as typed Successors edges elsewhere.
        if (!result.EndsWithControlFlow && !string.IsNullOrEmpty(blockDef.NextBlockName))
        {
            result.Successors.Add(new CFGEdge
            {
                FromBlockName = result.Name,
                ToBlockName = blockDef.NextBlockName,
                Type = CFGEdgeType.Sequential,
                PinName = "Exec"
            });
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
                // Per BlockScript §4.3, variable declarations are not allowed inside MainBlock/
                // NamedBlock; any other unhandled statement form is a user error, not a silent drop.
                context.Diagnostics.AddWarning("BS_UNSUPPORTED_STMT",
                    $"Unsupported statement kind '{stmt.GetType().Name}' in block '{blockName}' is skipped",
                    stmt.LineNumber > 0 ? stmt.LineNumber : null);
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
        var kind = _functionRegistry?.Get(functionName)?.StatementKind ?? ControlFlowMapping.ToKind(flowCtrl.ControlType);

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
            // Copy the full arm list so N-way Switch and any variadic shape survive.
            // ToLoopCond's loopback target lives in Arms[0] (IsLoopback=true), carried by this clone.
            Arms = flowCtrl.Arms.Select(a => a.Clone()).ToList(),
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
        return ControlFlowMapping.ToFunctionName(flowCtrl.ControlType);
    }

    // ──────────────────────────────────────────────
    // Expression statement formatting
    // ──────────────────────────────────────────────

    private List<CFGStatement> FormatExpressionStatement(ExpressionStatement exprStmt, string blockName, PipelineContext context)
    {
        var result = new List<CFGStatement>();
        var expression = exprStmt.Expression;

        // Prefer the invocation that BlockStatementExtractor already parsed and attached, avoiding
        // a second Roslyn parse of the same expression text (the double-parse smell). Fall back to
        // parsing Expression when ParsedInvocation is absent (e.g. programmatically-built statements
        // produced by CFG2BSConverter from a CFG, which have no source tree attached).
        ExpressionSyntax? rightExpr;
        string? assignedVar;
        if (exprStmt.ParsedInvocation is { } preParsed)
        {
            rightExpr = preParsed;
            assignedVar = exprStmt.AssignedVariable;
        }
        else
        {
            var parsed = ExprUtils.ParseStatement(expression);
            if (parsed == null)
            {
                context.Diagnostics.AddError("BS_UNPARSEABLE_STMT",
                    $"Could not parse statement in block '{blockName}': {expression}",
                    exprStmt.LineNumber > 0 ? exprStmt.LineNumber : null);
                return result;
            }

            var (r, a) = parsed.Value;

            if (r == null)
            {
                context.Diagnostics.AddError("BS_UNPARSEABLE_STMT",
                    $"Statement in block '{blockName}' has no right-hand expression: {expression}",
                    exprStmt.LineNumber > 0 ? exprStmt.LineNumber : null);
                return result;
            }

            rightExpr = r;
            assignedVar = a;
        }

        // Handle "NextBlock = ..." assignments (should be handled as FlowControl by parser)
        if (assignedVar != null && assignedVar == "NextBlock")
            return result;

        // If RHS is a "+" binary expression, expand it into a StringConcat call.
        // ExpandExpression synthesizes a vaaa#### = StringConcat(...) statement;
        // if there's an assignment target (v = "a" + "b"), redirect that statement's
        // PubVarTarget to v so we get a single clean CFG statement instead of a
        // vaaa#### temp + a separate v = vaaa#### assignment.
        if (rightExpr is BinaryExpressionSyntax binExpr && binExpr.OperatorToken.Text == "+")
        {
            var (expStmts, finalExpr) = ExpandExpression(rightExpr, blockName, context);

            if (!string.IsNullOrEmpty(assignedVar) && assignedVar != "_")
            {
                // Redirect the last expansion statement's PubVarTarget to assignedVar.
                // The last statement is the StringConcat synthesis (vaaa#### = StringConcat(...)).
                if (expStmts.Count > 0 && !string.IsNullOrEmpty(expStmts[^1].PubVarTarget))
                {
                    var oldTarget = expStmts[^1].PubVarTarget;
                    expStmts[^1].PubVarTarget = assignedVar;
                    // Update OriginalExpression for traceability.
                    expStmts[^1].OriginalExpression = expStmts[^1].OriginalExpression?
                        .Replace(oldTarget, assignedVar);
                    if (!context.PubVarNames.Contains(assignedVar))
                        context.PubVarNames.Add(assignedVar);
                }
                result.AddRange(expStmts);
            }
            else
            {
                // No assignment target — standalone expression (rare for +, but handle it).
                result.AddRange(expStmts);
            }
            return result;
        }

        // If RHS is a function invocation, process it
        if (rightExpr is InvocationExpressionSyntax invoke)
        {
            var funcName = ExprUtils.GetMethodName(invoke);
            if (string.IsNullOrEmpty(funcName))
            {
                context.Diagnostics.AddError("BS_EMPTY_FUNCNAME",
                    $"Invocation in block '{blockName}' has no resolvable function name: {expression}",
                    exprStmt.LineNumber > 0 ? exprStmt.LineNumber : null);
                return result;
            }

            // Skip flow control functions (handled by FlowControlStatement)
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } fcDef && fcDef.IsFlowControl)
                return result;

            var fullFuncName = ExprUtils.GetFullMethodName(invoke);
            result.AddRange(LowerAndPostProcess(invoke, funcName, blockName, context, assignedVar, fullFuncName,
                statementId: exprStmt.StatementId));
            return result;
        }

        // If RHS is just an identifier (variable reference), skip
        return result;
    }

    /// <summary>
    /// Formats a function invocation, expanding nested calls in arguments.
    /// </summary>
    private List<CFGStatement> LowerAndPostProcess(
        InvocationExpressionSyntax invoke, string funcName, string blockName,
        PipelineContext context, string? assignedVar, string? fullFuncName = null,
        string? statementId = null)
    {
        // "_" is the dummy LHS produced by ExprUtils.ParseStatement for standalone
        // expression statements (`_ = WriteTextFile(...)`); treat it as no assignment.
        if (assignedVar == "_") assignedVar = null;

        var result = new List<CFGStatement>();

        // Expand nested calls in arguments first
        var (expansionStmts, currentArgExprs) = ExpandArguments(invoke, blockName, context);
        result.AddRange(expansionStmts);

        // Lower: registered builtins via their descriptor; helpers/unknown via fallback assembly.
        List<CFGStatement> lowered;
        if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } funcDef)
        {
            lowered = funcDef.LowerToCFG(invoke, currentArgExprs, blockName, context, assignedVar);
        }
        else
        {
            // Helper or regular function call
            CFGStatementKind kind;
            string? pubVarTarget = null;
            if (!string.IsNullOrEmpty(assignedVar) && assignedVar != "_")
            {
                kind = CFGStatementKind.Assignment;
                pubVarTarget = assignedVar;
                if (!context.PubVarNames.Contains(assignedVar))
                    context.PubVarNames.Add(assignedVar);
            }
            else
            {
                kind = CFGStatementKind.Expression;
            }
            lowered = [new CFGStatement
            {
                BlockName = blockName,
                Kind = kind,
                FunctionName = funcName,
                FullFunctionName = fullFuncName,
                PubVarTarget = pubVarTarget,
                Arguments = currentArgExprs,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }
        result.AddRange(lowered);

        // Cross-cutting post-processing: descriptor owns core fields, BS2CFG owns bookkeeping.
        foreach (var s in lowered)
        {
            if (string.IsNullOrEmpty(s.StatementId))
                s.StatementId = !string.IsNullOrEmpty(statementId) ? statementId : Guid.NewGuid().ToString();

            if (s.Fingerprint == null
                && s.Kind is CFGStatementKind.Assignment or CFGStatementKind.Expression)
            {
                s.Fingerprint = ExprUtils.ComputeFingerprint(funcName, currentArgExprs);
            }

            s.FullFunctionName ??= fullFuncName;

            // Preserve the deleted PubVar-fallback side-effect: when assignedVar is used as
            // the statement's target, ensure it is tracked as a PubVar.
            if (!string.IsNullOrEmpty(assignedVar) && assignedVar != "_"
                && s.PubVarTarget == assignedVar && !context.PubVarNames.Contains(assignedVar))
            {
                context.PubVarNames.Add(assignedVar);
            }
        }

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

            // Non-extractable / flow-control functions stay inline (cannot be nested-call results).
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } inlineDef
                && (inlineDef.IsNonExtractable || inlineDef.IsFlowControl))
            {
                return (new(), invoke.ToString());
            }

            // Expand this call's arguments once (nested calls → PubVars), shared by both paths below.
            var (expansionStmts, expandedArgs) = ExpandArguments(invoke, blockName, context);

            // Registered value-producing functions lower via their descriptor.
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } regFuncDef)
            {
                var lowered = regFuncDef.LowerToCFG(invoke, expandedArgs, blockName, context, null);
                var lastStmt = lowered.LastOrDefault();
                if (lastStmt?.PubVarTarget != null)
                {
                    var combined = new List<CFGStatement>(expansionStmts);
                    combined.AddRange(lowered);
                    return (combined, lastStmt.PubVarTarget);
                }
                // No target (e.g. PluginCallWithTarget nested) → fall through to helper path,
                // which synthesizes a PubVar for the replacement expression.
            }

            // Helper / regular function call: synthesize a PubVar assignment.
            var pubVarName = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarName))
                context.PubVarNames.Add(pubVarName);

            var allStmts = new List<CFGStatement>(expansionStmts);
            allStmts.Add(new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Assignment,
                PubVarTarget = pubVarName,
                FunctionName = funcName,
                FullFunctionName = fullFuncName,
                Arguments = expandedArgs,
                OriginalExpression = $"{pubVarName} = {invoke}",
                Fingerprint = ExprUtils.ComputeFingerprint(funcName, expandedArgs)
            });

            return (allStmts, pubVarName);
        }

        // Parenthesized expression
        if (expr is ParenthesizedExpressionSyntax paren)
            return ExpandExpression(paren.Expression, blockName, context);

        // Binary expression (e.g. "prefix" + Get("var") + "suffix").
        // For the "+" operator, collect all operands (left-associative chaining) and
        // synthesize a single StringConcat(...) call so the CFG→BP path produces a
        // proper blueprint node instead of an opaque string expression.
        if (expr is BinaryExpressionSyntax binary)
        {
            var op = binary.OperatorToken.Text;
            if (op == "+")
            {
                // Flatten left-associative + chains: ((a + b) + c) → [a, b, c]
                var operands = new List<ExpressionSyntax>();
                CollectAddOperands(binary, operands);

                // Expand each operand (nested calls → temp PubVars), collect statements.
                var allStmts = new List<CFGStatement>();
                var argExprs = new List<string>();
                foreach (var operand in operands)
                {
                    var (oStmts, oExpr) = ExpandExpression(operand, blockName, context);
                    allStmts.AddRange(oStmts);
                    argExprs.Add(oExpr);
                }

                // Synthesize: vaaa#### = StringConcat(arg1, arg2, ...)
                var pubVarName = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
                if (!context.PubVarNames.Contains(pubVarName))
                    context.PubVarNames.Add(pubVarName);

                allStmts.Add(new CFGStatement
                {
                    BlockName = blockName,
                    Kind = CFGStatementKind.Assignment,
                    PubVarTarget = pubVarName,
                    FunctionName = "StringConcat",
                    FullFunctionName = "StringConcat",
                    Arguments = argExprs,
                    OriginalExpression = $"{pubVarName} = StringConcat({string.Join(", ", argExprs)})",
                    Fingerprint = ExprUtils.ComputeFingerprint("StringConcat", argExprs)
                });

                return (allStmts, pubVarName);
            }

            // Non-+ binary: recurse operands and rebuild as string (defensive fallback).
            var (leftStmts, leftExpr) = ExpandExpression(binary.Left, blockName, context);
            var (rightStmts, rightExpr) = ExpandExpression(binary.Right, blockName, context);
            var combined = new List<CFGStatement>(leftStmts);
            combined.AddRange(rightStmts);
            var rebuilt = $"{leftExpr} {op} {rightExpr}";
            return (combined, rebuilt);
        }

        // Default: return as-is
        return (new(), expr.ToString());
    }

    /// <summary>
    /// Recursively flattens a left-associative chain of "+" binary expressions into a
    /// flat list of leaf operands. e.g. ((a + b) + c) → [a, b, c].
    /// Non-+ leaves (literals, identifiers, calls) are collected as-is.
    /// </summary>
    private static void CollectAddOperands(BinaryExpressionSyntax binary, List<ExpressionSyntax> operands)
    {
        if (binary.Left is BinaryExpressionSyntax leftBinary
            && leftBinary.OperatorToken.Text == "+")
        {
            CollectAddOperands(leftBinary, operands);
        }
        else
        {
            operands.Add(binary.Left);
        }
        operands.Add(binary.Right);
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
            var formatted = LowerAndPostProcess(invoke, funcName, blockName, context, null, fullFuncName);

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
