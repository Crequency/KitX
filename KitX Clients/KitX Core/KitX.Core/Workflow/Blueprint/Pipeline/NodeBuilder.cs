using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Functions;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Phase 3: Creates all Blueprint nodes and exec flow edges from FormattedBlockScript.
/// Implements PubVar reuse detection during node creation (§6.5).
/// </summary>
public class NodeBuilder
{
    private readonly INodeRegistry _registry;
    private readonly List<HelperFunction> _helpers;
    private readonly HashSet<string> _helperNames;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    // Deferred cross-block edge definitions (resolved after all blocks processed)
    private readonly List<(string stmtId, string returnToBlock, string blockName, string? prevStmtId)> _loopBodyEndDefs = new();
    private readonly Dictionary<string, string> _blockLastStmtId = new();

    public NodeBuilder(INodeRegistry registry, List<HelperFunction> helpers, BuiltinFunctionRegistry? functionRegistry = null)
    {
        _registry = registry;
        _helpers = helpers;
        _helperNames = new HashSet<string>(helpers.Select(h => h.Name));
        _functionRegistry = functionRegistry;
    }

    public void Build(FormattedBlockScript script, PipelineContext context)
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

        Log.Debug("[NodeBuilder] Done: {NodeCount} nodes, {ExecEdgeCount} exec edges",
            context.AllNodes.Count, context.ExecEdges.Count);
    }

    // ──────────────────────────────────────────────
    // Block processing
    // ──────────────────────────────────────────────

    private void ProcessBlock(FormattedBlock block, PipelineContext context)
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

            // Record node ID in block membership (skip nulls like LoopBodyEnd)
            if (node != null)
                context.BlockNodeIds[block.Name].Add(node.Id);

            endsWithFlowCtrl = stmt.Kind is FormattedStatementKind.Branch
                or FormattedStatementKind.Loop
                or FormattedStatementKind.LoopBodyEnd
                or FormattedStatementKind.Break
                || IsRegistryFlowControlTerminator(stmt);
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

    private BlueprintNode? ProcessStatement(FormattedStatement stmt, string blockName,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        // Registry path: handle NEW control flow terminators with FunctionName set
        // (e.g. Flip). Standard functions (Branch/Loop/Break) go through FormatFlowControl
        // which doesn't set FunctionName, so they use the switch cases below.
        if (_functionRegistry != null && !string.IsNullOrEmpty(stmt.FunctionName))
        {
            var funcDef = _functionRegistry.Get(stmt.FunctionName);
            if (funcDef != null && funcDef.IsBlockTerminator)
            {
                // LoopBodyEnd: no node created, record for deferred resolution
                if (funcDef.IsFlowControl && stmt.Kind == FormattedStatementKind.LoopBodyEnd)
                {
                    _loopBodyEndDefs.Add((stmt.StatementId, stmt.LoopBodyEndReturnTo ?? "", blockName, prevStmtId));
                    return null;
                }

                var node = _registry.CreateBuiltinFunctionNode(stmt.FunctionName);
                node = funcDef.ConfigureNode(node, stmt);
                ChainNewNode(node, stmt, context, ref prevNode, ref prevStmtId);
                funcDef.OnNodeCreated(node, stmt, context);
                return node;
            }
        }

        // Standard function handling (FormatFlowControl sets Kind but not FunctionName)
        switch (stmt.Kind)
        {
            case FormattedStatementKind.Assignment:
            case FormattedStatementKind.Expression:
                return ProcessCallOrAssignment(stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Print:
                return ChainNewNode(_registry.Create(BlueprintNodeType.Print), stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Set:
                {
                    var node = (SetNode)_registry.Create(BlueprintNodeType.Set);
                    node.VarName = stmt.SetVarName ?? "";
                    return ChainNewNode(node, stmt, context, ref prevNode, ref prevStmtId);
                }

            case FormattedStatementKind.Pause:
                return ChainNewNode(_registry.Create(BlueprintNodeType.Pause), stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Branch:
                {
                    var node = ChainNewNode(_registry.Create(BlueprintNodeType.Branch), stmt, context, ref prevNode, ref prevStmtId);
                    context.BranchDefs.Add((stmt.StatementId, stmt.TrueBlockName, stmt.FalseBlockName));
                    return node;
                }

            case FormattedStatementKind.Loop:
                {
                    var node = ChainNewNode(_registry.Create(BlueprintNodeType.Loop), stmt, context, ref prevNode, ref prevStmtId);
                    context.LoopDefs.Add((stmt.StatementId, stmt.TrueBlockName, stmt.FalseBlockName, blockName));
                    context.LoopNodesByParent[blockName] = node!;
                    return node;
                }

            case FormattedStatementKind.LoopBodyEnd:
                _loopBodyEndDefs.Add((stmt.StatementId, stmt.LoopBodyEndReturnTo ?? "", blockName, prevStmtId));
                return null;

            case FormattedStatementKind.Break:
                return ChainNewNode(_registry.Create(BlueprintNodeType.Break), stmt, context, ref prevNode, ref prevStmtId);

            default:
                return null;
        }
    }

    /// <summary>
    /// Checks if a FormattedStatement corresponds to a registry-based control flow terminator.
    /// Used by ProcessBlock to set endsWithFlowCtrl flag.
    /// </summary>
    private bool IsRegistryFlowControlTerminator(FormattedStatement stmt)
    {
        if (_functionRegistry == null || string.IsNullOrEmpty(stmt.FunctionName)) return false;
        var def = _functionRegistry.Get(stmt.FunctionName);
        return def != null && def.IsBlockTerminator;
    }

    // ──────────────────────────────────────────────
    // Call / Assignment node creation (with PubVar reuse)
    // ──────────────────────────────────────────────

    private BlueprintNode? ProcessCallOrAssignment(FormattedStatement stmt,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        // --- PubVar reuse check (§6.5) ---
        // For Get statements: only LOOP_COND_DUP reuses by PubVarTarget; others always create new
        // For non-Get statements: reuse by fingerprint
        PubVarAssignment? existing = null;

        if (stmt.FunctionName == Get)
        {
            // Get nodes: reuse when PubVarTarget matches and reads the same variable.
            // This handles both LOOP_COND_DUP and round-trip re-parsed Get statements
            // that assign to the same PubVar for the same variable.
            if (!string.IsNullOrEmpty(stmt.PubVarTarget) && !string.IsNullOrEmpty(stmt.GetVarName))
            {
                existing = context.PubVarAssignments.Values
                    .FirstOrDefault(p => p.PubVarName == stmt.PubVarTarget
                        && p.SourceNode is GetNode gn && gn.VarName == stmt.GetVarName);
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

            Log.Debug("[NodeBuilder] Reused node: {Key}", stmt.Fingerprint ?? stmt.PubVarTarget);
            return existing.SourceNode;
        }

        // --- Create new node ---
        BlueprintNode mainNode;
        if (stmt.FunctionName == Get)
        {
            // Get assignment → create GetNode
            var varName = stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "";
            var getNode = (GetNode)_registry.Create(BlueprintNodeType.Get);
            getNode.VarName = varName;
            mainNode = getNode;
        }
        else if (_functionRegistry != null && _functionRegistry.Get(stmt.FunctionName!) is { } funcDef)
        {
            // BuiltinFunctionRegistry function → create BuiltinFunctionNode
            mainNode = _registry.CreateBuiltinFunctionNode(stmt.FunctionName!);
            mainNode = funcDef.ConfigureNode(mainNode, stmt);
        }
        else
        {
            var isHelper = _helperNames.Contains(stmt.FunctionName);
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
        if (!string.IsNullOrEmpty(stmt.PubVarTarget))
        {
            var outputPinName = stmt.FunctionName == Get ? Value : Return;
            var outputPin = mainNode.OutputPins.FirstOrDefault(p => p.Name == outputPinName)
                ?? mainNode.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
            if (outputPin != null)
            {
                // Get: key by PubVarTarget (each Get is unique, identified by its PubVar)
                // Others: key by fingerprint (for reuse detection)
                var key = stmt.FunctionName == Get ? stmt.PubVarTarget : (stmt.Fingerprint ?? stmt.PubVarTarget);
                context.PubVarAssignments[key] = new PubVarAssignment
                {
                    PubVarName = stmt.PubVarTarget,
                    SourceNode = mainNode,
                    SourcePin = outputPin,
                    StatementId = stmt.StatementId,
                };
            }
        }

        return mainNode;
    }

    // ──────────────────────────────────────────────
    // Cross-block edge resolution
    // ──────────────────────────────────────────────

    private void ResolveCrossBlockEdges(PipelineContext context)
    {
        // Branch: True → trueBlock first, False → falseBlock first
        foreach (var (stmtId, trueBlock, falseBlock) in context.BranchDefs)
        {
            if (!string.IsNullOrEmpty(trueBlock) &&
                context.BlockFirstNodes.TryGetValue(trueBlock, out var trueFirst))
            {
                var targetStmtId = FindStmtIdForNode(trueFirst, context);
                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = stmtId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = True,
                    TargetPinName = Exec,
                    IsSpecialRouting = true
                });
            }

            if (!string.IsNullOrEmpty(falseBlock) &&
                context.BlockFirstNodes.TryGetValue(falseBlock, out var falseFirst))
            {
                var targetStmtId = FindStmtIdForNode(falseFirst, context);
                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = stmtId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = False,
                    TargetPinName = Exec,
                    IsSpecialRouting = true
                });
            }
        }

        // Loop: LoopBody → body first, LoopEnd → end first
        foreach (var (stmtId, loopBody, loopEnd, _) in context.LoopDefs)
        {
            if (!string.IsNullOrEmpty(loopBody) &&
                context.BlockFirstNodes.TryGetValue(loopBody, out var bodyFirst))
            {
                var targetStmtId = FindStmtIdForNode(bodyFirst, context);
                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = stmtId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = LoopBody,
                    TargetPinName = Exec,
                    IsSpecialRouting = true
                });
            }

            if (!string.IsNullOrEmpty(loopEnd) &&
                context.BlockFirstNodes.TryGetValue(loopEnd, out var endFirst))
            {
                var targetStmtId = FindStmtIdForNode(endFirst, context);
                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = stmtId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = LoopEnd,
                    TargetPinName = Exec,
                    IsSpecialRouting = true
                });
            }
        }

        // LoopBodyEnd: prev node → Loop.Exec (of parent block)
        foreach (var (_, returnToBlock, _, prevStmtId) in _loopBodyEndDefs)
        {
            if (!context.LoopNodesByParent.TryGetValue(returnToBlock, out var loopNode)) continue;
            var loopStmtId = FindStmtIdForNode(loopNode, context);
            if (prevStmtId == null || loopStmtId == null) continue;

            context.ExecEdges.Add(new PendingExecEdge
            {
                SourceStatementId = prevStmtId,
                TargetStatementId = loopStmtId,
                SourcePinName = Exec,
                TargetPinName = Exec
            });
        }

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

            // Loopback edge (LoopBodyEnd-style)
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
    private BlueprintNode ChainNewNode(BlueprintNode node, FormattedStatement stmt,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        context.AllNodes.Add(node);
        context.NodeByStatementId[stmt.StatementId] = node;

        AddExecEdge(prevStmtId, stmt.StatementId, context);

        prevNode = node;
        prevStmtId = stmt.StatementId;
        return node;
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
}
