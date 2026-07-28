namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Text;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

internal sealed class StructuredCodegen : CodegenBase
{
    public StructuredCodegen(BuiltinFunctionRegistry registry) : base(registry) { }

    public override string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger = false)
    {
        _ir = ir;
        _helperNames = new HashSet<string>(
            ir.HelperFunctions.Where(h => !string.IsNullOrEmpty(h.Name)).Select(h => h.Name!),
            StringComparer.Ordinal);
        _sb.Clear();
        _indent = 0;
        EmitClassHeader(ir, lowering);
        EmitLine("public void RunAsync()");
        EmitLine("{");
        Indent();
        EmitBody(ir.Body);
        Dedent();
        EmitLine("}");
        EmitHelperFunctions(ir);
        EmitClassFooter();
        return _sb.ToString();
    }

    private void EmitHelperFunctions(Workflow ir)
    {
        if (ir.HelperFunctions.IsDefault || ir.HelperFunctions.Length == 0) return;
        EmitLine("");
        foreach (var func in ir.HelperFunctions)
        {
            var paramList = string.Join(", ",
                func.Parameters.Select(p => $"{p.Type} {p.Name}"));
            EmitLine($"public {func.ReturnType} {func.Name}({paramList})");
            EmitLine("{");
            Indent();
            if (!string.IsNullOrWhiteSpace(func.Code))
            {
                foreach (var codeLine in func.Code.Split('\n'))
                    EmitLine(codeLine.TrimEnd());
            }
            else
            {
                EmitLine($"return default({func.ReturnType});");
            }
            Dedent();
            EmitLine("}");
            EmitLine("");
        }
    }

    private void EmitBody(ImmutableArray<Statement> body)
    {
        foreach (var s in body)
            EmitStatement(s);
    }

    private void EmitStatement(Statement stmt)
    {
        EmitCheckpoint("", "", 0);
        switch (stmt)
        {
            case PipelineStatement p:
                EmitPipeline(p, 0, 0);
                break;
            case IfStatement iff:
                EmitLine($"if ({RenderKsNode(iff.Condition)})");
                EmitLine("{");
                Indent();
                EmitBody(iff.ThenBody);
                Dedent();
                if (iff.ElseBody.Length > 0)
                {
                    EmitLine("} else {");
                    Indent();
                    EmitBody(iff.ElseBody);
                    Dedent();
                }
                EmitLine("}");
                break;
            case ForEachStatement fe:
                EmitLine($"foreach (var {fe.ItemName} in {RenderForEachSource(fe.Source)})");
                EmitLine("{");
                Indent();
                PushLocal(fe.ItemName);
                EmitBody(fe.Body);
                PopLocal(fe.ItemName);
                Dedent();
                EmitLine("}");
                break;
            case WhileStatement ws:
                EmitLine($"while ({RenderKsNode(ws.Condition)})");
                EmitLine("{");
                Indent();
                EmitBody(ws.Body);
                Dedent();
                EmitLine("}");
                break;
            case SwitchStatement sw:
                EmitSwitch(sw);
                break;
            case BreakStatement:
                EmitLine("break;");
                break;
            case ContinueStatement:
                EmitLine("continue;");
                break;
            default:
                EmitLine($"/* unknown statement kind: {stmt.Kind} */");
                break;
        }
    }

    private void EmitSwitch(SwitchStatement sw)
    {
        EmitLine($"switch ({RenderKsNode(sw.Selector)})");
        EmitLine("{");
        Indent();
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            var label = i < sw.ArmLabels.Length ? sw.ArmLabels[i] : i;
            EmitLine($"case {label}:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Arms[i]);
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        if (sw.Default.Length > 0)
        {
            EmitLine("default:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Default);
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        Dedent();
        EmitLine("}");
    }

    protected override void EmitPipeline(PipelineStatement p, int ordinal, int depth)
    {
        if (p.Segments.Length == 0)
        {
            if (p.Sources.Length == 1 && p.Sources[0] is KsCall call)
            {
                EmitLine($"{RenderCallStatement(call)};");
                return;
            }
            EmitLine($"/* bare expression: {RenderKsNode(p.Sources[0])} */");
            return;
        }

        string? currentExpr = null;
        bool lastWasAssignment = false;

        for (int segIdx = 0; segIdx < p.Segments.Length; segIdx++)
        {
            var seg = p.Segments[segIdx];
            lastWasAssignment = false;

            bool isVarTap = seg.IsVariableTap
                         || (seg.Arguments.Length == 0 && !_registry.Contains(seg.Target) && !_helperNames.Contains(seg.Target));

            if (isVarTap)
            {
                var tapValue = currentExpr
                    ?? (p.Sources.Length > 0 ? RenderKsNode(p.Sources[0]) : "null");
                EmitLine($"this.{seg.Target} = {tapValue};");
                currentExpr = $"this.{seg.Target}";
                lastWasAssignment = true;
                continue;
            }

            IEnumerable<string> inputArgs = segIdx == 0
                ? p.Sources.Select(RenderKsNode)
                : new[] { currentExpr ?? "null" };
            var allArgs = BuildArgList(seg.Arguments, inputArgs);
            currentExpr = $"this.{MapMethodName(seg.Target)}({allArgs})";
        }

        if (!lastWasAssignment && currentExpr is not null)
            EmitLine($"{currentExpr};");
    }
}
