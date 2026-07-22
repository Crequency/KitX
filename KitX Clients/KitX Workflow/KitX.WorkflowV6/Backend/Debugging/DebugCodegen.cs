namespace KitX.WorkflowV6.Backend.Debugging;

using System.Text;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// DebugCodegen — wraps StructuredCodegen to insert checkpoint calls.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DebugCodegen
{
    private readonly BuiltinFunctionRegistry _registry;

    public DebugCodegen(BuiltinFunctionRegistry registry) => _registry = registry;

    public string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger)
    {
        if (!hasDebugger)
            return new RoslynBackend.StructuredCodegen(_registry).Generate(ir, lowering);

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using KitX.WorkflowV6.Backend.Runtime;");
        sb.AppendLine();
        sb.AppendLine("namespace KitX.WorkflowV6.Generated;");
        sb.AppendLine();
        sb.AppendLine("public sealed class G : ExecutionGlobals");
        sb.AppendLine("{");
        if (lowering is not null)
            foreach (var (name, type) in lowering.PubVarTypes)
                sb.AppendLine($"    public {type} {name};");
        sb.AppendLine();
        sb.AppendLine("    public void RunAsync()");
        sb.AppendLine("    {");
        GenBody(sb, ir.Body, 8, 0);
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void GenBody(StringBuilder sb, ImmutableArray<Statement> body, int indent, int depth)
    {
        var pad = new string(' ', indent);
        for (int i = 0; i < body.Length; i++)
            GenStmt(sb, body[i], pad, i, depth);
    }

    private void GenStmt(StringBuilder sb, Statement stmt, string pad, int ordinal, int depth)
    {
        var stmtId = Fingerprint.DeriveStableId($"/stmt/{ordinal}", stmt.Fingerprint, depth);
        sb.AppendLine($"{pad}this.Checkpoint(\"{stmtId}\", \"/stmt/{ordinal}\");");

        switch (stmt)
        {
            case PipelineStatement p: GenPipeline(sb, p, pad); break;
            case IfStatement iff: GenIf(sb, iff, pad, depth); break;
            case ForEachStatement fe: GenForEach(sb, fe, pad, depth); break;
            case WhileStatement ws: GenWhile(sb, ws, pad, depth); break;
            case SwitchStatement sw: GenSwitch(sb, sw, pad, depth); break;
            case BreakStatement: sb.AppendLine($"{pad}break;"); break;
            case ContinueStatement: sb.AppendLine($"{pad}continue;"); break;
            case ExitStatement: sb.AppendLine($"{pad}return;"); break;
        }
    }

    private void GenPipeline(StringBuilder sb, PipelineStatement p, string pad)
    {
        if (p.Sources.Length == 1 && p.Sources[0] is BsCall call)
        {
            var args = string.Join(", ", call.Args.Select(RenderArg));
            sb.AppendLine($"{pad}this.{call.MethodName}({args});");
        }
        else sb.AppendLine($"{pad}/* pipeline */");
    }

    private void GenIf(StringBuilder sb, IfStatement iff, string pad, int depth)
    {
        sb.AppendLine($"{pad}if ({RenderExpr(iff.Condition)})");
        sb.AppendLine($"{pad}{{");
        GenBody(sb, iff.ThenBody, pad.Length + 4, depth + 1);
        if (iff.ElseBody.Length > 0)
        {
            sb.AppendLine($"{pad}}} else {{");
            GenBody(sb, iff.ElseBody, pad.Length + 4, depth + 1);
        }
        sb.AppendLine($"{pad}}}");
    }

    private void GenForEach(StringBuilder sb, ForEachStatement fe, string pad, int depth)
    {
        sb.AppendLine($"{pad}foreach (var {fe.ItemName} in {RenderExpr(fe.Source)})");
        sb.AppendLine($"{pad}{{");
        GenBody(sb, fe.Body, pad.Length + 4, depth + 1);
        sb.AppendLine($"{pad}}}");
    }

    private void GenWhile(StringBuilder sb, WhileStatement ws, string pad, int depth)
    {
        sb.AppendLine($"{pad}while ({RenderExpr(ws.Condition)})");
        sb.AppendLine($"{pad}{{");
        GenBody(sb, ws.Body, pad.Length + 4, depth + 1);
        sb.AppendLine($"{pad}}}");
    }

    private void GenSwitch(StringBuilder sb, SwitchStatement sw, string pad, int depth)
    {
        sb.AppendLine($"{pad}switch ({RenderExpr(sw.Selector)})");
        sb.AppendLine($"{pad}{{");
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            sb.AppendLine($"{pad}case {i}:");
            sb.AppendLine($"{pad}{{");
            GenBody(sb, sw.Arms[i], pad.Length + 4, depth + 1);
            sb.AppendLine($"{pad}    break;");
            sb.AppendLine($"{pad}}}");
        }
        if (sw.Default.Length > 0)
        {
            sb.AppendLine($"{pad}default:");
            sb.AppendLine($"{pad}{{");
            GenBody(sb, sw.Default, pad.Length + 4, depth + 1);
            sb.AppendLine($"{pad}    break;");
            sb.AppendLine($"{pad}}}");
        }
        sb.AppendLine($"{pad}}}");
    }

    private string RenderExpr(BsNode node) => node switch
    {
        BsCall c => $"this.{c.MethodName}({string.Join(", ", c.Args.Select(RenderArg))})",
        BsLiteral l => l.Value is string s ? $"\"{s}\"" : (l.Value?.ToString() ?? "null"),
        BsIdentifier id => $"this.{id.Name}",
        _ => "false",
    };

    private string RenderArg(BsNode node) => node switch
    {
        BsLiteral l => l.Kind == BsLiteralKind.String ? $"\"{l.Value}\"" : (l.Value?.ToString() ?? "null"),
        BsIdentifier id => $"this.{id.Name}",
        _ => "null",
    };
}