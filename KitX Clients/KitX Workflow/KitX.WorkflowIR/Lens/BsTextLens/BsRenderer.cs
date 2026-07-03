namespace KitX.WorkflowIR.Lens.BsTextLens;

using System.Text;
using KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BsRenderer — immutable IrWorkflow → BlockScript source text.
//
// Replaces the legacy CFGRenderer (386 lines). The render is dramatically simpler
// because the IR already carries structured pipeline ASTs. The legacy renderer had
// to run a FoldCapacitors pass over flattened PubVar-assignment sequences to rebuild
// the `a, b > F > G > x` text — folding capacitor chains, detecting single-source
// redirects, rewriting `_` placeholders. None of that exists here: IrPipelineStatement
// keeps Sources + Segments verbatim, so rendering is just walking the AST and joining
// with ` > `.
//
// Smells ELIMINATED:
//   • BuiltinFunctionRegistry.Instance static-singleton dependency → constructor
//     injection. The renderer is a pure consumer of the IR + its injected registry.
//   • Render/Generate dual paths (legacy had both CFG→text and CFG→BlockScript) →
//     a single Project visitor: IrWorkflow → text.
//   • `ref int i` cursor + mutable list folding → LINQ + immutable reconstruction.
//   • FoldCapacitors / FoldChain / ConsumesPubVar → GONE (IR is structural).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Renders an immutable <see cref="IrWorkflow"/> to BlockScript source text. Pure:
/// the same IR always yields the same text, and the IR is not mutated.
/// </summary>
public sealed class BsRenderer
{
    private readonly int _indentWidth;

    /// <param name="indentWidth">Spaces per indentation level for block bodies (default 4).</param>
    public BsRenderer(int indentWidth = 4) => _indentWidth = indentWidth;

    /// <summary>
    /// Renders the full BS document: #ConstBlock, #PubVarBlock, then each block
    /// (#MainBlock first, then named #Block entries in IR order).
    /// </summary>
    public string Render(IrWorkflow ir)
    {
        var sb = new StringBuilder();

        // ── #ConstBlock ──
        if (ir.Constants.Count > 0)
        {
            sb.Append(BlockScriptWellKnown.Blocks.MarkerConstBlock).Append('\n');
            foreach (var c in ir.Constants.Values)
                sb.Append(Indent(1)).Append(RenderConstant(c)).Append('\n');
            sb.Append('\n');
        }

        // ── #PubVarBlock (non-capacitor globals only) ──
        var realGlobals = ir.GlobalVars.Values
            .Where(g => Util.PubVarNaming.TryExtractPubVarCounter(g.Name) is null);
        if (realGlobals.Any())
        {
            sb.Append(BlockScriptWellKnown.Blocks.MarkerPubVarBlock).Append('\n');
            foreach (var g in realGlobals)
                sb.Append(Indent(1)).Append(RenderGlobalVar(g)).Append('\n');
            sb.Append('\n');
        }

        // ── Blocks: MainBlock first, then the rest in IR order. ──
        // The entry block is always Blocks[0]; emit it first so the rendered text has
        // #MainBlock at the top (matching canonical BS layout), followed by named blocks.
        foreach (var block in ir.Blocks)
        {
            RenderBlock(sb, block, ir.MainBlockName);
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    // ── Declaration rendering ─────────────────────────────────────────────────

    private static string RenderConstant(IrConstant c)
    {
        var type = string.IsNullOrEmpty(c.Type) ? "dynamic" : c.Type;
        var init = RenderInitialValue(c.InitialValueExpression, c.DefaultValue);
        return init is null ? $"{type} {c.Name};" : $"{type} {c.Name} = {init};";
    }

    private static string RenderGlobalVar(IrGlobalVar g)
    {
        var type = string.IsNullOrEmpty(g.Type) ? "dynamic" : g.Type;
        var init = RenderInitialValue(g.InitialValueExpression, g.DefaultValue);
        return init is null ? $"{type} {g.Name};" : $"{type} {g.Name} = {init};";
    }

    private static string? RenderInitialValue(string? expr, object? value)
    {
        // Prefer the verbatim initialiser expression (lossless: keeps quoting/escaping).
        if (!string.IsNullOrEmpty(expr)) return expr;
        if (value is null) return null;
        return FormatValue(value);
    }

    /// <summary>Formats a typed value as a BS literal token (string → quoted, etc.).</summary>
    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        bool b => b ? "true" : "false",
        char c => $"'{c}'",
        _ => value.ToString() ?? "null",
    };

    // ── Block rendering ───────────────────────────────────────────────────────

    private void RenderBlock(StringBuilder sb, IrBlock block, string mainBlockName)
    {
        // Block-level comment (from annotations, §9.3) — rendered above the marker.
        var comment = BlockComment(block);
        if (comment is { Length: > 0 })
            sb.Append($"// {comment}\n");

        var marker = block.Name == mainBlockName
            ? BlockScriptWellKnown.Blocks.MarkerMainBlock
            : BlockScriptWellKnown.Blocks.MarkerBlockPrefix + block.Name;
        sb.Append(marker).Append('\n');

        // ##BlockVars (followed by ##BlockBody when present).
        if (block.BlockVars.Length > 0)
        {
            sb.Append(Indent(1)).Append(BlockScriptWellKnown.Blocks.MarkerBlockVars).Append('\n');
            foreach (var v in block.BlockVars)
            {
                var init = v.InitialValueExpression;
                var line = init is { Length: > 0 }
                    ? $"{v.Type} {v.Name} = {init};"
                    : $"{v.Type} {v.Name};";
                sb.Append(Indent(2)).Append(line).Append('\n');
            }
            sb.Append(Indent(1)).Append(BlockScriptWellKnown.Blocks.MarkerBlockBody).Append('\n');
        }
        else if (block.HasExplicitBlockBody)
        {
            sb.Append(Indent(1)).Append(BlockScriptWellKnown.Blocks.MarkerBlockBody).Append('\n');
        }

        // Statements.
        foreach (var stmt in block.Statements)
            sb.Append(Indent(1)).Append(RenderStatement(stmt)).Append('\n');

        // Sequential fall-through: only emit a Goto when the block has a Sequential
        // successor but no explicit control-flow terminator. Control-flow terminators
        // (Branch/ForLoop/Switch/Goto/Break/Exit) already carry their own targets, so
        // fall-through is implied by their statement — no synthesised Goto.
        if (block.Kind != IrBlockKind.BranchHeader
            && block.FallThroughTarget is { Length: > 0 } target)
        {
            sb.Append(Indent(1)).Append($"Goto(\"{target}\");\n");
        }
    }

    /// <summary>The block-level comment (if any) from annotations, else null.</summary>
    private static string? BlockComment(IrBlock block)
    {
        foreach (var ann in block.Annotations)
            if (ann.Kind == AnnotationKind.Comment && ann.Key == block.Name
                && ann.Value is string s)
                return s;
        return null;
    }

    private string Indent(int level) => new(' ', level * _indentWidth);

    // ── Statement rendering (the IR visitor) ──────────────────────────────────

    /// <summary>Renders one IR statement to its BS text line (without trailing newline).</summary>
    public string RenderStatement(IrStatement stmt)
    {
        var commentPrefix = stmt.Comment is { Length: > 0 } c ? $"// {c}\n" : "";

        return stmt switch
        {
            IrPipelineStatement pipe => commentPrefix + RenderPipeline(pipe) + ";",
            IrControlFlowStatement cf => commentPrefix + RenderControlFlow(cf) + ";",
            _ => commentPrefix + "null;",
        };
    }

    /// <summary>
    /// Renders an <see cref="IrPipelineStatement"/> from its structured Sources + Segments.
    /// This is the inverse of BsLowerer.LowerPipeline: sources joined with `, `, then each
    /// segment appended after ` > `. No capacitor folding needed — the IR keeps the shape.
    /// </summary>
    private string RenderPipeline(IrPipelineStatement pipe)
    {
        var sb = new StringBuilder();
        if (pipe.Sources.Length > 0)
            sb.Append(string.Join(", ", pipe.Sources));

        foreach (var seg in pipe.Segments)
        {
            sb.Append(" > ");
            if (seg.Kind == IrSegmentKind.Variable)
                sb.Append(seg.VariableName);
            else
                sb.Append(RenderFunctionSegment(seg));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders a FunctionCall segment as <c>Name(arg1, _, arg3)</c>. Placeholders (`_`) and
    /// literal arguments are interleaved in their stored order.
    /// </summary>
    private string RenderFunctionSegment(IrSegment seg)
    {
        var name = string.IsNullOrEmpty(seg.FunctionName) ? "?" : seg.FunctionName;
        var args = string.Join(", ", seg.Arguments.Select(RenderArgument));
        return $"{name}({args})";
    }

    private static string RenderArgument(IrPipelineArgument arg) => arg.Kind switch
    {
        IrPipelineArgumentKind.Placeholder => "_",
        _ => arg.Literal ?? "",
    };

    // ── Control-flow rendering ────────────────────────────────────────────────
    //
    // Each control-flow op has a known positional argument layout. We rebuild the BS
    // call from the flat Arguments array + the named Targets. When Arguments is empty
    // (an IR built from BP, not BS), we synthesise arguments purely from Targets so the
    // rendered text is still valid BS.

    private string RenderControlFlow(IrControlFlowStatement cf)
    {
        // Flip shares the Branch op (two-way control flow, no dedicated enum) but renders
        // differently: its arguments are the two block names, not a condition + targets.
        if (cf.FunctionName == "Flip")
            return RenderFlip(cf);

        return cf.Op switch
        {
            ControlFlowOp.Branch => RenderBranch(cf),
            ControlFlowOp.ForLoop => RenderForLoop(cf),
            ControlFlowOp.Switch => RenderSwitch(cf),
            ControlFlowOp.Goto => RenderGoto(cf),
            ControlFlowOp.Break => "Break()",
            ControlFlowOp.Exit => "Exit()",
            _ => $"{cf.FunctionName}()",
        };
    }

    /// <summary>Flip("blockA", "blockB") — alternating two-way control flow.</summary>
    private string RenderFlip(IrControlFlowStatement cf)
    {
        var a = BlockTarget(cf, "A");
        var b = BlockTarget(cf, "B");
        // Fall back to Arguments when Targets pins are absent (defensive).
        if (a.Length == 0 && cf.Arguments.Length > 0) a = cf.Arguments[0].Trim('"');
        if (b.Length == 0 && cf.Arguments.Length > 1) b = cf.Arguments[1].Trim('"');
        return $"Flip(\"{a}\", \"{b}\")";
    }

    /// <summary>Branch(cond, "trueBlock", "falseBlock") — cond then the two targets.</summary>
    private string RenderBranch(IrControlFlowStatement cf)
    {
        var cond = cf.Arguments.Length > 0 ? cf.Arguments[0] : "false";
        var trueBlock = BlockTarget(cf, "True");
        var falseBlock = BlockTarget(cf, "False");
        return $"{cf.FunctionName}({cond}, \"{trueBlock}\", \"{falseBlock}\")";
    }

    /// <summary>ForLoop(from, to, step, "indexName", "bodyBlock", "endBlock").</summary>
    private string RenderForLoop(IrControlFlowStatement cf)
    {
        // Arguments carry [from, to, step, indexName]; targets carry [LoopBody, LoopEnd].
        var from = cf.Arguments.Length > 0 ? cf.Arguments[0] : "0";
        var to = cf.Arguments.Length > 1 ? cf.Arguments[1] : "0";
        var step = cf.Arguments.Length > 2 ? cf.Arguments[2] : "1";
        var indexName = cf.Arguments.Length > 3 ? cf.Arguments[3] : "i";
        var body = BlockTarget(cf, "LoopBody");
        var end = BlockTarget(cf, "LoopEnd");
        return $"{cf.FunctionName}({from}, {to}, {step}, \"{indexName}\", \"{body}\", \"{end}\")";
    }

    /// <summary>Switch(selector, "defaultBlock", "b0", "b1", ...).</summary>
    private string RenderSwitch(IrControlFlowStatement cf)
    {
        var selector = cf.Arguments.Length > 0 ? cf.Arguments[0] : "0";
        // Targets are ordered: Default first, then 0, 1, ..., N-1. Preserve that order.
        var blocks = cf.Targets.Select(t => $"\"{t.TargetBlockName}\"");
        return $"{cf.FunctionName}({selector}, {string.Join(", ", blocks)})";
    }

    /// <summary>Goto("targetBlock").</summary>
    private string RenderGoto(IrControlFlowStatement cf)
    {
        var target = cf.Targets.Length > 0 ? cf.Targets[0].TargetBlockName : "";
        // Fallback: if targets are absent but Arguments carries the name, use it.
        if (target.Length == 0 && cf.Arguments.Length > 0)
            target = cf.Arguments[0].Trim('"');
        return $"{cf.FunctionName}(\"{target}\")";
    }

    /// <summary>Looks up the target block name for a given pin, or empty string.</summary>
    private static string BlockTarget(IrControlFlowStatement cf, string pinName)
    {
        foreach (var t in cf.Targets)
            if (t.PinName == pinName) return t.TargetBlockName;
        return "";
    }
}
