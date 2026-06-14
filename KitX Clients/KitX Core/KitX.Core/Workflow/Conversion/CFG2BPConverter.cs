using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Functions;
using KitX.Core.Workflow.CFG;

using KitX.Core.Workflow.Blueprint;
namespace KitX.Core.Workflow.Conversion;

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
        if (script.DebugContext != null)
        {
            foreach (var kvp in context.NodeByStatementId)
                script.DebugContext.StatementToNodeId[kvp.Key] = kvp.Value.Id;
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

        if (!string.IsNullOrEmpty(block.NextBlockName))
            context.BlockNextBlock[block.Name] = block.NextBlockName;

        context.BlockEndsWithFlowCtrl[block.Name] = endsWithFlowCtrl;
    }

    // ──────────────────────────────────────────────
    // Statement dispatch
    // ──────────────────────────────────────────────

    private BlueprintNode? ProcessStatement(CFGStatement stmt, string blockName,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        // PluginCallWithTarget must become an explicit CallNode carrying TargetDevice.
        // Route it before the registry dispatch — otherwise the registry creates a
        // BuiltinFunctionNode that ConfigureNode (which expects a CallNode) cannot convert,
        // leaving TargetDevice unset and producing no CallNode for the test to find.
        if (stmt.FunctionName == "PluginCallWithTarget")
            return ProcessCallOrAssignment(stmt, context, ref prevNode, ref prevStmtId);

        // Registry path: handle all registered builtin functions
        if (_functionRegistry != null && !string.IsNullOrEmpty(stmt.FunctionName))
        {
            var funcDef = _functionRegistry.Get(stmt.FunctionName);
            if (funcDef != null)
            {
                if (funcDef.IsBlockTerminator)
                {
                    var node = _registry.CreateBuiltinFunctionNode(stmt.FunctionName);
                    node = funcDef.ConfigureNode(node, stmt);
                    ChainNewNode(node, stmt, context, ref prevNode, ref prevStmtId);
                    funcDef.OnNodeCreated(node, stmt, context);
                    return node;
                }

                // Non-terminator registered function
                var nonTermNode = _registry.CreateBuiltinFunctionNode(stmt.FunctionName);
                nonTermNode = funcDef.ConfigureNode(nonTermNode, stmt);
                var result = ChainNewNode(nonTermNode, stmt, context, ref prevNode, ref prevStmtId);
                RegisterPubVarAssignment(stmt, nonTermNode, context);
                return result;
            }
        }

        // Non-registry statement handling
        switch (stmt.Kind)
        {
            case CFGStatementKind.Assignment:
            case CFGStatementKind.Expression:
                return ProcessCallOrAssignment(stmt, context, ref prevNode, ref prevStmtId);

            default:
                return null;
        }
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
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        // --- PubVar reuse check (§6.5) ---
        // For Get statements: reuse by PubVarTarget (each Get is unique based on its PubVar)
        // For non-Get statements: reuse by fingerprint
        PubVarAssignment? existing = null;

        if (stmt.FunctionName == Get && stmt.Arguments?.Count > 0)
        {
            if (!string.IsNullOrEmpty(stmt.PubVarTarget))
            {
                existing = context.PubVarAssignments.Values
                    .FirstOrDefault(p => p.PubVarName == stmt.PubVarTarget
                        && p.SourceNode is BuiltinFunctionNode bfn && bfn.FunctionName == "Get"
                        && bfn.InputPins.FirstOrDefault(pin => pin.Name == "VarName")?.DefaultValue == stmt.Arguments[0].Trim('"'));
            }
        }
        else if (!string.IsNullOrEmpty(stmt.Fingerprint)
            && context.PubVarAssignments.TryGetValue(stmt.Fingerprint, out var fpExisting))
        {
            existing = fpExisting;
        }

        if (existing != null)
        {
            context.NodeByStatementId[stmt.StatementId] = existing.SourceNode;

            // Chain to the shared main node
            AddExecEdge(prevStmtId, stmt.StatementId, context);
            prevNode = existing.SourceNode;
            prevStmtId = stmt.StatementId;

            Log.Debug("[CFG2BPConverter] Reused node: {Key}", stmt.Fingerprint ?? stmt.PubVarTarget);
            return existing.SourceNode;
        }

        // --- Create new node ---
        BlueprintNode mainNode;
        if (stmt.FunctionName == "PluginCallWithTarget")
        {
            // PluginCallWithTarget must be an explicit CallNode carrying TargetDevice.
            // Handle BEFORE the builtin registry dispatch — otherwise the registry shadows
            // it (PluginCallWithTarget is a registered builtin) and TargetDevice is never set.
            var callNode = (CallNode)_registry.Create(BlueprintNodeType.Call);
            var args = stmt.Arguments;
            // G.PluginCallWithTarget("plugin", "method", "device", ...)
            // Arguments[0]=pluginName, [1]=methodName, [2]=targetDevice
            callNode.PluginName = args?.Count > 0 ? StripQuotes(args[0]) : "";
            callNode.FunctionName = args?.Count > 1 ? StripQuotes(args[1]) : "";
            callNode.TargetDevice = args?.Count > 2 ? StripQuotes(args[2]) : null;
            // Preserve extra arguments (beyond plugin/method/device) for BS round-trip,
            // mirroring PluginCallWithTargetFunction.ConfigureNode.
            if (args != null && args.Count > 3)
                callNode.ExtraArguments = args.Skip(3).ToList();
            mainNode = callNode;

            // Add parameter pins based on argument count
            AddParamPins(mainNode, stmt.FunctionName!, stmt.Arguments?.Count ?? 0);
        }
        else if (_functionRegistry != null && _functionRegistry.Get(stmt.FunctionName!) is { } funcDef)
        {
            mainNode = _registry.CreateBuiltinFunctionNode(stmt.FunctionName!);
            mainNode = funcDef.ConfigureNode(mainNode, stmt);
        }
        else
        {
            var isHelper = _helperNames.Contains(stmt.FunctionName ?? string.Empty);
            if (isHelper)
            {
                var helperNode = (CallHelperNode)_registry.Create(BlueprintNodeType.CallHelper);
                helperNode.HelperFunctionName = stmt.FunctionName!;
                mainNode = helperNode;
            }
            else
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

                mainNode = callNode;
            }

            // Add parameter pins based on helper definition or argument count
            AddParamPins(mainNode, stmt.FunctionName!, stmt.Arguments?.Count ?? 0);
        }

        context.AllNodes.Add(mainNode);
        context.NodeByStatementId[stmt.StatementId] = mainNode;

        AddExecEdge(prevStmtId, stmt.StatementId, context);
        prevNode = mainNode;
        prevStmtId = stmt.StatementId;

        // Register PubVar assignment for reuse
        RegisterPubVarAssignment(stmt, mainNode, context);

        return mainNode;
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

    private void RegisterPubVarAssignment(CFGStatement stmt, BlueprintNode node, PipelineContext context)
    {
        if (string.IsNullOrEmpty(stmt.PubVarTarget)) return;

        var outputPin = node.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
        if (outputPin == null) return;

        // Get: key by PubVarTarget (each Get is unique based on what variable it reads)
        // Others: key by fingerprint for reuse detection
        var key = stmt.FunctionName == "Get"
            ? stmt.PubVarTarget
            : (stmt.Fingerprint ?? stmt.PubVarTarget);
        context.PubVarAssignments[key] = new PubVarAssignment
        {
            PubVarName = stmt.PubVarTarget,
            SourceNode = node,
            SourcePin = outputPin,
            StatementId = stmt.StatementId,
        };
    }

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

    /// <summary>Strips surrounding double-quote characters from a string literal.</summary>
    private static string StripQuotes(string s)
    {
        if (s == null) return "";
        s = s.Trim();
        if (s.Length >= 2 && s.StartsWith('"') && s.EndsWith('"'))
            return s[1..^1];
        return s;
    }
}
