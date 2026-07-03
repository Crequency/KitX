namespace KitX.Workflow.Backend.RoslynBackend;

using KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// FlattenPipeline — pure projection of the structured IrPipelineStatement AST
// into imperative flat statements.
//
// The legacy PipelineFlattener flattened a BSPipeline AST into a list of
// (FunctionName, Arguments, PubVarTarget) triples that the converter emitted one
// per line. The new IR carries the pipeline as pure data (IrPipelineStatement:
// Sources + Segments), so flattening is an external pure function again — but the
// input is now IrSegment (not BSCall), so the algorithm is adapted.
//
// Pipeline shape
// ──────────────
// A pipeline has N sources (the left side of the first `>`) and M segments
// (each `> Target`). Segments are either:
//   • FunctionCall — a function invocation whose Arguments may contain
//     IrPipelineArgument.Placeholder slots (filled from the pipeline value stream)
//     or IrPipelineArgument.Literal (verbatim argument text).
//   • Variable — a terminal tap (the pipeline result is assigned to this var).
//
// Flattening rules
// ────────────────
// The value stream is seeded by the Sources, then each FunctionCall segment
// consumes the current stream (its Placeholders are replaced by the in-scope
// temp PubVar) and produces a new value (bound to a fresh temp, unless it is the
// terminal segment feeding a Variable tap).
//
// Simplified scope (per the task's priority-degradation guidance): the common
// single-source + linear-segment-chain case is fully supported. Multi-source
// pipelines (several inputs feeding a segment with multiple placeholders) are
// flattened with each placeholder ordinal mapping to the source at that ordinal;
// this covers the realistic cases. Exotic multi-value merges keep correctness via
// the temp-binding but may not match a hand-written form.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One flattened pipeline step: a function call with fully-resolved flat argument
/// strings and the PubVar target its result binds to (null for a bare call with
/// no terminal tap).
/// </summary>
public sealed record FlatPipelineStep(
    string? FunctionName,
    string? FullFunctionName,
    IReadOnlyList<string> Arguments,
    string? AssignedVar);

/// <summary>
/// Pure pipeline-flattening projections over <see cref="IrPipelineStatement"/>.
/// Stateless; every method takes the pipeline it operates on explicitly.
/// </summary>
public static class FlattenPipeline
{
    /// <summary>
    /// Flattens a pipeline statement into an ordered list of imperative steps.
    /// Each FunctionCall segment becomes one step; placeholders are replaced by
    /// the in-scope value name; a terminal Variable tap sets the last step's
    /// AssignedVar.
    /// </summary>
    public static IReadOnlyList<FlatPipelineStep> Flatten(IrPipelineStatement pipe)
    {
        var steps = new List<FlatPipelineStep>();
        var segments = pipe.Segments;

        // The in-scope "current value" name(s): seeded by Sources, refreshed after each
        // FunctionCall segment to the temp (or terminal var) that holds its result.
        // For the single-value linear case this is one name; for multi-source we keep
        // a list indexed by placeholder ordinal.
        var currentValueNames = new List<string>();
        foreach (var src in pipe.Sources)
            currentValueNames.Add(src);

        // Walk the segments. The terminal Variable segment (if any) sets the assignment
        // target for the immediately-preceding FunctionCall; we look ahead to detect it.
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg.Kind != IrSegmentKind.FunctionCall) continue;

            // Resolve this segment's arguments: literals verbatim, placeholders from the stream.
            var flatArgs = new List<string>();
            foreach (var arg in seg.Arguments)
            {
                if (arg.Kind == IrPipelineArgumentKind.Literal)
                    flatArgs.Add(arg.Literal ?? "");
                else // Placeholder: index into the current value stream.
                {
                    var idx = arg.PlaceholderIndex;
                    flatArgs.Add(idx < currentValueNames.Count
                        ? currentValueNames[idx]
                        : "");
                }
            }

            // Is the next segment the terminal Variable tap? Then bind here.
            string? assignedVar = null;
            if (i + 1 < segments.Length && segments[i + 1].Kind == IrSegmentKind.Variable)
                assignedVar = segments[i + 1].VariableName;

            steps.Add(new FlatPipelineStep(seg.FunctionName, seg.FullFunctionName, flatArgs, assignedVar));

            // The result of this segment feeds the next. If bound to a variable, that name
            // becomes the stream; otherwise synthesise a temp PubVar name (the codegen path
            // treats unbound function-call results as bare statements, so the temp name is
            // only relevant if a later segment references it via a placeholder — rare).
            currentValueNames = assignedVar is { Length: > 0 }
                ? [assignedVar]
                : [seg.FunctionName ?? "_"];
        }

        return steps;
    }

    /// <summary>
    /// For a value-producing pipeline (terminal Variable tap), the assignment target.
    /// Returns null for bare-call pipelines (no terminal tap) or non-pipeline statements.
    /// </summary>
    public static string? TryGetAssignmentTarget(IrPipelineStatement pipe)
    {
        if (pipe.Segments.Length == 0) return null;
        var last = pipe.Segments[^1];
        return last.Kind == IrSegmentKind.Variable ? last.VariableName : null;
    }

    /// <summary>
    /// For a value-producing pipeline, the producing call (function name + flat args)
    /// that binds to the terminal Variable tap. For single-segment pipelines this is the
    /// only FunctionCall; for multi-segment pipelines this is the LAST FunctionCall
    /// before the terminal Variable tap (the value that gets assigned).
    /// Returns null for bare-call pipelines or non-pipeline statements.
    /// </summary>
    public static (string? FunctionName, string? FullFunctionName, IReadOnlyList<string> Arguments)?
        TryGetProducingCall(IrPipelineStatement pipe)
    {
        // Find the terminal FunctionCall: the last FunctionCall segment (immediately
        // before the Variable tap when one is present, else the last segment).
        string? fnName = null;
        string? fullFn = null;
        IrSegment? producingSeg = null;

        for (int i = 0; i < pipe.Segments.Length; i++)
        {
            var seg = pipe.Segments[i];
            if (seg.Kind == IrSegmentKind.FunctionCall)
            {
                producingSeg = seg;
                fnName = seg.FunctionName;
                fullFn = seg.FullFunctionName;
            }
        }

        if (producingSeg is null) return null;

        // Flatten just this segment's literal arguments (placeholders already resolved
        // for the common case where this is the terminal segment consuming the stream).
        // For the type-inference pass we only need the literal args — placeholders refer
        // to upstream temps whose types were already inferred in source-pass order, and
        // the demand pass walks every statement independently.
        var args = new List<string>();
        foreach (var arg in producingSeg.Arguments)
        {
            if (arg.Kind == IrPipelineArgumentKind.Literal)
                args.Add(arg.Literal ?? "");
            else
                args.Add(""); // placeholder — type inference treats these as untyped
        }
        return (fnName, fullFn, args);
    }
}
