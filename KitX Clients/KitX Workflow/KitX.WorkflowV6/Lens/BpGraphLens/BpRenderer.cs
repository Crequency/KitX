namespace KitX.WorkflowV6.Lens.BpGraphLens;

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// BpRenderer — structured IR → Blueprint graph data.
//
// v6.0 design (discussion notes §十二, refactoring plan Phase 2):
//   • ExecTail tracking — each statement consumes incoming exec tails and produces
//     new tails for the next statement. if/else merge both branches' tails.
//   • Single Entry — only top-level has an EntryNode; sub-scopes receive entry
//     tails from control-flow nodes' named output pins (True/False/Body).
//   • Variable def/use separation — const/var declarations produce standalone
//     definition nodes; pipeline usages produce separate VariableNode instances.
//   • Literal → DefaultValue — literal args inside function parens set the input
//     pin's DefaultValue instead of creating separate ConstNode + connection.
//   • Condition data input — Branch and While nodes get a Condition input pin;
//     the condition KsNode renders as a data source connected to this pin.
//   • NodeID = FNV-1a hash of path → short, deterministic, nesting-independent.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BpRenderer
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly ILayoutService _layout;
    private Blueprint _bp = null!;

    private readonly Stack<(BlueprintNode loopNode, string endPin)> _loopStack = new();

    /// <summary>
    /// The current statement's primary node — the node the exec chain enters (Branch/
    /// Each/While/Switch/control node, or the last function node of a pipeline). Set by
    /// each Render* method; read by <see cref="RenderStatement"/> to anchor the leading
    /// comment (GroupComment) and to attach the trailing comment. Save/restored across
    /// nested statements so sub-scope rendering never clobbers the parent's primary.
    /// </summary>
    private BlueprintNode? _currentPrimaryNode;

    /// <summary>Tracks which node + output pin is the current exec chain tail.</summary>
    private readonly record struct ExecTail(BlueprintNode Node, string OutputPin);

    public BpRenderer(BuiltinFunctionRegistry registry, ILayoutService? layout = null)
    {
        _registry = registry;
        _layout = layout ?? new LayoutService();
    }

    public Blueprint Render(Workflow ir)
    {
        _bp = new Blueprint { Name = "Workflow" };

        RenderDefinitions(ir);

        if (ir.Body.Length > 0)
        {
            var entry = Add(new EntryNode { Name = "Entry" }, "/entry");
            RenderScope(ir.Body, "/top", [new ExecTail(entry, BpPinNames.Exec)]);
        }

        _layout.Layout(_bp);
        return _bp;
    }

    // ── Definitions (standalone nodes, no connections) ──

    private void RenderDefinitions(Workflow ir)
    {
        foreach (var (name, c) in ir.Constants)
            Add(new ConstNode
            {
                Name = name,
                ConstName = name,
                ConstType = c.Type,
                // For dict consts carry the structured DictInitialiser as JSON so the reverse
                // path can rebuild it (Dict-Type design §3.3); otherwise the verbatim text.
                ConstValue = c.DictInitializer is not null
                    ? JsonSerializer.Serialize(c.DictInitializer)
                    : c.InitialValueExpression,
            }, $"/def/const/{name}");

        foreach (var (name, g) in ir.GlobalVars)
            Add(new VariableNode
            {
                Name = name,
                VarName = name,
                VarType = g.Type,
                VarKind = VariableKind.PubVar,
                // Carry a dict var's structured DictInitialiser as JSON (symmetric to ConstValue).
                VarInitialValue = g.DictInitializer is not null
                    ? JsonSerializer.Serialize(g.DictInitializer)
                    : g.InitialValueExpression,
            }, $"/def/var/{name}");
    }

    // ── Scope rendering ──

    private void RenderScope(ImmutableArray<Statement> body, string scopePath, List<ExecTail> entryTails)
    {
        var tails = entryTails;
        for (int i = 0; i < body.Length; i++)
            tails = RenderStatement(body[i], $"{scopePath}/stmt/{i}", tails);
    }

    /// <summary>Renders a sub-scope (if-then/else body, loop body). No Entry node.</summary>
    private List<ExecTail> RenderSubScope(ImmutableArray<Statement> body, string scopePath, List<ExecTail> entryTails)
    {
        var tails = entryTails;
        for (int i = 0; i < body.Length; i++)
            tails = RenderStatement(body[i], $"{scopePath}/stmt/{i}", tails);
        return tails;
    }

    private List<ExecTail> RenderStatement(Statement stmt, string path, List<ExecTail> prevTails)
    {
        var savedPrimary = _currentPrimaryNode;
        _currentPrimaryNode = null;

        var tails = stmt switch
        {
            PipelineStatement p => RenderPipelineStmt(p, path, prevTails),
            IfStatement iff => RenderIfElse(iff, path, prevTails),
            ForEachStatement fe => RenderForEach(fe, path, prevTails),
            WhileStatement ws => RenderWhile(ws, path, prevTails),
            SwitchStatement sw => RenderSwitch(sw, path, prevTails),
            BreakStatement => RenderCtrlNode("break", path, prevTails),
            ContinueStatement => RenderCtrlNode("continue", path, prevTails),
            _ => prevTails,
        };

        var primary = _currentPrimaryNode;
        _currentPrimaryNode = savedPrimary;

        if (primary is not null)
        {
            // Record the statement's leader (primary) node so the frontend can offer
            // group-comment anchoring on valid statement leaders (the reverse translator
            // reattaches leading comments by this node's id).
            _bp.StatementPrimaryNodeIds.Add(primary.Id);

            // Data subgraph: the connected component of DATA edges reachable from the
            // primary node. KS one line ⇔ one data subgraph; subgraphs never overlap —
            // cross-statement data edges do not exist (variable writes/reads are separate
            // usage nodes, control-flow data pins connect only their own statement's
            // subgraph, Each.Current is not wired). Every node in the component belongs
            // to THIS statement's primary (frontend snap target).
            var component = CollectDataComponent(primary, _bp);
            foreach (var id in component)
                _bp.StatementNodeToPrimary[id] = primary.Id;

            // The statement TrailingComment is attached to the LAST SOURCE node (parser
            // capture point A reads it back from the source list's final line) — NOT the
            // primary node, whose Comment is reserved for the last segment's inline
            // comment. This is done inside RenderPipelineStmt for pipelines; non-pipeline
            // statements (control flow) keep the trailing comment on the primary node.
            if (stmt is not PipelineStatement && stmt.TrailingComment is { Length: > 0 })
                primary.Comment = stmt.TrailingComment;

            // Emit a GroupComment anchoring the leading comment to this statement's data
            // subgraph. A statement with no data edges still keeps its primary node so the
            // dashed frame shows the node itself.
            if (stmt.LeadingComment is { Length: > 0 })
            {
                _bp.GroupComments.Add(new BlueprintGroupComment
                {
                    Comment = stmt.LeadingComment,
                    AnchorNodeId = primary.Id,
                    NodeIds = component.Count > 0 ? [.. component] : [primary.Id],
                });
            }
        }

        return tails;
    }

    /// <summary>
    /// Collects the connected component of data edges (undirected) reachable from the root
    /// node. Exec edges are excluded — the data subgraph is the pure data-flow region.
    /// </summary>
    private static HashSet<string> CollectDataComponent(BlueprintNode root, Blueprint bp)
    {
        var adj = new Dictionary<string, List<string>>();
        foreach (var conn in bp.Connections)
        {
            var src = bp.Nodes.FirstOrDefault(n => n.Id == conn.SourceNodeId);
            if (src == null) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;

            if (!adj.TryGetValue(conn.SourceNodeId, out var l1)) adj[conn.SourceNodeId] = l1 = new();
            l1.Add(conn.TargetNodeId);
            if (!adj.TryGetValue(conn.TargetNodeId, out var l2)) adj[conn.TargetNodeId] = l2 = new();
            l2.Add(conn.SourceNodeId);
        }

        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        visited.Add(root.Id);
        queue.Enqueue(root.Id);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!adj.TryGetValue(id, out var neighbors)) continue;
            foreach (var n in neighbors)
                if (visited.Add(n))
                    queue.Enqueue(n);
        }
        return visited;
    }

    private List<ExecTail> RenderCtrlNode(string name, string path, List<ExecTail> prevTails)
    {
        var node = AddCtrlNode(name, path);
        _currentPrimaryNode = node;
        ConnectExecTails(prevTails, node);
        return [];
    }

    // ── Pipeline rendering ──

    private List<ExecTail> RenderPipelineStmt(PipelineStatement p, string path, List<ExecTail> prevTails)
    {
        // Bare call: Print("hello") — one source that is a KsCall, no segments.
        if (p.Segments.Length == 0 && p.Sources.Length == 1 && p.Sources[0] is KsCall call)
        {
            var func = AddBuiltin(call.MethodName, path);
            _currentPrimaryNode = func;
            WireCallArgs(func, call.Args, $"{path}/args");
            ConnectExecTails(prevTails, func);
            // Bare call: the trailing comment lands on the single function node
            // (there is no source node; the reverse translator reads it back here).
            if (p.TrailingComment is { Length: > 0 } && func.Comment is null)
                func.Comment = p.TrailingComment;
            return [new ExecTail(func, BpPinNames.Exec)];
        }

        // General pipeline: every source and every segment node is created with Exec pins
        // (via AddUsageNode for ConstNode/VariableNode; AddBuiltin already adds Exec pins)
        // and threaded into the exec chain in left-to-right order. This guarantees the BP
        // exec graph stays connected for pure-assignment pipelines like `0 > counter`,
        // which would otherwise produce an isolated ConstNode→VariableNode sub-graph.
        BlueprintNode? lastNode = null;
        var sourceNodes = new List<BlueprintNode>();
        var currentTails = prevTails;

        // 1. Render every source as a node and chain it into the exec flow.
        for (int i = 0; i < p.Sources.Length; i++)
        {
            var srcNode = RenderSourceAsNode(p.Sources[i], $"{path}/src/{i}");
            sourceNodes.Add(srcNode);
            // Statement TrailingComment lands on the LAST source node (parser capture
            // point A reads it back from the source list's final line) — unless the
            // source itself carries an inline comment.
            if (i == p.Sources.Length - 1
                && p.TrailingComment is { Length: > 0 }
                && srcNode.Comment is null)
            {
                srcNode.Comment = p.TrailingComment;
            }
            ConnectExecTails(currentTails, srcNode);
            currentTails = [new ExecTail(srcNode, BpPinNames.Exec)];
            lastNode = srcNode;
        }

        // 2. Render every segment and chain it into the exec flow.
        for (int i = 0; i < p.Segments.Length; i++)
        {
            var seg = p.Segments[i];
            string segPath = $"{path}/seg/{i}";
            bool isVarTap = seg.IsVariableTap
                         || (seg.Arguments.Length == 0 && !_registry.Contains(seg.Target));

            BlueprintNode segNode;
            if (isVarTap)
            {
                var vn = AddUsageNode(new VariableNode
                {
                    Name = seg.Target, VarName = seg.Target,
                    VarKind = VariableKind.PubVar,
                }, segPath);
                // Per-segment inline comment → this segment's variable-tap node Comment.
                if (seg.Comment is { Length: > 0 })
                    vn.Comment = seg.Comment;
                var dataSource = lastNode ?? (sourceNodes.Count > 0 ? sourceNodes[^1] : null);
                if (dataSource is not null) ConnectValue(dataSource, vn);
                segNode = vn;
            }
            else
            {
                var fn = AddBuiltin(seg.Target, segPath);
                // Per-segment inline comment → this segment's function node Comment.
                if (seg.Comment is { Length: > 0 })
                    fn.Comment = seg.Comment;
                WireCallArgs(fn, seg.Arguments, $"{segPath}/args");
                if (i == 0)
                {
                    ConnectPipelineSources(fn, seg.Arguments, sourceNodes);
                }
                else if (lastNode is not null)
                {
                    // Connect prev function's first data output → this function's first data input.
                    // Use ConnectValue (direction-based) not ConnectToInput(name-based): the prev
                    // node's output pin name (e.g. "Keys") rarely matches the next node's input pin
                    // name (e.g. "Value"), so name-matching would silently drop the data edge and
                    // break BP→IR round-trip for any function→function pipeline segment.
                    ConnectValue(lastNode, fn);
                }
                segNode = fn;
            }

            ConnectExecTails(currentTails, segNode);
            currentTails = [new ExecTail(segNode, BpPinNames.Exec)];
            lastNode = segNode;
        }

        if (lastNode is not null)
        {
            _currentPrimaryNode = lastNode;
            return currentTails;
        }

        // No sources and no segments — should be unreachable (Parser rejects bare
        // expressions via KS053), but keep the safety net for direct IR construction.
        return prevTails;
    }

    /// <summary>
    /// Wires literal/identifier args from a function call's parens to the function
    /// node's input pins. Per v6.0 rule, parens may only contain literals/placeholders.
    /// Literals → input pin DefaultValue; identifiers → usage VariableNode + Connect.
    /// Args are matched to pins by position: arg[i] → the i-th non-Exec data input pin.
    /// </summary>
    private void WireCallArgs(BuiltinFunctionNode func, ImmutableArray<KsNode> args, string path)
    {
        // Collect data input pins (exclude Exec) in order.
        var dataPins = func.InputPins.Where(p => p.Name != BpPinNames.Exec).ToList();
        // Variadic input group (P5-C1): args beyond the static PortSpec extend the group.
        var variadic = _registry?.Get(func.FunctionName)?.InputVariadic;
        for (int i = 0; i < args.Length; i++)
        {
            // Skip placeholders — pipeline sources fill these positions separately.
            if (args[i] is KsPlaceholder) continue;

            var pin = i < dataPins.Count ? dataPins[i] : null;
            if (pin is null)
            {
                // Beyond the static PortSpec: append a variadic input pin when the
                // function declares one. Without this, KS calls with more arguments than
                // static pins would silently drop the extra args on the way to the
                // Blueprint, breaking the KS↔BP round-trip for e.g. StringConcat(a, b, c).
                if (variadic is null) continue;
                var variadicCount = dataPins.Count(p =>
                    !string.IsNullOrEmpty(variadic.BasePinName)
                    && p.Name.StartsWith(variadic.BasePinName, StringComparison.Ordinal));
                var name = string.IsNullOrEmpty(variadic.BasePinName)
                    ? (variadic.StartIndex + variadicCount).ToString()
                    : $"{variadic.BasePinName}{variadic.StartIndex + variadicCount}";
                pin = MakePin(name, PinDirection.Input, variadic.PinType);
                func.InputPins.Add(pin);
                dataPins.Add(pin);
            }

            switch (args[i])
            {
                case KsLiteral lit:
                    pin.DefaultValue = lit.Value?.ToString() ?? "null";
                    break;
                case KsIdentifier id:
                    var vn = AddUsageNode(new VariableNode
                    {
                        Name = id.Name, VarName = id.Name,
                        VarKind = VariableKind.PubVar,
                    }, $"{path}/{i}");
                    if (id.Comment is { Length: > 0 })
                        vn.Comment = id.Comment;
                    ConnectToInput(vn, func, pin.Name);
                    break;
            }
        }
    }

    /// <summary>
    /// Connects pipeline source nodes to the first segment function's data input pins.
    /// Placeholder positions in the segment's Arguments determine which pin each source
    /// connects to; sources without an explicit placeholder are appended to remaining
    /// pins in order (v6 rule: no-placeholder → sources fill remaining arg slots).
    /// </summary>
    private void ConnectPipelineSources(BuiltinFunctionNode fn, ImmutableArray<KsNode> segArgs, List<BlueprintNode> sourceNodes)
    {
        var dataPins = fn.InputPins.Where(p => p.Name != BpPinNames.Exec).ToList();
        if (dataPins.Count == 0 || sourceNodes.Count == 0) return;

        // Map: arg index → pin index. Placeholders mark where pipeline sources insert.
        // Non-placeholder args (literals/identifiers) are already wired by WireCallArgs
        // and occupy their positional pin. Sources fill placeholder slots first, then
        // any remaining pins (the append rule for no-placeholder pipelines).
        var placeholderPinIndices = new List<int>();
        var occupiedPinIndices = new HashSet<int>();
        for (int i = 0; i < segArgs.Length; i++)
        {
            if (i >= dataPins.Count) break;
            if (segArgs[i] is KsPlaceholder)
                placeholderPinIndices.Add(i);
            else
                occupiedPinIndices.Add(i);
        }

        int sourceIdx = 0;
        // Fill placeholder positions first.
        foreach (var pinIdx in placeholderPinIndices)
        {
            if (sourceIdx >= sourceNodes.Count) break;
            ConnectToInput(sourceNodes[sourceIdx], fn, dataPins[pinIdx].Name);
            sourceIdx++;
        }
        // Append remaining sources to unoccupied pins in order.
        for (int pinIdx = 0; pinIdx < dataPins.Count && sourceIdx < sourceNodes.Count; pinIdx++)
        {
            if (occupiedPinIndices.Contains(pinIdx)) continue;
            if (placeholderPinIndices.Contains(pinIdx)) continue;
            ConnectToInput(sourceNodes[sourceIdx], fn, dataPins[pinIdx].Name);
            sourceIdx++;
        }
    }

    /// <summary>Returns the name of the first non-Exec output data pin, or "Value" as fallback.</summary>
    private static string FirstDataOutputPinName(BlueprintNode node)
    {
        var dataOut = node.OutputPins.Find(p => p.Name != BpPinNames.Exec);
        return dataOut?.Name ?? BpPinNames.Value;
    }

    // ── If/Else ──

    private List<ExecTail> RenderIfElse(IfStatement iff, string path, List<ExecTail> prevTails)
    {
        // Condition node is rendered first and threaded into the exec chain before Branch.
        // Per the v6 design principle "every non-definition node participates in the exec
        // graph", the condition node (VariableNode / ConstNode / pipeline of function nodes)
        // is a usage node with Exec pins and is reached by the exec flow before Branch.
        //
        // The BpReverseTranslator correctly handles this via _consumedNodes tracking: when
        // ReverseIf reads the condition via ReadDataInput, it marks the entire condition
        // sub-graph as consumed; WalkExecChain then skips those nodes, avoiding spurious
        // standalone PipelineStatements for the condition expression.
        var condNode = RenderCondition(iff.Condition, $"{path}/cond", prevTails);
        var afterCondTails = new List<ExecTail> { new(condNode, BpPinNames.Exec) };

        var br = Add(new BuiltinFunctionNode { Name = "Branch", FunctionName = "Branch" }, path);
        _currentPrimaryNode = br;
        br.InputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        br.InputPins.Add(MakePin(BpPinNames.Condition, PinDirection.Input, PinType.Boolean));
        br.OutputPins.Add(MakePin(BpPinNames.True, PinDirection.Output, PinType.Execution));
        br.OutputPins.Add(MakePin(BpPinNames.False, PinDirection.Output, PinType.Execution));
        br.OutputPins.Add(MakePin(BpPinNames.End, PinDirection.Output, PinType.Execution));

        ConnectExecTails(afterCondTails, br);
        ConnectToInput(condNode, br, BpPinNames.Condition);

        // Sub-scope bodies' exec-out tails are left dangling (no target) per the v6
        // End-pin model: a branch body naturally ends → control returns to Branch.End,
        // which is the single continuation point. The dangling tails are intentionally
        // discarded here — the caller threads the post-if statement from Branch.End.
        _ = RenderSubScope(iff.ThenBody, $"{path}/then",
            [new ExecTail(br, BpPinNames.True)]);
        if (iff.ElseBody.Length > 0)
        {
            _ = RenderSubScope(iff.ElseBody, $"{path}/else",
                [new ExecTail(br, BpPinNames.False)]);
        }

        return [new ExecTail(br, BpPinNames.End)];
    }

    // ── ForEach ──

    private List<ExecTail> RenderForEach(ForEachStatement fe, string path, List<ExecTail> prevTails)
    {
        // Source node threaded into exec chain before Each. See RenderIfElse comment for
        // the _consumedNodes-based reverse-translation rationale.
        var sourceNode = RenderSourceAsNode(fe.Source, $"{path}/src", prevTails);
        var afterSrcTails = new List<ExecTail> { new(sourceNode, BpPinNames.Exec) };

        var each = Add(new BuiltinFunctionNode { Name = "Each", FunctionName = "Each" }, path);
        _currentPrimaryNode = each;
        each.Properties["ItemName"] = fe.ItemName;
        each.InputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        each.InputPins.Add(MakePin(BpPinNames.List, PinDirection.Input, PinType.Any));
        each.OutputPins.Add(MakePin(BpPinNames.Body, PinDirection.Output, PinType.Execution));
        each.OutputPins.Add(MakePin(BpPinNames.End, PinDirection.Output, PinType.Execution));
        each.OutputPins.Add(MakePin(BpPinNames.Current, PinDirection.Output, PinType.Any));

        ConnectExecTails(afterSrcTails, each);
        ConnectToInput(sourceNode, each, BpPinNames.List);

        _loopStack.Push((each, BpPinNames.End));
        RenderSubScope(fe.Body, $"{path}/body", [new ExecTail(each, BpPinNames.Body)]);
        _loopStack.Pop();

        return [new ExecTail(each, BpPinNames.End)];
    }

    // ── While ──

    private List<ExecTail> RenderWhile(WhileStatement ws, string path, List<ExecTail> prevTails)
    {
        // Condition node threaded into exec chain before While.
        var condNode = RenderCondition(ws.Condition, $"{path}/cond", prevTails);
        var afterCondTails = new List<ExecTail> { new(condNode, BpPinNames.Exec) };

        var wh = Add(new BuiltinFunctionNode { Name = "While", FunctionName = "While" }, path);
        _currentPrimaryNode = wh;
        wh.InputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        wh.InputPins.Add(MakePin(BpPinNames.Condition, PinDirection.Input, PinType.Boolean));
        wh.OutputPins.Add(MakePin(BpPinNames.Body, PinDirection.Output, PinType.Execution));
        wh.OutputPins.Add(MakePin(BpPinNames.End, PinDirection.Output, PinType.Execution));

        ConnectExecTails(afterCondTails, wh);
        ConnectToInput(condNode, wh, BpPinNames.Condition);

        _loopStack.Push((wh, BpPinNames.End));
        RenderSubScope(ws.Body, $"{path}/body", [new ExecTail(wh, BpPinNames.Body)]);
        _loopStack.Pop();

        return [new ExecTail(wh, BpPinNames.End)];
    }

    // ── Switch ──

    private List<ExecTail> RenderSwitch(SwitchStatement sw, string path, List<ExecTail> prevTails)
    {
        // Selector node threaded into exec chain before Switch.
        var selNode = RenderCondition(sw.Selector, $"{path}/sel", prevTails);
        var afterSelTails = new List<ExecTail> { new(selNode, BpPinNames.Exec) };

        var sn = Add(new BuiltinFunctionNode { Name = "Switch", FunctionName = "Switch" }, path);
        _currentPrimaryNode = sn;
        sn.InputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        sn.InputPins.Add(MakePin(BpPinNames.Selector, PinDirection.Input, PinType.Integer));

        ConnectExecTails(afterSelTails, sn);
        ConnectToInput(selNode, sn, BpPinNames.Selector);

        // Each arm body's exec-out tails are left dangling per the v6 End-pin model:
        // an arm naturally ends → control returns to Switch.End, the single
        // continuation point. Dangling tails are intentionally discarded.
        // Arm pin names use the label value (value-match semantics) so the BP graph
        // is self-documenting — e.g. pin "43" means "case 43:".
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            var label = i < sw.ArmLabels.Length ? sw.ArmLabels[i] : i;
            var pinName = label.ToString();
            sn.OutputPins.Add(MakePin(pinName, PinDirection.Output, PinType.Execution));
            _ = RenderSubScope(sw.Arms[i], $"{path}/arm/{i}",
                [new ExecTail(sn, pinName)]);
        }
        if (sw.Default.Length > 0)
        {
            sn.OutputPins.Add(MakePin(BpPinNames.Default, PinDirection.Output, PinType.Execution));
            _ = RenderSubScope(sw.Default, $"{path}/default",
                [new ExecTail(sn, BpPinNames.Default)]);
        }
        sn.OutputPins.Add(MakePin(BpPinNames.End, PinDirection.Output, PinType.Execution));
        return [new ExecTail(sn, BpPinNames.End)];
    }

    // ── Condition / source rendering ──

    /// <summary>
    /// Renders a condition KsNode as a data-source node whose output is the condition value.
    /// Handles KsIdentifier (variable read), KsCall (function call), and KsPipeline
    /// (pipeline condition like `a, b > Compare("BEQ")`).
    ///
    /// Per the v6 contract (§3.3.2-3.3.5 of KScript-Blueprint-Correspondence.md), the
    /// condition/source sub-graph IS threaded into the exec chain before the control-flow
    /// node — every non-definition node participates in the exec graph. <paramref name="prevTails"/>
    /// are connected to the condition's first node; the returned node's Exec out becomes
    /// the tail the caller threads into the Branch/Each/While/Switch node.
    /// </summary>
    private BlueprintNode RenderCondition(KsNode cond, string path, List<ExecTail> prevTails)
    {
        switch (cond)
        {
            case KsIdentifier id:
                {
                    var vn = AddUsageNode(new VariableNode
                    {
                        Name = id.Name, VarName = id.Name,
                        VarKind = VariableKind.PubVar,
                    }, path);
                    ConnectExecTails(prevTails, vn);
                    return vn;
                }
            case KsLiteral lit:
                {
                    var cn = AddUsageNode(new ConstNode
                    {
                        Name = lit.Value?.ToString() ?? "null",
                        ConstName = lit.Value?.ToString() ?? "null",
                        ConstValue = lit.Value?.ToString(),
                    }, path);
                    ConnectExecTails(prevTails, cn);
                    return cn;
                }
            case KsCall call:
                {
                    var fn = AddBuiltin(call.MethodName, path);
                    WireCallArgs(fn, call.Args, $"{path}/args");
                    ConnectExecTails(prevTails, fn);
                    return fn;
                }
            case KsPipeline pipe:
                return RenderPipelineAsCondition(pipe, path, prevTails);
            default:
                throw new InvalidOperationException($"Unexpected condition node: {cond.GetType().Name}");
        }
    }

    /// <summary>
    /// Renders a KsPipeline condition as a chain of data nodes and returns the last
    /// function node whose output is the condition value.
    /// </summary>
    private BlueprintNode RenderPipelineAsCondition(KsPipeline pipe, string path, List<ExecTail> prevTails)
    {
        // Same exec-chain threading as RenderPipelineStmt: every source and every
        // segment node participates in the exec graph (v6 contract §3.3.1/§3.3.2 —
        // "every non-definition node participates in the exec graph", else the data
        // sub-graph feeding a control-flow pin is ambiguous during reverse translation).
        // prevTails → src0 → src1 → seg0 → ... → lastFunc; the caller threads
        // lastFunc's Exec out into the Branch/Each/While/Switch node.
        var currentTails = prevTails;
        BlueprintNode? lastFunc = null;
        var sourceNodes = new List<BlueprintNode>();

        for (int i = 0; i < pipe.Sources.Length; i++)
        {
            var srcNode = RenderSourceAsNode(pipe.Sources[i], $"{path}/src/{i}");
            sourceNodes.Add(srcNode);
            ConnectExecTails(currentTails, srcNode);
            currentTails = [new ExecTail(srcNode, BpPinNames.Exec)];
        }

        for (int i = 0; i < pipe.Segments.Length; i++)
        {
            var seg = pipe.Segments[i];
            if (seg.Args.Length == 0 && !_registry.Contains(seg.Target))
            {
                var vn = AddUsageNode(new VariableNode
                {
                    Name = seg.Target, VarName = seg.Target,
                    VarKind = VariableKind.PubVar,
                }, $"{path}/seg/{i}");
                if (lastFunc is not null) ConnectValue(lastFunc, vn);
                ConnectExecTails(currentTails, vn);
                currentTails = [new ExecTail(vn, BpPinNames.Exec)];
                lastFunc = vn;
            }
            else
            {
                var fn = AddBuiltin(seg.Target, $"{path}/seg/{i}");
                // Per-segment inline comment (condition pipeline) → this segment's node Comment.
                if (seg.Comment is { Length: > 0 })
                    fn.Comment = seg.Comment;
                WireCallArgs(fn, seg.Args, $"{path}/seg/{i}/args");
                if (lastFunc is null)
                {
                    ConnectPipelineSources(fn, seg.Args, sourceNodes);
                }
                else
                {
                    // See RenderPipelineStmt: connect by direction, not pin-name match.
                    ConnectValue(lastFunc, fn);
                }
                ConnectExecTails(currentTails, fn);
                currentTails = [new ExecTail(fn, BpPinNames.Exec)];
                lastFunc = fn;
            }
        }

        if (lastFunc is not null)
            return lastFunc;
        if (sourceNodes.Count > 0)
            return sourceNodes[0];

        // No sources and no segments — safety net (Parser rejects empty conditions).
        var fallback = AddUsageNode(new ConstNode { Name = "true", ConstName = "true", ConstValue = "true" }, $"{path}/fallback");
        ConnectExecTails(currentTails, fallback);
        return fallback;
    }

    /// <summary>
    /// Renders a single KsNode as a data-source BP node. When <paramref name="prevTails"/>
    /// is non-null, the node is threaded into the exec chain (usage sites of
    /// control-flow sources); when null the caller (RenderPipelineStmt) threads it itself.
    /// </summary>
    private BlueprintNode RenderSourceAsNode(KsNode node, string path, List<ExecTail>? prevTails = null)
    {
        switch (node)
        {
            case KsLiteral lit:
                {
                    var cn = AddUsageNode(new ConstNode
                    {
                        Name = lit.Value?.ToString() ?? "null",
                        ConstName = lit.Value?.ToString() ?? "null",
                        ConstValue = lit.Value?.ToString(),
                    }, path);
                    if (lit.Comment is { Length: > 0 })
                        cn.Comment = lit.Comment;
                    if (prevTails is not null) ConnectExecTails(prevTails, cn);
                    return cn;
                }
            case KsIdentifier id:
                {
                    var vn = AddUsageNode(new VariableNode
                    {
                        Name = id.Name, VarName = id.Name,
                        VarKind = VariableKind.PubVar,
                    }, path);
                    if (id.Comment is { Length: > 0 })
                        vn.Comment = id.Comment;
                    if (prevTails is not null) ConnectExecTails(prevTails, vn);
                    return vn;
                }
            case KsCall call:
                {
                    var fn = AddBuiltin(call.MethodName, path);
                    if (call.Comment is { Length: > 0 })
                        fn.Comment = call.Comment;
                    WireCallArgs(fn, call.Args, $"{path}/args");
                    if (prevTails is not null) ConnectExecTails(prevTails, fn);
                    return fn;
                }
            case KsPipeline pipe:
                // forEach source that is itself a pipeline (e.g. `loopMax > Range(0, _, 1) > forEach as i`).
                return RenderPipelineAsCondition(pipe, path, prevTails ?? []);
            default:
                throw new InvalidOperationException($"Unexpected pipeline source: {node.GetType().Name}");
        }
    }

    // ── Node factories ──

    private BuiltinFunctionNode AddBuiltin(string name, string path)
    {
        var n = new BuiltinFunctionNode { Name = name, FunctionName = name };
        n.InputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        n.OutputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Output, PinType.Execution));
        var bi = _registry.Get(name);
        if (bi is not null)
        {
            // Create named data pins from the function's PortSpec (v5.1 pattern).
            foreach (var port in bi.InputPorts)
                n.InputPins.Add(MakePin(port.Name, PinDirection.Input, port.Type));
            foreach (var port in bi.OutputPorts)
                n.OutputPins.Add(MakePin(port.Name, PinDirection.Output, port.Type));
        }
        else
        {
            // Fallback for unknown functions (e.g. user helpers not in registry):
            // single generic Value pin, as before.
            n.InputPins.Add(MakePin(BpPinNames.Value, PinDirection.Input, PinType.Any));
            n.OutputPins.Add(MakePin(BpPinNames.Value, PinDirection.Output, PinType.Any));
        }
        return Add(n, path);
    }

    /// <summary>
    /// Adds a *usage* (non-definition) ConstNode/VariableNode to the blueprint, equipped
    /// with Exec input/output pins in addition to the data pins the constructor set up.
    ///
    /// Per the v6 design principle: every node outside the definition region
    /// (/def/const/{name}, /def/var/{name}) participates in the execution graph.
    /// Definition nodes carry only data pins; usage nodes carry Exec pins so they can be
    /// reached by the exec chain (WalkExecChain in BpReverseTranslator). Without this,
    /// a pure assignment like `0 > counter` would create an isolated ConstNode→VariableNode
    /// sub-graph disconnected from the main exec chain — BP→IR round-trip would lose the
    /// statement entirely, and BP-only editors couldn't determine when it executes.
    /// </summary>
    private T AddUsageNode<T>(T node, string path) where T : BlueprintNode
    {
        // Prepend Exec pins (matching AddBuiltin's pin ordering convention: Exec first).
        node.InputPins.Insert(0, MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        node.OutputPins.Insert(0, MakePin(BpPinNames.Exec, PinDirection.Output, PinType.Execution));
        return Add(node, path);
    }

    private BuiltinFunctionNode AddCtrlNode(string name, string path)
    {
        var n = new BuiltinFunctionNode { Name = name, FunctionName = name };
        n.InputPins.Add(MakePin(BpPinNames.Exec, PinDirection.Input, PinType.Execution));
        return Add(n, path);
    }

    // ── ID + pin helpers ──

    private T Add<T>(T node, string path) where T : BlueprintNode
    {
        node.Id = NodeId.Of(path);
        _bp.Nodes.Add(node);
        return node;
    }

    private static BlueprintPin MakePin(string name, PinDirection dir, PinType type = PinType.Any)
        => new() { Id = Guid.NewGuid().ToString(), Name = name, Direction = dir, Type = type };

    // ── Connection helpers ──

    private void ConnectExecTails(List<ExecTail> tails, BlueprintNode target)
    {
        var tp = target.InputPins.Find(p => p.Name == BpPinNames.Exec);
        if (tp is null) return;
        foreach (var tail in tails)
        {
            var fp = tail.Node.OutputPins.Find(p => p.Name == tail.OutputPin);
            if (fp is not null)
                _bp.Connections.Add(new BlueprintConnection
                {
                    SourceNodeId = tail.Node.Id, SourcePinId = fp.Id,
                    TargetNodeId = target.Id, TargetPinId = tp.Id,
                });
        }
    }

    private void ConnectValue(BlueprintNode from, BlueprintNode to)
    {
        var fp = from.OutputPins.Find(p => p.Name != BpPinNames.Exec);
        var tp = to.InputPins.Find(p => p.Name != BpPinNames.Exec);
        if (fp is not null && tp is not null)
            _bp.Connections.Add(new BlueprintConnection
            {
                SourceNodeId = from.Id, SourcePinId = fp.Id,
                TargetNodeId = to.Id, TargetPinId = tp.Id,
            });
    }

    private void ConnectToInput(BlueprintNode from, BlueprintNode to, string inputPinName)
    {
        var fp = from.OutputPins.Find(p => p.Name != BpPinNames.Exec);
        var tp = to.InputPins.Find(p => p.Name == inputPinName);
        if (fp is not null && tp is not null)
            _bp.Connections.Add(new BlueprintConnection
            {
                SourceNodeId = from.Id, SourcePinId = fp.Id,
                TargetNodeId = to.Id, TargetPinId = tp.Id,
            });
    }
}
