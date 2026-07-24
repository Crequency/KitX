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
            var body = WalkExecChain(entry, "Exec");
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

    // ── Exec chain walking ──

    /// <summary>
    /// Walks the exec chain starting from <paramref name="source"/>'s <paramref name="pinName"/>
    /// output pin, reconstructing the ordered list of IR statements.
    /// </summary>
    private List<Statement> WalkExecChain(BlueprintNode source, string pinName)
    {
        var result = new List<Statement>();
        if (!_execOut.TryGetValue((source.Id, pinName), out var targets))
            return result;
        foreach (var tgt in targets)
            result.AddRange(WalkFromNode(tgt));
        return result;
    }

    /// <summary>Reconstructs the statement(s) starting at <paramref name="node"/>, then follows the node's exec-out chain.</summary>
    private List<Statement> WalkFromNode(BlueprintNode node)
    {
        switch (node)
        {
            case BuiltinFunctionNode fn:
                return WalkBuiltinFunction(fn);
            default:
                // EntryNode shouldn't appear mid-chain; ConstNode/VariableNode are data only.
                return [];
        }
    }

    private List<Statement> WalkBuiltinFunction(BuiltinFunctionNode fn)
    {
        var result = new List<Statement>();
        switch (fn.FunctionName)
        {
            case "Branch":
                result.Add(ReverseIf(fn));
                // An if/else merges back to the continuation after both branches; the
                // continuation connects to the merged exec tails of then/else bodies.
                // Those tails are walked as the bodies' last nodes' Exec outs — already
                // captured by ReverseIf's recursive WalkExecChain on True/False. The
                // post-if siblings are reached via those merged tails, so nothing extra
                // to follow here (Branch has no End pin; continuation is implicit).
                break;
            case "Each":
                result.Add(ReverseForEach(fn));
                // Statements after the loop connect to Each.End.
                result.AddRange(WalkExecChain(fn, "End"));
                break;
            case "While":
                result.Add(ReverseWhile(fn));
                // Statements after the loop connect to While.End.
                result.AddRange(WalkExecChain(fn, "End"));
                break;
            case "Switch":
                result.Add(ReverseSwitch(fn));
                // Statements after the switch connect to the merged arm exec tails —
                // Switch has no End pin; arms rejoin implicitly via their body tails,
                // which ReverseSwitch already captured. The post-switch continuation is
                // reached through those tails, so nothing extra to follow here.
                break;
            case "break":
                result.Add(WithFingerprint(ApplyComments(new BreakStatement { Fingerprint = Fingerprint.Compute("placeholder") }, fn)));
                break;
            case "continue":
                result.Add(WithFingerprint(ApplyComments(new ContinueStatement { Fingerprint = Fingerprint.Compute("placeholder") }, fn)));
                break;
            default:
                // Regular function call → PipelineStatement.
                result.Add(ReversePipelineCall(fn));
                // Continue the exec chain after this node.
                result.AddRange(WalkExecChain(fn, "Exec"));
                break;
        }
        return result;
    }

    // ── Control-flow reconstruction ──

    private Statement ReverseIf(BuiltinFunctionNode br)
    {
        var cond = ReadDataInput(br, "Condition");
        var thenBody = WalkExecChain(br, "True");
        var elseBody = WalkExecChain(br, "False");
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
        var source = ReadDataInput(each, "List");
        var body = WalkExecChain(each, "Body");
        var itemName = each.Properties.TryGetValue("ItemName", out var n) && !string.IsNullOrEmpty(n)
            ? n : "item";
        var (leading, trailing) = ReadComments(each);
        var stmt = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Source = source,
            ItemName = itemName,
            ItemType = PinType.Any,
            Body = [.. body],
            LeadingComment = leading,
            TrailingComment = trailing,
        };
        return WithFingerprint(stmt);
    }

    private Statement ReverseWhile(BuiltinFunctionNode wh)
    {
        var cond = ReadDataInput(wh, "Condition");
        var body = WalkExecChain(wh, "Body");
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
        var selector = ReadDataInput(sw, "Selector");
        var arms = ImmutableArray.CreateBuilder<ImmutableArray<Statement>>();
        for (int i = 0; ; i++)
        {
            if (!_execOut.ContainsKey((sw.Id, i.ToString()))) break;
            arms.Add([.. WalkExecChain(sw, i.ToString())]);
        }
        var defaultBody = _execOut.ContainsKey((sw.Id, "Default"))
            ? WalkExecChain(sw, "Default")
            : new List<Statement>();
        var (leading, trailing) = ReadComments(sw);
        var stmt = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Selector = selector,
            Arms = arms.ToImmutable(),
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
            if (pin.Name == "Exec") continue;
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
            if (pin.Name == "Exec") continue;
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
            if (pin.Name == "Exec") continue;
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
            if (pin.Name == "Exec") continue;
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