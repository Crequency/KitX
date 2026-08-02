namespace KitX.WorkflowV6.Lens.KsTextLens;

using System.Text;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// KsRenderer — immutable Workflow → indented KS source text.
//
// The v6 indented renderer walks the structured Statement tree and emits text with
// 4-space indentation per level (discussion notes §十二-A). Control-flow
// statements (if/switch/forEach/while) render their keyword + condition on one
// line, then their bodies on indented lines, then `else` (if any) on a dedented
// line — mirroring the parser's grammar exactly so the round-trip is idempotent.
//
// Conditions and selectors are rendered from the structured KsNode AST (the
// <see cref="KsNode.SourceText"/> field carries the verbatim source form, so
// rendering is just string concatenation — no re-formatting needed).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Renders an immutable <see cref="Workflow"/> to indented KS source text. Pure:
/// the same IR always yields the same text, and the IR is not mutated.
/// </summary>
internal sealed class KsRenderer
{
    private const int IndentWidth = 4;

    /// <summary>Renders the full KS document: const/var blocks then the top-level body.</summary>
    public string Render(Workflow ir)
    {
        var sb = new StringBuilder();

        // ── const { ... } ──
        if (ir.Constants.Count > 0)
        {
            sb.Append("const {").Append('\n');
            foreach (var c in ir.Constants.Values)
                sb.Append(Indent(1)).Append(RenderConstant(c)).Append('\n');
            sb.Append('}').Append('\n');
        }

        // ── var { ... } ──
        if (ir.GlobalVars.Count > 0)
        {
            sb.Append("var {").Append('\n');
            foreach (var g in ir.GlobalVars.Values)
                sb.Append(Indent(1)).Append(RenderGlobalVar(g)).Append('\n');
            sb.Append('}').Append('\n');
        }

        // ── top-level body ──
        RenderBody(sb, ir.Body, 0);

        // Trim trailing whitespace and ensure single trailing newline.
        var text = sb.ToString().TrimEnd();
        return text + "\n";
    }

    private static string RenderConstant(Constant c) =>
        $"{c.Type} {c.Name}{RenderDeclInit(c.DictInitializer, c.InitialValueExpression)}";

    private static string RenderGlobalVar(GlobalVar g) =>
        $"{g.Type} {g.Name}{RenderDeclInit(g.DictInitializer, g.InitialValueExpression)}";

    /// <summary>
    /// Renders the <c>= &lt;initialiser&gt;</c> suffix for a declaration row: a dict literal
    /// when <paramref name="dictInit"/> is set, else the legacy verbatim expression text.
    /// </summary>
    private static string RenderDeclInit(KsDictLiteral? dictInit, string? initialValueExpression)
    {
        if (dictInit is { } dl) return " = " + RenderDictLiteral(dl);
        return initialValueExpression is null ? "" : " = " + initialValueExpression;
    }

    /// <summary>Renders a KsDictLiteral as KS source text <c>{k: v, ...}</c>.</summary>
    private static string RenderDictLiteral(KsDictLiteral dict)
    {
        var entries = dict.Entries.Select(e => $"{RenderKsNode(e.Key)}: {RenderKsNode(e.Value)}");
        return "{" + string.Join(", ", entries) + "}";
    }

    private void RenderBody(StringBuilder sb, ImmutableArray<Statement> body, int level)
    {
        foreach (var s in body)
            RenderStatement(sb, s, level);
    }

    private void RenderStatement(StringBuilder sb, Statement stmt, int level)
    {
        RenderLeadingComments(sb, stmt, level);
        switch (stmt)
        {
            case PipelineStatement p:
                RenderPipelineStmt(sb, p, level);
                break;

            case IfStatement iff:
                RenderControlFlowHeader(sb, level, "if", iff.Condition, trailing: iff.TrailingComment);
                RenderBody(sb, iff.ThenBody, level + 1);
                if (iff.ElseBody.Length > 0)
                {
                    // Always render `else:` with a nested body — the nested-if form is
                    // preserved verbatim (a nested IfStatement in the else body renders as
                    // an indented `if ...:` block, NOT the `else if` sugar). This keeps the
                    // round-trip text structurally identical to the source.
                    sb.Append(Indent(level)).Append("else:\n");
                    RenderBody(sb, iff.ElseBody, level + 1);
                }
                break;

            case SwitchStatement sw:
                sb.Append(Indent(level)).Append("switch ").Append(RenderKsNode(sw.Selector)).Append(":\n");
                for (int i = 0; i < sw.Arms.Length; i++)
                {
                    var label = i < sw.ArmLabels.Length ? sw.ArmLabels[i] : i;
                    sb.Append(Indent(level + 1)).Append(label).Append(": ").Append('\n');
                    RenderBody(sb, sw.Arms[i], level + 2);
                }
                if (sw.Default.Length > 0)
                {
                    sb.Append(Indent(level + 1)).Append("default:").Append('\n');
                    RenderBody(sb, sw.Default, level + 2);
                }
                break;

            case ForEachStatement fe:
                RenderControlFlowHeader(sb, level, "forEach", fe.Source, " as " + fe.ItemName, fe.TrailingComment);
                RenderBody(sb, fe.Body, level + 1);
                break;

            case WhileStatement ws:
                RenderControlFlowHeader(sb, level, "while", ws.Condition, trailing: ws.TrailingComment);
                RenderBody(sb, ws.Body, level + 1);
                break;

            case BreakStatement:
                sb.Append(Indent(level)).Append("break");
                AppendTrailing(sb, stmt.TrailingComment);
                sb.Append('\n');
                break;

            case ContinueStatement:
                sb.Append(Indent(level)).Append("continue");
                AppendTrailing(sb, stmt.TrailingComment);
                sb.Append('\n');
                break;

            default:
                // Unknown statement — emit a placeholder so rendering never silently drops.
                sb.Append(Indent(level)).Append($"/* unknown: {stmt.Kind} */").Append('\n');
                break;
        }
    }

    /// <summary>Emits full-line leading comments above a statement, one <c>//</c> line each.</summary>
    private static void RenderLeadingComments(StringBuilder sb, Statement stmt, int level)
    {
        if (stmt.LeadingComment is { Length: > 0 } lc)
        {
            foreach (var line in lc.Split('\n'))
            {
                sb.Append(Indent(level));
                if (line.Length == 0) sb.Append("//");
                else sb.Append("// ").Append(line);
                sb.Append('\n');
            }
        }
    }

    /// <summary>Appends an inline trailing comment (<c> // cmt</c>) when non-null/non-empty.</summary>
    private static void AppendTrailing(StringBuilder sb, string? trailing)
    {
        if (trailing is { Length: > 0 })
            sb.Append(" // ").Append(trailing);
    }

    /// <summary>
    /// Renders a pipeline statement. When any source or segment carries a comment, the
    /// multi-line form is used with comment-driven line folding: contiguous elements
    /// WITHOUT comments share a line; an element WITH a comment terminates its line
    /// (the inline comment sits at that line's end), and following elements continue on
    /// a new line (indent + 1). The statement's TrailingComment lands on the LAST source
    /// line (Parser capture point A reads it back); a source comment there would
    /// conflict and the trailing comment is dropped (rare edge). Otherwise renders the
    /// compact single-line form.
    /// </summary>
    private void RenderPipelineStmt(StringBuilder sb, PipelineStatement p, int level)
    {
        bool multiline = p.Sources.Any(s => s.Comment is { Length: > 0 })
                      || p.Segments.Any(s => s.Comment is { Length: > 0 });
        if (multiline)
        {
            RenderSourceBlock(sb, p.Sources, level, prependFirstIndent: true, trailingComment: p.TrailingComment);

            // Segment block: contiguous comment-free segments share a line; a commented
            // segment terminates its line (inline comment at line end).
            for (int i = 0; i < p.Segments.Length; i++)
            {
                var seg = p.Segments[i];
                if (i == 0 || p.Segments[i - 1].Comment is { Length: > 0 })
                    sb.Append(Indent(level + 1)).Append("> ");
                else
                    sb.Append(" > ");
                sb.Append(RenderSegmentText(seg));
                AppendTrailing(sb, seg.Comment);
                if (seg.Comment is { Length: > 0 })
                    sb.Append('\n');
            }
            if (p.Segments.Length == 0 || p.Segments[^1].Comment is not { Length: > 0 })
                sb.Append('\n');
        }
        else
        {
            sb.Append(Indent(level)).Append(RenderPipelineSingleLine(p));
            AppendTrailing(sb, p.TrailingComment);
            sb.Append('\n');
        }
    }

    /// <summary>
    /// Renders a source list with comment-driven line folding: the first source starts
    /// the line (indented unless <paramref name="prependFirstIndent"/> is false — the
    /// control-flow header already wrote "keyword "); a commented source terminates its
    /// line; following sources continue on the same line with ", " or a new line at
    /// indent + 1 after a commented predecessor. The statement TrailingComment attaches
    /// to the LAST source line (dropped if that line already carries a source comment).
    /// </summary>
    private static void RenderSourceBlock(StringBuilder sb, ImmutableArray<KsNode> sources, int level,
        bool prependFirstIndent, string? trailingComment)
    {
        for (int i = 0; i < sources.Length; i++)
        {
            var src = sources[i];
            bool hasNext = i < sources.Length - 1;
            if (i == 0)
            {
                if (prependFirstIndent)
                    sb.Append(Indent(level));
            }
            else if (sources[i - 1].Comment is { Length: > 0 })
                sb.Append(Indent(level + 1));
            else
                sb.Append(' ');
            sb.Append(RenderKsNode(src));
            // Source separator comma sits BEFORE the source's inline comment: `a, // cmt`.
            if (hasNext)
                sb.Append(',');
            AppendTrailing(sb, src.Comment);
            if (src.Comment is { Length: > 0 })
                sb.Append('\n');
        }
        // TrailingComment: only when the last source line is free of a source comment.
        if (sources.Length > 0 && sources[^1].Comment is not { Length: > 0 })
            AppendTrailing(sb, trailingComment);
        if (sources.Length == 0 || sources[^1].Comment is not { Length: > 0 })
            sb.Append('\n');
    }

    /// <summary>
    /// Renders an IR Segment (from the lowered <see cref="PipelineStatement"/>).
    /// Not merged with <see cref="RenderAstSegmentText"/> because IR
    /// <see cref="Segment.Arguments"/> and AST <see cref="KsPipelineSegment.Args"/>
    /// are different property names on unrelated types — no common interface exists.
    /// </summary>
    private static string RenderSegmentText(Segment seg)
    {
        if (seg.IsVariableTap || seg.Arguments.Length == 0)
            return seg.Target;  // variable tap, or bare `> Func` (implicit single arg)
        return $"{seg.Target}({string.Join(", ", seg.Arguments.Select(RenderKsNode))})";
    }

    private static string RenderPipelineSingleLine(PipelineStatement p)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(", ", p.Sources.Select(RenderKsNode)));
        foreach (var seg in p.Segments)
            sb.Append(" > ").Append(RenderSegmentText(seg));
        return sb.ToString();
    }

    /// <summary>
    /// Renders a control-flow header line: <c>keyword &lt;condition&gt;:</c> or, when
    /// any intermediate condition segment carries a comment, the multi-line form. The
    /// last segment's comment (post-colon) follows the colon on the header's final line.
    /// <paramref name="suffix"/> (e.g. " as i" for forEach) is appended to the last
    /// segment before the colon.
    /// </summary>
    private void RenderControlFlowHeader(StringBuilder sb, int level, string keyword, KsNode cond, string suffix = "", string? trailing = null)
    {
        sb.Append(Indent(level)).Append(keyword).Append(' ');
        RenderControlFlowHeaderInline(sb, level, keyword, cond, suffix, trailing);
    }

    /// <summary>Inline portion of a control-flow header (after the leading "keyword ").</summary>
    private void RenderControlFlowHeaderInline(StringBuilder sb, int level, string keyword, KsNode cond, string suffix = "", string? trailing = null)
    {
        if (cond is KsPipeline pipe && NeedsMultiLineHeader(pipe))
        {
            // Multi-line condition: source block (keyword already written on the first
            // line) + segment block with comment-driven folding. The last segment's line
            // ends with the suffix (forEach "as i"), the ':', and its inline comment.
            RenderSourceBlock(sb, pipe.Sources, level, prependFirstIndent: false, trailingComment: null);
            int lastIdx = pipe.Segments.Length - 1;
            for (int i = 0; i < pipe.Segments.Length; i++)
            {
                var seg = pipe.Segments[i];
                if (i == 0 || pipe.Segments[i - 1].Comment is { Length: > 0 })
                    sb.Append(Indent(level + 1)).Append("> ");
                else
                    sb.Append(" > ");
                sb.Append(RenderAstSegmentText(seg));
                if (i == lastIdx)
                {
                    sb.Append(suffix).Append(':');
                    AppendTrailing(sb, seg.Comment);
                }
                else
                {
                    AppendTrailing(sb, seg.Comment);
                    if (seg.Comment is { Length: > 0 })
                        sb.Append('\n');
                }
            }
            if (pipe.Segments[^1].Comment is not { Length: > 0 })
                sb.Append('\n');
        }
        else
        {
            // Single-line header: keyword + condition + suffix + ':' [+ comment].
            // For a pipeline condition, the last segment's inline comment follows ':'.
            // For a simple condition, the statement's TrailingComment follows ':'.
            sb.Append(RenderKsNode(cond)).Append(suffix).Append(':');
            if (cond is KsPipeline p && p.Segments.Length > 0)
                AppendTrailing(sb, p.Segments[^1].Comment);
            else
                AppendTrailing(sb, trailing);
            sb.Append('\n');
        }
    }

    /// <summary>
    /// True when a pipeline header needs the multi-line form: a SOURCE comment or an
    /// INTERMEDIATE segment comment. A lone last-segment comment stays single-line
    /// (it renders post-colon: <c>if a &gt; FA: // cmt</c>).
    /// </summary>
    private static bool NeedsMultiLineHeader(KsPipeline pipe)
        => pipe.Sources.Any(s => s.Comment is { Length: > 0 })
        || (pipe.Segments.Length > 1
            && pipe.Segments.Take(pipe.Segments.Length - 1).Any(s => s.Comment is { Length: > 0 }));

    /// <summary>
    /// Renders an AST KsPipelineSegment (from the <see cref="KsPipeline"/> AST).
    /// Not merged with <see cref="RenderSegmentText"/> because AST
    /// <see cref="KsPipelineSegment.Args"/> and IR <see cref="Segment.Arguments"/>
    /// are different property names on unrelated types — no common interface exists.
    /// </summary>
    private static string RenderAstSegmentText(KsPipelineSegment seg)
    {
        if (seg.IsVariableTap || seg.Args.Length == 0)
            return seg.Target;
        return $"{seg.Target}({string.Join(", ", seg.Args.Select(RenderKsNode))})";
    }

    /// <summary>
    /// Renders a KsNode expression. Uses <see cref="KsNode.SourceText"/> when available
    /// (lossless round-trip); otherwise falls back to structural rendering.
    /// </summary>
    private static string RenderKsNode(KsNode node) => node switch
    {
        KsLiteral lit => RenderLiteral(lit),
        KsIdentifier id => id.Name,
        KsCall call => $"{call.MethodName}({string.Join(", ", call.Args.Select(RenderKsNode))})",
        KsPipeline pipe => pipe.RenderPipelineSource(),
        KsPipelineSegment seg => seg.IsVariableTap
            ? seg.Target
            : $"{seg.Target}({string.Join(", ", seg.Args.Select(RenderKsNode))})",
        KsPlaceholder => "_",
        _ => node.SourceText.Length > 0 ? node.SourceText : node.GetType().Name,
    };

    private static string RenderLiteral(KsLiteral lit) => lit.Kind switch
    {
        KsLiteralKind.String => $"\"{lit.Value}\"",
        KsLiteralKind.Integer => lit.Value?.ToString() ?? "0",
        KsLiteralKind.Double => lit.Value?.ToString() ?? "0.0",
        KsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        KsLiteralKind.Char => $"'{lit.Value}'",
        KsLiteralKind.Null => "null",
        _ => lit.SourceText,
    };

    private static string Indent(int level) => new(' ', level * IndentWidth);
}