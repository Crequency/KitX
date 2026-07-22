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
        switch (stmt)
        {
            case PipelineStatement p:
                sb.Append(Indent(level)).Append(RenderPipeline(p)).Append('\n');
                break;

            case IfStatement iff:
                sb.Append(Indent(level)).Append("if ").Append(RenderBsNode(iff.Condition)).Append('\n');
                RenderBody(sb, iff.ThenBody, level + 1);
                if (iff.ElseBody.Length > 0)
                {
                    // If the else body is a single nested IfStatement, render as `else if`.
                    if (iff.ElseBody.Length == 1 && iff.ElseBody[0] is IfStatement nested)
                    {
                        sb.Append(Indent(level)).Append("else if ").Append(RenderBsNode(nested.Condition)).Append('\n');
                        RenderBody(sb, nested.ThenBody, level + 1);
                        if (nested.ElseBody.Length > 0)
                        {
                            sb.Append(Indent(level)).Append("else").Append('\n');
                            RenderBody(sb, nested.ElseBody, level + 1);
                        }
                    }
                    else
                    {
                        sb.Append(Indent(level)).Append("else").Append('\n');
                        RenderBody(sb, iff.ElseBody, level + 1);
                    }
                }
                break;

            case SwitchStatement sw:
                sb.Append(Indent(level)).Append("switch ").Append(RenderBsNode(sw.Selector)).Append('\n');
                for (int i = 0; i < sw.Arms.Length; i++)
                {
                    sb.Append(Indent(level + 1)).Append(i).Append(": ").Append('\n');
                    RenderBody(sb, sw.Arms[i], level + 2);
                }
                if (sw.Default.Length > 0)
                {
                    sb.Append(Indent(level + 1)).Append("default:").Append('\n');
                    RenderBody(sb, sw.Default, level + 2);
                }
                break;

            case ForEachStatement fe:
                sb.Append(Indent(level))
                  .Append("forEach ").Append(RenderBsNode(fe.Source))
                  .Append(" as ").Append(fe.ItemName).Append('\n');
                RenderBody(sb, fe.Body, level + 1);
                break;

            case WhileStatement ws:
                sb.Append(Indent(level)).Append("while ").Append(RenderBsNode(ws.Condition)).Append('\n');
                RenderBody(sb, ws.Body, level + 1);
                break;

            case BreakStatement:
                sb.Append(Indent(level)).Append("break").Append('\n');
                break;

            case ContinueStatement:
                sb.Append(Indent(level)).Append("continue").Append('\n');
                break;

            case ExitStatement:
                sb.Append(Indent(level)).Append("exit").Append('\n');
                break;

            default:
                // Unknown statement — emit a placeholder so rendering never silently drops.
                sb.Append(Indent(level)).Append($"/* unknown: {stmt.Kind} */").Append('\n');
                break;
        }
    }

    private static string RenderPipeline(PipelineStatement p)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(", ", p.Sources.Select(RenderBsNode)));
        foreach (var seg in p.Segments)
        {
            sb.Append(" > ");
            if (seg.IsVariableTap)
            {
                sb.Append(seg.Target);
            }
            else if (seg.Arguments.Length == 0)
            {
                // Bare `> Func` — the pipeline value is the implicit single arg.
                sb.Append(seg.Target);
            }
            else
            {
                sb.Append(seg.Target).Append('(');
                sb.Append(string.Join(", ", seg.Arguments.Select(RenderBsNode)));
                sb.Append(')');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Renders a KsNode expression. Uses <see cref="KsNode.SourceText"/> when available
    /// (lossless round-trip); otherwise falls back to structural rendering.
    /// </summary>
    private static string RenderBsNode(KsNode node) => node switch
    {
        KsLiteral lit => RenderLiteral(lit),
        KsIdentifier id => id.Name,
        KsCall call => $"{call.MethodName}({string.Join(", ", call.Args.Select(RenderBsNode))})",
        KsPipeline pipe => pipe.RenderPipelineSource(),
        KsPipelineSegment seg => seg.IsVariableTap
            ? seg.Target
            : $"{seg.Target}({string.Join(", ", seg.Args.Select(RenderBsNode))})",
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