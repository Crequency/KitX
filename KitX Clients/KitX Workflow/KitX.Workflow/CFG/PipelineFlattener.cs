using KitX.Core.Contract.Workflow;
using KitX.Workflow.Models;

namespace KitX.Workflow.CFG;

/// <summary>
/// Context the <see cref="PipelineFlattener"/> needs from its host converter (BS2CFGConverter).
/// Decouples the flattening algorithm from the converter's private instance members so it can be
/// unit-tested and reused if a second producer of pipelines ever appears.
/// </summary>
public interface IPipelineFlattenContext
{
    /// <summary>True if the function is registered with ArgLayout != null (flow-control).</summary>
    bool IsFlowControl(string functionName);

    /// <summary>True if <paramref name="name"/> is a known variable (PubVar/ConstBlock/PubVarBlock).</summary>
    bool IsVariableName(string name);

    /// <summary>True if <paramref name="name"/> is a registered builtin or declared helper.</summary>
    bool IsFunctionName(string name);

    /// <summary>
    /// Expand a single BS expression (literal / identifier / nested call / binary / paren) into
    /// zero or more prefix CFGStatements plus a final expression string (a PubVar name, literal,
    /// or identifier). <paramref name="terminalAssignedVar"/> is the v5.0 single-source-call →
    /// terminal-variable redirect target (null when not applicable).
    /// </summary>
    (List<CFGStatement> prefix, string finalExpr) ExpandExpression(
        BSExpression expr, string blockName, string? terminalAssignedVar);
}

/// <summary>
/// Pure flattening of a v5.0 pipeline (<c>&gt;</c>) AST into an imperative sequence of plain
/// <see cref="CFGStatement"/>s. Extracted verbatim from BS2CFGConverter.FormatPipeline in
/// Step 1 of the PipelineStatement refactor (behaviour-equivalent; capacitor minimization is
/// Step 4).
/// </summary>
/// <remarks>
/// The flattener performs: source expansion (nested calls → temp PubVars), target iteration with
/// placeholder substitution, PubVar capacitor synthesis for non-terminal segments, and the
/// single-source / next-is-terminal redirect optimizations that keep round-trip stable.
/// </remarks>
public static class PipelineFlattener
{
    /// <summary>
    /// Flatten the pipeline into the imperative sequence of plain CFGStatements consumed by
    /// CFG2BP/CFG2CS/executor via CFGBlock.GetEffectiveStatements.
    /// </summary>
    public static List<CFGStatement> Flatten(
        BSPipeline pipeline, string blockName, IPipelineFlattenContext ctx,
        ForwardConversionState context)
    {
        var result = new List<CFGStatement>();

        // ── v5.0 single-source-call → terminal-variable redirect ──
        // `Func(args) > var` (one source that is a call, one terminal variable target):
        // expand the source call with the terminal variable as its PubVarTarget so no intermediate
        // vaaa#### is synthesised (which would drift across round-trips).
        string? sourceRedirectVar = null;
        if (pipeline.Sources.Count == 1
            && pipeline.Sources[0] is BSCall
            && pipeline.Targets.Count == 1
            && ctx.IsVariableName(pipeline.Targets[0].MethodName)
            && !ctx.IsFunctionName(pipeline.Targets[0].MethodName))
        {
            sourceRedirectVar = pipeline.Targets[0].MethodName;
            if (!context.PubVarNames.Contains(sourceRedirectVar))
                context.PubVarNames.Add(sourceRedirectVar);
        }

        // ── Sources: expand each (nested calls → temp PubVars) and collect current inputs. ──
        var currentInputs = new List<string>();
        foreach (var source in pipeline.Sources)
        {
            var (srcStmts, srcExpr) = ctx.ExpandExpression(source, blockName, sourceRedirectVar);
            result.AddRange(srcStmts);
            currentInputs.Add(srcExpr);
        }

        // ── Targets: fill arguments from current inputs, emit one statement each. ──
        for (int t = 0; t < pipeline.Targets.Count; t++)
        {
            var target = pipeline.Targets[t];
            bool isTerminal = t == pipeline.Targets.Count - 1;
            var resolvedArgs = ResolveArgs(target, currentInputs, blockName, ctx, context, result);

            bool isVariableTarget = ctx.IsVariableName(target.MethodName)
                && !ctx.IsFunctionName(target.MethodName);

            if (isVariableTarget)
            {
                var assignedVar = target.MethodName;
                if (!context.PubVarNames.Contains(assignedVar))
                    context.PubVarNames.Add(assignedVar);

                // Skip-opt: previous target was a function whose pubVarTarget was redirected to
                // this variable → the assignment is already captured. Skip unless self-increment
                // (x > Add(_,1) > x needs the assignment separate so read precedes write).
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

                var assignStmt = new CfgStatementBuilder
                {
                    BlockName = blockName,
                    FunctionName = null,  // pure assignment (Expr > var tap)
                    PubVarTarget = assignedVar,
                    Arguments = currentInputs,
                }.Build(context.PubVarNames);
                result.Add(assignStmt);
                currentInputs = new List<string> { assignedVar };
                continue;
            }

            // Non-terminal function target: synthesize a PubVar capacitor (or redirect to the
            // terminal variable when the NEXT target is it and this isn't a self-increment).
            string? pubVarTarget = null;
            if (!isTerminal)
            {
                var nextTarget = pipeline.Targets[t + 1];
                bool nextIsVariable = ctx.IsVariableName(nextTarget.MethodName)
                    && !ctx.IsFunctionName(nextTarget.MethodName);
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

            var stmt = new CfgStatementBuilder
            {
                BlockName = blockName,
                FunctionName = target.MethodName,
                FullFunctionName = target.FullMethodName,
                PubVarTarget = pubVarTarget,
                Arguments = resolvedArgs,
            }.Build(context.PubVarNames);
            result.Add(stmt);

            currentInputs = pubVarTarget != null ? new List<string> { pubVarTarget } : new List<string>();
        }

        return result;
    }

    /// <summary>
    /// Resolve a pipeline target's arguments: substitute <c>_</c> placeholders positionally from
    /// current inputs; expand nested calls in non-placeholder args; when there are no placeholders,
    /// append all current inputs after the explicit args.
    /// </summary>
    private static List<string> ResolveArgs(
        BSCall target, List<string> currentInputs, string blockName,
        IPipelineFlattenContext ctx, ForwardConversionState context,
        List<CFGStatement> result)
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
                var (nested, finalExpr) = ctx.ExpandExpression(arg, blockName, null);
                result.AddRange(nested);
                resolved.Add(finalExpr);
            }
        }

        if (placeholders.Count == 0)
        {
            foreach (var input in currentInputs)
                resolved.Add(input);
        }

        return resolved;
    }
}
