namespace KitX.WorkflowV6.Lens.BpGraphLens;

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// BpReverseTranslator — Blueprint → structured IR (the reverse of BpRenderer).
//
// Restores a Workflow IR tree from a Blueprint graph produced by BpRenderer.
// Walks the Exec-edge topology starting from the EntryNode, reconstructing the
// ordered statement body. Control-flow nodes (Branch/Each/While/Switch) are
// recursively expanded: their named output pins (True/False/Body/0/1/Default)
// define sub-bodies that become ThenBody/ElseBody/Body/Arms/Default on the
// corresponding IR statement.
//
// Data edges reconstruct KsNode expressions for conditions and sources:
//   • VariableNode → KsIdentifier
//   • ConstNode → KsLiteral
//   • BuiltinFunctionNode (data role) → KsCall, args from wired Value inputs
//     or pin DefaultValues
//
// Scope: closes the BP→IR→BP round-trip so that Project→Reverse yields an IR
// structurally equal to the original. Full bidirectional fidelity (BP-first
// edits producing real IR statements) is also enabled: AddNodeInBlock with a
// known BpNodeKind produces the matching IR statement in the diff path.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BpReverseTranslator
{
    private readonly BuiltinFunctionRegistry _registry;
    private Blueprint _bp = null!;

    // Immutable graph index built once per Reverse call: node lookup, the outgoing
    // exec-edge index, and all data-edge queries the walk performs (see GraphIndex).
    private GraphIndex _graph = null!;

    // Leading comments keyed by their anchor (statement primary) node id.
    private Dictionary<string, BlueprintGroupComment> _groupCommentsByAnchor = new();

    // Nodes already consumed by a control-flow node's condition/selector read.
    // WalkExecChain skips these so condition sub-graphs (e.g. `a, b > Compare("BEQ")`
    // feeding Branch.Condition) don't get re-emitted as standalone PipelineStatements.
    // Populated by MarkConsumedSubtree, called from PreMarkControlFlowConsumed when a
    // control-flow node is reached on the exec chain.
    private HashSet<string> _consumedNodes = new();

    // Every node visited by any WalkExecChain call (the whole Entry-reachable exec
    // graph, including sub-scope bodies). Populated alongside the per-call local
    // `visited` set; used to identify detached (exec-unreachable) components.
    private readonly HashSet<string> _mainChainVisited = new();

    public BpReverseTranslator(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Reconstructs a <see cref="Workflow"/> from <paramref name="bp"/>. The Blueprint
    /// must have been produced by <see cref="BpRenderer"/> (or be structurally equivalent).
    /// </summary>
    public Workflow Reverse(Blueprint bp)
    {
        ArgumentNullException.ThrowIfNull(bp);
        _bp = bp;
        IndexGraph();

        var ir = new Workflow();

        // Restore constants and global vars from definition nodes.
        // Definition nodes (emitted at /def/... by BpRenderer) have NO connections —
        // they are standalone declarations. Usage VariableNodes participate in data edges.
        foreach (var node in bp.Nodes)
        {
            if (node is ConstNode cn && cn.ConstName is not null && !_graph.HasAnyConnection(node))
            {
                ir = ir with
                {
                    Constants = ir.Constants.Add(cn.ConstName, new Constant
                    {
                        Name = cn.ConstName,
                        Type = cn.ConstType ?? "object",
                        // The KS script keeps its declaration initialiser (DefaultValue);
                        // the user value (ConstValue) is an override handled by the editor
                        // layer, so it never rewrites the script text.
                        InitialValueExpression = cn.DefaultValue,
                        // Rebuild the structured dict initialiser from the JSON payload BpRenderer
                        // stored in DefaultValue (Dict-Type design §3.3). Non-dict consts leave null.
                        DictInitializer = (cn.ConstType == "dict") ? TryDeserializeDictInit(cn.DefaultValue) : null,
                    }),
                };
            }
            else if (node is VariableNode vn && vn.VarKind == VariableKind.PubVar && vn.VarName is not null && !_graph.HasAnyConnection(node))
            {
                ir = ir with
                {
                    GlobalVars = ir.GlobalVars.Add(vn.VarName, new GlobalVar
                    {
                        Name = vn.VarName,
                        Type = vn.VarType ?? "object",
                        // Same split as constants: DefaultValue → script initialiser; the
                        // user value (VarInitialValue) is an editor-layer override. (This
                        // also fixes the old bug where the scalar initialiser was dropped.)
                        InitialValueExpression = vn.DefaultValue,
                        DictInitializer = (vn.VarType == "dict") ? TryDeserializeDictInit(vn.DefaultValue) : null,
                    }),
                };
            }
        }

        // Restore the top-level body by walking exec edges from the entry node.
        // A PluginTriggerNode replaces the EntryNode when TriggerType=PluginEvent (same
        // 0-in/1-Exec-out pin shape) — treat both as the exec-graph root.
        var entry = bp.Nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);
        if (entry is not null)
        {
            var body = WalkExecChain(entry, BpPinNames.Exec);
            ir = ir with { Body = [.. body] };
        }

        // Preserve exec-unreachable sub-graphs as detached snapshots (BP-side privilege):
        // nodes not visited by the main-chain walk and not consumed by control-flow data
        // reads are grouped into connected components and carried in Workflow.DetachedGraphs
        // so they survive KS↔BP and file round-trips instead of being silently dropped.
        ir = ir with { DetachedGraphs = CollectDetachedGraphs() };

        return ir;
    }

    /// <summary>
    /// Groups all exec-unreachable, non-consumed, non-definition nodes into connected
    /// components (edges of BOTH kinds — exec and data — join a component, so no edge
    /// between detached nodes is ever dropped). Definition-like nodes (const/var block
    /// declarations, which the definition-restore loop above already folded into
    /// Constants/GlobalVars) are excluded.
    /// </summary>
    private ImmutableArray<DetachedGraph> CollectDetachedGraphs()
    {
        var candidateIds = _bp.Nodes
            .Where(IsDetachedCandidate)
            .Select(n => n.Id)
            .ToHashSet();
        if (candidateIds.Count == 0) return [];

        var adj = candidateIds.ToDictionary(id => id, _ => new List<string>());
        foreach (var conn in _bp.Connections)
        {
            if (candidateIds.Contains(conn.SourceNodeId) && candidateIds.Contains(conn.TargetNodeId))
            {
                adj[conn.SourceNodeId].Add(conn.TargetNodeId);
                adj[conn.TargetNodeId].Add(conn.SourceNodeId);
            }
        }

        var visited = new HashSet<string>();
        var result = new List<DetachedGraph>();
        foreach (var id in candidateIds)
        {
            if (!visited.Add(id)) continue;

            // BFS the undirected component.
            var component = new List<string> { id };
            var stack = new Stack<string>();
            stack.Push(id);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var next in adj[cur])
                    if (visited.Add(next))
                    {
                        component.Add(next);
                        stack.Push(next);
                    }
            }

            var componentSet = component.ToHashSet();
            result.Add(new DetachedGraph
            {
                Id = component[0],
                Nodes = component.Select(nid => DetachedGraphUtil.CloneNode(_graph.GetNode(nid)!)).ToImmutableArray(),
                Connections = _bp.Connections
                    .Where(c => componentSet.Contains(c.SourceNodeId) && componentSet.Contains(c.TargetNodeId))
                    .Select(DetachedGraphUtil.CloneConnection)
                    .ToImmutableArray(),
            });
        }
        return [.. result];
    }

    /// <summary>
    /// True when the node belongs to a detached component: not definition-like, not on
    /// the Entry-reachable exec chain, not consumed by a control-flow data read.
    /// </summary>
    private bool IsDetachedCandidate(BlueprintNode n)
    {
        // The exec-graph roots are never detached (WalkExecChain starts from them but
        // never enqueues them, so they are absent from _mainChainVisited).
        if (n is EntryNode or PluginTriggerNode) return false;

        // Definition-like nodes were already folded into Constants/GlobalVars above
        // (same predicates as the definition-restore loop) — they are NOT detached.
        if (n is ConstNode cn && cn.ConstName is not null && !_graph.HasAnyConnection(n)) return false;
        if (n is VariableNode vn && vn.VarName is not null && !_graph.HasAnyConnection(n)) return false;
        if (n is ConstNode { IsDefinition: true } or VariableNode { IsDefinition: true }) return false;

        if (_mainChainVisited.Contains(n.Id)) return false;
        if (_consumedNodes.Contains(n.Id)) return false;
        return true;
    }

    /// <summary>
    /// Attempts to deserialize a JSON payload back into a <see cref="KsDictLiteral"/>. Returns
    /// null on failure (e.g. payload is plain text rather than JSON). Used to rebuild dict
    /// declaration initialisers during BP→IR reverse translation (Dict-Type design §3.3).
    /// </summary>
    private static KsDictLiteral? TryDeserializeDictInit(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<KsDictLiteral>(json); }
        catch (JsonException) { return null; }
    }

    // ── Graph indexing ──

    private void IndexGraph()
    {
        _graph = new GraphIndex(_bp);
        _consumedNodes.Clear();
        _mainChainVisited.Clear();
        _groupCommentsByAnchor = _bp.GroupComments
            .Where(g => !string.IsNullOrEmpty(g.AnchorNodeId))
            .GroupBy(g => g.AnchorNodeId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    // ── Exec chain walking (pipeline-merging model) ──
    //
    // The reverse translator walks the exec chain and groups consecutive nodes that
    // belong to the same KS pipeline statement. A group is closed when:
    //   1. A write-type var tap VariableNode is reached (it ends a pipeline as the
    //      assignment target).
    //   2. A control-flow node (Branch/Each/While/Switch) is reached — it forms its
    //      own statement; the preceding group is flushed first.
    //   3. A break/continue is reached.
    //   4. The next node has no data continuity with the current group (e.g. two
    //      independent bare calls Print("a") → Print("b")).
    //
    // Nodes marked as "_consumed" (condition sub-graphs of control-flow nodes) are
    // skipped entirely — they're reconstructed as KsNode expressions via ReadDataInput
    // when the consuming control-flow node is processed.
    //
    // Traversal model: the exec chain is treated as a linear sequence. Each node has
    // at most one outgoing "main" exec edge (control-flow nodes have multiple named
    // outputs like True/False/Body/End, which are handled by WalkBuiltinFunction's
    // recursive calls to WalkExecChain). We follow the chain via a queue, pushing the
    // exec-out target of each visited node so the traversal continues naturally.

    /// <summary>
    /// Walks the exec chain starting from <paramref name="source"/>'s <paramref name="pinName"/>
    /// output pin, reconstructing the ordered list of IR statements. Consecutive nodes
    /// participating in the same KS pipeline are merged into a single PipelineStatement.
    /// </summary>
    private List<Statement> WalkExecChain(BlueprintNode source, string pinName)
    {
        var result = new List<Statement>();
        var group = new List<BlueprintNode>();
        var visited = new HashSet<string>();

        var queue = new Queue<BlueprintNode>();
        if (_graph.TryGetExecTargets(source.Id, pinName, out var initialTargets))
        {
            foreach (var t in initialTargets)
                queue.Enqueue(t);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node.Id)) continue;  // already processed (e.g. if-merge node)
            _mainChainVisited.Add(node.Id);       // detached-graph tracking (shared across sub-walks)

            // Already-consumed node (part of a control-flow condition sub-graph).
            if (_consumedNodes.Contains(node.Id))
            {
                FlushGroup();
                continue;
            }

            // Control-flow node: marks its own condition sub-graph as consumed, flushes
            // the in-progress group (filtering out newly-consumed nodes), then processes
            // the control-flow statement (which recursively walks its sub-scopes).
            // Note: break/continue are terminators, not control-flow statements, but
            // WalkBuiltinFunction handles them too — the routing must accept both.
            if (node is BuiltinFunctionNode fn
                && (BpPinNames.IsControlFlowName(fn.FunctionName) || BpPinNames.IsTerminatorName(fn.FunctionName)))
            {
                PreMarkControlFlowConsumed(fn);
                FlushGroup();
                result.AddRange(WalkBuiltinFunction(fn));
                continue;  // control-flow node's downstream handled by WalkBuiltinFunction
            }

            // Ordinary pipeline node — test continuity with the current group.
            bool canExtend = group.Count == 0 || IsDataContinuous(group[^1], node);
            if (!canExtend)
                FlushGroup();
            group.Add(node);

            // Write-type var tap closes the pipeline.
            if (_graph.IsWriteVarTap(node))
                FlushGroup();

            // Continue the linear exec chain by following this node's exec out.
            if (_graph.TryGetExecTargets(node.Id, BpPinNames.Exec, out var nextTargets))
            {
                foreach (var t in nextTargets)
                    queue.Enqueue(t);
            }
        }

        FlushGroup();
        return result;

        void FlushGroup()
        {
            // Filter out any nodes consumed by a control-flow condition sub-graph
            // (e.g. when group = [condNode] but condNode got consumed by Branch).
            var live = group.Where(n => !_consumedNodes.Contains(n.Id)).ToList();
            group.Clear();
            if (live.Count == 0) return;
            result.Add(BuildPipelineFromGroup(live));
        }
    }

    /// <summary>
    /// Pre-marks the condition/selector sub-graph of a control-flow node as consumed,
    /// so that FlushGroup filters out nodes that were speculatively added to the group
    /// before the control-flow node was recognised.
    /// </summary>
    private void PreMarkControlFlowConsumed(BuiltinFunctionNode fn)
    {
        string? dataInputPin = fn.FunctionName switch
        {
            "Branch" => BpPinNames.Condition,
            "While" => BpPinNames.Condition,
            "Each" => BpPinNames.List,
            "Switch" => BpPinNames.Selector,
            _ => null,
        };
        if (dataInputPin is null) return;

        var pin = fn.InputPins.Find(p => p.Name == dataInputPin);
        if (pin is null) return;
        // First connection (in connection order) targeting this pin — the original
        // scan broke after the first match. GraphIndex's per-pin list preserves that
        // order; see the GraphIndex header for the dangling-source convergence note.
        var edges = _graph.IncomingTo(fn.Id, pin.Id);
        if (edges is { Count: > 0 })
            MarkConsumedSubtree(edges[0].Source);
    }

    /// <summary>
    /// Determines whether <paramref name="next"/> can extend the current pipeline group
    /// (i.e. it's the next segment in the same KS pipeline as <paramref name="prev"/>).
    /// The rule is data-flow-driven: there must be a data edge between prev and next
    /// (or prev must be a read source and next is another read source joining the same
    /// pipeline's source list).
    /// </summary>
    private bool IsDataContinuous(BlueprintNode prev, BlueprintNode next)
    {
        bool prevIsRead = IsReadSource(prev);
        bool nextIsRead = IsReadSource(next);

        // Read → Read: a, b both sources of the same pipeline (e.g. `a, b > Compare`).
        // Only when the previous read actually FLOWS somewhere (has an outgoing data
        // edge) is it a genuine multi-source member; a read without any outgoing data
        // edge is a no-op exec anchor (BP-side usage node on the chain with no data
        // connections) and must be split into its own bare-line statement — otherwise
        // the reverse would fabricate a data edge that never existed.
        if (prevIsRead && nextIsRead && _graph.HasOutgoingDataEdge(prev)) return true;

        // Read → Function: function consumes prev's value (e.g. `a > Print`).
        if (prevIsRead && next is BuiltinFunctionNode fn)
            return _graph.HasIncomingDataFrom(fn, prev);

        // Read → VarTap: var tap receives prev's value (e.g. `0 > counter`).
        if (prevIsRead && next is VariableNode tapVn && _graph.HasIncomingDataEdge(tapVn))
            return _graph.HasIncomingDataFrom(tapVn, prev);

        // Function → Function: next function consumes prev function's output
        // (e.g. `Range > Print` — Range's output flows to Print's input).
        if (prev is BuiltinFunctionNode prevFn && next is BuiltinFunctionNode nextFn)
            return _graph.HasIncomingDataFrom(nextFn, prevFn);

        // Function → VarTap: var tap receives prev function's output (e.g. `func > counter`).
        if (prev is BuiltinFunctionNode prevFn2 && next is VariableNode vn2 && _graph.HasIncomingDataEdge(vn2))
            return _graph.HasIncomingDataFrom(vn2, prevFn2);

        // VarTap → Function / VarTap → VarTap: tap-mode var tap (has outgoing data edge)
        // acts as pass-through — its Value output may feed the next segment. This keeps
        // multi-segment pipelines like `0 > counter > Print` merged into one statement.
        if (prev is VariableNode prevTap && _graph.HasIncomingDataEdge(prevTap))
        {
            if (next is BuiltinFunctionNode nextFnFromTap)
                return _graph.HasIncomingDataFrom(nextFnFromTap, prevTap);
            if (next is VariableNode nextVnFromTap && _graph.HasIncomingDataEdge(nextVnFromTap))
                return _graph.HasIncomingDataFrom(nextVnFromTap, prevTap);
        }

        return false;
    }

    /// <summary>True if <paramref name="node"/> is a pipeline source (read role): ConstNode or read-type VariableNode.</summary>
    private bool IsReadSource(BlueprintNode node)
    {
        if (node is ConstNode) return true;
        if (node is VariableNode vn && !_graph.HasIncomingDataEdge(vn)) return true;
        return false;
    }

    /// <summary>
    /// Builds a single PipelineStatement from a group of consecutive exec-chain nodes.
    /// The first node determines the Sources (read VarNode/ConstNode → source;
    /// function/var-tap → goes into Segments). Subsequent nodes append to Segments.
    /// Bare call form (Sources=[KsCall], Segments=[]) is preserved when the group is
    /// a single function node with no wired inputs.
    ///
    /// Source ORDER follows the first consuming function segment's WIRED INPUT PIN
    /// declaration order (BP data edges are the semantic truth), NOT the exec-chain
    /// order — a manually re-wired exec chain must not scramble which source lands on
    /// which argument placeholder (`b, a > Compare("BEQ", _, _)` would feed A=b).
    /// </summary>
    private Statement BuildPipelineFromGroup(List<BlueprintNode> group)
    {
        var primary = group[0];
        var sourceNodes = new List<BlueprintNode>();
        var segments = ImmutableArray.CreateBuilder<Segment>();

        // Walk the group left-to-right, dispatching by node type.
        // Read ConstNode / read VariableNode → Sources
        // Function node → Segments.Add (with Arguments reconstructed)
        // Var tap VariableNode → Segments.Add (IsVariableTap=true)
        // Write VariableNode (no outgoing data) → Segments.Add (IsVariableTap=true, closes pipeline)
        BlueprintNode? lastFuncOrTap = null;

        foreach (var node in group)
        {
            switch (node)
            {
                case ConstNode cn:
                    sourceNodes.Add(cn);
                    break;
                case VariableNode vn:
                    if (_graph.HasIncomingDataEdge(vn))
                    {
                        // Var tap segment (write or tap); the tap node's Comment is its
                        // inline segment comment (`> x // cmt`).
                        segments.Add(new Segment
                        {
                            Target = vn.VarName ?? vn.Name,
                            IsVariableTap = true,
                            Comment = vn.Comment is { Length: > 0 } ? vn.Comment : null,
                        });
                        lastFuncOrTap = vn;
                    }
                    else
                    {
                        // Read VariableNode → source identifier.
                        sourceNodes.Add(vn);
                    }
                    break;
                case BuiltinFunctionNode fn:
                    // A function that STARTS the group with NO wired inputs is a function
                    // SOURCE (`PluginCall(...) > JsonAsString > x`), not a segment — it
                    // must be restored as a KsCall source, otherwise the pipeline's
                    // leading call is dropped (round-trip produces `> PluginCall(...)`).
                    // The bare-call form (whole group = single function) is handled below.
                    if (IsGroupLeadingFunctionSource(fn, group))
                    {
                        sourceNodes.Add(fn);
                        break;
                    }
                    // Build the segment with full Arguments (preserves literals + placeholders).
                    var seg = BuildSegmentFromFunctionNode(fn);
                    // Sources that feed this function via wired inputs are collected
                    // when they appear earlier in the group as ConstNode/read VarNode.
                    // But if the function is the FIRST node in group (no preceding read
                    // sources), it's a bare call form — handle below.
                    segments.Add(seg);
                    lastFuncOrTap = fn;
                    break;
            }
        }

        // Source order follows the first consuming function segment's wired input pin
        // declaration order (data edges = semantic truth; exec order may diverge after
        // manual rewiring and must not scramble placeholder assignment).
        ReorderSourcesByPinOrder(group, sourceNodes);
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        foreach (var n in sourceNodes)
        {
            // Function sources keep the literal-inlined KsCall reconstruction (bare
            // `PluginCall(...)` at group head); read nodes convert via NodeToKsNode.
            sources.Add(n is BuiltinFunctionNode fnSrc && IsGroupLeadingFunctionSource(fnSrc, group)
                ? BuildKsCallFromFunctionNode(fnSrc)
                : NodeToKsNode(n));
        }

        // Determine bare call vs pipeline form.
        // Bare call: single function node with no wired inputs AND no preceding sources.
        var bareCall = TryBuildBareCall(group);
        if (bareCall is not null) return bareCall;

        // Pipeline form: Sources=[collected sources], Segments=[collected segments].
        // The primary node's Comment is the LAST SEGMENT's inline comment (rendered on
        // the segment line and read back by BuildSegmentFromFunctionNode / the tap
        // branch) — NOT a statement trailing comment. The statement TrailingComment
        // lives on the LAST SOURCE node (parser capture point A reads it back from the
        // source list's final line), so we lift it off the final source here.
        var primaryForComments = lastFuncOrTap ?? primary;
        var leadingOnly = ReadComments(primaryForComments).Leading;
        string? pipeTrailing = LiftTrailingSourceComment(sources);
        return WithFingerprint(new PipelineStatement
        {
            Fingerprint = default,
            Sources = sources.ToImmutable(),
            Segments = segments.ToImmutable(),
            LeadingComment = leadingOnly,
            TrailingComment = pipeTrailing,
        });
    }

    /// <summary>
    /// True when <paramref name="fn"/> is the group-leading function source: it occupies
    /// position 0 of the group AND has no wired data inputs. Such a function is restored
    /// as a KsCall source rather than a pipeline segment (the bare-call / leading-call
    /// forms), so it is never treated as a consuming segment.
    /// </summary>
    private bool IsGroupLeadingFunctionSource(BuiltinFunctionNode fn, List<BlueprintNode> group)
        => ReferenceEquals(group[0], fn) && !_graph.HasWiredInputs(fn);

    /// <summary>
    /// Attempts to build the bare-call form: a group that is a SINGLE function node
    /// with no wired inputs and no preceding sources. Returns null when the group is
    /// not in that form (caller falls through to the pipeline form).
    /// </summary>
    private Statement? TryBuildBareCall(List<BlueprintNode> group)
    {
        if (group.Count == 1 && group[0] is BuiltinFunctionNode singleFn
            && !_graph.HasWiredInputs(singleFn))
        {
            var (leading, trailing) = ReadComments(singleFn);
            var call = BuildKsCallFromFunctionNode(singleFn);
            return WithFingerprint(new PipelineStatement
            {
                Fingerprint = default,
                Sources = [call],
                Segments = [],
                LeadingComment = leading,
                TrailingComment = trailing,
            });
        }
        return null;
    }

    /// <summary>
    /// Lifts the statement TrailingComment off the LAST SOURCE node (parser capture
    /// point A reads it back from the source list's final line) and clears the source's
    /// own inline comment so it isn't duplicated on re-parse.
    /// </summary>
    private static string? LiftTrailingSourceComment(ImmutableArray<KsNode>.Builder sources)
    {
        if (sources.Count > 0 && sources[^1].Comment is { Length: > 0 })
        {
            var trailing = sources[^1].Comment;
            sources[^1] = sources[^1] with { Comment = null };
            return trailing;
        }
        return null;
    }

    /// <summary>
    /// Reorders <paramref name="sourceNodes"/> so that sources feeding the first
    /// consuming function segment appear in that segment's WIRED INPUT PIN declaration
    /// order (the order the KS placeholder `_` slots will be filled). Sources not wired
    /// to any input pin of the consumer keep their relative order afterwards.
    /// Var-tap segments consume a single upstream value and never participate.
    /// </summary>
    private void ReorderSourcesByPinOrder(List<BlueprintNode> group, List<BlueprintNode> sourceNodes)
    {
        // The first consuming function segment: a BuiltinFunctionNode that is NOT the
        // group-leading function source (bare `PluginCall(...)` at position 0 with no
        // wired inputs is a source itself, not a consumer).
        BuiltinFunctionNode? consumer = null;
        foreach (var node in group)
        {
            if (node is not BuiltinFunctionNode fn) continue;
            if (IsGroupLeadingFunctionSource(fn, group)) continue;
            consumer = fn;
            break;
        }
        if (consumer is null || sourceNodes.Count <= 1) return;

        var pinOrdered = new List<BlueprintNode>();
        var seen = new HashSet<BlueprintNode>();
        foreach (var pin in consumer.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            // First connection (in connection order) targeting this pin; the original
            // scan broke after the first match regardless of source resolution.
            var edges = _graph.IncomingTo(consumer.Id, pin.Id);
            if (edges is { Count: > 0 })
            {
                var src = edges[0].Source;
                if (sourceNodes.Contains(src) && seen.Add(src))
                    pinOrdered.Add(src);
            }
        }
        if (pinOrdered.Count == 0) return;

        var remaining = sourceNodes.Where(n => !pinOrdered.Contains(n)).ToList();
        sourceNodes.Clear();
        sourceNodes.AddRange(pinOrdered);
        sourceNodes.AddRange(remaining);
    }

    /// <summary>
    /// Builds a Segment from a function node's input pins. Arguments are populated
    /// ONLY when the segment has at least one literal DefaultValue arg — this preserves
    /// the literal values plus the explicit `_` placeholders marking wired positions
    /// (e.g. `Range(0, _, 1)`). When ALL non-Exec inputs are wired (no literals), the
    /// KS source is in the append form `i > Print` and Arguments stays empty — this
    /// matches the parser's canonical append representation.
    /// </summary>
    private Segment BuildSegmentFromFunctionNode(BuiltinFunctionNode fn)
    {
        var args = ImmutableArray.CreateBuilder<KsNode>();
        var rawArgs = ImmutableArray.CreateBuilder<string>();
        bool hasLiteralArg = false;

        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;

            // Check for a wired source (any resolved edge targeting this pin).
            bool isWired = _graph.IncomingTo(fn.Id, pin.Id) is { Count: > 0 };

            if (isWired)
            {
                args.Add(new KsPlaceholder { SourceText = "_" });
                rawArgs.Add("_");
            }
            else if (pin.DefaultValue is not null)
            {
                var lit = ParseDefaultValue(pin.DefaultValue);
                args.Add(lit);
                rawArgs.Add(lit.SourceText);
                hasLiteralArg = true;
            }
        }

        // Populate Arguments on the segment only when there are literal args — this
        // distinguishes `Range(0, _, 1)` (literal 0 and 1 force Arguments=[0, _, 1])
        // from `i > Print` (all wired, append form → Arguments=[]).
        ImmutableArray<KsNode> finalArgs = hasLiteralArg ? args.ToImmutable() : [];
        ImmutableArray<string> finalRawArgs = hasLiteralArg ? rawArgs.ToImmutable() : [];

        var segComment = fn.Comment is { Length: > 0 } ? fn.Comment : null;
        var seg = new Segment
        {
            Target = fn.FunctionName,
            IsVariableTap = false,
            Arguments = finalArgs,
            RawArguments = finalRawArgs,
            Comment = segComment,
        };
        return seg;
    }

    /// <summary>
    /// Marks the entire condition/selector sub-graph rooted at <paramref name="node"/>
    /// as consumed so WalkExecChain skips it. Recursively walks upstream data edges.
    /// Called from PreMarkControlFlowConsumed when a control-flow node is reached on
    /// the exec chain.
    /// </summary>
    private void MarkConsumedSubtree(BlueprintNode node)
    {
        if (!_consumedNodes.Add(node.Id)) return;  // already marked
        // Walk upstream data edges, mark all source nodes recursively. Edges whose
        // source pin resolves to a data pin only (the original predicate).
        var incoming = _graph.IncomingToNode(node.Id);
        if (incoming is null) return;
        foreach (var e in incoming)
        {
            if (e.SourcePin is null || e.SourcePin.Type == PinType.Execution) continue;
            MarkConsumedSubtree(e.Source);
        }
    }

    private List<Statement> WalkBuiltinFunction(BuiltinFunctionNode fn)
    {
        var result = new List<Statement>();
        switch (fn.FunctionName)
        {
            case "Branch":
                result.Add(ReverseIf(fn));
                // v6 End-pin model: statements after the if/else connect to Branch.End,
                // the single continuation point. Sub-scope body tails are dangling
                // (naturally ended), so no merge-point coordination is needed.
                result.AddRange(WalkExecChain(fn, BpPinNames.End));
                break;
            case "Each":
                result.Add(ReverseForEach(fn));
                // Statements after the loop connect to Each.End.
                result.AddRange(WalkExecChain(fn, BpPinNames.End));
                break;
            case "While":
                result.Add(ReverseWhile(fn));
                // Statements after the loop connect to While.End.
                result.AddRange(WalkExecChain(fn, BpPinNames.End));
                break;
            case "Switch":
                result.Add(ReverseSwitch(fn));
                // v6 End-pin model: statements after the switch connect to Switch.End,
                // the single continuation point. Arm body tails are dangling.
                result.AddRange(WalkExecChain(fn, BpPinNames.End));
                break;
            case "break":
                result.Add(WithFingerprint(ApplyComments(new BreakStatement { Fingerprint = default }, fn)));
                break;
            case "continue":
                result.Add(WithFingerprint(ApplyComments(new ContinueStatement { Fingerprint = default }, fn)));
                break;
            default:
                // Non-control-flow BuiltinFunctionNode must never reach here — the active
                // WalkExecChain routes them through BuildPipelineFromGroup instead. A hit
                // means a graph shape the reverse translator does not model: fail loudly
                // rather than emit a semantically wrong bare call (former ReversePipelineCall
                // fallback was dead code and produced wrong statements).
                throw new InvalidOperationException(
                    $"BpReverseTranslator: unexpected non-control-flow node '{fn.FunctionName}' in WalkBuiltinFunction " +
                    $"(id={fn.Id}). The graph shape is not covered by the structured reduction walk.");
        }
        return result;
    }

    // ── Control-flow reconstruction ──

    private Statement ReverseIf(BuiltinFunctionNode br)
    {
        var cond = ReadDataInput(br, BpPinNames.Condition);
        var thenBody = WalkExecChain(br, BpPinNames.True);
        var elseBody = WalkExecChain(br, BpPinNames.False);
        var (leading, trailing) = ReadComments(br);
        var stmt = new IfStatement
        {
            Fingerprint = default,
            Condition = cond,
            ThenBody = [.. thenBody],
            ElseBody = [.. elseBody],
            LeadingComment = leading,
            TrailingComment = trailing,
        };
        return WithFingerprint(stmt);
    }

    private Statement ReverseForEach(BuiltinFunctionNode each)
    {
        var source = ReadDataInput(each, BpPinNames.List);
        var body = WalkExecChain(each, BpPinNames.Body);
        var itemName = each.Properties.TryGetValue("ItemName", out var n) && !string.IsNullOrEmpty(n)
            ? n : "item";
        var (leading, trailing) = ReadComments(each);
        var stmt = new ForEachStatement
        {
            Fingerprint = default,
            Source = source,
            ItemName = itemName,
            Body = [.. body],
            LeadingComment = leading,
            TrailingComment = trailing,
        };
        return WithFingerprint(stmt);
    }

    private Statement ReverseWhile(BuiltinFunctionNode wh)
    {
        var cond = ReadDataInput(wh, BpPinNames.Condition);
        var body = WalkExecChain(wh, BpPinNames.Body);
        var (leading, trailing) = ReadComments(wh);
        var stmt = new WhileStatement
        {
            Fingerprint = default,
            Condition = cond,
            Body = [.. body],
            LeadingComment = leading,
            TrailingComment = trailing,
        };
        return WithFingerprint(stmt);
    }

    private Statement ReverseSwitch(BuiltinFunctionNode sw)
    {
        var selector = ReadDataInput(sw, BpPinNames.Selector);
        var arms = ImmutableArray.CreateBuilder<ImmutableArray<Statement>>();
        var armLabels = ImmutableArray.CreateBuilder<int>();

        // Enumerate arm pins by scanning OutputPins for integer-named exec pins.
        // Preserve the pin insertion order (which mirrors the original KS arm order)
        // rather than sorting by label value — this keeps round-trip stable when arms
        // are not in ascending label order (e.g. BF: 43, 45, 62, 60, 46, 44, 91, 93).
        var armPins = sw.OutputPins
            .Where(p => p.Type == PinType.Execution && int.TryParse(p.Name, out _))
            .Select(p => (Label: int.Parse(p.Name), PinName: p.Name))
            .ToList();

        foreach (var (label, pinName) in armPins)
        {
            armLabels.Add(label);
            arms.Add([.. WalkExecChain(sw, pinName)]);
        }

        var defaultBody = _graph.HasExecTargets(sw.Id, BpPinNames.Default)
            ? WalkExecChain(sw, BpPinNames.Default)
            : new List<Statement>();
        var (leading, trailing) = ReadComments(sw);
        var stmt = new SwitchStatement
        {
            Fingerprint = default,
            Selector = selector,
            Arms = arms.ToImmutable(),
            ArmLabels = armLabels.ToImmutable(),
            Default = [.. defaultBody],
            LeadingComment = leading,
            TrailingComment = trailing,
        };
        return WithFingerprint(stmt);
    }

    /// <summary>Replaces the placeholder fingerprint with the real structural one.</summary>
    private static Statement WithFingerprint(Statement stmt) =>
        stmt with { Fingerprint = Fingerprint.Compute(stmt) };

    /// <summary>
    /// Reads the leading comment (from <see cref="Blueprint.GroupComments"/> anchored at
    /// <paramref name="primary"/>) and the trailing comment (from the node's own
    /// <c>Comment</c> field) for a statement whose primary node is <paramref name="primary"/>.
    /// </summary>
    private (string? Leading, string? Trailing) ReadComments(BlueprintNode primary)
    {
        string? trailing = primary.Comment is { Length: > 0 } ? primary.Comment : null;
        _groupCommentsByAnchor.TryGetValue(primary.Id, out var gc);
        string? leading = gc?.Comment is { Length: > 0 } ? gc.Comment : null;
        return (leading, trailing);
    }

    /// <summary>Sets LeadingComment/TrailingComment on a statement from its primary node.</summary>
    private Statement ApplyComments(Statement stmt, BlueprintNode primary)
    {
        var (leading, trailing) = ReadComments(primary);
        return stmt with { LeadingComment = leading, TrailingComment = trailing };
    }

    // ── Data input reconstruction ──

    /// <summary>
    /// Reads the KsNode expression feeding a named data input pin on <paramref name="node"/>.
    /// Returns a KsIdentifier("true") fallback when the pin is unwired (e.g. literal condition
    /// collapsed to DefaultValue by BpRenderer).
    /// </summary>
    private KsNode ReadDataInput(BlueprintNode node, string pinName)
    {
        var pin = node.InputPins.Find(p => p.Name == pinName);
        if (pin is null)
        {
            // Defensive fallback for malformed BP graphs (pin missing). Returns 'true'
            // so a degenerate graph still round-trips; the frontend's strong-constraint
            // editing prevents this shape from being reachable in practice.
            return MakeBoolLiteral(true);
        }

        // Find the incoming data connection targeting this pin. First edge (in
        // connection order) whose source pin resolves to a data pin — GraphIndex's
        // per-pin list preserves connection order, so this is the original first-match.
        var incoming = _graph.IncomingTo(node.Id, pin.Id);
        if (incoming is not null)
        {
            foreach (var e in incoming)
            {
                if (e.SourcePin is null || e.SourcePin.Type == PinType.Execution) continue;
                // Consumption is marked once, in WalkExecChain, by PreMarkControlFlowConsumed
                // before the control-flow node's sub-scope is walked — this method is only
                // reachable through that path, so no defensive re-mark is needed here.
                return NodeToKsNode(e.Source);
            }
        }

        // No wired source — use the pin's DefaultValue if available.
        if (pin.DefaultValue is not null)
            return ParseDefaultValue(pin.DefaultValue);

        // Defensive fallback for malformed BP graphs (default value missing) — see above.
        return MakeBoolLiteral(true);
    }

    /// <summary>Converts a data-source BP node into the corresponding KsNode expression.</summary>
    private KsNode NodeToKsNode(BlueprintNode node)
    {        KsNode result = node switch
        {
            // Defensive: a usage VariableNode whose name was never chosen (frontend
            // palette creation leaves VarName empty until the user picks one) must not
            // produce an empty identifier — fall back to the node's display name.
            VariableNode vn => new KsIdentifier { Name = IdentifierOrFallback(vn), SourceText = IdentifierOrFallback(vn) },
            ConstNode cn => ParseDefaultValue(cn.ConstValue ?? cn.ConstName ?? "null"),
            BuiltinFunctionNode fn => ReconstructPipelineOrCall(fn),
            // Defensive fallback for malformed BP graphs (unknown node type). Returns
            // 'true' so a degenerate graph still round-trips; unreachable through the
            // frontend's strong-constraint editing.
            _ => MakeBoolLiteral(true),
        };

        // Source-node inline comment (multi-line source lists, `a, // cmt`): only
        // NON-primary data-subgraph nodes carry it — the primary node's Comment is the
        // statement's TrailingComment (set by BpRenderer). Restore it onto the KsNode so
        // BP→KS round-trip keeps the source annotation.
        if (node.Comment is { Length: > 0 }
            && _bp.StatementNodeToPrimary.TryGetValue(node.Id, out var primaryId)
            && primaryId != node.Id)
        {
            result.Comment = node.Comment;
        }
        return result;
    }

    /// <summary>VarName with a defensive fallback for unnamed usage VariableNodes.</summary>
    private static string IdentifierOrFallback(VariableNode vn)
    {
        var name = vn.VarName ?? vn.Name;
        return string.IsNullOrEmpty(name) ? "var" : name;
    }

    /// <summary>
    /// Reconstructs a function node back into a <see cref="KsNode"/> expression. When the
    /// function's data input pins carry wired sources (variables/other nodes — which per
    /// the v6 bracket-narrowing rule can ONLY have arrived via pipeline sources, never as
    /// bracket args), reconstructs a <see cref="KsPipeline"/> with those sources and a
    /// single segment whose <see cref="KsPipelineSegment.Arguments"/> preserve the PIN
    /// ORDER: a literal pin → its literal arg, a wired pin → a <c>_</c> placeholder at
    /// that position (the pipeline source flows into it). When all data inputs are
    /// unwired (a bare literal-arg call like <c>Print("x")</c>), reconstructs a flat
    /// <see cref="KsCall"/>.
    /// </summary>
    /// <remarks>
    /// <para>Positional <c>_</c> reconstruction is REQUIRED for semantic correctness: a
    /// source that wired into a non-last pin (e.g. <c>loopMax &gt; Range(0, _, 1)</c>
    /// where loopMax feeds the <em>To</em> pin, not the last <em>Step</em> pin) must keep
    /// its <c>_</c> slot, otherwise the append rule would route the source into the wrong
    /// pin on re-parse (Range(0,1) + append → Step, corrupting the To/Step values).</para>
    /// <para>Canonical-form note: the append form (<c>a, b &gt; Compare("BEQ")</c>, no
    /// explicit <c>_</c>) and the explicit-<c>_</c> form (<c>Compare("BEQ", _, _)</c>)
    /// produce identical BP wiring, so BP→KS cannot tell them apart. We canonicalise to
    /// the explicit-<c>_</c> form (semantically unambiguous); an append-form input is
    /// "upgraded" to explicit <c>_</c> through BP round-trip — semantically equivalent,
    /// just a more explicit KS rendering.</para>
    /// <para>Single-segment conditions/sources are fully reconstructed. Multi-segment
    /// conditions where an intermediate segment is itself a function node remain
    /// partially reconstructed (the intermediate appears as a source via
    /// <see cref="NodeToKsNode"/>).</para>
    /// </remarks>
    private KsNode ReconstructPipelineOrCall(BuiltinFunctionNode fn)
    {
        // Read each non-Exec data pin IN ORDER. A wired pin → a `_` placeholder arg at
        // that position + the wired source; an unwired pin → its literal DefaultValue arg.
        // This preserves pin positions so the source routes into the correct pin on
        // re-parse (fixing the To/Step swap corruption for forms like Range(0, _, 1)).
        var args = ImmutableArray.CreateBuilder<KsNode>();
        var rawArgs = ImmutableArray.CreateBuilder<string>();
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        bool anyWired = false;
        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            KsNode? wired = null;
            // First resolved edge (in connection order) targeting this pin.
            var incoming = _graph.IncomingTo(fn.Id, pin.Id);
            if (incoming is { Count: > 0 })
                wired = NodeToKsNode(incoming[0].Source);
            if (wired is not null)
            {
                anyWired = true;
                sources.Add(wired);
                args.Add(new KsPlaceholder { SourceText = "_" });
                rawArgs.Add("_");
            }
            else
            {
                var lit = ParseDefaultValue(pin.DefaultValue ?? "null");
                args.Add(lit);
                rawArgs.Add(lit.SourceText);
            }
        }

        // Bare call form (no wired sources): flat KsCall — all-args-literal, v6-legal.
        if (!anyWired)
        {
            var flatArgs = args.ToImmutable();
            return new KsCall
            {
                MethodName = fn.FunctionName,
                FullMethodName = fn.FunctionName,
                Args = flatArgs,
                RawArgs = [.. rawArgs],
                SourceText = $"{fn.FunctionName}({string.Join(", ", flatArgs.Select(a => a.SourceText))})",
            };
        }

        // Pipeline form: sources → single segment (args preserve pin order: literals +
        // `_` placeholders at wired positions). The function node's Comment carries the
        // last condition segment's inline comment (forward: RenderPipelineAsCondition
        // sets seg.Comment → fn.Comment).
        var segComment = fn.Comment is { Length: > 0 } ? fn.Comment : null;
        var seg = new KsPipelineSegment
        {
            Target = fn.FunctionName,
            Args = args.ToImmutable(),
            RawArgs = rawArgs.ToImmutable(),
            IsVariableTap = false,
            Comment = segComment,
        };
        seg.SourceText = $"{fn.FunctionName}({string.Join(", ", rawArgs)})";
        var srcArr = sources.ToImmutable();
        return new KsPipeline
        {
            Sources = srcArr,
            Segments = [seg],
            SourceLine = srcArr.Length > 0 ? srcArr[0].SourceLine : 0,
        };
    }

    /// <summary>
    /// Builds a KsCall from a function node's named data input pins. Each pin is either
    /// wired (→ VariableNode/ConstNode/FunctionNode source) or carries a DefaultValue.
    /// Pins are read in order to reconstruct the original argument list.
    /// </summary>
    private KsCall BuildKsCallFromFunctionNode(BuiltinFunctionNode fn)
    {
        var args = ImmutableArray.CreateBuilder<KsNode>();
        // Read data input pins in order (exclude Exec), matching the InputPorts order
        // that BpRenderer used when creating the node.
        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            // Check for a wired data source first (first resolved edge in connection order).
            var incoming = _graph.IncomingTo(fn.Id, pin.Id);
            KsNode? wired = incoming is { Count: > 0 } ? NodeToKsNode(incoming[0].Source) : null;
            args.Add(wired ?? ParseDefaultValue(pin.DefaultValue ?? "null"));
        }
        return new KsCall
        {
            MethodName = fn.FunctionName,
            FullMethodName = fn.FunctionName,
            Args = args.ToImmutable(),
            RawArgs = [.. args.Select(a => a.SourceText)],
            SourceText = $"{fn.FunctionName}({string.Join(", ", args.Select(a => a.SourceText))})",
        };
    }

    private static KsLiteral ParseDefaultValue(string value)
    {
        if (value is null) return MakeBoolLiteral(true);
        if (bool.TryParse(value, out var b)) return MakeBoolLiteral(b);
        if (int.TryParse(value, out var i)) return new KsLiteral { Kind = KsLiteralKind.Integer, Value = i, SourceText = value };
        if (double.TryParse(value, out var d)) return new KsLiteral { Kind = KsLiteralKind.Double, Value = d, SourceText = value };
        // String literal: BpRenderer stores raw value (without quotes); wrap as string.
        return new KsLiteral { Kind = KsLiteralKind.String, Value = value, SourceText = $"\"{value}\"" };
    }

    private static KsLiteral MakeBoolLiteral(bool value)
        => new() { Kind = KsLiteralKind.Boolean, Value = value, SourceText = value ? "true" : "false" };
}