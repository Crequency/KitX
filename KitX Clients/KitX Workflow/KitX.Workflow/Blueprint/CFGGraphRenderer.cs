using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Blueprint;

/// <summary>
/// Renders a <see cref="ControlFlowGraph"/> into a <see cref="KitX.Core.Contract.Workflow.Blueprint"/>
/// graph — the v5.1 G-3 path: CFG is the single source of truth, BP is a rendered view.
///
/// <para><b>§11 stable subset implemented here:</b></para>
/// <list type="bullet">
///   <item><b>§11.1</b> — each <c>#Block</c> renders as one <see cref="BlockNode"/>
///       (MainBlock as <see cref="EntryNode"/>); title = block name; standard Exec input pin;
///       <see cref="BlueprintNode.Comment"/> from the block-level BS comment (§9.3).</item>
///   <item><b>§11.3</b> — control-flow terminators (<c>Goto/Branch/ForLoop/Switch</c>) emit NO
///       standalone node. Instead each terminator arm becomes one Exec output pin on the block's
///       node (named per the arm's <see cref="BranchArm.PinName"/>) wired to the target block's
///       Exec input pin. Sequential fall-through (<c>Goto</c>-equivalent with no arm) is a plain
///       Exec wire to <see cref="CFGBlock.FallThroughTarget"/>.</item>
/// </list>
///
/// <para><b>Deliberately out of scope</b> (see <c>Package/Blueprint-Editor-Redesign-Plan.md</c> and
/// the design discussion that scoped this iteration): §11.2 generic EntryPoint/ExitPoint
/// data-boundary derivation (meaningless for BS — cross-block data flow is PubVar-only via global
/// storage); ForLoop index EntryPoint (pending ForLoop → Each evolution); BlockVar inner
/// VariableNode (BlockVar to be removed as over-engineering); §11.4 auto-temp long-name;
/// LayoutX/Y position preservation.</para>
/// </summary>
public class CFGGraphRenderer : ICFGGraphRenderer
{
    /// <inheritdoc/>
    public KitX.Core.Contract.Workflow.Blueprint Render(ControlFlowGraph cfg)
    {
        var bp = new KitX.Core.Contract.Workflow.Blueprint { Name = "Rendered" };

        // ── Phase 1: materialise one node per block, keyed by block name ──
        // PubVars become VariableNodes first so data edges can target them.
        var varNodes = new Dictionary<string, VariableNode>();
        foreach (var pubVar in cfg.PubVarDeclarations)
        {
            var node = new VariableNode
            {
                VarName = pubVar,
                VarKind = VariableKind.PubVar,
                VarType = cfg.PubVarTypes.TryGetValue(pubVar, out var t) ? t : "dynamic",
                Name = pubVar,
            };
            bp.AddNode(node);
            varNodes[pubVar] = node;
        }

        var blockNodes = new Dictionary<string, BlueprintNode>();
        foreach (var block in cfg.Blocks)
        {
            BlueprintNode node;
            bool isMain = block.Name == cfg.MainBlockName;
            if (isMain)
            {
                node = new EntryNode { Name = block.Name };
            }
            else
            {
                node = new BlockNode
                {
                    BlockName = block.Name,
                    Name = block.Name,
                    IsMainBlock = false,
                };
            }
            node.Comment = block.BlockComment;
            bp.AddNode(node);
            blockNodes[block.Name] = node;
        }

        // ── Phase 2: Exec output pins + Exec connections from each terminator ──
        // §11.3: control-flow functions emit no standalone node; their arms become Exec output pins
        // on the block's node, each wired to the target block's Exec input pin.
        foreach (var block in cfg.Blocks)
        {
            var node = blockNodes[block.Name];

            // Add a pin per terminator arm (Branch True/False, ForLoop LoopBody/LoopEnd, Switch Default/N, Goto Exec).
            var terminator = GetTerminator(block);
            if (terminator != null)
            {
                foreach (var arm in terminator.Arms)
                {
                    node.OutputPins.Add(new BlueprintPin
                    {
                        Name = arm.PinName,
                        Direction = PinDirection.Output,
                        Type = PinType.Execution,
                    });
                }
                // Wire each arm to its target block's Exec input pin.
                foreach (var arm in terminator.Arms)
                {
                    if (!blockNodes.TryGetValue(arm.TargetBlockName, out var targetNode)) continue;
                    var srcPin = node.OutputPins.First(p => p.Name == arm.PinName && p.Type == PinType.Execution);
                    var tgtPin = targetNode.InputPins.FirstOrDefault(p => p.Type == PinType.Execution);
                    if (tgtPin == null) continue;
                    bp.AddConnection(new BlueprintConnection
                    {
                        SourceNodeId = node.Id,
                        SourcePinId = srcPin.Id,
                        TargetNodeId = targetNode.Id,
                        TargetPinId = tgtPin.Id,
                    });
                }
            }
            else
            {
                // Sequential fall-through (no explicit terminator): wire to FallThroughTarget.
                var fallThrough = block.FallThroughTarget;
                if (!string.IsNullOrEmpty(fallThrough) &&
                    blockNodes.TryGetValue(fallThrough!, out var targetNode))
                {
                    var srcPin = node.OutputPins.FirstOrDefault(p => p.Type == PinType.Execution);
                    var tgtPin = targetNode.InputPins.FirstOrDefault(p => p.Type == PinType.Execution);
                    if (srcPin != null && tgtPin != null)
                    {
                        bp.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = node.Id,
                            SourcePinId = srcPin.Id,
                            TargetNodeId = targetNode.Id,
                            TargetPinId = tgtPin.Id,
                        });
                    }
                }
            }
        }

        // ── Phase 3: data edges from PubVar-writing statements ──
        // (Pipeline `0 > x` writes to a PubVar; that becomes a data connection.)
        foreach (var block in cfg.Blocks)
        {
            foreach (var stmt in block.GetEffectiveStatements())
            {
                if (string.IsNullOrEmpty(stmt.PubVarTarget)) continue;
                if (!varNodes.TryGetValue(stmt.PubVarTarget, out var targetVar))
                {
                    targetVar = new VariableNode
                    {
                        VarName = stmt.PubVarTarget,
                        VarKind = VariableKind.PubVar,
                        Name = stmt.PubVarTarget,
                    };
                    bp.AddNode(targetVar);
                    varNodes[stmt.PubVarTarget] = targetVar;
                }
                // Data connection marked with PubVarName (the data-edge discriminator).
                bp.Connections.Add(new BlueprintConnection
                {
                    SourceNodeId = string.Empty,
                    TargetNodeId = targetVar.Id,
                    PubVarName = stmt.PubVarTarget,
                });
            }
        }

        // ── Phase 4: BlueprintBlockScope metadata ──
        // Captures block boundaries for reverse conversion (no BlockVars — that category is being removed).
        foreach (var block in cfg.Blocks)
        {
            var scope = new BlueprintBlockScope
            {
                Name = block.Name,
                IsMainBlock = block.Name == cfg.MainBlockName,
                NextBlockName = block.FallThroughTarget,
                HasExplicitBlockBody = block.HasExplicitBlockBody,
            };
            bp.BlockScopes.Add(scope);
        }

        return bp;
    }

    /// <summary>If the block ends with a control-flow terminator, return that statement;
    /// otherwise null (sequential fall-through).</summary>
    private static CFGStatement? GetTerminator(CFGBlock block)
    {
        if (!block.EndsWithControlFlow) return null;
        var stmts = block.Statements;
        return stmts.Count > 0 ? stmts[stmts.Count - 1] : null;
    }
}
