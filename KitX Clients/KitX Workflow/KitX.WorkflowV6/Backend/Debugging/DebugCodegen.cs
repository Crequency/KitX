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
    private HashSet<string> _helperNames = new(StringComparer.Ordinal);
    private int _pipeCounter;
    private readonly HashSet<string> _localNames = new(StringComparer.Ordinal);

    public DebugCodegen(BuiltinFunctionRegistry registry)
    {
        _registry = registry;
    }

    public string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger = false)
    {
        _pipeCounter = 0;
        _helperNames = new HashSet<string>(
            ir.HelperFunctions.Where(h => !string.IsNullOrEmpty(h.Name)).Select(h => h.Name!),
            StringComparer.Ordinal);

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
            case PipelineStatement p: GenPipeline(sb, p, pad, depth); break;
            case IfStatement iff: GenIf(sb, iff, pad, depth); break;
            case ForEachStatement fe: GenForEach(sb, fe, pad, depth); break;
            case WhileStatement ws: GenWhile(sb, ws, pad, depth); break;
            case SwitchStatement sw: GenSwitch(sb, sw, pad, depth); break;
            case BreakStatement: sb.AppendLine($"{pad}break;"); break;
            case ContinueStatement: sb.AppendLine($"{pad}continue;"); break;
        }
    }

    private void GenPipeline(StringBuilder sb, PipelineStatement p, string pad, int depth)
    {
        // Bare call: no segments, single KsCall source → this.Method(args);
        if (p.Segments.Length == 0 && p.Sources.Length == 1 && p.Sources[0] is KsCall call)
        {
            sb.AppendLine($"{pad}this.{MapMethodName(call.MethodName)}({string.Join(", ", call.Args.Select(RenderNode))});");
            return;
        }

        if (p.Segments.Length == 0)
        {
            sb.AppendLine($"{pad}/* bare expression: {RenderNode(p.Sources[0])} */");
            return;
        }

        string? currentVar = null;

        for (int i = 0; i < p.Segments.Length; i++)
        {
            var seg = p.Segments[i];
            string outputVar = $"__pipe_{_pipeCounter++}";
            bool isVarTap = seg.IsVariableTap
                         || (seg.Arguments.Length == 0
                             && !_registry.Contains(seg.Target)
                             && !_helperNames.Contains(seg.Target));

            if (isVarTap)
            {
                if (i == 0)
                {
                    var src = RenderNode(p.Sources[0]);
                    sb.AppendLine($"{pad}this.{seg.Target} = {src};");
                    sb.AppendLine($"{pad}var {outputVar} = {src};");
                }
                else
                {
                    sb.AppendLine($"{pad}this.{seg.Target} = {currentVar};");
                    sb.AppendLine($"{pad}var {outputVar} = {currentVar};");
                }
            }
            else
            {
                IEnumerable<string> inputs = i == 0
                    ? p.Sources.Select(RenderNode)
                    : [currentVar!];
                string args = BuildArgList(seg.Arguments, inputs);
                sb.AppendLine($"{pad}var {outputVar} = this.{MapMethodName(seg.Target)}({args});");
            }

            currentVar = outputVar;
        }
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
        _localNames.Add(fe.ItemName);
        GenBody(sb, fe.Body, pad.Length + 4, depth + 1);
        _localNames.Remove(fe.ItemName);
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

    private string RenderExpr(KsNode node) => node switch
    {
        KsCall c => $"this.{c.MethodName}({string.Join(", ", c.Args.Select(RenderArg))})",
        KsLiteral l => l.Value is string s ? $"\"{s}\"" : (l.Value?.ToString() ?? "null"),
        KsIdentifier id => _localNames.Contains(id.Name) ? id.Name : $"this.{id.Name}",
        _ => "false",
    };

    private string RenderArg(KsNode node) => node switch
    {
        KsLiteral l => l.Kind == KsLiteralKind.String ? $"\"{l.Value}\"" : (l.Value?.ToString() ?? "null"),
        KsIdentifier id => _localNames.Contains(id.Name) ? id.Name : $"this.{id.Name}",
        _ => "null",
    };

    // ── Pipeline helpers ──

    private string RenderNode(KsNode node) => node switch
    {
        KsLiteral lit => RenderLiteral(lit),
        KsIdentifier id => _localNames.Contains(id.Name) ? id.Name : $"this.{id.Name}",
        KsCall call => call.Args.Length == 0
            ? $"this.{MapMethodName(call.MethodName)}()"
            : $"this.{MapMethodName(call.MethodName)}({string.Join(", ", call.Args.Select(RenderNode))})",
        KsPlaceholder => "_",
        _ => "null",
    };

    private string RenderLiteral(KsLiteral lit) => lit.Kind switch
    {
        KsLiteralKind.String => $"\"{EscapeString(lit.Value?.ToString() ?? "")}\"",
        KsLiteralKind.Integer => lit.Value?.ToString() ?? "0",
        KsLiteralKind.Double => (lit.Value?.ToString() ?? "0.0") + "d",
        KsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        KsLiteralKind.Char => $"'{lit.Value}'",
        KsLiteralKind.Null => "null",
        _ => "null",
    };

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string MapMethodName(string name)
        => name == "StringConcat" ? "StringConcatMethod" : name;

    private bool IsKnownFunction(string name)
        => _registry.Contains(name) || _helperNames.Contains(name);

    private string BuildArgList(ImmutableArray<KsNode> args, IEnumerable<string> inputs)
    {
        var queue = new Queue<string>(inputs);
        var result = new List<string>();
        foreach (var arg in args)
        {
            if (arg is KsPlaceholder)
                result.Add(queue.Count > 0 ? queue.Dequeue() : "null");
            else
                result.Add(RenderNode(arg));
        }
        while (queue.Count > 0)
            result.Add(queue.Dequeue());
        return string.Join(", ", result);
    }
}