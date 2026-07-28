namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

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
//   E4  KS103  Sub-scope termination — sub-scope tails must dangle; no leak to outer.
//   E5  KS104  Scope isolation — exec edges may not cross control-flow sub-scope boundary.
//   E6  KS105  Back-edge rule — no explicit exec cycles; loops are implicit.
//   D1  KS110  Data DAG — data graph must be acyclic.
//   D2  KS111  Single data input — each data input pin ≤1 incoming edge.
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
    public static string? Check(Blueprint blueprint)
    {
        if (blueprint.Nodes.Count == 0) return null;

        var nodeById = blueprint.Nodes.ToDictionary(n => n.Id);
        var entry = blueprint.Nodes.OfType<EntryNode>().FirstOrDefault();

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
                return $"KS102: 节点 '{n?.Name ?? nodeId}' 的 Exec input 有 {count} 条 incoming edges，违反唯一前驱约束（E3）。v6 End-pin 模型不允许菱形合流；建议：让子作用域末节点 exec-out 悬空，后续语句连接到控制流节点的 End pin。";
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
                return $"KS111: 节点 '{n?.Name ?? nodeId}' 的 data input pin 有 {count} 条 incoming edges，违反单输入约束（D2）。每个 data input pin 至多一条 incoming edge。";
            }
        }

        // ── E6 (KS105): No explicit exec back-edges ──
        if (HasCycle(blueprint, nodeById, execOnly: true))
            return "KS105: 检测到显式 exec 回环，违反回边规则（E6）。循环的\"回到循环头\"语义应通过 body 末节点 exec-out 悬空隐式表达；不允许显式画从 body 末节点到循环节点的 exec edge。";

        // ── D1 (KS110): Data DAG — data graph must be acyclic ──
        if (HasCycle(blueprint, nodeById, execOnly: false))
            return "KS110: Data graph 成环，违反 DAG 约束（D1）。值的定义不能循环依赖。";

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
                if (!nodeById.TryGetValue(id, out var n)) continue;
                foreach (var outPin in n.OutputPins)
                {
                    if (outPin.Type != PinType.Execution) continue;
                    foreach (var conn in blueprint.Connections)
                    {
                        if (conn.SourceNodeId != id || conn.SourcePinId != outPin.Id) continue;
                        if (nodeById.ContainsKey(conn.TargetNodeId))
                            bfs.Enqueue(conn.TargetNodeId);
                    }
                }
            }
            // Data-reachable set: nodes reachable from execReachable nodes via data edges.
            var dataReachable = new HashSet<string>();
            var dataBfs = new Queue<string>();
            foreach (var rid in execReachable)
            {
                if (!nodeById.TryGetValue(rid, out var n)) continue;
                foreach (var inPin in n.InputPins)
                {
                    if (inPin.Type == PinType.Execution) continue;
                    foreach (var conn in blueprint.Connections)
                    {
                        if (conn.TargetNodeId != rid || conn.TargetPinId != inPin.Id) continue;
                        if (!dataReachable.Contains(conn.SourceNodeId))
                        {
                            dataReachable.Add(conn.SourceNodeId);
                            dataBfs.Enqueue(conn.SourceNodeId);
                        }
                    }
                }
            }
            while (dataBfs.Count > 0)
            {
                var id = dataBfs.Dequeue();
                if (!nodeById.TryGetValue(id, out var n)) continue;
                foreach (var inPin in n.InputPins)
                {
                    if (inPin.Type == PinType.Execution) continue;
                    foreach (var conn in blueprint.Connections)
                    {
                        if (conn.TargetNodeId != id || conn.TargetPinId != inPin.Id) continue;
                        if (!dataReachable.Contains(conn.SourceNodeId))
                        {
                            dataReachable.Add(conn.SourceNodeId);
                            dataBfs.Enqueue(conn.SourceNodeId);
                        }
                    }
                }
            }
            foreach (var node in blueprint.Nodes)
            {
                if (IsDefinitionNode(node)) continue;
                if (execReachable.Contains(node.Id)) continue;
                if (dataReachable.Contains(node.Id)) continue;  // proxied via data edge
                return $"KS100: 节点 '{node.Name ?? node.Id}' 未接入 exec graph，违反连通性约束（E1）。建议：将该节点的 Exec input 连接到上游节点的 Exec output。";
            }
        }

        // ── E2 (KS101) + E4 (KS103) + E5 (KS104) + KS140: Structured reducibility walk ──
        // A single recursive walk verifies: graph reduces to a structured tree rooted
        // at EntryNode, sub-scope tails dangle, no scope leak, break/continue in loop.
        if (entry is not null)
        {
            var walkError = CheckStructuredReducibility(blueprint, nodeById, entry);
            if (walkError is not null) return walkError;
        }

        // ── C1 (KS120): Non-definition node must have Exec pins ──
        foreach (var node in blueprint.Nodes)
        {
            if (IsDefinitionNode(node)) continue;
            if (node is EntryNode) continue;  // EntryNode has no Exec input (only output)
            bool hasExecIn = node.InputPins.Any(p => p.Type == PinType.Execution);
            bool hasExecOut = node.OutputPins.Any(p => p.Type == PinType.Execution)
                              || IsTerminatorNode(node);
            if (!hasExecIn || !hasExecOut)
                return $"KS120: 节点 '{node.Name ?? node.Id}' 是使用型节点但缺少 Exec pin，违反双图耦合约束（C1）。除定义型节点（const/var 块声明）和终结符外，所有节点必须有 Exec input/output pin 并接入 exec graph。";
        }

        // ── N2 (KS130): VarName consistency ──
        var defVarNames = new HashSet<string>();
        foreach (var node in blueprint.Nodes.OfType<VariableNode>())
        {
            if (IsDefinitionNode(node) && node.VarName is not null)
                defVarNames.Add(node.VarName);
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
        foreach (var node in blueprint.Nodes.OfType<VariableNode>())
        {
            if (IsDefinitionNode(node)) continue;
            if (node.VarName is null) continue;
            if (!defVarNames.Contains(node.VarName))
                return $"KS130: 使用型 VariableNode '{node.VarName}' 没有对应的定义型节点，违反 VarName 一致性约束（N2）。建议：在 var {{ ... }} 块中声明该变量。";
        }

        return null;  // structurally valid
    }

    // ── Helpers ──

    private static bool IsDefinitionNode(BlueprintNode node)
    {
        // Definition nodes (from const/var blocks) have NO connections and no Exec pins.
        if (node is ConstNode cn && cn.InputPins.Count == 0
            && !cn.OutputPins.Any(p => p.Type == PinType.Execution))
            return true;
        if (node is VariableNode vn && !vn.InputPins.Any(p => p.Type == PinType.Execution)
            && !vn.OutputPins.Any(p => p.Type == PinType.Execution))
            return true;
        return false;
    }

    private static bool IsTerminatorNode(BlueprintNode node)
        => node is BuiltinFunctionNode fn
           && (fn.FunctionName == "break" || fn.FunctionName == "continue");

    private static bool IsControlFlowNode(BlueprintNode node)
        => node is BuiltinFunctionNode fn
           && (fn.FunctionName == "Branch" || fn.FunctionName == "Each"
               || fn.FunctionName == "While" || fn.FunctionName == "Switch");

    private static bool IsControlFlowName(string name)
        => name is "Branch" or "Each" or "While" or "Switch";

    /// <summary>
    /// Cycle detection. When execOnly is true, follows only exec pins (E6);
    /// when false, follows all pins (D1 data DAG).
    /// </summary>
    private static bool HasCycle(Blueprint bp, Dictionary<string, BlueprintNode> nodeById, bool execOnly)
    {
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();
        foreach (var node in bp.Nodes)
        {
            if (visited.Contains(node.Id)) continue;
            if (HasCycleFrom(bp, nodeById, node.Id, execOnly, visited, inStack))
                return true;
        }
        return false;
    }

    private static bool HasCycleFrom(Blueprint bp, Dictionary<string, BlueprintNode> nodeById,
        string nodeId, bool execOnly, HashSet<string> visited, HashSet<string> inStack)
    {
        if (inStack.Contains(nodeId)) return true;
        if (visited.Contains(nodeId)) return false;
        visited.Add(nodeId);
        inStack.Add(nodeId);

        if (nodeById.TryGetValue(nodeId, out var node))
        {
            foreach (var outPin in node.OutputPins)
            {
                if (execOnly && outPin.Type != PinType.Execution) continue;
                foreach (var conn in bp.Connections)
                {
                    if (conn.SourceNodeId != nodeId || conn.SourcePinId != outPin.Id) continue;
                    if (HasCycleFrom(bp, nodeById, conn.TargetNodeId, execOnly, visited, inStack))
                        return true;
                }
            }
        }

        inStack.Remove(nodeId);
        return false;
    }

    /// <summary>
    /// E2 (KS101) + E4 (KS103) + E5 (KS104) + KS140: Walks the exec graph as a
    /// structured tree. Verifies the graph reduces to a single structured tree rooted
    /// at the EntryNode. Each control-flow node's sub-scope pins start independent
    /// sub-walks that terminate at dangling tails. The End pin continues to the
    /// post-construct statement. Also enforces break/continue inside loop scope.
    /// </summary>
    private static string? CheckStructuredReducibility(Blueprint bp,
        Dictionary<string, BlueprintNode> nodeById, EntryNode entry)
    {
        var visited = new HashSet<string>();
        // The EntryNode itself is the root — mark it visited before walking its exec out.
        visited.Add(entry.Id);
        var error = WalkStructured(bp, nodeById, entry.Id, BpPinNames.Exec, visited, new Stack<string>());
        if (error is not null) return error;

        // Data-reachable set: nodes proxied into exec graph via data edges (condition
        // sub-graph nodes whose Exec pin is intentionally dangling). These are not
        // visited by the exec-only structured walk but are legitimately connected.
        var dataReachable = new HashSet<string>();
        foreach (var v in visited)
        {
            if (!nodeById.TryGetValue(v, out var n)) continue;
            foreach (var inPin in n.InputPins)
            {
                if (inPin.Type == PinType.Execution) continue;
                foreach (var conn in bp.Connections)
                {
                    if (conn.TargetNodeId != v || conn.TargetPinId != inPin.Id) continue;
                    CollectDataAncestors(bp, nodeById, conn.SourceNodeId, dataReachable);
                }
            }
        }

        // After the structured walk, every non-definition node should be visited or
        // data-reachable. Orphan nodes indicate non-structural edges.
        foreach (var node in bp.Nodes)
        {
            if (IsDefinitionNode(node)) continue;
            if (visited.Contains(node.Id)) continue;
            if (dataReachable.Contains(node.Id)) continue;
            return $"KS101: 节点 '{node.Name ?? node.Id}' 未被结构化归约遍历到，违反结构化归约性（E2）。exec graph 含非结构化模式。";
        }
        return null;
    }

    /// <summary>
    /// Recursively collects upstream data-edge ancestors (condition sub-graph nodes
    /// that are proxied into the exec graph via data flow).
    /// </summary>
    private static void CollectDataAncestors(Blueprint bp,
        Dictionary<string, BlueprintNode> nodeById, string nodeId, HashSet<string> set)
    {
        if (!set.Add(nodeId)) return;
        if (!nodeById.TryGetValue(nodeId, out var n)) return;
        foreach (var inPin in n.InputPins)
        {
            if (inPin.Type == PinType.Execution) continue;
            foreach (var conn in bp.Connections)
            {
                if (conn.TargetNodeId != nodeId || conn.TargetPinId != inPin.Id) continue;
                CollectDataAncestors(bp, nodeById, conn.SourceNodeId, set);
            }
        }
    }

    /// <summary>
    /// Recursive structured walk. Follows exec edges linearly; at control-flow nodes,
    /// recursively walks each sub-scope pin independently in a fresh sub-scope context,
    /// then continues from End pin. Returns an error message on structural violation.
    /// </summary>
    private static string? WalkStructured(Blueprint bp, Dictionary<string, BlueprintNode> nodeById,
        string sourceId, string pinName, HashSet<string> visited, Stack<string> loopScopeStack)
    {
        // Find all exec edges leaving (sourceId, pinName). Match by pin *name* (not id)
        // because a node may have auto-generated pins from InitializePinsFromDescriptor
        // alongside manually-created ones; name+type uniquely identifies the exec out pin.
        var source = nodeById.GetValueOrDefault(sourceId);
        if (source is null) return null;
        var hasOutPin = source.OutputPins.Any(p => p.Name == pinName && p.Type == PinType.Execution);
        if (!hasOutPin) return null;  // pin doesn't exist — dangling

        var targets = new List<string>();
        foreach (var conn in bp.Connections)
        {
            if (conn.SourceNodeId != sourceId) continue;
            // Resolve the source pin by id to verify it's the named exec pin.
            var srcPin = source.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Name != pinName || srcPin.Type != PinType.Execution) continue;
            targets.Add(conn.TargetNodeId);
        }
        if (targets.Count == 0) return null;  // dangling tail — fine (natural end)

        foreach (var targetId in targets)
        {
            if (!visited.Add(targetId))
            {
                // Re-visiting a node in a *different* path = merge point = structural error.
                var n = nodeById.GetValueOrDefault(targetId);
                return $"KS101: 节点 '{n?.Name ?? targetId}' 被多个 exec 路径访问（菱形合流），违反结构化归约性（E2）。v6 End-pin 模型不允许合流点；子作用域末节点应悬空，后续语句连接到控制流节点的 End pin。";
            }

            if (!nodeById.TryGetValue(targetId, out var node))
                return $"KS101: 节点 {targetId} 不存在。";

            if (node is BuiltinFunctionNode fn && IsControlFlowName(fn.FunctionName))
            {
                // Control-flow node: walk each sub-scope pin in a fresh context,
                // then continue from End pin.
                bool isLoop = fn.FunctionName is "Each" or "While";
                if (isLoop) loopScopeStack.Push(targetId);
                foreach (var subPin in fn.OutputPins)
                {
                    if (subPin.Name == BpPinNames.End) continue;
                    if (subPin.Type != PinType.Execution) continue;
                    var subError = WalkStructured(bp, nodeById, targetId, subPin.Name,
                        visited, loopScopeStack);
                    if (subError is not null) return subError;
                }
                if (isLoop) loopScopeStack.Pop();

                // Continue from End pin (the post-construct continuation).
                var endError = WalkStructured(bp, nodeById, targetId, BpPinNames.End,
                    visited, loopScopeStack);
                if (endError is not null) return endError;
            }
            else if (IsTerminatorNode(node))
            {
                // break/continue: must be inside a loop scope.
                if (loopScopeStack.Count == 0)
                    return $"KS140: {(node as BuiltinFunctionNode)!.FunctionName} 不在循环作用域内。break/continue 必须在 forEach 或 while body 内使用。";
                // Terminator has no exec-out — walk ends here.
            }
            else
            {
                // Ordinary node: continue the exec chain from its Exec output.
                var contError = WalkStructured(bp, nodeById, targetId, BpPinNames.Exec,
                    visited, loopScopeStack);
                if (contError is not null) return contError;
            }
        }
        return null;
    }
}