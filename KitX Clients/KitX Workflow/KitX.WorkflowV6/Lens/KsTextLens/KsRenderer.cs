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
        $"{c.Type} {c.Name}{(c.InitialValueExpression is null ? "" : " = " + c.InitialValueExpression)}";

    private static string RenderGlobalVar(GlobalVar g) =>
        $"{g.Type} {g.Name}{(g.InitialValueExpression is null ? "" : " = " + g.InitialValueExpression)}";

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
                    if (iff.ElseBody.Length == 1 && iff.ElseBody[0] is IfStatement nested
                        && nested.LeadingComment is null)
                    {
                        sb.Append(Indent(level)).Append("else ");
                        RenderControlFlowHeaderInline(sb, level, "if", nested.Condition, trailing: nested.TrailingComment);
                        RenderBody(sb, nested.ThenBody, level + 1);
                        if (nested.ElseBody.Length > 0)
                        {
                            sb.Append(Indent(level)).Append("else:\n");
                            RenderBody(sb, nested.ElseBody, level + 1);
                        }
                    }
                    else
                    {
                        sb.Append(Indent(level)).Append("else:\n");
                        RenderBody(sb, iff.ElseBody, level + 1);
                    }
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
    /// Renders a pipeline statement. When any segment carries a comment, renders the
    /// multi-line form (sources on the first line, each segment on its own indented
    /// continuation line) so per-segment comments can attach. Otherwise renders the
    /// compact single-line form.
    /// </summary>
    private void RenderPipelineStmt(StringBuilder sb, PipelineStatement p, int level)
    {
        bool multiline = false;
        foreach (var s in p.Segments)
            if (s.Comment is { Length: > 0 }) { multiline = true; break; }

        if (multiline)
        {
            // Sources line (+ optional source-line trailing comment).
            sb.Append(Indent(level)).Append(string.Join(", ", p.Sources.Select(RenderKsNode)));
            AppendTrailing(sb, p.TrailingComment);
            sb.Append('\n');
            // Each segment on its own indented continuation line.
            foreach (var seg in p.Segments)
            {
                sb.Append(Indent(level + 1)).Append("> ").Append(RenderSegmentText(seg));
                AppendTrailing(sb, seg.Comment);
                sb.Append('\n');
            }
        }
        else
        {
            sb.Append(Indent(level)).Append(RenderPipelineSingleLine(p));
            AppendTrailing(sb, p.TrailingComment);
            sb.Append('\n');
        }
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

    /// <summary>Inline portion (after the leading "keyword ") — also used by `else if`.</summary>
    private void RenderControlFlowHeaderInline(StringBuilder sb, int level, string keyword, KsNode cond, string suffix = "", string? trailing = null)
    {
        if (cond is KsPipeline pipe && pipe.Segments.Length > 1 && HasIntermediateSegComment(pipe))
        {
            // Multi-line condition: sources on the first line, each segment on its own
            // indented continuation line. The last segment's line ends with the suffix
            // (forEach "as i"), the ':', and the last segment's inline comment.
            sb.Append(string.Join(", ", pipe.Sources.Select(RenderKsNode))).Append('\n');
            int lastIdx = pipe.Segments.Length - 1;
            for (int i = 0; i < pipe.Segments.Length; i++)
            {
                var seg = pipe.Segments[i];
                sb.Append(Indent(level + 1)).Append("> ").Append(RenderAstSegmentText(seg));
                if (i == lastIdx)
                {
                    sb.Append(suffix).Append(':');
                    AppendTrailing(sb, seg.Comment);
                }
                else
                {
                    AppendTrailing(sb, seg.Comment);
                }
                sb.Append('\n');
            }
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

    private static bool HasIntermediateSegComment(KsPipeline pipe)
    {
        for (int i = 0; i < pipe.Segments.Length - 1; i++)
            if (pipe.Segments[i].Comment is { Length: > 0 }) return true;
        return false;
    }

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