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

    private List<CFGStatement> FormatFlowControl(FlowControlStatement flowCtrl, string blockName, PipelineContext context)
    {
        var result = new List<CFGStatement>();

        // Determine the function name from the source code or control type
        var functionName = GetFunctionNameFromFlowControl(flowCtrl);
        var def = _functionRegistry?.Get(functionName);
        var kind = def?.StatementKind ?? ControlFlowMapping.ToKind(flowCtrl.ControlType);
        var shape = def?.FlowControlShape ?? flowCtrl.ControlType;

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
            FlowControlShape = shape,
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
    private static bool IsVariableName(string name, PipelineContext context)
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
    private bool IsFunctionName(string name, PipelineContext context)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (_functionRegistry?.Get(name) != null) return true;
        if (context.HelperFunctions.Any(h => h.Name == name)) return true;
        return false;
    }

    private List<CFGStatement> FormatExpressionStatement(ExpressionStatement exprStmt, string blockName, PipelineContext context)
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

        // Pipeline (\-) statements flatten into a sequence of PubVar assignments + calls sharing
        // a PipelineId for round-trip reconstruction. Handled before the general call/binary paths.
        if (rightExpr is BSPipeline pipeline)
            return FormatPipeline(pipeline, blockName, context);

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
        PipelineContext context, string? assignedVar, string? fullFuncName = null,
        string? statementId = null)
    {
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
                OriginalExpression = invoke.SourceText,
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
        BSCall invoke, string blockName, PipelineContext context)
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
        BSExpression expr, string blockName, PipelineContext context, string? terminalAssignedVar = null)
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
                var lowered = regFuncDef.LowerToCFG(invoke, expandedArgs, blockName, context, terminalAssignedVar);
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
            allStmts.Add(new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Assignment,
                PubVarTarget = pubVarName,
                FunctionName = funcName,
                FullFunctionName = fullFuncName,
                Arguments = expandedArgs,
                OriginalExpression = $"{pubVarName} = {invoke.SourceText}",
                Fingerprint = ExprUtils.ComputeFingerprint(funcName, expandedArgs)
            });

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
    // Pipeline (\-) flattening
    // ──────────────────────────────────────────────

    /// <summary>
    /// Flattens a <see cref="BSPipeline"/> into a sequence of CFG statements that share a
    /// <see cref="CFGStatement.PipelineId"/> for round-trip reconstruction.
    /// <para>
    /// Each Source becomes an assignment; each Target becomes a call whose arguments are filled
    /// from the current inputs (Sources for the first Target, the previous Target's single result
    /// for subsequent ones). Targets containing <c>_</c> placeholders consume inputs at those
    /// positions; targets without placeholders consume all inputs positionally.
    /// </para>
    /// <para>
    /// The flattened form is semantically equivalent to nested calls — the pipeline is pure
    /// text-side sugar. The CFG's flat PubVar-assignment form is what executes.
    /// </para>
    /// </summary>
    private List<CFGStatement> FormatPipeline(BSPipeline pipeline, string blockName, PipelineContext context)
    {
        var result = new List<CFGStatement>();
        var pipelineId = Guid.NewGuid().ToString();
        int segIndex = 0;
        // Preserve the verbatim pipeline source text on the first segment so CFG2BSConverter can
        // rebuild the pipeline statement text without re-deriving it from the flattened form.
        var pipelineSource = pipeline.SourceText;

        // ── Sources: expand each (nested calls → temp PubVars) and record as assignments. ──
        // The PubVar name (or literal/identifier text) of each source is the "current input"
        // fed to the first Target.
        // v5.0: single-source-call → single-terminal-variable redirect. When the pipeline is of
        // the form `Func(args) > var` (one source that is a call, one terminal target that is a
        // bare variable), expand the source call with the terminal variable as its PubVarTarget.
        // This avoids synthesising an intermediate vaaa#### that would drift across round-trips
        // (Round 1: `Func() > var`; Round 2: `Func() > vaaaNNNN; vaaaNNNN > var`). The terminal
        // assignment is then emitted by the variable-target branch below, which detects the
        // redirect via the skip-opt (prevStmt.PubVarTarget == assignedVar) and drops the
        // redundant assignment. This mirrors the existing next-is-terminal-variable redirect in
        // the Targets loop but covers the case where the call is the lone SOURCE, not a target.
        string? sourceRedirectVar = null;
        if (pipeline.Sources.Count == 1
            && pipeline.Sources[0] is BSCall
            && pipeline.Targets.Count == 1
            && IsVariableName(pipeline.Targets[0].MethodName, context)
            && !IsFunctionName(pipeline.Targets[0].MethodName, context))
        {
            sourceRedirectVar = pipeline.Targets[0].MethodName;
            if (!context.PubVarNames.Contains(sourceRedirectVar))
                context.PubVarNames.Add(sourceRedirectVar);
        }

        var currentInputs = new List<string>();
        foreach (var source in pipeline.Sources)
        {
            var (srcStmts, srcExpr) = ExpandExpression(source, blockName, context, sourceRedirectVar);
            foreach (var s in srcStmts)
            {
                s.PipelineId = pipelineId;
                s.PipelineSegmentIndex = segIndex++;
                result.Add(s);
            }
            currentInputs.Add(srcExpr);
        }

        // ── Targets: fill arguments from current inputs, emit one call each. ──
        // Each non-terminal target synthesizes a PubVar to hold its result so the next segment
        // can consume it; the terminal target (last) is a bare side-effect call with no PubVar.
        // v5.0: a target that is a bare variable name (in PubVarNames/ConstBlock, not a registered
        // function) is a variable assignment (tap semantics: Expr > var), NOT a function call —
        // it must produce an assignment CFGStatement with PubVarTarget = varName, so BP→BS round-trip
        // reconstructs `Expr > var` rather than the malformed `var(Expr)`.
        for (int t = 0; t < pipeline.Targets.Count; t++)
        {
            var target = pipeline.Targets[t];
            bool isTerminal = t == pipeline.Targets.Count - 1;
            var resolvedArgs = ResolvePipelineArgs(target, currentInputs, blockName, context, result, pipelineId, ref segIndex);

            // v5.0: detect a variable-assignment target (tap). A bare identifier that is a known
            // variable (PubVar or ConstBlock) and NOT a registered/hesper function → assignment.
            bool isVariableTarget = IsVariableName(target.MethodName, context)
                && !IsFunctionName(target.MethodName, context);

            if (isVariableTarget)
            {
                // Expr > var  →  assignment to var (implicit Set), with tap pass-through.
                var assignedVar = target.MethodName;
                if (!context.PubVarNames.Contains(assignedVar))
                    context.PubVarNames.Add(assignedVar);

                // v5.0: when the previous target was a function whose pubVarTarget was redirected
                // to this variable (next-is-terminal-variable optimization), the assignment is
                // already captured — skip the redundant __assign node. BUT only when the variable
                // is not also the function's input (self-increment like `x > Add(_,1) > x` needs
                // the assignment to be separate so the read precedes the write in codegen).
                if (isTerminal && result.Count > 0)
                {
                    var prevStmt = result[^1];
                    if (prevStmt.PubVarTarget == assignedVar
                        && !string.IsNullOrEmpty(prevStmt.FunctionName)
                        && !(prevStmt.Arguments?.Contains(assignedVar) == true))
                    {
                        currentInputs = new List<string> { assignedVar };
                        continue;
                    }
                }

                var assignStmt = new CFGStatement
                {
                    BlockName = blockName,
                    Kind = CFGStatementKind.Assignment,
                    FunctionName = null,
                    PubVarTarget = assignedVar,
                    // The RHS is the single piped value (currentInputs has one entry for chains).
                    Arguments = currentInputs,
                    OriginalExpression = $"{currentInputs.FirstOrDefault() ?? "null"} > {assignedVar}",
                    PipelineId = pipelineId,
                    PipelineSegmentIndex = segIndex++
                };
                if (string.IsNullOrEmpty(assignStmt.StatementId))
                    assignStmt.StatementId = Guid.NewGuid().ToString();
                result.Add(assignStmt);
                // Tap: the assigned value passes through to the next segment.
                currentInputs = new List<string> { assignedVar };
                continue;
            }

            // Synthesize a PubVar target for non-terminal segments so the result flows forward.
            // v5.0: when the NEXT target is the terminal variable assignment, use its name as this
            // function's pubVarTarget — the result flows directly into the variable (no intermediate
            // vaaa, no __assign node). BUT skip this optimization when the variable is also this
            // function's input (self-increment `x > Add(_,1) > x` needs a temp so read precedes write).
            string? pubVarTarget = null;
            if (!isTerminal)
            {
                var nextTarget = pipeline.Targets[t + 1];
                bool nextIsVariable = IsVariableName(nextTarget.MethodName, context)
                    && !IsFunctionName(nextTarget.MethodName, context);
                bool nextIsTerminal = t + 1 == pipeline.Targets.Count - 1;
                bool selfIncrement = resolvedArgs.Contains(nextTarget.MethodName);
                if (nextIsVariable && nextIsTerminal && !selfIncrement)
                {
                    pubVarTarget = nextTarget.MethodName;
                    if (!context.PubVarNames.Contains(pubVarTarget))
                        context.PubVarNames.Add(pubVarTarget);
                }
                else
                {
                    pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
                    if (!context.PubVarNames.Contains(pubVarTarget))
                        context.PubVarNames.Add(pubVarTarget);
                }
            }

            var funcDef = _functionRegistry?.Get(target.MethodName);
            var stmt = new CFGStatement
            {
                BlockName = blockName,
                Kind = pubVarTarget != null ? CFGStatementKind.Assignment : CFGStatementKind.Expression,
                FlowControlShape = funcDef?.FlowControlShape,
                FunctionName = target.MethodName,
                FullFunctionName = target.FullMethodName,
                PubVarTarget = pubVarTarget,
                Arguments = resolvedArgs,
                OriginalExpression = pubVarTarget != null
                    ? $"{pubVarTarget} = {target.MethodName}({string.Join(", ", resolvedArgs)})"
                    : $"{target.MethodName}({string.Join(", ", resolvedArgs)})",
                Fingerprint = ExprUtils.ComputeFingerprint(target.MethodName, resolvedArgs),
                PipelineId = pipelineId,
                PipelineSegmentIndex = segIndex++
            };
            if (string.IsNullOrEmpty(stmt.StatementId))
                stmt.StatementId = Guid.NewGuid().ToString();
            result.Add(stmt);

            currentInputs = pubVarTarget != null ? new List<string> { pubVarTarget } : new List<string>();
        }

        // Stamp the verbatim pipeline source text on the first segment so CFG2BS can rebuild
        // the pipeline statement without re-deriving it from the flattened PubVar form.
        if (result.Count > 0 && !string.IsNullOrEmpty(pipelineSource))
            result[0].OriginalExpression = pipelineSource;

        return result;
    }

    /// <summary>
    /// Resolves a pipeline target's arguments to concrete PubVar/literal strings, substituting
    /// <c>_</c> placeholders and non-placeholder args. When the target has placeholders, each
    /// placeholder consumes one input in order; the remaining (literal) args are kept verbatim.
    /// When the target has no placeholders, all current inputs fill the argument positions in order.
    /// Any nested calls in non-placeholder args are expanded (appended to <paramref name="result"/>).
    /// </summary>
    private List<string> ResolvePipelineArgs(BSCall target, List<string> currentInputs,
        string blockName, PipelineContext context, List<CFGStatement> result,
        string pipelineId, ref int segIndex)
    {
        var placeholders = target.Args.OfType<BSPlaceholder>().ToList();
        var resolved = new List<string>();
        int inputCursor = 0;

        foreach (var arg in target.Args)
        {
            if (arg is BSPlaceholder)
            {
                resolved.Add(inputCursor < currentInputs.Count
                    ? currentInputs[inputCursor++]
                    : "null");
            }
            else
            {
                // Expand any nested call in this arg, then use its final expression string.
                var (nested, finalExpr) = ExpandExpression(arg, blockName, context);
                foreach (var s in nested)
                {
                    s.PipelineId = pipelineId;
                    s.PipelineSegmentIndex = segIndex++;
                    result.Add(s);
                }
                resolved.Add(finalExpr);
            }
        }

        // No placeholders → fill positional slots from currentInputs. When the call had no
        // explicit args, ALL inputs apply (e.g. `a, b > StringConcat`). When the call had some
        // explicit args (e.g. `guessNum, targetNum > HelperFuncCompare("BEQ")`), the inputs
        // append AFTER the literal args (BEQ stays first, then guessNum, targetNum).
        if (placeholders.Count == 0)
        {
            foreach (var input in currentInputs)
                resolved.Add(input);
        }

        return resolved;
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
