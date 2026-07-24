namespace KitX.WorkflowV6.Backend.Debugging;

using System.Text;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Backend.RoslynBackend;

internal sealed class DebugCodegen : CodegenBase
{
    private readonly StructuredCodegen? _structured;

    public DebugCodegen(BuiltinFunctionRegistry registry) : base(registry)
    {
        _structured = new StructuredCodegen(registry);
    }

    public override string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger = false)
    {
        _pipeCounter = 0;
        _ir = ir;  // set in all paths so RenderIdentifier etc. work consistently
        _helperNames = new HashSet<string>(
            ir.HelperFunctions.Where(h => !string.IsNullOrEmpty(h.Name)).Select(h => h.Name!),
            StringComparer.Ordinal);

        if (!hasDebugger)
            return _structured!.Generate(ir, lowering, false);

        _sb.Clear();
        _indent = 0;
        EmitClassHeader(ir, lowering);
        EmitLine("public void RunAsync()");
        EmitLine("{");
        Indent();
        EmitBody(ir.Body, 0);
        Dedent();
        EmitLine("}");
        EmitClassFooter();
        return _sb.ToString();
    }

    private void EmitBody(ImmutableArray<Statement> body, int depth)
    {
        for (int i = 0; i < body.Length; i++)
            EmitStatement(body[i], i, depth);
    }

    private void EmitStatement(Statement stmt, int ordinal, int depth)
    {
        var stmtId = Fingerprint.DeriveStableId($"/stmt/{ordinal}", stmt.Fingerprint, depth);
        EmitCheckpoint(stmtId, $"/stmt/{ordinal}", ordinal);

        switch (stmt)
        {
            case PipelineStatement p: EmitPipeline(p, ordinal, depth); break;
            case IfStatement iff: EmitIf(iff, depth); break;
            case ForEachStatement fe: EmitForEach(fe, depth); break;
            case WhileStatement ws: EmitWhile(ws, depth); break;
            case SwitchStatement sw: EmitSwitch(sw, depth); break;
            case BreakStatement: EmitLine("break;"); break;
            case ContinueStatement: EmitLine("continue;"); break;
        }
    }

    protected override void EmitCheckpoint(string stmtId, string lexicalPath, int ordinal)
    {
        EmitLine($"this.Checkpoint(\"{stmtId}\", \"{lexicalPath}\");");
    }

    protected override void EmitPipeline(PipelineStatement p, int ordinal, int depth)
    {
        if (p.Segments.Length == 0 && p.Sources.Length == 1 && p.Sources[0] is KsCall call)
        {
            EmitLine($"this.{MapMethodName(call.MethodName)}({string.Join(", ", call.Args.Select(RenderKsNode))});");
            return;
        }

        if (p.Segments.Length == 0)
        {
            EmitLine($"/* bare expression: {RenderKsNode(p.Sources[0])} */");
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
                    var src = RenderKsNode(p.Sources[0]);
                    EmitLine($"this.{seg.Target} = {src};");
                    EmitLine($"var {outputVar} = {src};");
                }
                else
                {
                    EmitLine($"this.{seg.Target} = {currentVar};");
                    EmitLine($"var {outputVar} = {currentVar};");
                }
            }
            else
            {
                IEnumerable<string> inputs = i == 0
                    ? p.Sources.Select(RenderKsNode)
                    : [currentVar!];
                string args = BuildArgList(seg.Arguments, inputs);
                EmitLine($"var {outputVar} = this.{MapMethodName(seg.Target)}({args});");
            }

            currentVar = outputVar;
        }
    }

    private void EmitIf(IfStatement iff, int depth)
    {
        EmitLine($"if ({RenderKsNode(iff.Condition)})");
        EmitLine("{");
        Indent();
        EmitBody(iff.ThenBody, depth + 1);
        Dedent();
        if (iff.ElseBody.Length > 0)
        {
            EmitLine("} else {");
            Indent();
            EmitBody(iff.ElseBody, depth + 1);
            Dedent();
        }
        EmitLine("}");
    }

    private void EmitForEach(ForEachStatement fe, int depth)
    {
        EmitLine($"foreach (var {fe.ItemName} in {RenderForEachSource(fe.Source)})");
        EmitLine("{");
        Indent();
        PushLocal(fe.ItemName);
        EmitBody(fe.Body, depth + 1);
        PopLocal(fe.ItemName);
        Dedent();
        EmitLine("}");
    }

    private void EmitWhile(WhileStatement ws, int depth)
    {
        EmitLine($"while ({RenderKsNode(ws.Condition)})");
        EmitLine("{");
        Indent();
        EmitBody(ws.Body, depth + 1);
        Dedent();
        EmitLine("}");
    }

    private void EmitSwitch(SwitchStatement sw, int depth)
    {
        EmitLine($"switch ({RenderKsNode(sw.Selector)})");
        EmitLine("{");
        Indent();
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            EmitLine($"case {i}:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Arms[i], depth + 1);
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        if (sw.Default.Length > 0)
        {
            EmitLine("default:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Default, depth + 1);
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        Dedent();
        EmitLine("}");
    }
}
