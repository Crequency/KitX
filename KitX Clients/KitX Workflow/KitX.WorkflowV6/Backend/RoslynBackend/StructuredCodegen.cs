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

    /// <summary>
    /// Emit-time statement counter driving the global cancellation-check cadence.
    /// Reset at the start of every <see cref="Generate"/> call (alongside
    /// _pipeCounter, per the CodegenBase contract).
    /// </summary>
    private int _cancelCheckCounter;

    /// <summary>
    /// PubVar types from the current Generate call's lowering, used by
    /// <see cref="RenderForEachSource"/> to decide whether a forEach source needs the
    /// runtime Enumerate bridge.
    /// </summary>
    private IReadOnlyDictionary<string, string>? _pubVarTypes;

    /// <summary>
    /// Emit one cancellation check per this many emitted statements. Bounds the
    /// instrumentation overhead on long straight-line programs while keeping
    /// cancellation latency bounded (worst case: a check fires N statements late).
    /// </summary>
    private const int CancelCheckInterval = 1000;

    public override string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger = false)
    {
        _cancelCheckCounter = 0;
        _ir = ir;
        _pubVarTypes = lowering?.PubVarTypes;
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

    /// <summary>
    /// Renders a forEach collection source. <see cref="System.Text.Json.JsonElement"/>
    /// does not implement IEnumerable, and object?-typed sources may hold a JSON array
    /// at runtime — both are routed through the runtime <c>Enumerate</c> helper so the
    /// standard "iterate a JSON array" idiom compiles. Strongly-typed collections
    /// (e.g. Range's int[]) keep the direct foreach, byte-identical to before.
    /// </summary>
    private string RenderForEachSource(KsNode source)
    {
        string? staticType = null;
        if (source is KsIdentifier id && _pubVarTypes is not null
            && _pubVarTypes.TryGetValue(id.Name, out var varType))
        {
            staticType = varType;
        }
        else if (source is KsCall call && _registry.Contains(call.MethodName))
        {
            staticType = BuiltinRuntimeTypes.Resolve(call.MethodName) switch
            {
                { } actual when actual == typeof(object) => "object",
                { } actual when actual == typeof(System.Text.Json.JsonElement) => "JsonElement",
                _ => null,   // typed collection (int[], List, ...) — direct foreach
            };
        }

        if (staticType is "JsonElement" or "object")
            return $"this.Enumerate({RenderKsNode(source)})";
        return RenderKsNode(source);
    }

    /// <summary>
    /// Global cancellation check: emitted every <see cref="CancelCheckInterval"/>
    /// statements so long straight-line programs stay cancellable (W-1). The token is
    /// <see cref="ExecutionGlobals.DebugToken"/>, set by the backend from the caller's
    /// CancellationToken — the same field the debug path's Checkpoint consults. On a
    /// cancelled token <c>ThrowIfCancellationRequested</c> throws
    /// <see cref="OperationCanceledException"/>, which the backend unwraps from the
    /// reflection TargetInvocationException and rethrows as a cancellation.
    /// </summary>
    private void EmitCancellationCheck()
    {
        if (++_cancelCheckCounter < CancelCheckInterval) return;
        _cancelCheckCounter = 0;
        EmitLine("this.DebugToken.ThrowIfCancellationRequested();");
    }

    /// <summary>
    /// Per-iteration cancellation check emitted at the top of every loop body
    /// (while/foreach). The emit-time global counter alone cannot bound an infinite
    /// loop — a body of a few statements would never accumulate 1000 emits — so every
    /// iteration pays one token check (a near-free field read on a non-cancelled token)
    /// and `while true` workflows become stoppable via Stop/cancellation (W-1).
    /// </summary>
    private void EmitLoopIterationCheck()
        => EmitLine("this.DebugToken.ThrowIfCancellationRequested();");

    private void EmitStatement(Statement stmt)
    {
        EmitCancellationCheck();
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
                EmitLine($"foreach (var {fe.ItemName} in {RenderForEachSource(fe.Source)})");
                EmitLine("{");
                Indent();
                EmitLoopIterationCheck();
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
                EmitLoopIterationCheck();
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
            currentExpr = $"this.{seg.Target}({allArgs})";
        }

        if (!lastWasAssignment && currentExpr is not null)
            EmitLine($"{currentExpr};");
    }
}
