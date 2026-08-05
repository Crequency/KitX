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

    private void EmitBody(ImmutableArray<Statement> body)
    {
        foreach (var s in body)
            EmitStatement(s);
    }

    private void EmitStatement(Statement stmt)
    {
        switch (stmt)
        {
            case PipelineStatement p:
                EmitPipeline(p, "");
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
                EmitLine($"foreach (var {fe.ItemName} in {RenderKsNode(fe.Source)})");
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
                throw new InvalidOperationException($"Unknown statement kind: {stmt.Kind}");
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

    protected override void EmitPipeline(PipelineStatement p, string stmtPath)
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

            // A helper-named segment is NEVER a variable tap — helper bodies are
            // emitted as methods on G, so writing `this.{helper} = ...` would be
            // CS1656 (method group). This also heals IR that was reverse-projected
            // before BpRenderer learned the helper names.
            bool isVarTap = (seg.IsVariableTap && !_helperNames.Contains(seg.Target))
                         || KsSegmentClassifier.IsVariableTap(seg, _registry, _helperNames);

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
