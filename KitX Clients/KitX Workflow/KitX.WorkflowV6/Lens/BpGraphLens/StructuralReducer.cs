namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// StructuralReducer — validates that a Blueprint Exec graph is structurally
// well-formed per KScript-Blueprint-Correspondence.md §五 (BP-side constraint list).
//
// v6 End-pin model (pure tree-shaped DAG):
//   • Every control-flow node (Branch/Each/While/Switch) has an End output pin —
//     the single continuation point after the construct.
//   • Sub-scope body tails (True/False/Body/arms/Default) are dangling (exec-out
//     has no target) — "naturally ended, returns to End".
//   • No diamond-merge: each node's Exec input has at most one incoming edge.
//   • No explicit back-edges: loop iteration is implicit via dangling body tails.
//
// Constraints implemented (KS100-KS140):
//   E1  KS100  Connectivity — every non-definition node reachable from EntryNode.
//   E2  KS101  Structural reducibility — exec graph reduces to a structured tree.
//   E3  KS102  Unique predecessor — each Exec input ≤1 incoming edge (no merge).
//   E4  KS103  Sub-scope termination — covered INDIRECTLY by the E2 walk (no standalone code).
//   E5  KS104  Scope isolation — covered INDIRECTLY by the E2 walk (no standalone code).
//   E6  KS105  Back-edge rule — no explicit exec cycles; loops are implicit.
//   D1  KS110  Data DAG — data graph must be acyclic.
//   D2  KS111  Single data input — each data input pin ≤1 incoming edge.
//   D3  KS112  Data-scope reachability — a data edge's source must be same-scope or
//              outer relative to the consumer.
//   D4  KS113  Condition sub-graph containment — a control-flow node's condition/
//              source sub-graph nodes must share the control-flow node's scope.
//   C1  KS120  Non-definition node must have Exec pins.
//   N2  KS130  VarName consistency — usage VarNode has a matching definition VarNode.
//   KS140      break/continue must be inside a loop scope.
//
// MVP: one-shot full-graph check; no incremental update (§十二-J).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Validates the structural integrity of a Blueprint graph per the v6 End-pin model.
/// Pure: the blueprint is never mutated. Returns null on success or a user-facing
/// error message (with KS error code) on failure.
/// </summary>
internal static class StructuralReducer
{
    /// <summary>
    /// Checks whether the Blueprint's connections form a valid structured graph.
    /// Returns null on success, or a user-facing error message on failure.
    /// </summary>
    public static string? Check(Blueprint blueprint) => CheckInternal(blueprint)?.Message;

    /// <summary>
    /// Checks whether the Blueprint's connections form a valid structured graph
    /// and returns a structured <see cref="ConstraintViolation"/> on failure
    /// (with node IDs for frontend highlighting) or null on success.
    /// </summary>
    public static ConstraintViolation? CheckDetailed(Blueprint blueprint) => CheckInternal(blueprint);

    private static ConstraintViolation? CheckInternal(Blueprint blueprint)
    {
        if (blueprint.Nodes.Count == 0) return null;

        var nodeById = blueprint.Nodes.ToDictionary(n => n.Id);
        var graph = new GraphIndex(blueprint);
        // Entry or PluginTrigger (the trigger entry node replaces Entry when TriggerType=PluginEvent).
        var entry = blueprint.Nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);

        // ── E3 (KS102): Unique predecessor — every Exec input ≤1 incoming edge ──
        // The v6 End-pin model is a pure tree-shaped DAG; no diamond merge exists.
        var execInputCount = new Dictionary<(string NodeId, string PinId), int>();
        foreach (var conn in blueprint.Connections)
        {
            if (!nodeById.TryGetValue(conn.TargetNodeId, out var target)) continue;
            var targetPin = target.InputPins.Find(p => p.Id == conn.TargetPinId);
            if (targetPin is null || targetPin.Type != PinType.Execution) continue;
            var key = (conn.TargetNodeId, conn.TargetPinId);
            execInputCount[key] = execInputCount.GetValueOrDefault(key) + 1;
        }
        foreach (var ((nodeId, _), count) in execInputCount)
        {
            if (count > 1)
            {
                var n = nodeById.GetValueOrDefault(nodeId);
                return new ConstraintViolation(KsConstraintErrors.KS102, "E3", $"{KsConstraintErrors.KS102}: 节点 '{n?.Name ?? nodeId}' 的 Exec input 有 {count} 条 incoming edges，违反唯一前驱约束（E3）。v6 End-pin 模型不允许菱形合流；建议：让子作用域末节点 exec-out 悬空，后续语句连接到控制流节点的 End pin。", new[] { nodeId }, null, "让子作用域末节点 exec-out 悬空，后续语句连接到控制流节点的 End pin。", IsConnectionStructural: true);
            }
        }

        // ── D2 (KS111): Single data input — each data input pin ≤1 incoming edge ──
        var dataInputCount = new Dictionary<(string NodeId, string PinId), int>();
        foreach (var conn in blueprint.Connections)
        {
            if (!nodeById.TryGetValue(conn.TargetNodeId, out var target)) continue;
            var targetPin = target.InputPins.Find(p => p.Id == conn.TargetPinId);
            if (targetPin is null || targetPin.Type == PinType.Execution) continue;
            var key = (conn.TargetNodeId, conn.TargetPinId);
            dataInputCount[key] = dataInputCount.GetValueOrDefault(key) + 1;
        }
        foreach (var ((nodeId, _), count) in dataInputCount)
        {
            if (count > 1)
            {
                var n = nodeById.GetValueOrDefault(nodeId);
                return new ConstraintViolation(KsConstraintErrors.KS111, "D2", $"{KsConstraintErrors.KS111}: 节点 '{n?.Name ?? nodeId}' 的 data input pin 有 {count} 条 incoming edges，违反单输入约束（D2）。每个 data input pin 至多一条 incoming edge。", new[] { nodeId }, null, "每个 data input pin 至多一条 incoming edge，删除多余的连线。", IsConnectionStructural: true);
            }
        }

        // ── E6 (KS105): No explicit exec back-edges ──
        var execCycle = FindCycle(graph, blueprint, execOnly: true);
        if (execCycle is not null)
            return new ConstraintViolation(KsConstraintErrors.KS105, "E6", $"{KsConstraintErrors.KS105}: 检测到显式 exec 回环，违反回边规则（E6）。循环的\"回到循环头\"语义应通过 body 末节点 exec-out 悬空隐式表达；不允许显式画从 body 末节点到循环节点的 exec edge。",
                execCycle, null, "使用 Each/While 控制流节点表达循环，让 body 末节点 exec-out 悬空（自然结束）。", IsConnectionStructural: true);

        // ── D1 (KS110): Data DAG — data graph must be acyclic ──
        var dataCycle = FindCycle(graph, blueprint, execOnly: false);
        if (dataCycle is not null)
            return new ConstraintViolation(KsConstraintErrors.KS110, "D1", $"{KsConstraintErrors.KS110}: Data graph 成环，违反 DAG 约束（D1）。值的定义不能循环依赖。",
                dataCycle, null, "检查数据连线，消除循环依赖。", IsConnectionStructural: true);

        // ── E1 (KS100): Connectivity ──
        // Every non-definition node must be reachable from EntryNode via exec edges,
        // OR be a data-source sub-graph node that is "proxied" into the exec graph by
        // a consumer reachable via data edges (e.g. control-flow condition sub-graph
        // nodes whose Exec pin is intentionally dangling per the v6 design — see
        // KScriptGrammarRule §14.6 note: "控制流的条件节点虽有 Exec pin 但悬空不接入
        // 主 exec 链"). Such nodes are connected to the exec graph *through* their
        // data consumer, which is itself exec-reachable.
        if (entry is not null)
        {
            var execReachable = new HashSet<string>();
            var bfs = new Queue<string>();
            bfs.Enqueue(entry.Id);
            while (bfs.Count > 0)
            {
                var id = bfs.Dequeue();
                if (!execReachable.Add(id)) continue;
                var n = graph.GetNode(id);
                if (n is null) continue;
                foreach (var outPin in n.OutputPins)
                {
                    if (outPin.Type != PinType.Execution) continue;
                    // Loose exec index: the original scan filtered the source pin only.
                    if (graph.TryGetLooseExecTargets(id, outPin.Name, out var targets))
                        foreach (var t in targets)
                            bfs.Enqueue(t.Id);
                }
            }
            // Data-reachable set: nodes reachable from execReachable nodes via data edges.
            var dataReachable = new HashSet<string>();
            var dataBfs = new Queue<string>();
            foreach (var rid in execReachable)
            {
                var n = graph.GetNode(rid);
                if (n is null) continue;
                foreach (var inPin in n.InputPins)
                {
                    if (inPin.Type == PinType.Execution) continue;
                    EnqueueDataSources(graph, dataReachable, dataBfs, rid, inPin);
                }
            }
            while (dataBfs.Count > 0)
            {
                var id = dataBfs.Dequeue();
                var n = graph.GetNode(id);
                if (n is null) continue;
                foreach (var inPin in n.InputPins)
                {
                    if (inPin.Type == PinType.Execution) continue;
                    EnqueueDataSources(graph, dataReachable, dataBfs, id, inPin);
                }
            }
            foreach (var node in graph.Nodes)
            {
                if (BlueprintNodePredicates.IsDefinitionNodeByPins(node)) continue;
                if (execReachable.Contains(node.Id)) continue;
                if (dataReachable.Contains(node.Id)) continue;  // proxied via data edge
                return new ConstraintViolation(KsConstraintErrors.KS100, "E1", $"{KsConstraintErrors.KS100}: 节点 '{node.Name ?? node.Id}' 未接入 exec graph，违反连通性约束（E1）。建议：将该节点的 Exec input 连接到上游节点的 Exec output。", new[] { node.Id }, null, "将该节点的 Exec input 连接到上游节点的 Exec output。");
            }
        }

        // ── E2 (KS101) + E4 (KS103) + E5 (KS104) + KS140: Structured reducibility walk ──
        // A single recursive walk verifies: graph reduces to a structured tree rooted
        // at EntryNode, sub-scope tails dangle, no scope leak, break/continue in loop.
        if (entry is not null)
        {
            var walkError = CheckStructuredReducibility(graph, entry, out var nodeScope);
            if (walkError is not null) return walkError;

            // ── D4 (KS113): Condition sub-graph containment ──
            // Evaluated BEFORE D3/KS112: a condition source in the wrong scope also
            // violates data-scope reachability, and D4 is the more specific constraint
            // for control-flow condition/source sub-graphs.
            var d4Error = CheckConditionSubgraphContained(graph, nodeScope);
            if (d4Error is not null) return d4Error;

            // ── D3 (KS112): Data-scope reachability ──
            var d3Error = CheckDataScopeReachability(blueprint, nodeById, nodeScope);
            if (d3Error is not null) return d3Error;
        }

        // ── C1 (KS120): Non-definition node must have Exec pins ──
        foreach (var node in blueprint.Nodes)
        {
            if (BlueprintNodePredicates.IsDefinitionNodeByPins(node)) continue;
            if (node is EntryNode or PluginTriggerNode) continue;  // entry nodes have no Exec input (only output)
            bool hasExecIn = node.InputPins.Any(p => p.Type == PinType.Execution);
            bool hasExecOut = node.OutputPins.Any(p => p.Type == PinType.Execution)
                              || IsTerminatorNode(node);
            if (!hasExecIn || !hasExecOut)
                return new ConstraintViolation(KsConstraintErrors.KS120, "C1", $"{KsConstraintErrors.KS120}: 节点 '{node.Name ?? node.Id}' 是使用型节点但缺少 Exec pin，违反双图耦合约束（C1）。除定义型节点（const/var 块声明）和终结符外，所有节点必须有 Exec input/output pin 并接入 exec graph。", new[] { node.Id }, null, "为该节点添加 Exec input/output pin 并接入执行流。");
        }

        // ── N2 (KS130): VarName consistency ──
        var defVarNames = new HashSet<string>();
        foreach (var node in blueprint.Nodes.OfType<VariableNode>())
        {
            if (BlueprintNodePredicates.IsDefinitionNodeByPins(node) && node.VarName is not null)
                defVarNames.Add(node.VarName);
        }
        // const declarations (ConstNode definition nodes) are read-only, but a const
        // reference is still rendered as a VariableNode usage (VarKind=Const) — register
        // their names too, otherwise const references trip a KS130 false positive.
        foreach (var cn in blueprint.Nodes.OfType<ConstNode>())
        {
            if (cn.IsDefinition && cn.ConstName is { Length: > 0 })
                defVarNames.Add(cn.ConstName);
        }
        // ForEach Current item variable: declared via Each.Properties["ItemName"],
        // not via a definition VariableNode.
        foreach (var each in blueprint.Nodes.OfType<BuiltinFunctionNode>()
            .Where(n => n.FunctionName == "Each"))
        {
            if (each.Properties.TryGetValue("ItemName", out var itemName)
                && !string.IsNullOrEmpty(itemName))
                defVarNames.Add(itemName);
        }
        // dict declarations (DictNew) declare a variable the same way a definition
        // VariableNode does — register their DeclName (both DeclKind "var" and "const")
        // so usage VariableNodes referencing them satisfy KS130.
        foreach (var dictNew in blueprint.Nodes.OfType<BuiltinFunctionNode>()
            .Where(n => n.FunctionName == "DictNew"))
        {
            if (dictNew.Properties.TryGetValue("DeclName", out var declName)
                && !string.IsNullOrEmpty(declName))
                defVarNames.Add(declName);
        }
        foreach (var node in blueprint.Nodes.OfType<VariableNode>())
        {
            if (BlueprintNodePredicates.IsDefinitionNodeByPins(node)) continue;
            if (node.VarName is null) continue;
            if (!defVarNames.Contains(node.VarName))
                return new ConstraintViolation(KsConstraintErrors.KS130, "N2", $"{KsConstraintErrors.KS130}: 使用型 VariableNode '{node.VarName}' 没有对应的定义型节点，违反 VarName 一致性约束（N2）。建议：在 var {{ ... }} 块中声明该变量。", new[] { node.Id }, null, "在 var { ... } 块中声明该变量。");
        }

        return null;  // structurally valid
    }

    // ── Helpers ──

    /// <summary>
    /// Enqueues the resolved data sources feeding (nodeId, inPin) into the
    /// data-reachable BFS (E1 connectivity). Replaces the original per-pin connection
    /// scan with GraphIndex's per-pin data index.
    /// </summary>
    private static void EnqueueDataSources(GraphIndex graph, HashSet<string> dataReachable,
        Queue<string> dataBfs, string nodeId, BlueprintPin inPin)
    {
        var edges = graph.IncomingTo(nodeId, inPin.Id);
        if (edges is null) return;
        foreach (var e in edges)
        {
            var sourceId = e.Source.Id;
            if (!dataReachable.Contains(sourceId))
            {
                dataReachable.Add(sourceId);
                dataBfs.Enqueue(sourceId);
            }
        }
    }

    private static bool IsTerminatorNode(BlueprintNode node)
        => node is BuiltinFunctionNode fn && BpPinNames.IsTerminatorName(fn.FunctionName);

    /// <summary>
    /// Cycle detection. When execOnly is true, follows only exec pins (E6) via the
    /// GraphIndex LOOSE exec index (the original scan filtered the source pin only —
    /// a back-edge whose target pin does not resolve stays visible). When false,
    /// follows ALL pins (D1 data DAG) — this mode keeps its inline connection scan:
    /// GraphIndex's data index excludes exec edges, but the original D1 scan followed
    /// every edge, and mixed exec+data cycles (e.g. br→body→br, noted in KS113 tests)
    /// must still trip KS110. Returns null if no cycle is found, or a list of node IDs
    /// forming the cycle.
    /// </summary>
    private static List<string>? FindCycle(GraphIndex graph, Blueprint bp, bool execOnly)
    {
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();
        foreach (var node in bp.Nodes)
        {
            if (visited.Contains(node.Id)) continue;
            var cycle = FindCycleFrom(graph, bp, node.Id, execOnly, visited, inStack, new List<string>());
            if (cycle is not null) return cycle;
        }
        return null;
    }

    private static List<string>? FindCycleFrom(GraphIndex graph, Blueprint bp,
        string nodeId, bool execOnly, HashSet<string> visited, HashSet<string> inStack, List<string> path)
    {
        if (inStack.Contains(nodeId))
        {
            var startIdx = path.IndexOf(nodeId);
            return startIdx >= 0 ? path.GetRange(startIdx, path.Count - startIdx) : new List<string> { nodeId };
        }
        if (visited.Contains(nodeId)) return null;
        visited.Add(nodeId);
        inStack.Add(nodeId);
        path.Add(nodeId);

        var node = graph.GetNode(nodeId);
        if (node is not null)
        {
            if (execOnly)
            {
                foreach (var outPin in node.OutputPins)
                {
                    if (outPin.Type != PinType.Execution) continue;
                    if (!graph.TryGetLooseExecTargets(nodeId, outPin.Name, out var targets)) continue;
                    foreach (var t in targets)
                    {
                        var result = FindCycleFrom(graph, bp, t.Id, execOnly, visited, inStack, path);
                        if (result is not null) return result;
                    }
                }
            }
            else
            {
                // D1 (KS110): follow ALL edges (exec + data) — see FindCycle's note on
                // why GraphIndex's data-only index cannot reproduce this scan.
                foreach (var outPin in node.OutputPins)
                {
                    foreach (var conn in bp.Connections)
                    {
                        if (conn.SourceNodeId != nodeId || conn.SourcePinId != outPin.Id) continue;
                        var result = FindCycleFrom(graph, bp, conn.TargetNodeId, execOnly, visited, inStack, path);
                        if (result is not null) return result;
                    }
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(nodeId);
        return null;
    }

    /// <summary>
    /// E2 (KS101) + E4 (KS103) + E5 (KS104) + KS140: Walks the exec graph as a
    /// structured tree. Verifies the graph reduces to a single structured tree rooted
    /// at the EntryNode. Each control-flow node's sub-scope pins start independent
    /// sub-walks that terminate at dangling tails. The End pin continues to the
    /// post-construct statement. Also enforces break/continue inside loop scope.
    /// Populates <paramref name="nodeScope"/> (node id → scope path) for the
    /// D3/D4 scope checks. Traversal is driven by the shared ExecGraphWalker skeleton
    /// (see ExecGraphWalker for the recursion contract).
    /// </summary>
    private static ConstraintViolation? CheckStructuredReducibility(GraphIndex graph,
        BlueprintNode entry, out Dictionary<string, string> nodeScope)
    {
        var visitor = new StructuredWalkVisitor();
        // The EntryNode itself is the root — mark it visited before walking its exec out.
        visitor.Visited.Add(entry.Id);
        visitor.NodeScope[entry.Id] = NodePath.Top;
        visitor.Walk(graph, entry.Id, BpPinNames.Exec, NodePath.Top);
        if (visitor.Error is not null)
        {
            nodeScope = visitor.NodeScope;
            return visitor.Error;
        }
        nodeScope = visitor.NodeScope;

        // Data-reachable set: nodes proxied into exec graph via data edges (condition
        // sub-graph nodes whose Exec pin is intentionally dangling). These are not
        // visited by the exec-only structured walk but are legitimately connected.
        var dataReachable = new HashSet<string>();
        foreach (var v in visitor.Visited)
        {
            var n = graph.GetNode(v);
            if (n is null) continue;
            foreach (var inPin in n.InputPins)
            {
                if (inPin.Type == PinType.Execution) continue;
                var edges = graph.IncomingTo(v, inPin.Id);
                if (edges is null) continue;
                foreach (var e in edges)
                    CollectDataAncestors(graph, e.Source.Id, dataReachable);
            }
        }

        // After the structured walk, every non-definition node should be visited or
        // data-reachable. Orphan nodes indicate non-structural edges.
        foreach (var node in graph.Nodes)
        {
            if (BlueprintNodePredicates.IsDefinitionNodeByPins(node)) continue;
            if (visitor.Visited.Contains(node.Id)) continue;
            if (dataReachable.Contains(node.Id)) continue;
            return new ConstraintViolation(KsConstraintErrors.KS101, "E2", $"{KsConstraintErrors.KS101}: 节点 '{node.Name ?? node.Id}' 未被结构化归约遍历到，违反结构化归约性（E2）。exec graph 含非结构化模式。", new[] { node.Id }, null, "检查该节点的连线是否符合结构化控制流模式。", IsConnectionStructural: true);
        }
        return null;
    }

    /// <summary>
    /// Recursively collects upstream data-edge ancestors (condition sub-graph nodes
    /// that are proxied into the exec graph via data flow). Incoming edges come from
    /// GraphIndex's per-pin data index (target pin resolved — dangling connections
    /// cannot exist under strong-constraint editing).
    /// </summary>
    private static void CollectDataAncestors(GraphIndex graph, string nodeId, HashSet<string> set)
    {
        if (!set.Add(nodeId)) return;
        var n = graph.GetNode(nodeId);
        if (n is null) return;
        foreach (var inPin in n.InputPins)
        {
            if (inPin.Type == PinType.Execution) continue;
            var edges = graph.IncomingTo(nodeId, inPin.Id);
            if (edges is null) continue;
            foreach (var e in edges)
                CollectDataAncestors(graph, e.Source.Id, set);
        }
    }

    /// <summary>
    /// ExecGraphWalker visitor for the E2 structured-reducibility walk: records each
    /// node's scope path (KS112/KS113 inputs), reports KS101 on merge-point re-visits
    /// (short-circuiting the whole walk — the first error wins), and tracks the loop
    /// scope stack for KS140 (break/continue outside any loop). Loop-scope push/pop
    /// brackets Each/While sub-scope recursion, exactly like the original WalkStructured.
    /// </summary>
    private sealed class StructuredWalkVisitor : ExecGraphWalker
    {
        /// <summary>Nodes visited by the structured walk (the EntryNode is pre-seeded).</summary>
        public readonly HashSet<string> Visited = new();

        /// <summary>Node id → scope path (NodePath conventions: /top /then /else /body /arm/{index} /default).</summary>
        public readonly Dictionary<string, string> NodeScope = new();

        /// <summary>The first structural error, if the walk failed (short-circuits the walk).</summary>
        public ConstraintViolation? Error;

        private readonly Stack<string> _loopScopeStack = new();

        protected override VisitDecision OnNode(BlueprintNode node, string scopePath)
        {
            if (!Visited.Add(node.Id))
            {
                // Re-visiting a node in a *different* path = merge point = structural error.
                Error = new ConstraintViolation(KsConstraintErrors.KS101, "E2", $"{KsConstraintErrors.KS101}: 节点 '{node.Name ?? node.Id}' 被多个 exec 路径访问（菱形合流），违反结构化归约性（E2）。v6 End-pin 模型不允许合流点；子作用域末节点应悬空，后续语句连接到控制流节点的 End pin。", new[] { node.Id }, null, "子作用域末节点应悬空，后续语句连接到控制流节点的 End pin。", IsConnectionStructural: true);
                return VisitDecision.Stop;
            }

            // Record the node's scope BEFORE recursing into any sub-scopes: a control-flow
            // node belongs to its OUTER scope, while the nodes inside its bodies get the
            // sub-scope paths appended below. A re-visit (diamond merge) never reaches
            // here, so NodeScope is never overwritten.
            NodeScope[node.Id] = scopePath;

            if (node is BuiltinFunctionNode fn && BpPinNames.IsTerminatorName(fn.FunctionName))
            {
                // break/continue: must be inside a loop scope.
                if (_loopScopeStack.Count == 0)
                {
                    Error = new ConstraintViolation(KsConstraintErrors.KS140, "BreakContinue", $"{KsConstraintErrors.KS140}: {fn.FunctionName} 不在循环作用域内。break/continue 必须在 forEach 或 while body 内使用。", new[] { node.Id }, null, "将 break/continue 移到 forEach 或 while 的 body 内。", IsConnectionStructural: true);
                    return VisitDecision.Stop;
                }
                // Terminator has no exec-out — the walker ends the chain here.
            }
            return VisitDecision.Visit;
        }

        protected override void OnEnterControlFlow(BuiltinFunctionNode fn, string scopePath)
        {
            if (fn.FunctionName is "Each" or "While") _loopScopeStack.Push(fn.Id);
        }

        protected override void OnExitControlFlow(BuiltinFunctionNode fn, string scopePath)
        {
            if (fn.FunctionName is "Each" or "While") _loopScopeStack.Pop();
        }
    }

    /// <summary>
    /// D3 (KS112): Data-scope reachability — for every data edge, the source must
    /// live in the consumer's scope or an outer scope. Edges whose source or consumer
    /// has no recorded scope (e.g. DetachedGraph snapshot nodes) are skipped.
    /// </summary>
    private static ConstraintViolation? CheckDataScopeReachability(Blueprint bp,
        Dictionary<string, BlueprintNode> nodeById, Dictionary<string, string> nodeScope)
    {
        foreach (var conn in bp.Connections)
        {
            if (!nodeById.TryGetValue(conn.SourceNodeId, out var src)) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            if (!nodeScope.TryGetValue(conn.SourceNodeId, out var sourceScope)) continue;
            if (!nodeScope.TryGetValue(conn.TargetNodeId, out var consumerScope)) continue;
            if (consumerScope == sourceScope
                || consumerScope.StartsWith(sourceScope + "/", StringComparison.Ordinal)) continue;
            return new ConstraintViolation(KsConstraintErrors.KS112, "D3",
                $"{KsConstraintErrors.KS112}: 数据边从作用域 '{sourceScope}' 引用内层作用域 '{consumerScope}' 的节点，违反作用域可达性约束（D3）。建议：将该数据源移到与消费者同级或外层作用域。",
                new[] { conn.SourceNodeId, conn.TargetNodeId }, null,
                "将该数据源移到与消费者同级或外层作用域。", IsConnectionStructural: true);
        }
        return null;
    }

    /// <summary>
    /// D4 (KS113): Condition sub-graph containment — every node in a control-flow
    /// node's condition/source sub-graph (data ancestors of its data input pins) must
    /// share the control-flow node's exact scope. Nodes without a recorded scope are
    /// skipped (detached snapshots / definition nodes). Incoming edges come from
    /// GraphIndex's per-pin data index.
    /// </summary>
    private static ConstraintViolation? CheckConditionSubgraphContained(GraphIndex graph,
        Dictionary<string, string> nodeScope)
    {
        foreach (var node in graph.Nodes)
        {
            if (node is not BuiltinFunctionNode fn || !BpPinNames.IsControlFlowName(fn.FunctionName)) continue;
            if (!nodeScope.TryGetValue(fn.Id, out var ctrlScope)) continue;
            var subgraph = new HashSet<string>();
            foreach (var inPin in fn.InputPins)
            {
                if (inPin.Type == PinType.Execution) continue;
                var edges = graph.IncomingTo(fn.Id, inPin.Id);
                if (edges is null) continue;
                foreach (var e in edges)
                    CollectDataAncestors(graph, e.Source.Id, subgraph);
            }
            foreach (var id in subgraph)
            {
                if (!nodeScope.TryGetValue(id, out var s)) continue;
                if (s == ctrlScope) continue;
                var n = graph.GetNode(id);
                return new ConstraintViolation(KsConstraintErrors.KS113, "D4",
                    $"{KsConstraintErrors.KS113}: 控制流节点的条件/源子图节点越出同级作用域，违反条件子图 contained 约束（D4）。建议：将条件/源子图的所有节点连接到控制流节点的同级 exec 链。",
                    new[] { fn.Id, id }, null,
                    "将条件/源子图的所有节点连接到控制流节点的同级 exec 链。", IsConnectionStructural: true);
            }
        }
        return null;
    }
}
