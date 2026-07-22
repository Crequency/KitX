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
                break;
            case "Each":
                result.Add(ReverseForEach(fn));
                break;
            case "While":
                result.Add(ReverseWhile(fn));
                break;
            case "Switch":
                result.Add(ReverseSwitch(fn));
                break;
            case "break":
                result.Add(WithFingerprint(new BreakStatement { Fingerprint = Fingerprint.Compute("placeholder") }));
                break;
            case "continue":
                result.Add(WithFingerprint(new ContinueStatement { Fingerprint = Fingerprint.Compute("placeholder") }));
                break;
            case "exit":
                result.Add(WithFingerprint(new ExitStatement { Fingerprint = Fingerprint.Compute("placeholder") }));
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
        var stmt = new IfStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Condition = cond,
            ThenBody = [.. thenBody],
            ElseBody = [.. elseBody],
        };
        return WithFingerprint(stmt);
    }

    private Statement ReverseForEach(BuiltinFunctionNode each)
    {
        var source = ReadDataInput(each, "List");
        var body = WalkExecChain(each, "Body");
        var itemName = each.Properties.TryGetValue("ItemName", out var n) && !string.IsNullOrEmpty(n)
            ? n : "item";
        var stmt = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Source = source,
            ItemName = itemName,
            ItemType = PinType.Any,
            Body = [.. body],
        };
        return WithFingerprint(stmt);
    }

    private Statement ReverseWhile(BuiltinFunctionNode wh)
    {
        var cond = ReadDataInput(wh, "Condition");
        var body = WalkExecChain(wh, "Body");
        var stmt = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Condition = cond,
            Body = [.. body],
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
        var stmt = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Selector = selector,
            Arms = arms.ToImmutable(),
            Default = [.. defaultBody],
        };
        return WithFingerprint(stmt);
    }

    private Statement ReversePipelineCall(BuiltinFunctionNode fn)
    {
        var call = BuildBsCallFromFunctionNode(fn);

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
            var pipe = new PipelineStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Sources = [call],
                Segments = [],
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
                if (src is not null) sources.Add(NodeToBsNode(src));
                break;
            }
        }
        var pipelineSeg = new Segment
        {
            Target = fn.FunctionName,
            IsVariableTap = false,
        };
        var pipeStmt = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = sources.ToImmutable(),
            Segments = [pipelineSeg],
        };
        return WithFingerprint(pipeStmt);
    }

    /// <summary>Replaces the placeholder fingerprint with the real structural one.</summary>
    private static Statement WithFingerprint(Statement stmt) =>
        stmt with { Fingerprint = Fingerprint.Compute(stmt) };

    // ── Data input reconstruction ──

    /// <summary>
    /// Reads the KsNode expression feeding a named data input pin on <paramref name="node"/>.
    /// Returns a KsIdentifier("true") fallback when the pin is unwired (e.g. literal condition
    /// collapsed to DefaultValue by BpRenderer).
    /// </summary>
    private KsNode ReadDataInput(BlueprintNode node, string pinName)
    {
        var pin = node.InputPins.Find(p => p.Name == pinName);
        if (pin is null) return MakeBoolLiteral(true);

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
            return NodeToBsNode(src);
        }

        // No wired source — use the pin's DefaultValue if available.
        if (pin.DefaultValue is not null)
            return ParseDefaultValue(pin.DefaultValue);

        return MakeBoolLiteral(true);
    }

    /// <summary>Converts a data-source BP node into the corresponding KsNode expression.</summary>
    private KsNode NodeToBsNode(BlueprintNode node)
    {
        switch (node)
        {
            case VariableNode vn:
                return new KsIdentifier { Name = vn.VarName ?? vn.Name, SourceText = vn.VarName ?? vn.Name };
            case ConstNode cn:
                return ParseDefaultValue(cn.ConstValue ?? cn.ConstName ?? "null");
            case BuiltinFunctionNode fn:
                return BuildBsCallFromFunctionNode(fn);
            default:
                return MakeBoolLiteral(true);
        }
    }

    /// <summary>
    /// Builds a KsCall from a function node's named data input pins. Each pin is either
    /// wired (→ VariableNode/ConstNode/FunctionNode source) or carries a DefaultValue.
    /// Pins are read in order to reconstruct the original argument list.
    /// </summary>
    private KsCall BuildBsCallFromFunctionNode(BuiltinFunctionNode fn)
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
                if (src is not null) { wired = NodeToBsNode(src); break; }
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