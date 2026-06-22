using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using Serilog;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using KitX.Workflow.CFG;

using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Phase 3: Creates all Blueprint nodes and exec flow edges from ControlFlowGraph.
/// Implements PubVar reuse detection during node creation (§6.5).
/// </summary>
public class CFG2BPConverter
{
    private readonly INodeRegistry _registry;
    private readonly List<HelperFunction> _helpers;
    private readonly HashSet<string> _helperNames;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    // Deferred cross-block edge definitions (resolved after all blocks processed)
    private readonly List<(string stmtId, string returnToBlock, string blockName, string? prevStmtId)> _loopBodyEndDefs = [];
    private readonly Dictionary<string, string> _blockLastStmtId = new();

    public CFG2BPConverter(INodeRegistry registry, List<HelperFunction> helpers, BuiltinFunctionRegistry? functionRegistry = null)
    {
        _registry = registry;
        _helpers = helpers;
        _helperNames = new HashSet<string>(helpers.Select(h => h.Name));
        _functionRegistry = functionRegistry;
    }

    public void Build(ControlFlowGraph script, PipelineContext context)
    {
        // Create EntryNode
        var entry = (EntryNode)_registry.Create(BlueprintNodeType.Entry);
        entry.X = 0;
        entry.Y = 0;
        context.EntryNode = entry;
        context.AllNodes.Add(entry);
        context.NodeByStatementId["__entry__"] = entry;

        // Process all blocks in order (MainBlock first)
        foreach (var block in script.Blocks)
            ProcessBlock(block, context);

        // Resolve cross-block edges
        ResolveCrossBlockEdges(context);

        // Populate debug context: StatementId → NodeId mapping
        if (script.DebugStatementToNodeId != null)
        {
            foreach (var kvp in context.NodeByStatementId)
                script.DebugStatementToNodeId[kvp.Key] = kvp.Value.Id;
        }

        Log.Debug("[CFG2BPConverter] Done: {NodeCount} nodes, {ExecEdgeCount} exec edges",
            context.AllNodes.Count, context.ExecEdges.Count);
    }

    // ──────────────────────────────────────────────
    // Block processing
    // ──────────────────────────────────────────────

    private void ProcessBlock(CFGBlock block, PipelineContext context)
    {
        string? prevStmtId = null;
        BlueprintNode? prevNode = null;
        BlueprintNode? firstNode = null;
        bool endsWithFlowCtrl = false;

        // Initialize block node ID list for scope tracking
        context.BlockNodeIds[block.Name] = new List<string>();

        // MainBlock chains from Entry
        if (block.Name == context.FormattedScript.MainBlockName)
        {
            prevStmtId = "__entry__";
            prevNode = context.EntryNode;
        }

        foreach (var stmt in block.Statements)
        {
            var node = ProcessStatement(stmt, block.Name, context, ref prevNode, ref prevStmtId);
            if (firstNode == null && node != null)
                firstNode = node;

            // Record node ID in block membership (skip nulls like ToLoopCond)
            if (node != null)
                context.BlockNodeIds[block.Name].Add(node.Id);

            endsWithFlowCtrl = IsRegistryFlowControlTerminator(stmt);
        }

        if (firstNode != null)
        {
            context.BlockFirstNodes[block.Name] = firstNode;
            _blockLastStmtId[block.Name] = prevStmtId ?? "";
        }

        // Sequential fall-through target now derived from the CFG's Sequential Successors edge
        // (single source of truth) rather than a parallel NextBlockName field.
        if (!string.IsNullOrEmpty(block.FallThroughTarget))
            context.BlockNextBlock[block.Name] = block.FallThroughTarget;

        context.BlockEndsWithFlowCtrl[block.Name] = endsWithFlowCtrl;
    }

    // ──────────────────────────────────────────────
    // Statement dispatch
    // ──────────────────────────────────────────────

    private BlueprintNode? ProcessStatement(CFGStatement stmt, string blockName,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        var funcDef = !string.IsNullOrEmpty(stmt.FunctionName)
            ? _functionRegistry?.Get(stmt.FunctionName)
            : null;

        // Control-flow terminators (Branch/Loop/Flip/ToLoopCond): node + deferred cross-block edges.
        if (funcDef is { IsBlockTerminator: true })
        {
            var node = CreateAndConfigureNode(stmt, funcDef, context);
            ChainNewNode(node, stmt, context, ref prevNode, ref prevStmtId);
            funcDef.OnNodeCreated(node, stmt, context);
            return node;
        }

        // Calls / assignments: any registered non-terminator builtin (Get/Set/Print/...,
        // including PluginCallWithTarget whose Kind is its own enum value) OR an unregistered
        // helper/plugin call (Kind = Assignment/Expression). Routing by registration rather
        // than Kind, because builtins like Print/Set carry their own Kind values.
        if (funcDef != null
            || stmt.Kind is CFGStatementKind.Assignment or CFGStatementKind.Expression)
        {
            return ProcessCallOrAssignment(stmt, funcDef, context, ref prevNode, ref prevStmtId);
        }

        return null;
    }

    /// <summary>
    /// Checks if a CFGStatement corresponds to a registry-based control flow terminator.
    /// Used by ProcessBlock to set endsWithFlowCtrl flag.
    /// </summary>
    private bool IsRegistryFlowControlTerminator(CFGStatement stmt)
    {
        if (_functionRegistry == null || string.IsNullOrEmpty(stmt.FunctionName)) return false;
        var def = _functionRegistry.Get(stmt.FunctionName);
        return def != null && def.IsBlockTerminator;
    }

    // ──────────────────────────────────────────────
    // Call / Assignment node creation (with PubVar reuse)
    // ──────────────────────────────────────────────

    private BlueprintNode? ProcessCallOrAssignment(CFGStatement stmt,
        IBuiltinFunctionDefinition? funcDef, PipelineContext context,
        ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        // --- PubVar reuse check (§6.5) ---
        // Reuse key is descriptor-declared: null for most builtins (each stmt → own node,
        // safe for side-effecting calls); Fingerprint for opt-ins (e.g. PluginCallWithTarget);
        // Fingerprint fallback for unregistered helpers/plugin calls.
        var reuseKey = funcDef == null ? stmt.Fingerprint : funcDef.GetReuseKey(stmt);

        if (!string.IsNullOrEmpty(reuseKey)
            && context.PubVarAssignments.TryGetValue(reuseKey, out var existing))
        {
            context.NodeByStatementId[stmt.StatementId] = existing.SourceNode;
            AddExecEdge(prevStmtId, stmt.StatementId, context);
            prevNode = existing.SourceNode;
            prevStmtId = stmt.StatementId;
            Log.Debug("[CFG2BPConverter] Reused node: {Key}", reuseKey);
            return existing.SourceNode;
        }

        // --- Create + configure (single descriptor-driven path) ---
        var mainNode = CreateAndConfigureNode(stmt, funcDef, context);

        context.AllNodes.Add(mainNode);
        context.NodeByStatementId[stmt.StatementId] = mainNode;
        AddExecEdge(prevStmtId, stmt.StatementId, context);
        prevNode = mainNode;
        prevStmtId = stmt.StatementId;

        RegisterPubVarAssignment(stmt, mainNode, funcDef, context);
        return mainNode;
    }

    /// <summary>
    /// Single descriptor-driven node creation. Resolves the right node type from
    /// <see cref="IBuiltinFunctionDefinition.NodeKind"/> (BuiltinFunction vs Call), applies
    /// <see cref="IBuiltinFunctionDefinition.ConfigureNode"/>, and adds param pins for bare
    /// Call/CallHelper nodes. Unregistered statements fall back to helper/plugin-call nodes.
    /// </summary>
    private BlueprintNode CreateAndConfigureNode(CFGStatement stmt, IBuiltinFunctionDefinition? funcDef, PipelineContext context)
    {
        BlueprintNode node;
        if (funcDef != null)
        {
            node = funcDef.NodeKind == BuiltinNodeKind.Call
                ? _registry.Create(BlueprintNodeType.Call)
                : _registry.CreateBuiltinFunctionNode(stmt.FunctionName!);
            node = funcDef.ConfigureNode(node, stmt);
            // Bare CallNode has no descriptor pins → add param pins like helper/plugin calls.
            if (funcDef.NodeKind == BuiltinNodeKind.Call)
                AddParamPins(node, stmt.FunctionName!, stmt.Arguments?.Count ?? 0);
        }
        else if (string.IsNullOrEmpty(stmt.FunctionName) && !string.IsNullOrEmpty(stmt.PubVarTarget))
        {
            // v5.0: pure variable assignment (`Expr > var`). De-dupe by target name — one
            // __assign node per variable, reused across multiple writes. This is closer to
            // stable round-trip than no-dedup (which grows unboundedly each round).
            var existingAssign = context.AllNodes.OfType<CallNode>()
                .FirstOrDefault(c => c.PluginName == "__assign" && c.FunctionName == stmt.PubVarTarget);
            if (existingAssign != null)
            {
                node = existingAssign;
            }
            else
            {
                node = _registry.Create(BlueprintNodeType.Call);
                if (node is CallNode callNode)
                {
                    callNode.PluginName = "__assign";
                    callNode.FunctionName = stmt.PubVarTarget;
                    var valuePin = new BlueprintPin
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "Value",
                        Direction = PinDirection.Input,
                        Type = PinType.Any
                    };
                    callNode.InputPins.Add(valuePin);
                }
            }
        }
        else
        {
            node = _helperNames.Contains(stmt.FunctionName ?? string.Empty)
                ? CreateHelperNode(stmt)
                : CreatePluginCallNode(stmt);
            AddParamPins(node, stmt.FunctionName!, stmt.Arguments?.Count ?? 0);
        }
        // v5.0 §9: carry the statement comment onto the blueprint node.
        if (!string.IsNullOrEmpty(stmt.Comment))
            node.Comment = stmt.Comment;
        return node;
    }

    private BlueprintNode CreateHelperNode(CFGStatement stmt)
    {
        var helperNode = (CallHelperNode)_registry.Create(BlueprintNodeType.CallHelper);
        helperNode.HelperFunctionName = stmt.FunctionName!;
        return helperNode;
    }

    private BlueprintNode CreatePluginCallNode(CFGStatement stmt)
    {
        var callNode = (CallNode)_registry.Create(BlueprintNodeType.Call);
        // Parse plugin name from full dotted method name (e.g. "TestPlugin.WPF.Core.HelloKitX")
        if (!string.IsNullOrEmpty(stmt.FullFunctionName) && stmt.FullFunctionName.Contains('.'))
        {
            var lastDot = stmt.FullFunctionName.LastIndexOf('.');
            callNode.PluginName = stmt.FullFunctionName.Substring(0, lastDot);
            callNode.FunctionName = stmt.FullFunctionName.Substring(lastDot + 1);
        }
        else
        {
            callNode.FunctionName = stmt.FunctionName!;
        }
        return callNode;
    }

    // ──────────────────────────────────────────────
    // Cross-block edge resolution
    // ──────────────────────────────────────────────

    private void ResolveCrossBlockEdges(PipelineContext context)
    {
        // Sequential fall-through for blocks without flow control endings
        foreach (var (blockName, nextBlockName) in context.BlockNextBlock)
        {
            if (context.BlockEndsWithFlowCtrl.GetValueOrDefault(blockName)) continue;
            if (string.IsNullOrEmpty(nextBlockName)) continue;
            if (!context.BlockFirstNodes.TryGetValue(nextBlockName, out var nextFirst)) continue;
            if (!_blockLastStmtId.TryGetValue(blockName, out var lastStmtId)) continue;

            var targetStmtId = FindStmtIdForNode(nextFirst, context);
            if (targetStmtId == null) continue;

            context.ExecEdges.Add(new PendingExecEdge
            {
                SourceStatementId = lastStmtId,
                TargetStatementId = targetStmtId,
                SourcePinName = Exec,
                TargetPinName = Exec
            });
        }

        // Generic deferred edges from IBuiltinFunctionDefinition.OnNodeCreated
        foreach (var deferred in context.DeferredEdges)
        {
            foreach (var (pinName, targetBlockName) in deferred.Arms)
            {
                if (string.IsNullOrEmpty(targetBlockName)) continue;
                if (!context.BlockFirstNodes.TryGetValue(targetBlockName, out var firstNode)) continue;
                var targetStmtId = FindStmtIdForNode(firstNode, context);
                if (targetStmtId == null) continue;

                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = deferred.SourceStatementId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = pinName,
                    TargetPinName = Exec,
                    IsSpecialRouting = true
                });
            }

            // Loopback edge (ToLoopCond-style)
            if (!string.IsNullOrEmpty(deferred.LoopbackTargetBlock))
            {
                // Find the LoopNode in the target block's parent for loopback
                if (context.LoopNodesByParent.TryGetValue(deferred.LoopbackTargetBlock, out var loopNode))
                {
                    var loopStmtId = FindStmtIdForNode(loopNode, context);
                    if (loopStmtId != null)
                    {
                        context.ExecEdges.Add(new PendingExecEdge
                        {
                            SourceStatementId = deferred.SourceStatementId,
                            TargetStatementId = loopStmtId,
                            SourcePinName = Exec,
                            TargetPinName = Exec
                        });
                    }
                }
            }
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    /// <summary>Creates a node, registers it, and chains it into the exec flow.</summary>
    private BlueprintNode ChainNewNode(BlueprintNode node, CFGStatement stmt,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        context.AllNodes.Add(node);
        context.NodeByStatementId[stmt.StatementId] = node;

        AddExecEdge(prevStmtId, stmt.StatementId, context);

        prevNode = node;
        prevStmtId = stmt.StatementId;
        return node;
    }

    private void RegisterPubVarAssignment(CFGStatement stmt, BlueprintNode node,
        IBuiltinFunctionDefinition? funcDef, PipelineContext context)
    {
        if (string.IsNullOrEmpty(stmt.PubVarTarget)) return;

        // v5.0: __assign placeholder CallNode — wire the assignment source to its Value input
        // pin (so the source's output is "consumed" → PostProcessCallReturn prefixes the pubVar),
        // then register the __assign node's Return pin as the PubVarTarget producer so downstream
        // reads connect to it.
        if (node is CallNode assignCall && assignCall.PluginName == "__assign")
        {
            var valuePin = assignCall.InputPins.FirstOrDefault(p => p.Name == "Value");
            if (valuePin != null)
                WireAssignSource(stmt, assignCall, valuePin, context);
            var returnPin = assignCall.OutputPins.FirstOrDefault(p => p.Name == "Return");
            if (returnPin != null)
            {
                context.PubVarAssignments[stmt.PubVarTarget] = new PubVarAssignment
                {
                    PubVarName = stmt.PubVarTarget,
                    SourceNode = assignCall,
                    SourcePin = returnPin,
                    StatementId = stmt.StatementId,
                };
            }
            return;
        }

        var outputPin = node.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
        if (outputPin == null) return;

        // The key only matters for reuse lookup (ProcessCallOrAssignment). DataEdgeBuilder
        // connects data edges by the PubVarName field below, so a null reuse key safely falls
        // back to PubVarTarget as a non-null dict index without affecting data edges.
        var reuseKey = funcDef == null ? stmt.Fingerprint : funcDef.GetReuseKey(stmt);
        var key = reuseKey ?? stmt.PubVarTarget;
        context.PubVarAssignments[key] = new PubVarAssignment
        {
            PubVarName = stmt.PubVarTarget,
            SourceNode = node,
            SourcePin = outputPin,
            StatementId = stmt.StatementId,
        };
    }

    /// <summary>
    /// v5.0: wires the pure-assignment source (stmt.Arguments[0]) to the __assign node's Value
    /// input pin. The source may be a PubVar (→ existing PubVarAssignment producer), a ConstBlock
    /// variable (→ ConstNode), or a literal (→ DefaultValue on the pin).
    /// </summary>
    private void WireAssignSource(CFGStatement stmt, BlueprintNode assignNode, BlueprintPin valuePin, PipelineContext context)
    {
        var source = stmt.Arguments.FirstOrDefault()?.Trim() ?? "null";

        var existingAssignment = context.PubVarAssignments.Values
            .FirstOrDefault(a => a.PubVarName == source);
        if (existingAssignment != null)
        {
            context.DataEdges.Add(new PendingDataEdge
            {
                SourceNodeId = existingAssignment.SourceNode.Id,
                SourcePinName = existingAssignment.SourcePin.Name,
                TargetNodeId = assignNode.Id,
                TargetPinName = "Value",
                // PubVarName = the SOURCE pubVar (not the target), so PostProcessCallReturn
                // on the source node prefixes the correct source name, not the assign target.
                PubVarName = source
            });
            return;
        }

        if (context.ConstNodes.TryGetValue(source, out var cn))
        {
            var cpin = cn.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
            if (cpin != null)
            {
                context.DataEdges.Add(new PendingDataEdge
                {
                    SourceNodeId = cn.Id,
                    SourcePinName = cpin.Name,
                    TargetNodeId = assignNode.Id,
                    TargetPinName = "Value",
                    PubVarName = source
                });
            }
            return;
        }

        // Literal → set DefaultValue.
        valuePin.DefaultValue = source.Trim('"');
    }

    // v5.0 NOTE: VariableNode-based variable write/round-trip (GetOrCreateVariableNode /
    // RegisterVariableWrite) was attempted but requires full BP→CS/CFG2CS integration to avoid
    // duplicate-declaration (CS0128) on the compile path. Deferred to the dedicated
    // DataEdgeBuilder/VariableNode model refactor. The PostProcessCallReturn fix below remains
    // (it correctly prefixes builtin-with-return results as `value > pubVar`).


    private void AddExecEdge(string? sourceStmtId, string targetStmtId, PipelineContext context)
    {
        if (sourceStmtId == null) return;
        context.ExecEdges.Add(new PendingExecEdge
        {
            SourceStatementId = sourceStmtId,
            TargetStatementId = targetStmtId
        });
    }

    /// <summary>Finds the statement ID that maps to a given node.</summary>
    private string? FindStmtIdForNode(BlueprintNode node, PipelineContext context)
    {
        foreach (var kvp in context.NodeByStatementId)
        {
            if (kvp.Value.Id == node.Id)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>Adds parameter input pins to a Call/CallHelper node.</summary>
    private void AddParamPins(BlueprintNode node, string funcName, int argCount)
    {
        var helper = _helpers.FirstOrDefault(h => h.Name == funcName);
        if (helper != null)
        {
            for (int i = 0; i < helper.Parameters.Count; i++)
            {
                node.InputPins.Add(new BlueprintPin
                {
                    Name = helper.Parameters[i].Name,
                    Direction = PinDirection.Input,
                    Type = PinType.Any
                });
            }
        }
        else
        {
            for (int i = 0; i < argCount; i++)
            {
                node.InputPins.Add(new BlueprintPin
                {
                    Name = $"param{i + 1}",
                    Direction = PinDirection.Input,
                    Type = PinType.Any
                });
            }
        }
    }
}
