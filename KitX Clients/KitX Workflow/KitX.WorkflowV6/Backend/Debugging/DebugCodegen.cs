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
        EmitBody(ir.Body, NodePath.Top);
        // Execution-complete stop point: in step-through mode the debugger pauses here
        // once more after the last node, so the user steps once to formally finish the
        // debug session (free-run / Continue passes straight through).
        EmitCheckpoint(ExecutionEndCheckpointId, "end");
        Dedent();
        EmitLine("}");
        // Helpers MUST be emitted on the debug path too — Run and Debug generate the
        // same G surface, otherwise every helper call fails with CS1061 in debug runs.
        EmitHelperFunctions(ir);
        EmitClassFooter();
        return _sb.ToString();
    }

    /// <summary>Checkpoint id for the execution-complete stop point (never collides with n_XXXXXXXX node ids).</summary>
    private const string ExecutionEndCheckpointId = "end";

    // ── Body / statement dispatch ──
    //
    // Path convention mirrors BpRenderer exactly (Lens/BpGraphLens/BpRenderer.cs);
    // all path shapes are centralised in Ir/NodePath.cs (single source of truth):
    //   • top-level:      NodePath.Stmt(NodePath.Top, i)          → /top/stmt/{i}
    //   • if-then body:   NodePath.Stmt(NodePath.Then(p), i)      → {parentPath}/then/stmt/{i}   (parentPath = the If statement's own path)
    //   • if-else body:   NodePath.Stmt(NodePath.Else(p), i)      → {parentPath}/else/stmt/{i}
    //   • forEach body:   NodePath.Stmt(NodePath.Body(p), i)      → {parentPath}/body/stmt/{i}
    //   • while body:     NodePath.Stmt(NodePath.Body(p), i)      → {parentPath}/body/stmt/{i}
    //   • switch arm i:   NodePath.Stmt(NodePath.Arm(p, i), j)    → {parentPath}/arm/{i}/stmt/{j}
    //   • switch default: NodePath.Stmt(NodePath.Default(p), j)   → {parentPath}/default/stmt/{j}
    //
    // Control-flow node paths (used as the data-wire target identifier):
    //   • Branch (If):    {parentPath}                     — Condition wire: w:{NodeId.Of(parentPath)}:Condition
    //   • Each:           {parentPath}                     — List wire:       w:{NodeId.Of(parentPath)}:List
    //   • While:          {parentPath}                     — Condition wire: w:{NodeId.Of(parentPath)}:Condition
    //   • Switch:         {parentPath}                     — Selector wire:   w:{NodeId.Of(parentPath)}:Selector
    //
    // Pipeline segment path: NodePath.Segment(stmtPath, i)  — output wire: w:{NodeId.Of(stmtPath + "/seg/" + i)}

    private void EmitBody(ImmutableArray<Statement> body, string scopePath)
    {
        for (int i = 0; i < body.Length; i++)
            EmitStatement(body[i], NodePath.Stmt(scopePath, i));
    }

    private void EmitStatement(Statement stmt, string stmtPath)
    {
        // Checkpoint ids equal BP node ids (both derive from the same path via
        // NodeId.Of — see Ir/NodeId.cs). This is the foundation that lets a
        // breakpoint set on a BP node fire when execution reaches the matching
        // IR statement. (Discussion notes §十二-I MVP-required debug UX.)
        //
        // Pipeline statements checkpoint at NODE granularity — every source and
        // segment node gets its own stop point (the BP user's mental model is
        // node-by-node stepping: `a > Print` pauses on a, then on Print).
        // Control-flow statements checkpoint INSIDE their Emit* method, AFTER the
        // condition/source sub-graph evaluates — so the highlight order matches the
        // BP exec chain (… → condition nodes → Branch/Each/While/Switch → body).
        // break/continue (terminators, no sub-graph) keep the statement-level point.
        if (stmt is PipelineStatement p)
        {
            EmitPipeline(p, stmtPath);
            return;
        }

        switch (stmt)
        {
            case IfStatement iff: EmitIf(iff, stmtPath); break;
            case ForEachStatement fe: EmitForEach(fe, stmtPath); break;
            case WhileStatement ws: EmitWhile(ws, stmtPath); break;
            case SwitchStatement sw: EmitSwitch(sw, stmtPath); break;
            case BreakStatement:
                EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
                EmitLine("break;");
                break;
            case ContinueStatement:
                EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
                EmitLine("continue;");
                break;
            default:
                throw new InvalidOperationException($"Unknown statement kind: {stmt.Kind}");
        }
    }

    protected override void EmitCheckpoint(string stmtId, string lexicalPath)
    {
        EmitLine($"this.Checkpoint(\"{stmtId}\", \"{lexicalPath}\");");
    }

    protected override void EmitPipeline(PipelineStatement p, string stmtPath)
    {
        // Bare call: Print("hello") — one source that is a KsCall, no segments.
        // The function node occupies the statement's own path (mirrors BpRenderer:164).
        if (p.Segments.Length == 0 && p.Sources.Length == 1 && p.Sources[0] is KsCall call)
        {
            EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
            EmitLine($"this.{call.MethodName}({string.Join(", ", call.Args.Select(RenderKsNode))});");
            // No data output to record (bare call has no Value pin consumer).
            return;
        }

        if (p.Segments.Length == 0)
        {
            // No-op read (bare identifier/literal line): a single usage node at src/0.
            var srcPath = NodePath.Source(stmtPath, 0);
            EmitCheckpoint(NodeId.Of(srcPath), srcPath);
            EmitLine($"/* bare expression: {RenderKsNode(p.Sources[0])} */");
            return;
        }

        // Node-granularity checkpoints: every source node and every segment node gets
        // its own stop point before it "executes". A source's value is read inside the
        // first segment's argument list, so its checkpoint is a pacing point; segment
        // checkpoints bracket the actual call. Each source ALSO publishes its value on
        // its data-output wire (w:{srcPath}) so the BP source node's data port tooltip
        // shows the flowing value — without this the source port would stay empty.
        // Sources are identifiers / literals / literal-arg calls (KS051), so the extra
        // evaluation is side-effect free.
        for (int i = 0; i < p.Sources.Length; i++)
        {
            var srcPath = NodePath.Source(stmtPath, i);
            EmitCheckpoint(NodeId.Of(srcPath), srcPath);
            EmitLine($"this.OnWireValue(\"w:{NodeId.Of(srcPath)}\", {RenderKsNode(p.Sources[i])});");
        }

        string? currentVar = null;

        for (int i = 0; i < p.Segments.Length; i++)
        {
            var seg = p.Segments[i];
            string segPath = NodePath.Segment(stmtPath, i);
            string segNodeId = NodeId.Of(segPath);
            EmitCheckpoint(segNodeId, segPath);
            string outputVar = $"__pipe_{_pipeCounter++}";
            bool isVarTap = (seg.IsVariableTap && !_helperNames.Contains(seg.Target))
                         || KsSegmentClassifier.IsVariableTap(seg, _registry, _helperNames);

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
                currentVar = outputVar;
            }
            else
            {
                IEnumerable<string> inputs = i == 0
                    ? p.Sources.Select(RenderKsNode)
                    : [currentVar!];
                string args = BuildArgList(seg.Arguments, inputs);
                string callExpr = $"this.{seg.Target}({args})";

                if (IsVoidFunction(seg.Target))
                {
                    // Void segment (Print / Pause / WriteTextFile / StartPlugin / ...):
                    // no return value to bind into a __pipe variable or to publish as a
                    // wire value. The pipeline's value stream ends here — a following
                    // segment defensively receives null.
                    EmitLine($"{callExpr};");
                    currentVar = null;
                }
                else
                {
                    EmitLine($"var {outputVar} = {callExpr};");
                    // Function call output wire — segment node's primary data output pin.
                    EmitLine($"this.OnWireValue(\"w:{segNodeId}\", {outputVar});");
                    currentVar = outputVar;
                }
            }
        }
    }

    /// <summary>
    /// True when the named function returns no value (no output ports). Used to avoid
    /// <c>var x = this.Print(...)</c> (CS0815) in debug codegen — void segments are
    /// emitted as bare statements and the pipeline value stream ends there.
    /// </summary>
    private bool IsVoidFunction(string name)
    {
        if (_registry.Get(name) is { } bi)
            return !bi.OutputPorts.Any();
        foreach (var h in _ir.HelperFunctions)
        {
            if (string.Equals(h.Name, name, StringComparison.Ordinal))
                return string.Equals(h.ReturnType, "void", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private void EmitIf(IfStatement iff, string stmtPath)
    {
        // Condition wire: the data source feeding Branch.Condition. Source path
        // is {stmtPath}/cond (mirrors BpRenderer:323). The wireId targets the
        // Branch node + its Condition input pin, so the frontend can compose it
        // from BlueprintConnection.TargetNodeId + TargetPin.Name.
        EmitConditionEvaluation(iff.Condition, stmtPath, "Condition", NodePath.Condition(stmtPath));
        // Branch checkpoint AFTER the condition sub-graph (BP exec order:
        // … → condition nodes → Branch → branches).
        EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
        EmitLine($"if (__cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        EmitBody(iff.ThenBody, NodePath.Then(stmtPath));
        Dedent();
        if (iff.ElseBody.Length > 0)
        {
            EmitLine("} else {");
            Indent();
            EmitBody(iff.ElseBody, NodePath.Else(stmtPath));
            Dedent();
        }
        EmitLine("}");
    }

    private void EmitForEach(ForEachStatement fe, string stmtPath)
    {
        // List wire: the data source feeding Each.List. Source path is {stmtPath}/src.
        EmitConditionEvaluation(fe.Source, stmtPath, "List", NodePath.SourceRoot(stmtPath));
        // Each checkpoint AFTER the source sub-graph (BP exec order:
        // … → source nodes → Each → body).
        EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
        EmitLine($"foreach (var {fe.ItemName} in __cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        PushLocal(fe.ItemName);
        EmitBody(fe.Body, NodePath.Body(stmtPath));
        PopLocal(fe.ItemName);
        Dedent();
        EmitLine("}");
    }

    private void EmitWhile(WhileStatement ws, string stmtPath)
    {
        // Condition wire: the data source feeding While.Condition. Source path is {stmtPath}/cond.
        //
        // CRITICAL: the condition must be re-evaluated EVERY iteration (its variables
        // typically change inside the body). Evaluating it before the loop would freeze
        // the condition at its initial value — a true initial condition then loops
        // forever (the generated `while (__cond_0)` never re-reads the variables).
        // The while(true) + break form keeps the per-iteration OnWireValue publication.
        // The While checkpoint sits AFTER the condition sub-graph checkpoints, matching
        // the BP exec order (… → condition nodes → While → body) on every iteration.
        EmitLine("while (true)");
        EmitLine("{");
        Indent();
        EmitConditionEvaluation(ws.Condition, stmtPath, "Condition", NodePath.Condition(stmtPath));
        EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
        EmitLine($"if (!__cond_{_condCounter - 1}) break;");
        EmitBody(ws.Body, NodePath.Body(stmtPath));
        Dedent();
        EmitLine("}");
    }

    private void EmitSwitch(SwitchStatement sw, string stmtPath)
    {
        // Selector wire: the data source feeding Switch.Selector. Source path is {stmtPath}/sel.
        EmitConditionEvaluation(sw.Selector, stmtPath, "Selector", NodePath.Selector(stmtPath));
        // Switch checkpoint AFTER the selector sub-graph (BP exec order:
        // … → selector nodes → Switch → arms).
        EmitCheckpoint(NodeId.Of(stmtPath), stmtPath);
        EmitLine($"switch (__cond_{_condCounter - 1})");
        EmitLine("{");
        Indent();
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            var label = i < sw.ArmLabels.Length ? sw.ArmLabels[i] : i;
            EmitLine($"case {label}:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Arms[i], NodePath.Arm(stmtPath, i));  // path stays index-based for stable diff
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        if (sw.Default.Length > 0)
        {
            EmitLine("default:");
            EmitLine("{");
            Indent();
            EmitBody(sw.Default, NodePath.Default(stmtPath));
            EmitLine("break;");
            Dedent();
            EmitLine("}");
        }
        Dedent();
        EmitLine("}");
    }

    /// <summary>
    /// Evaluates a control-flow condition/selector/source expression into a fresh local
    /// (<c>__cond_N</c>), emitting node-granularity checkpoints and wire publications
    /// that mirror BpRenderer's condition sub-graph paths:
    ///   • single expression (`while i:`)           → one node at {condPath}
    ///   • pipeline (`while i, 3 > Compare(...)`)   → {condPath}/src/{i} + {condPath}/seg/{i}
    /// Every condition/source node therefore gets its own StepOver stop point and its
    /// data port shows the runtime value — previously the whole condition was one
    /// inlined expression, so StepOver jumped over the condition nodes and their ports
    /// stayed empty. Also emits the control-flow input wire
    /// (w:{ctrlNodeId}:{pinName}) that feeds the Branch/While/Switch/Each data pin.
    /// </summary>
    /// <param name="cond">The condition/selector KsNode (Identifier / Literal / Call / Pipeline).</param>
    /// <param name="ctrlNodePath">Path of the control-flow node itself (used to compute its nodeId).</param>
    /// <param name="inputPinName">Name of the input pin this value feeds (Condition / List / Selector).</param>
    /// <param name="condPath">Path of the condition data-source node (mirrors BpRenderer's {path}/cond|src|sel).</param>
    private void EmitConditionEvaluation(KsNode cond, string ctrlNodePath, string inputPinName, string condPath)
    {
        string ctrlNodeId = NodeId.Of(ctrlNodePath);
        string condVar = $"__cond_{_condCounter++}";

        if (cond is KsPipeline pipe && pipe.Segments.Length > 0)
        {
            for (int i = 0; i < pipe.Sources.Length; i++)
            {
                var srcPath = NodePath.Source(condPath, i);
                EmitCheckpoint(NodeId.Of(srcPath), srcPath);
                // Sources are identifiers / literals / literal-arg calls (KS051), so the
                // extra evaluation for the wire publication is side-effect free.
                EmitLine($"this.OnWireValue(\"w:{NodeId.Of(srcPath)}\", {RenderKsNode(pipe.Sources[i])});");
            }
            for (int i = 0; i < pipe.Segments.Length; i++)
            {
                var segPath = NodePath.Segment(condPath, i);
                EmitCheckpoint(NodeId.Of(segPath), segPath);
            }
            EmitLine($"var {condVar} = {RenderKsNode(cond)};");
            var lastSegPath = NodePath.Segment(condPath, pipe.Segments.Length - 1);
            EmitLine($"this.OnWireValue(\"w:{NodeId.Of(lastSegPath)}\", {condVar});");
        }
        else
        {
            EmitCheckpoint(NodeId.Of(condPath), condPath);
            EmitLine($"var {condVar} = {RenderKsNode(cond)};");
            EmitLine($"this.OnWireValue(\"w:{NodeId.Of(condPath)}\", {condVar});");
        }

        // The control-flow input wire: value flowing into the Branch/While/Switch/Each
        // data input pin (the frontend composes this id from the connection's target
        // node + pin name).
        EmitLine($"this.OnWireValue(\"w:{ctrlNodeId}:{inputPinName}\", {condVar});");
    }
}
