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
    // ─────────────────────────────────────────────────────────────────────────
    // Per-statement condition counter, used to name condition temporaries
    // (__cond_0, __cond_1, ...) inside a single RunAsync body. Reset together
    // with _pipeCounter at the start of every Generate call.
    // ─────────────────────────────────────────────────────────────────────────
    private int _condCounter;

    private readonly StructuredCodegen? _structured;

    public DebugCodegen(BuiltinFunctionRegistry registry) : base(registry)
    {
        _structured = new StructuredCodegen(registry);
    }

    public override string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger = false)
    {
        _pipeCounter = 0;
        _condCounter = 0;
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
        EmitBody(ir.Body, "/top");
        Dedent();
        EmitLine("}");
        EmitClassFooter();
        return _sb.ToString();
    }

    // ── Body / statement dispatch ──
    //
    // Path convention mirrors BpRenderer exactly (Lens/BpGraphLens/BpRenderer.cs):
    //   • top-level:      /top/stmt/{i}
    //   • if-then body:   {parentPath}/then/stmt/{i}     (parentPath = the If statement's own path)
    //   • if-else body:   {parentPath}/else/stmt/{i}
    //   • forEach body:   {parentPath}/body/stmt/{i}
    //   • while body:     {parentPath}/body/stmt/{i}
    //   • switch arm i:   {parentPath}/arm/{i}/stmt/{j}
    //   • switch default: {parentPath}/default/stmt/{j}
    //
    // Control-flow node paths (used as the data-wire target identifier):
    //   • Branch (If):    {parentPath}                     — Condition wire: w:{NodeId.Of(parentPath)}:Condition
    //   • Each:           {parentPath}                     — List wire:       w:{NodeId.Of(parentPath)}:List
    //   • While:          {parentPath}                     — Condition wire: w:{NodeId.Of(parentPath)}:Condition
    //   • Switch:         {parentPath}                     — Selector wire:   w:{NodeId.Of(parentPath)}:Selector
    //
    // Pipeline segment path: {stmtPath}/seg/{i}            — output wire: w:{NodeId.Of(stmtPath + "/seg/" + i)}

    private void EmitBody(ImmutableArray<Statement> body, string scopePath)
    {
        for (int i = 0; i < body.Length; i++)
            EmitStatement(body[i], $"{scopePath}/stmt/{i}");
    }

    private void EmitStatement(Statement stmt, string stmtPath)
    {
        // Checkpoint id now equals the BP node id (both derive from the same path
        // via NodeId.Of — see Ir/NodeId.cs). This is the foundation that lets a
        // breakpoint set on a BP node fire when execution reaches the matching
        // IR statement. (Discussion notes §十二-I MVP-required debug UX.)
        //
        // The path passed to NodeId.Of is the statement's *primary node path* —
        // the BP node that visually represents the statement. For most statements
        // this is the statement's own path; for a PipelineStatement it is the
        // last segment's path (mirrors BpRenderer's _currentPrimaryNode tracking,
        // BpRenderer.cs:213). For bare calls (Print("hello")) there are no
        // segments so the primary node sits at the statement's own path.
        var primaryPath = GetPrimaryNodePath(stmt, stmtPath);
        var stmtId = NodeId.Of(primaryPath);
        EmitCheckpoint(stmtId, primaryPath, 0);

        switch (stmt)
        {
            case PipelineStatement p: EmitPipeline(p, stmtPath); break;
            case IfStatement iff: EmitIf(iff, stmtPath); break;
            case ForEachStatement fe: EmitForEach(fe, stmtPath); break;
            case WhileStatement ws: EmitWhile(ws, stmtPath); break;
            case SwitchStatement sw: EmitSwitch(sw, stmtPath); break;
            case BreakStatement: EmitLine("break;"); break;
            case ContinueStatement: EmitLine("continue;"); break;
        }
    }

    /// <summary>
    /// Computes the BP-node path that visually represents this statement — i.e.
    /// the path whose FNV-1a hash becomes both the BP node id (BpRenderer) and
    /// the Checkpoint statement id (this codegen). Keep in sync with
    /// BpRenderer.RenderStatement's <c>_currentPrimaryNode</c> tracking.
    ///
    /// Since BpRenderer now threads every usage node (including variable taps) into
    /// the exec chain, the primary node is always the last node of the pipeline
    /// (function call OR variable tap) — both cases share the same path scheme.
    /// </summary>
    private string GetPrimaryNodePath(Statement stmt, string stmtPath)
    {
        if (stmt is not PipelineStatement p)
            return stmtPath;  // control-flow + break/continue: primary node sits at stmtPath.

        // Bare call: Print("hello") — one source that is a KsCall, no segments.
        // The function node occupies the statement's own path (BpRenderer.cs:164).
        if (p.Segments.Length == 0 && p.Sources.Length == 1 && p.Sources[0] is KsCall)
            return stmtPath;

        // General pipeline (incl. pure assignment `0 > counter`): primary = last segment.
        // After the BP-renderer change, every segment node (function or var tap) is in
        // the exec chain, so the last segment is always the primary anchor.
        if (p.Segments.Length > 0)
            return $"{stmtPath}/seg/{p.Segments.Length - 1}";

        // Fallback for any direct-IR-constructed edge case (Parser rejects bare
        // expressions via KS053, so this path is unreachable from KS text).
        return stmtPath;
    }

    protected override void EmitCheckpoint(string stmtId, string lexicalPath, int ordinal)
    {
        EmitLine($"this.Checkpoint(\"{stmtId}\", \"{lexicalPath}\");");
    }

    protected override void EmitPipeline(PipelineStatement p, int ordinal, int depth)
    {
        // Legacy 2-arg signature retained by CodegenBase; not used by DebugCodegen
        // (which threads the full stmtPath through EmitPipeline(PipelineStatement, string) below).
        throw new NotSupportedException(
            "DebugCodegen.EmitPipeline requires a stmtPath; use the (PipelineStatement, string) overload.");
    }

    private void EmitPipeline(PipelineStatement p, string stmtPath)
    {
        // Bare call: Print("hello") — one source that is a KsCall, no segments.
        // The function node occupies the statement's own path (mirrors BpRenderer:164).
        if (p.Segments.Length == 0 && p.Sources.Length == 1 && p.Sources[0] is KsCall call)
        {
            EmitLine($"this.{MapMethodName(call.MethodName)}({string.Join(", ", call.Args.Select(RenderKsNode))});");
            // No data output to record (bare call has no Value pin consumer).
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
            string segPath = $"{stmtPath}/seg/{i}";
            string segNodeId = NodeId.Of(segPath);
            string outputVar = $"__pipe_{_pipeCounter++}";
            bool isVarTap = seg.IsVariableTap
                         || (seg.Arguments.Length == 0
                             && !_registry.Contains(seg.Target)
                             && !_helperNames.Contains(seg.Target));

            if (isVarTap)
            {
                // Variable tap: write to PubVar + notify. The "wire" here is the
                // VariableNode's input pin — its value equals what was written.
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
                // Notify PubVar change (variable panel update).
                EmitLine($"this.OnVarChanged(\"{seg.Target}\", this.{seg.Target});");
                // Also publish as a wire value so a connection hovered between the
                // upstream segment and this VariableNode shows the flowing value.
                // The wireId uses the VariableNode's own path (matches BpRenderer:190).
                EmitLine($"this.OnWireValue(\"w:{segNodeId}\", {outputVar});");
            }
            else
            {
                IEnumerable<string> inputs = i == 0
                    ? p.Sources.Select(RenderKsNode)
                    : [currentVar!];
                string args = BuildArgList(seg.Arguments, inputs);
                EmitLine($"var {outputVar} = this.{MapMethodName(seg.Target)}({args});");
                // Function call output wire — segment node's primary data output pin.
                EmitLine($"this.OnWireValue(\"w:{segNodeId}\", {outputVar});");
            }

            currentVar = outputVar;
        }
    }

    private void EmitIf(IfStatement iff, string stmtPath)
    {
        // Condition wire: the data source feeding Branch.Condition. Source path
        // is {stmtPath}/cond (mirrors BpRenderer:323). The wireId targets the
        // Branch node + its Condition input pin, so the frontend can compose it
        // from BlueprintConnection.TargetNodeId + TargetPin.Name.
        EmitConditionEvaluation(iff.Condition, stmtPath, "Condition", $"{stmtPath}/cond");
        EmitLine($"if (__cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        EmitBody(iff.ThenBody, $"{stmtPath}/then");
        Dedent();
        if (iff.ElseBody.Length > 0)
        {
            EmitLine("} else {");
            Indent();
            EmitBody(iff.ElseBody, $"{stmtPath}/else");
            Dedent();
        }
        EmitLine("}");
    }

    private void EmitForEach(ForEachStatement fe, string stmtPath)
    {
        // List wire: the data source feeding Each.List. Source path is {stmtPath}/src.
        EmitConditionEvaluation(fe.Source, stmtPath, "List", $"{stmtPath}/src");
        EmitLine($"foreach (var {fe.ItemName} in __cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        PushLocal(fe.ItemName);
        EmitBody(fe.Body, $"{stmtPath}/body");
        PopLocal(fe.ItemName);
        Dedent();
        EmitLine("}");
    }

    private void EmitWhile(WhileStatement ws, string stmtPath)
    {
        // Condition wire: the data source feeding While.Condition. Source path is {stmtPath}/cond.
        EmitConditionEvaluation(ws.Condition, stmtPath, "Condition", $"{stmtPath}/cond");
        EmitLine($"while (__cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        EmitBody(ws.Body, $"{stmtPath}/body");
        Dedent();
        EmitLine("}");
    }

    private void EmitSwitch(SwitchStatement sw, string stmtPath)
    {
        // Selector wire: the data source feeding Switch.Selector. Source path is {stmtPath}/sel.
        EmitConditionEvaluation(sw.Selector, stmtPath, "Selector", $"{stmtPath}/sel");
        EmitLine($"switch (__cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            var label = i < sw.ArmLabels.Length ? sw.ArmLabels[i] : i;
            EmitLine($"case {label}:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Arms[i], $"{stmtPath}/arm/{i}");  // path stays index-based for stable diff
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        if (sw.Default.Length > 0)
        {
            EmitLine("default:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Default, $"{stmtPath}/default");
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        Dedent();
        EmitLine("}");
    }

    /// <summary>
    /// Evaluates a control-flow condition/selector expression into a fresh local
    /// (<c>__cond_N</c>) and emits an <see cref="ExecutionGlobals.OnWireValue"/>
    /// notification tagged with the target control-flow node's id and input pin
    /// name. This exposes the runtime value flowing on the wire that BP renders
    /// as `dataSource → Branch.Condition` / `→ Each.List` / `→ While.Condition`
    /// / `→ Switch.Selector`.
    /// </summary>
    /// <param name="cond">The condition/selector KsNode (Identifier / Literal / Call / Pipeline).</param>
    /// <param name="ctrlNodePath">Path of the control-flow node itself (used to compute its nodeId).</param>
    /// <param name="inputPinName">Name of the input pin this value feeds (Condition / List / Selector).</param>
    /// <param name="condPath">Path of the condition data-source node (for diagnostic use only).</param>
    private void EmitConditionEvaluation(KsNode cond, string ctrlNodePath, string inputPinName, string condPath)
    {
        string ctrlNodeId = NodeId.Of(ctrlNodePath);
        string condVar = $"__cond_{_condCounter++}";
        EmitLine($"var {condVar} = {RenderKsNode(cond)};");
        EmitLine($"this.OnWireValue(\"w:{ctrlNodeId}:{inputPinName}\", {condVar});");
    }
}
