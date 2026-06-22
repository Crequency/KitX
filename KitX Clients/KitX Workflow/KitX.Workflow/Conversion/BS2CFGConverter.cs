using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using Serilog;

using KitX.Workflow.CFG;
using KitX.Workflow.Models;

using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Phase 2: Takes a parsed BlockScript AST and produces a ControlFlowGraph
/// where all nested function calls have been expanded into PubVar assignments.
/// Also duplicates Loop condition evaluations before ToLoopCond statements.
/// </summary>
public class BS2CFGConverter : IPipelineFlattenContext
{
    private readonly List<HelperFunction> _helperFunctions;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public BS2CFGConverter(List<HelperFunction> helperFunctions, BuiltinFunctionRegistry? functionRegistry = null)
    {
        _helperFunctions = helperFunctions;
        _functionRegistry = functionRegistry;
    }

    /// <summary>IPipelineFlattenContext: lookup a builtin's flow-control shape.</summary>
    FlowControlType? IPipelineFlattenContext.GetFlowControlShape(string functionName)
        => _functionRegistry?.Get(functionName)?.FlowControlShape;

    /// <summary>IPipelineFlattenContext: delegate to the instance IsVariableName.</summary>
    bool IPipelineFlattenContext.IsVariableName(string name) => IsVariableName(name, _currentContext!);

    /// <summary>IPipelineFlattenContext: delegate to the instance IsFunctionName.</summary>
    bool IPipelineFlattenContext.IsFunctionName(string name) => IsFunctionName(name, _currentContext!);

    /// <summary>IPipelineFlattenContext: delegate to ExpandExpression (terminalAssignedVar optional).</summary>
    (List<CFGStatement> prefix, string finalExpr) IPipelineFlattenContext.ExpandExpression(
        BSExpression expr, string blockName, string? terminalAssignedVar)
        => ExpandExpression(expr, blockName, _currentContext!, terminalAssignedVar);

    /// <summary>The ForwardConversionState for the current Format pass (set by Format).</summary>
    private ForwardConversionState _currentContext = null!;

    public ControlFlowGraph Format(BlockScript script, ForwardConversionState context)
    {
        var result = new ControlFlowGraph();
        _currentContext = context;

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

    private CFGBlock FormatBlock(BlockDefinition blockDef, ForwardConversionState context)
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
    private List<CFGStatement> FormatStatement(BlockStatement stmt, string blockName, ForwardConversionState context)
    {
        List<CFGStatement> result;
        switch (stmt)
        {
            case FlowControlStatement flowCtrl:
                result = FormatFlowControl(flowCtrl, blockName, context);
                break;
            case ExpressionStatement expr:
                result = FormatExpressionStatement(expr, blockName, context);
                break;
            default:
                // Per BlockScript §4.3, variable declarations are not allowed inside MainBlock/
                // NamedBlock; any other unhandled statement form is a user error, not a silent drop.
                context.Diagnostics.AddWarning("BS_UNSUPPORTED_STMT",
                    $"Unsupported statement kind '{stmt.GetType().Name}' in block '{blockName}' is skipped",
                    stmt.LineNumber > 0 ? stmt.LineNumber : null);
                return new();
        }

        // v5.0 §9.1: anchor the statement's leading comment to the first CFG statement
        // (for pipelines, this is segment 0, consistent with OriginalExpression stamping).
        if (result.Count > 0 && !string.IsNullOrEmpty(stmt.Comment))
            result[0].Comment ??= stmt.Comment;

        return result;
    }

    // ──────────────────────────────────────────────
    // Flow control formatting
    // ──────────────────────────────────────────────

    private List<CFGStatement> FormatFlowControl(FlowControlStatement flowCtrl, string blockName, ForwardConversionState context)
    {
        var result = new List<CFGStatement>();

        // Determine the function name from the source code or control type
        var functionName = GetFunctionNameFromFlowControl(flowCtrl);
        var def = _functionRegistry?.Get(functionName);
        var shape = def?.FlowControlShape ?? flowCtrl.ControlType;

        // Expand condition for Branch/Loop (nested calls in the condition → temp PubVars).
        // v5.0: when ExpandCondition materialises the condition into a PubVar, we overwrite
        // ConditionExpression with that PubVar name so the field is the single source of truth
        // for the condition source. Consumers (EmitStatements, DataEdgeBuilder, InferPubVarTypes)
        // read ConditionExpression directly — no separate ConditionPubVar field needed.
        var hasCondition = !string.IsNullOrEmpty(flowCtrl.ConditionExpression);
        string? effectiveCondition = flowCtrl.ConditionExpression;
        if (hasCondition)
        {
            var (condStmts, pubVar) = ExpandCondition(flowCtrl.ConditionExpression, blockName, context);
            result.AddRange(condStmts);
            if (!string.IsNullOrEmpty(pubVar))
                effectiveCondition = pubVar;
        }

        var stmt = new CfgStatementBuilder
        {
            StatementId = !string.IsNullOrEmpty(flowCtrl.StatementId) ? flowCtrl.StatementId : null,
            BlockName = blockName,
            FlowControlShape = shape,
            FunctionName = functionName,
            ConditionExpression = effectiveCondition,
            // Variadic control-flow forms (ForLoop) carry from/to/step/indexName as positional args;
            // copy them through so EmitStatements/CFG2BS can consume without re-parsing source text.
            Arguments = flowCtrl.FlowArguments.Count > 0
                ? new List<string>(flowCtrl.FlowArguments)
                : [],
            // Copy the full arm list so N-way Switch and any variadic shape survive.
            // ToLoopCond's loopback target lives in Arms[0] (IsLoopback=true), carried by this clone.
            Arms = flowCtrl.Arms.Select(a => a.Clone()).ToList(),
            SourceText = flowCtrl.SourceCode,
            SourceLine = flowCtrl.LineNumber,
        }.Build();
        result.Add(stmt);

        // v5.0: the v4.0 LoopConditions bookkeeping (for CFGConditionDuplicator) is removed —
        // ForLoop/Goto make condition re-evaluation natural via block re-entry.

        return result;
    }

    private static string GetFunctionNameFromFlowControl(FlowControlStatement flowCtrl)
        // The ControlType is authoritative for flow-control statements — it was set by the
        // builtin's ExtractStatement at parse time and uniquely maps to the function name.
        // No need to re-parse SourceCode.
        => ControlFlowMapping.ToFunctionName(flowCtrl.ControlType);

    // ──────────────────────────────────────────────
    // Expression statement formatting
    // ──────────────────────────────────────────────

    /// <summary>
    /// v5.0: returns true if <paramref name="name"/> is a known variable (PubVar or ConstBlock).
    /// Used to distinguish a pipeline variable-assignment target (tap) from a function call.
    /// </summary>
    private static bool IsVariableName(string name, ForwardConversionState context)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (context.PubVarNames.Contains(name)) return true;
        if (context.Script.ConstBlock?.Variables.Any(v => v.Name == name) == true) return true;
        if (context.Script.PubVarBlock?.Variables.Any(v => v.Name == name) == true) return true;
        return false;
    }

    /// <summary>
    /// v5.0: returns true if <paramref name="name"/> is a registered builtin or a declared helper.
    /// </summary>
    private bool IsFunctionName(string name, ForwardConversionState context)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (_functionRegistry?.Get(name) != null) return true;
        if (context.HelperFunctions.Any(h => h.Name == name)) return true;
        return false;
    }

    private List<CFGStatement> FormatExpressionStatement(ExpressionStatement exprStmt, string blockName, ForwardConversionState context)
    {
        var result = new List<CFGStatement>();

        // The BS AST is attached by BlockStatementExtractor at parse time. Every ExpressionStatement
        // reaching BS2CFG comes from parsed source, so ParsedExpression is always present.
        if (exprStmt.ParsedExpression is not { } rightExpr)
        {
            context.Diagnostics.AddError("BS_UNPARSEABLE_STMT",
                $"Statement in block '{blockName}' has no pre-parsed expression: {exprStmt.Expression}",
                exprStmt.LineNumber > 0 ? exprStmt.LineNumber : null);
            return result;
        }

        // v5.0: the pipeline (>) is the data-flow primitive of the functional core. Emit it as a
        // first-class PipelineStatement carrying the BSPipeline AST verbatim. Flattening into the
        // imperative PubVar-assignment form is deferred to PipelineStatement.FlattenedStatements
        // (consumed by CFG2BP/CFG2CS/executor via CFGBlock.GetEffectiveStatements).
        if (rightExpr is BSPipeline pipeline)
        {
            var ps = new PipelineStatement
            {
                BlockName = blockName,
                Pipeline = pipeline,
                StatementId = exprStmt.StatementId,
                SourceLine = exprStmt.LineNumber,
                Comment = exprStmt.Comment,
            };
            // Set the flattener closure — captures this converter (IPipelineFlattenContext) and
            // the current ForwardConversionState. Lazy + cached inside PipelineStatement.
            var self = this;
            ps.Flattener = stmt => PipelineFlattener.Flatten(
                stmt.Pipeline, stmt.BlockName, self, _currentContext);
            return [ps];
        }

        var assignedVar = exprStmt.AssignedVariable;

        // Handle "NextBlock = ..." assignments (should be handled as FlowControl by parser)
        if (assignedVar != null && assignedVar == "NextBlock")
            return result;

        // If RHS is a "+" binary expression, expand it into a StringConcat call.
        // ExpandExpression synthesizes a vaaa#### = StringConcat(...) statement;
        // if there's an assignment target (v = "a" + "b"), redirect that statement's
        // PubVarTarget to v so we get a single clean CFG statement instead of a
        // vaaa#### temp + a separate v = vaaa#### assignment.
        if (rightExpr is BSBinary binExpr && binExpr.Operator == "+")
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
        if (rightExpr is BSCall invoke)
        {
            var funcName = invoke.MethodName;
            if (string.IsNullOrEmpty(funcName))
            {
                context.Diagnostics.AddError("BS_EMPTY_FUNCNAME",
                    $"Invocation in block '{blockName}' has no resolvable function name: {exprStmt.Expression}",
                    exprStmt.LineNumber > 0 ? exprStmt.LineNumber : null);
                return result;
            }

            // Skip flow control functions (handled by FlowControlStatement)
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } fcDef && fcDef.IsFlowControl)
                return result;

            var fullFuncName = invoke.FullMethodName;
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
        BSCall invoke, string funcName, string blockName,
        ForwardConversionState context, string? assignedVar, string? fullFuncName = null,
        string? statementId = null)
    {
        if (assignedVar == "_") assignedVar = null;

        var result = new List<CFGStatement>();

        // Expand nested calls in arguments first
        var (expansionStmts, currentArgExprs) = ExpandArguments(invoke, blockName, context);
        result.AddRange(expansionStmts);

        // Cross-cutting bookkeeping the descriptor needs: StatementId, FullFunctionName, plus the
        // PubVar-name set so auto-minted temps stay visible. The descriptor builds statements via
        // ctx.Build(...) so StatementId/Fingerprint/FullFunctionName are derived by the builder.
        var lowerCtx = new LowerContext
        {
            BlockName = blockName,
            StatementId = statementId,
            FullFunctionName = fullFuncName,
            PubVarNames = context.PubVarNames,
        };

        // Lower: registered builtins via their descriptor; helpers/unknown via fallback assembly.
        List<CFGStatement> lowered;
        if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } funcDef)
        {
            lowered = funcDef.LowerToCFG(invoke, currentArgExprs, lowerCtx, context, assignedVar);
        }
        else
        {
            // Helper or regular function call → single Assignment/Expression statement.
            lowered = [lowerCtx.Build(b =>
            {
                b.FunctionName = funcName;
                b.PubVarTarget = !string.IsNullOrEmpty(assignedVar) && assignedVar != "_" ? assignedVar : null;
                b.Arguments = currentArgExprs;
                b.SourceText = invoke.SourceText;
            })];
        }
        result.AddRange(lowered);

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
        BSCall invoke, string blockName, ForwardConversionState context)
    {
        var stmts = new List<CFGStatement>();
        var argExprs = new List<string>();

        foreach (var arg in invoke.Args)
        {
            var (expanded, finalExpr) = ExpandExpression(arg, blockName, context);
            stmts.AddRange(expanded);
            argExprs.Add(finalExpr);
        }

        return (stmts, argExprs);
    }

    /// <summary>
    /// Recursively expands nested calls within a single expression.
    /// Returns (expansionStatements, finalExpressionString).
    /// <paramref name="terminalAssignedVar"/> (v5.0): when non-null, the top-level call is
    /// lowered with this PubVarTarget instead of synthesising a vaaa#### temp. Used by
    /// FormatPipeline for `Func() > var` so the call writes directly to the terminal variable.
    /// </summary>
    private (List<CFGStatement> stmts, string finalExpr) ExpandExpression(
        BSExpression expr, string blockName, ForwardConversionState context, string? terminalAssignedVar = null)
    {
        // Literal → return as-is
        if (expr is BSLiteral)
            return (new(), expr.SourceText);

        // Simple identifier → return as-is
        if (expr is BSIdentifier)
            return (new(), expr.SourceText);

        // Invocation → may need expansion
        if (expr is BSCall invoke)
        {
            var funcName = invoke.MethodName;
            var fullFuncName = invoke.FullMethodName;

            // Non-extractable / flow-control functions stay inline (cannot be nested-call results).
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } inlineDef
                && (inlineDef.IsNonExtractable || inlineDef.IsFlowControl))
            {
                return (new(), invoke.SourceText);
            }

            // Expand this call's arguments once (nested calls → PubVars), shared by both paths below.
            var (expansionStmts, expandedArgs) = ExpandArguments(invoke, blockName, context);

            // Registered value-producing functions lower via their descriptor.
            if (_functionRegistry != null && _functionRegistry.Get(funcName) is { } regFuncDef)
            {
                // v5.0: pass terminalAssignedVar so `Func() > var` writes directly to var.
                // Descriptors don't need StatementId/fullFuncName here (this is a synthesized temp
                // assignment, not a top-level source statement) — LowerContext leaves them null
                // and the builder mints a fresh Guid, matching the previous helper-path behaviour.
                var lowerCtx = new LowerContext
                {
                    BlockName = blockName,
                    StatementId = null,
                    FullFunctionName = fullFuncName,
                    PubVarNames = context.PubVarNames,
                };
                var lowered = regFuncDef.LowerToCFG(invoke, expandedArgs, lowerCtx, context, terminalAssignedVar);
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
            // v5.0: when terminalAssignedVar is set, use it instead of a generated temp.
            var pubVarName = terminalAssignedVar ?? ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarName))
                context.PubVarNames.Add(pubVarName);

            var allStmts = new List<CFGStatement>(expansionStmts);
            allStmts.Add(new CfgStatementBuilder
            {
                BlockName = blockName,
                FunctionName = funcName,
                FullFunctionName = fullFuncName,
                PubVarTarget = pubVarName,
                Arguments = expandedArgs,
                // Render the {pubVar} = {invoke.SourceText} form explicitly so the original
                // (possibly nested) call text is preserved for traceability.
                SourceText = $"{pubVarName} = {invoke.SourceText}",
            }.Build(context.PubVarNames));

            return (allStmts, pubVarName);
        }

        // Parenthesized expression
        if (expr is BSParenthesized paren)
            return ExpandExpression(paren.Inner, blockName, context);

        // Binary expression (e.g. "prefix" + Get("var") + "suffix").
        // For the "+" operator, collect all operands (left-associative chaining) and
        // synthesize a single StringConcat(...) call so the CFG→BP path produces a
        // proper blueprint node instead of an opaque string expression.
        if (expr is BSBinary binary)
        {
            var op = binary.Operator;
            if (op == "+")
            {
                // Flatten left-associative + chains: ((a + b) + c) → [a, b, c]
                var operands = new List<BSExpression>();
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

                allStmts.Add(new CfgStatementBuilder
                {
                    BlockName = blockName,
                    FunctionName = "StringConcat",
                    FullFunctionName = "StringConcat",
                    PubVarTarget = pubVarName,
                    Arguments = argExprs,
                    SourceText = $"{pubVarName} = StringConcat({string.Join(", ", argExprs)})",
                }.Build(context.PubVarNames));

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
        return (new(), expr.SourceText);
    }

    /// <summary>
    /// Recursively flattens a left-associative chain of "+" binary expressions into a
    /// flat list of leaf operands. e.g. ((a + b) + c) → [a, b, c].
    /// Non-+ leaves (literals, identifiers, calls) are collected as-is.
    /// </summary>
    private static void CollectAddOperands(BSBinary binary, List<BSExpression> operands)
    {
        if (binary.Left is BSBinary leftBinary && leftBinary.Operator == "+")
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
    // Pipeline flattening — MOVED to PipelineFlattener.cs (invoked via PipelineStatement.FlattenedStatements)
    // ──────────────────────────────────────────────

    // ──────────────────────────────────────────────
    // Condition expansion (for Branch/Loop)
    // ──────────────────────────────────────────────

    private (List<CFGStatement> stmts, string? pubVar) ExpandCondition(
        string conditionExpression, string blockName, ForwardConversionState context)
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

        // Parse the expression (one-shot Roslyn parse at the boundary)
        var expr = BSExpressionAdapter.Parse(trimmed);
        if (expr == null)
            return (result, null);

        // If it's a simple identifier, no expansion needed
        if (expr is BSIdentifier)
            return (result, null);

        // If it's an invocation, expand it
        if (expr is BSCall invoke)
        {
            var funcName = invoke.MethodName;
            if (string.IsNullOrEmpty(funcName)) return (result, null);

            var fullFuncName = invoke.FullMethodName;
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
