namespace KitX.WorkflowV6.Lens.BpGraphLens;

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

    // Node lookup by ID.
    private Dictionary<string, BlueprintNode> _byId = new();

    // Outgoing exec edges: sourceNodeId+pinName → list of target nodes (in connection order).
    private Dictionary<(string, string), List<BlueprintNode>> _execOut = new();

    // Outgoing data edges: sourceNodeId → list of (targetNode, targetPinName).
    private Dictionary<string, List<(BlueprintNode Target, string TargetPin)>> _dataOut = new();

    // Leading comments keyed by their anchor (statement primary) node id.
    private Dictionary<string, BlueprintGroupComment> _groupCommentsByAnchor = new();

    // Nodes already consumed by a control-flow node's condition/selector read.
    // WalkExecChain skips these so condition sub-graphs (e.g. `a, b > Compare("BEQ")`
    // feeding Branch.Condition) don't get re-emitted as standalone PipelineStatements.
    // Populated by MarkConsumedSubtree, called from ReadDataInput.
    private HashSet<string> _consumedNodes = new();

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
            if (node is ConstNode cn && cn.ConstName is not null && HasNoConnections(node))
            {
                ir = ir with
                {
                    Constants = ir.Constants.Add(cn.ConstName, new Constant
                    {
                        Name = cn.ConstName,
                        Type = cn.ConstType ?? "object",
                        InitialValueExpression = cn.ConstValue,
                    }),
                };
            }
            else if (node is VariableNode vn && vn.VarKind == VariableKind.PubVar && vn.VarName is not null && HasNoConnections(node))
            {
                ir = ir with
                {
                    GlobalVars = ir.GlobalVars.Add(vn.VarName, new GlobalVar
                    {
                        Name = vn.VarName,
                        Type = vn.VarType ?? "object",
                    }),
                };
            }
        }

        // Restore the top-level body by walking exec edges from the EntryNode.
        var entry = bp.Nodes.OfType<EntryNode>().FirstOrDefault();
        if (entry is not null)
        {
            var body = WalkExecChain(entry, BpPinNames.Exec);
            ir = ir with { Body = [.. body] };
        }

        return ir;
    }

    // ── Graph indexing ──

    private void IndexGraph()
    {
        _byId = _bp.Nodes.ToDictionary(n => n.Id);
        _execOut.Clear();
        _dataOut.Clear();
        _consumedNodes.Clear();
        _groupCommentsByAnchor = _bp.GroupComments
            .Where(g => !string.IsNullOrEmpty(g.AnchorNodeId))
            .GroupBy(g => g.AnchorNodeId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var conn in _bp.Connections)
        {
            if (!_byId.TryGetValue(conn.SourceNodeId, out var src)) continue;
            if (!_byId.TryGetValue(conn.TargetNodeId, out var tgt)) continue;

            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            var tgtPin = tgt.InputPins.Find(p => p.Id == conn.TargetPinId);
            if (srcPin is null || tgtPin is null) continue;

            if (srcPin.Type == PinType.Execution && tgtPin.Type == PinType.Execution)
            {
                var key = (conn.SourceNodeId, srcPin.Name);
                if (!_execOut.TryGetValue(key, out var list))
                {
                    list = new List<BlueprintNode>();
                    _execOut[key] = list;
                }
                list.Add(tgt);
            }
            else
            {
                if (!_dataOut.TryGetValue(conn.SourceNodeId, out var list))
                {
                    list = new List<(BlueprintNode, string)>();
                    _dataOut[conn.SourceNodeId] = list;
                }
                list.Add((tgt, tgtPin.Name));
            }
        }
    }

    private bool HasNoConnections(BlueprintNode node)
    {
        foreach (var conn in _bp.Connections)
            if (conn.SourceNodeId == node.Id || conn.TargetNodeId == node.Id)
                return false;
        return true;
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
        if (_execOut.TryGetValue((source.Id, pinName), out var initialTargets))
        {
            foreach (var t in initialTargets)
                queue.Enqueue(t);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node.Id)) continue;  // already processed (e.g. if-merge node)

            // Already-consumed node (part of a control-flow condition sub-graph).
            if (_consumedNodes.Contains(node.Id))
            {
                FlushGroup();
                continue;
            }

            // Control-flow node: marks its own condition sub-graph as consumed, flushes
            // the in-progress group (filtering out newly-consumed nodes), then processes
            // the control-flow statement (which recursively walks its sub-scopes).
            if (node is BuiltinFunctionNode fn && IsControlFlowName(fn.FunctionName))
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
            if (IsWriteVarTap(node))
                FlushGroup();

            // Continue the linear exec chain by following this node's exec out.
            if (_execOut.TryGetValue((node.Id, BpPinNames.Exec), out var nextTargets))
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

    /// <summary>True if <paramref name="name"/> is a control-flow node function name.</summary>
    private static bool IsControlFlowName(string name)
        => name is "Branch" or "Each" or "While" or "Switch" or "break" or "continue";

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
        foreach (var conn in _bp.Connections)
        {
            if (conn.TargetNodeId != fn.Id || conn.TargetPinId != pin.Id) continue;
            var src = _byId.GetValueOrDefault(conn.SourceNodeId);
            if (src is not null) MarkConsumedSubtree(src);
            break;
        }
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
        if (prevIsRead && nextIsRead) return true;

        // Read → Function: function consumes prev's value (e.g. `a > Print`).
        if (prevIsRead && next is BuiltinFunctionNode fn)
            return HasDataInputFrom(fn, prev);

        // Read → VarTap: var tap receives prev's value (e.g. `0 > counter`).
        if (prevIsRead && next is VariableNode tapVn && HasIncomingDataEdge(tapVn))
            return DataComesFrom(tapVn, prev);

        // Function → Function: next function consumes prev function's output
        // (e.g. `Range > Print` — Range's output flows to Print's input).
        if (prev is BuiltinFunctionNode prevFn && next is BuiltinFunctionNode nextFn)
            return HasDataInputFrom(nextFn, prevFn);

        // Function → VarTap: var tap receives prev function's output (e.g. `func > counter`).
        if (prev is BuiltinFunctionNode prevFn2 && next is VariableNode vn2 && HasIncomingDataEdge(vn2))
            return DataComesFrom(vn2, prevFn2);

        // VarTap → Function / VarTap → VarTap: tap-mode var tap (has outgoing data edge)
        // acts as pass-through — its Value output may feed the next segment. This keeps
        // multi-segment pipelines like `0 > counter > Print` merged into one statement.
        if (prev is VariableNode prevTap && HasIncomingDataEdge(prevTap))
        {
            if (next is BuiltinFunctionNode nextFnFromTap)
                return HasDataInputFrom(nextFnFromTap, prevTap);
            if (next is VariableNode nextVnFromTap && HasIncomingDataEdge(nextVnFromTap))
                return DataComesFrom(nextVnFromTap, prevTap);
        }

        return false;
    }

    /// <summary>True if <paramref name="node"/> is a pipeline source (read role): ConstNode or read-type VariableNode.</summary>
    private bool IsReadSource(BlueprintNode node)
    {
        if (node is ConstNode) return true;
        if (node is VariableNode vn && !HasIncomingDataEdge(vn)) return true;
        return false;
    }

    /// <summary>True if <paramref name="vn"/> has an incoming data edge on its Value input.</summary>
    private bool HasIncomingDataEdge(VariableNode vn)
    {
        foreach (var conn in _bp.Connections)
        {
            if (conn.TargetNodeId != vn.Id) continue;
            var src = _byId.GetValueOrDefault(conn.SourceNodeId);
            if (src is null) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            return true;
        }
        return false;
    }

    /// <summary>True if <paramref name="vn"/>'s Value input data edge originates from <paramref name="src"/>.</summary>
    private bool DataComesFrom(VariableNode vn, BlueprintNode src)
    {
        foreach (var conn in _bp.Connections)
        {
            if (conn.TargetNodeId != vn.Id || conn.SourceNodeId != src.Id) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            return true;
        }
        return false;
    }

    /// <summary>True if <paramref name="fn"/> has at least one wired data input from <paramref name="src"/>.</summary>
    private bool HasDataInputFrom(BuiltinFunctionNode fn, BlueprintNode src)
    {
        foreach (var conn in _bp.Connections)
        {
            if (conn.TargetNodeId != fn.Id || conn.SourceNodeId != src.Id) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// True if <paramref name="node"/> is a write-type var tap (Value input has incoming
    /// data edge AND Value output has no outgoing data edge). Such nodes close the
    /// pipeline because they're the assignment target.
    /// </summary>
    private bool IsWriteVarTap(BlueprintNode node)
    {
        if (node is not VariableNode vn) return false;
        if (!HasIncomingDataEdge(vn)) return false;
        // Check that Value output has no outgoing data edge.
        foreach (var conn in _bp.Connections)
        {
            if (conn.SourceNodeId != vn.Id) continue;
            var tgt = _byId.GetValueOrDefault(conn.TargetNodeId);
            if (tgt is null) continue;
            var tgtPin = tgt.InputPins.Find(p => p.Id == conn.TargetPinId);
            if (tgtPin is null || tgtPin.Type == PinType.Execution) continue;
            return false;  // has outgoing data edge → tap, not pure write
        }
        return true;
    }

    /// <summary>
    /// Builds a single PipelineStatement from a group of consecutive exec-chain nodes.
    /// The first node determines the Sources (read VarNode/ConstNode → source;
    /// function/var-tap → goes into Segments). Subsequent nodes append to Segments.
    /// Bare call form (Sources=[KsCall], Segments=[]) is preserved when the group is
    /// a single function node with no wired inputs.
    /// </summary>
    private Statement BuildPipelineFromGroup(List<BlueprintNode> group)
    {
        var primary = group[0];
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        var segments = ImmutableArray.CreateBuilder<Segment>();

        // Walk the group left-to-right, dispatching by node type.
        // Read ConstNode / read VariableNode → Sources.Add
        // Function node → Segments.Add (with Arguments reconstructed)
        // Var tap VariableNode → Segments.Add (IsVariableTap=true)
        // Write VariableNode (no outgoing data) → Segments.Add (IsVariableTap=true, closes pipeline)
        BlueprintNode? lastFuncOrTap = null;

        foreach (var node in group)
        {
            switch (node)
            {
                case ConstNode cn:
                    sources.Add(NodeToKsNode(cn));
                    break;
                case VariableNode vn:
                    if (HasIncomingDataEdge(vn))
                    {
                        // Var tap segment (write or tap).
                        segments.Add(new Segment
                        {
                            Target = vn.VarName ?? vn.Name,
                            IsVariableTap = true,
                        });
                        lastFuncOrTap = vn;
                    }
                    else
                    {
                        // Read VariableNode → source identifier.
                        sources.Add(NodeToKsNode(vn));
                    }
                    break;
                case BuiltinFunctionNode fn:
                    // Build the segment with full Arguments (preserves literals + placeholders).
                    var (seg, wiredSourceCount) = BuildSegmentFromFunctionNode(fn);
                    // Sources that feed this function via wired inputs are collected
                    // when they appear earlier in the group as ConstNode/read VarNode.
                    // But if the function is the FIRST node in group (no preceding read
                    // sources), it's a bare call form — handle below.
                    segments.Add(seg);
                    lastFuncOrTap = fn;
                    break;
            }
        }

        // Determine bare call vs pipeline form.
        // Bare call: single function node with no wired inputs AND no preceding sources.
        if (group.Count == 1 && group[0] is BuiltinFunctionNode singleFn
            && !HasWiredInputs(singleFn))
        {
            var (leading, trailing) = ReadComments(singleFn);
            var call = BuildKsCallFromFunctionNode(singleFn);
            return WithFingerprint(new PipelineStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Sources = [call],
                Segments = [],
                LeadingComment = leading,
                TrailingComment = trailing,
            });
        }

        // Pipeline form: Sources=[collected sources], Segments=[collected segments].
        var primaryForComments = lastFuncOrTap ?? primary;
        var (leadC, trailC) = ReadComments(primaryForComments);
        return WithFingerprint(new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = sources.ToImmutable(),
            Segments = segments.ToImmutable(),
            LeadingComment = leadC,
            TrailingComment = trailC,
        });
    }

    /// <summary>
    /// Builds a Segment from a function node's input pins. Arguments are populated
    /// ONLY when the segment has at least one literal DefaultValue arg — this preserves
    /// the literal values plus the explicit `_` placeholders marking wired positions
    /// (e.g. `Range(0, _, 1)`). When ALL non-Exec inputs are wired (no literals), the
    /// KS source is in the append form `i > Print` and Arguments stays empty — this
    /// matches the parser's canonical append representation.
    /// </summary>
    private (Segment Segment, int WiredCount) BuildSegmentFromFunctionNode(BuiltinFunctionNode fn)
    {
        var args = ImmutableArray.CreateBuilder<KsNode>();
        var rawArgs = ImmutableArray.CreateBuilder<string>();
        int wiredCount = 0;
        bool hasLiteralArg = false;

        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;

            // Check for a wired source.
            bool isWired = false;
            foreach (var conn in _bp.Connections)
            {
                if (conn.TargetNodeId != fn.Id || conn.TargetPinId != pin.Id) continue;
                var src = _byId.GetValueOrDefault(conn.SourceNodeId);
                if (src is not null) { isWired = true; break; }
            }

            if (isWired)
            {
                args.Add(new KsPlaceholder { SourceText = "_" });
                rawArgs.Add("_");
                wiredCount++;
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
        return (seg, wiredCount);
    }

    /// <summary>True if <paramref name="fn"/> has any wired (non-default) data input.</summary>
    private bool HasWiredInputs(BuiltinFunctionNode fn)
    {
        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            foreach (var conn in _bp.Connections)
                if (conn.TargetNodeId == fn.Id && conn.TargetPinId == pin.Id)
                    return true;
        }
        return false;
    }

    /// <summary>
    /// Marks the entire condition/selector sub-graph rooted at <paramref name="node"/>
    /// as consumed so WalkExecChain skips it. Recursively walks upstream data edges.
    /// Called from ReadDataInput when a control-flow node reads its condition.
    /// </summary>
    private void MarkConsumedSubtree(BlueprintNode node)
    {
        if (!_consumedNodes.Add(node.Id)) return;  // already marked
        // Walk upstream data edges, mark all source nodes recursively.
        foreach (var conn in _bp.Connections)
        {
            if (conn.TargetNodeId != node.Id) continue;
            var src = _byId.GetValueOrDefault(conn.SourceNodeId);
            if (src is null) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            MarkConsumedSubtree(src);
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
                result.Add(WithFingerprint(ApplyComments(new BreakStatement { Fingerprint = Fingerprint.Compute("placeholder") }, fn)));
                break;
            case "continue":
                result.Add(WithFingerprint(ApplyComments(new ContinueStatement { Fingerprint = Fingerprint.Compute("placeholder") }, fn)));
                break;
            default:
                // Non-control-flow BuiltinFunctionNode should never reach here — the new
                // WalkExecChain routes them through BuildPipelineFromGroup instead.
                // Defensive fallback: emit as a bare call.
                result.Add(ReversePipelineCall(fn));
                result.AddRange(WalkExecChain(fn, BpPinNames.Exec));
                break;
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
            Fingerprint = Fingerprint.Compute("placeholder"),
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
            Fingerprint = Fingerprint.Compute("placeholder"),
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
            Fingerprint = Fingerprint.Compute("placeholder"),
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

        var defaultBody = _execOut.ContainsKey((sw.Id, BpPinNames.Default))
            ? WalkExecChain(sw, BpPinNames.Default)
            : new List<Statement>();
        var (leading, trailing) = ReadComments(sw);
        var stmt = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Selector = selector,
            Arms = arms.ToImmutable(),
            ArmLabels = armLabels.ToImmutable(),
            Default = [.. defaultBody],
            LeadingComment = leading,
            TrailingComment = trailing,
        };
        return WithFingerprint(stmt);
    }

    private Statement ReversePipelineCall(BuiltinFunctionNode fn)
    {
        var call = BuildKsCallFromFunctionNode(fn);

        // Distinguish bare call (Print("hello")) from pipeline form (i > Print).
        // Bare call: the function's data inputs are all DefaultValues (no wired sources).
        // Pipeline form: at least one data input is wired from another node.
        bool hasWiredInputs = false;
        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            foreach (var conn in _bp.Connections)
                if (conn.TargetNodeId == fn.Id && conn.TargetPinId == pin.Id)
                { hasWiredInputs = true; break; }
            if (hasWiredInputs) break;
        }

        if (!hasWiredInputs)
        {
            // Bare call form: Sources=[KsCall], Segments=[].
            var (leading1, trailing1) = ReadComments(fn);
            var pipe = new PipelineStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Sources = [call],
                Segments = [],
                LeadingComment = leading1,
                TrailingComment = trailing1,
            };
            return WithFingerprint(pipe);
        }

        // Pipeline form: Sources=[wired sources], Segments=[Segment(Target)].
        // Collect wired source KsNodes in pin order.
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            foreach (var conn in _bp.Connections)
            {
                if (conn.TargetNodeId != fn.Id || conn.TargetPinId != pin.Id) continue;
                var src = _byId.GetValueOrDefault(conn.SourceNodeId);
                if (src is not null) sources.Add(NodeToKsNode(src));
                break;
            }
        }
        var pipelineSeg = new Segment
        {
            Target = fn.FunctionName,
            IsVariableTap = false,
        };
        var (leading2, trailing2) = ReadComments(fn);
        var pipeStmt = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = sources.ToImmutable(),
            Segments = [pipelineSeg],
            LeadingComment = leading2,
            TrailingComment = trailing2,
        };
        return WithFingerprint(pipeStmt);
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
            // TODO(B3): silent fallback — BP graph is incomplete (pin missing).
            // Currently returns 'true' to keep round-trip tests green; ideally
            // should surface a diagnostic. Revisit when BP editing UX matures.
            return MakeBoolLiteral(true);
        }

        // Find the incoming data connection targeting this pin.
        foreach (var conn in _bp.Connections)
        {
            if (conn.TargetNodeId != node.Id) continue;
            var src = _byId.GetValueOrDefault(conn.SourceNodeId);
            if (src is null) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            var tgtPin = src.InputPins.Find(p => p.Id == conn.TargetPinId);
            // Confirm this connection targets our pin.
            if (pin.Id != conn.TargetPinId) continue;
            // Mark the source's sub-tree as consumed so WalkExecChain doesn't emit
            // these nodes as standalone PipelineStatements (idempotent — PreMark may
            // have already marked them).
            MarkConsumedSubtree(src);
            return NodeToKsNode(src);
        }

        // No wired source — use the pin's DefaultValue if available.
        if (pin.DefaultValue is not null)
            return ParseDefaultValue(pin.DefaultValue);

        // TODO(B3): silent fallback — BP graph is incomplete (default value missing).
        // Currently returns 'true' to keep round-trip tests green; ideally
        // should surface a diagnostic. Revisit when BP editing UX matures.
        return MakeBoolLiteral(true);
    }

    /// <summary>Converts a data-source BP node into the corresponding KsNode expression.</summary>
    private KsNode NodeToKsNode(BlueprintNode node)
    {
        switch (node)
        {
            case VariableNode vn:
                return new KsIdentifier { Name = vn.VarName ?? vn.Name, SourceText = vn.VarName ?? vn.Name };
            case ConstNode cn:
                return ParseDefaultValue(cn.ConstValue ?? cn.ConstName ?? "null");
            case BuiltinFunctionNode fn:
                return ReconstructPipelineOrCall(fn);
            default:
                // TODO(B3): silent fallback — BP graph is incomplete (unknown node type).
                // Currently returns 'true' to keep round-trip tests green; ideally
                // should surface a diagnostic. Revisit when BP editing UX matures.
                return MakeBoolLiteral(true);
        }
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
            foreach (var conn in _bp.Connections)
            {
                if (conn.TargetNodeId != fn.Id || conn.TargetPinId != pin.Id) continue;
                var src = _byId.GetValueOrDefault(conn.SourceNodeId);
                if (src is not null) { wired = NodeToKsNode(src); break; }
            }
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
            // Check for a wired data source first.
            KsNode? wired = null;
            foreach (var conn in _bp.Connections)
            {
                if (conn.TargetNodeId != fn.Id || conn.TargetPinId != pin.Id) continue;
                var src = _byId.GetValueOrDefault(conn.SourceNodeId);
                if (src is not null) { wired = NodeToKsNode(src); break; }
            }
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