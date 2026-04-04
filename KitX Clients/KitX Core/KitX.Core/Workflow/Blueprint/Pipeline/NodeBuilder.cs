using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Phase 3: Creates all Blueprint nodes and exec flow edges from FormattedBlockScript.
/// Implements PubVar reuse detection during node creation (§6.5).
/// </summary>
public class NodeBuilder
{
    private readonly INodeCreationService _factory;
    private readonly List<HelperFunction> _helpers;
    private readonly HashSet<string> _helperNames;

    // Deferred cross-block edge definitions (resolved after all blocks processed)
    private readonly List<(string stmtId, string? trueBlock, string? falseBlock)> _branchDefs = new();
    private readonly List<(string stmtId, string? loopBody, string? loopEnd, string parentBlock)> _loopDefs = new();
    private readonly List<(string stmtId, string returnToBlock, string blockName, string? prevStmtId)> _loopBodyEndDefs = new();
    private readonly Dictionary<string, string> _blockNextBlock = new();
    private readonly Dictionary<string, string> _blockLastStmtId = new();
    private readonly Dictionary<string, bool> _blockEndsWithFlowCtrl = new();

    public NodeBuilder(INodeCreationService factory, List<HelperFunction> helpers)
    {
        _factory = factory;
        _helpers = helpers;
        _helperNames = new HashSet<string>(helpers.Select(h => h.Name));
    }

    public void Build(FormattedBlockScript script, PipelineContext context)
    {
        // Create EntryNode
        var entry = _factory.CreateEntryNode(0, 0);
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

            endsWithFlowCtrl = stmt.Kind is FormattedStatementKind.Branch
                or FormattedStatementKind.Loop
                or FormattedStatementKind.LoopBodyEnd
                or FormattedStatementKind.Break;
        }

        if (firstNode != null)
        {
            context.BlockFirstNodes[block.Name] = firstNode;
            _blockLastStmtId[block.Name] = prevStmtId ?? "";
        }

        if (!string.IsNullOrEmpty(block.NextBlockName))
            _blockNextBlock[block.Name] = block.NextBlockName;

        _blockEndsWithFlowCtrl[block.Name] = endsWithFlowCtrl;
    }

    // ──────────────────────────────────────────────
    // Statement dispatch
    // ──────────────────────────────────────────────

    private BlueprintNode? ProcessStatement(FormattedStatement stmt, string blockName,
        PipelineContext context, ref BlueprintNode? prevNode, ref string? prevStmtId)
    {
        switch (stmt.Kind)
        {
            case FormattedStatementKind.Assignment:
            case FormattedStatementKind.Expression:
                return ProcessCallOrAssignment(stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Print:
                return ChainNewNode(_factory.CreatePrintNode(), stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Set:
                return ChainNewNode(_factory.CreateSetNode(stmt.SetVarName ?? ""), stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Get:
                return ChainNewNode(_factory.CreateGetNode(stmt.GetVarName ?? ""), stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Pause:
                return ChainNewNode(_factory.CreatePauseNode(), stmt, context, ref prevNode, ref prevStmtId);

            case FormattedStatementKind.Branch:
                {
                    var node = ChainNewNode(_factory.CreateBranchNode(), stmt, context, ref prevNode, ref prevStmtId);
                    _branchDefs.Add((stmt.StatementId, stmt.TrueBlockName, stmt.FalseBlockName));
                    return node;
                }

            case FormattedStatementKind.Loop:
                {
                    var node = ChainNewNode(_factory.CreateLoopNode(), stmt, context, ref prevNode, ref prevStmtId);
                    _loopDefs.Add((stmt.StatementId, stmt.TrueBlockName, stmt.FalseBlockName, blockName));
                    context.LoopNodesByParent[blockName] = (LoopNode)node!;
                    return node;
                }

            case FormattedStatementKind.LoopBodyEnd:
                // No node created. Record for deferred resolution.
                _loopBodyEndDefs.Add((stmt.StatementId, stmt.LoopBodyEndReturnTo ?? "", blockName, prevStmtId));
                return null;

            case FormattedStatementKind.Break:
                return ChainNewNode(_factory.CreateBreakNode(), stmt, context, ref prevNode, ref prevStmtId);

            default:
                return null;
        }
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

        if (stmt.FunctionName == "Get")
        {
            // Get nodes: only reuse for LOOP_COND_DUP with matching PubVarTarget
            if (stmt.IsLoopConditionDuplication && !string.IsNullOrEmpty(stmt.PubVarTarget))
            {
                existing = context.PubVarAssignments.Values
                    .FirstOrDefault(p => p.PubVarName == stmt.PubVarTarget
                        && p.SourceNode.NodeType == BlueprintNodeType.Get);
            }
            // Non-DUP Get: never reuse → existing stays null
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
        if (stmt.FunctionName == "Get")
        {
            // Get assignment → create GetNode
            var varName = stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "";
            mainNode = _factory.CreateGetNode(varName);
        }
        else
        {
            var isHelper = _helperNames.Contains(stmt.FunctionName);
            mainNode = isHelper
                ? _factory.CreateCallHelperNode(stmt.FunctionName!)
                : _factory.CreateCallNode(stmt.FunctionName!);

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
            var outputPinName = stmt.FunctionName == "Get" ? "Value" : "Return";
            var outputPin = mainNode.OutputPins.First(p => p.Name == outputPinName);
            // Get: key by PubVarTarget (each Get is unique, identified by its PubVar)
            // Others: key by fingerprint (for reuse detection)
            var key = stmt.FunctionName == "Get" ? stmt.PubVarTarget : (stmt.Fingerprint ?? stmt.PubVarTarget);
            context.PubVarAssignments[key] = new PubVarAssignment
            {
                PubVarName = stmt.PubVarTarget,
                SourceNode = mainNode,
                SourcePin = outputPin,
                StatementId = stmt.StatementId,
            };
        }

        return mainNode;
    }

    // ──────────────────────────────────────────────
    // Cross-block edge resolution
    // ──────────────────────────────────────────────

    private void ResolveCrossBlockEdges(PipelineContext context)
    {
        // Branch: True → trueBlock first, False → falseBlock first
        foreach (var (stmtId, trueBlock, falseBlock) in _branchDefs)
        {
            if (!string.IsNullOrEmpty(trueBlock) &&
                context.BlockFirstNodes.TryGetValue(trueBlock, out var trueFirst))
            {
                var targetStmtId = FindStmtIdForNode(trueFirst, context);
                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = stmtId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = "True",
                    TargetPinName = "Exec",
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
                    SourcePinName = "False",
                    TargetPinName = "Exec",
                    IsSpecialRouting = true
                });
            }
        }

        // Loop: LoopBody → body first, LoopEnd → end first
        foreach (var (stmtId, loopBody, loopEnd, _) in _loopDefs)
        {
            if (!string.IsNullOrEmpty(loopBody) &&
                context.BlockFirstNodes.TryGetValue(loopBody, out var bodyFirst))
            {
                var targetStmtId = FindStmtIdForNode(bodyFirst, context);
                context.ExecEdges.Add(new PendingExecEdge
                {
                    SourceStatementId = stmtId,
                    TargetStatementId = targetStmtId,
                    SourcePinName = "LoopBody",
                    TargetPinName = "Exec",
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
                    SourcePinName = "LoopEnd",
                    TargetPinName = "Exec",
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
                SourcePinName = "Exec",
                TargetPinName = "Exec"
            });
        }

        // Sequential fall-through for blocks without flow control endings
        foreach (var (blockName, nextBlockName) in _blockNextBlock)
        {
            if (_blockEndsWithFlowCtrl.GetValueOrDefault(blockName)) continue;
            if (string.IsNullOrEmpty(nextBlockName)) continue;
            if (!context.BlockFirstNodes.TryGetValue(nextBlockName, out var nextFirst)) continue;
            if (!_blockLastStmtId.TryGetValue(blockName, out var lastStmtId)) continue;

            var targetStmtId = FindStmtIdForNode(nextFirst, context);
            if (targetStmtId == null) continue;

            context.ExecEdges.Add(new PendingExecEdge
            {
                SourceStatementId = lastStmtId,
                TargetStatementId = targetStmtId,
                SourcePinName = "Exec",
                TargetPinName = "Exec"
            });
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
