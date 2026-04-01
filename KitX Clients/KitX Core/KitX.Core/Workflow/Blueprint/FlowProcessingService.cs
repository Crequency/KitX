using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

public class FlowProcessingService : IFlowProcessingService
{
    private readonly IConnectionCreationService _connectionCreationService;

    public FlowProcessingService(IConnectionCreationService connectionCreationService)
    {
        _connectionCreationService = connectionCreationService;
    }

    public void ProcessConstBlock(BlockDefinition block, ConversionContext context)
    {
        foreach (var variable in block.Variables)
        {
            var constValue = variable.DefaultValue?.ToString();
            if (string.IsNullOrEmpty(constValue))
            {
                constValue = variable.Type.ToLower() switch
                {
                    "int" or "integer" or "long" or "short" or "byte" => "0",
                    "float" or "double" or "decimal" => "0",
                    "bool" or "boolean" => "false",
                    "string" => "",
                    "char" => "\0",
                    _ => ""
                };
            }

            var constNode = new ConstNode
            {
                ConstName = variable.Name,
                ConstType = variable.Type,
                ConstValue = constValue
            };

            // Set Value pin PinType based on ConstType for correct data edge coloring
            var valuePin = constNode.OutputPins.FirstOrDefault(p => p.Name == "Value");
            if (valuePin != null)
            {
                valuePin.Type = variable.Type.ToLower() switch
                {
                    "int" or "integer" or "long" or "short" or "byte" => PinType.Integer,
                    "float" or "double" or "decimal" => PinType.Double,
                    "bool" or "boolean" => PinType.Boolean,
                    "string" or "char" => PinType.String,
                    _ => PinType.Any
                };
            }

            context.Blueprint.AddNode(constNode);
            context.Blueprint.ConstValues.Add(new VariableConstant
            {
                Name = variable.Name,
                Type = variable.Type,
                DefaultValue = variable.DefaultValue
            });
            Log.Debug("Created ConstNode: Name={ConstName}, Id={NodeId}", constNode.Name, constNode.Id);
        }
    }

    public void ProcessPubVarBlock(BlockDefinition block, ConversionContext context)
    {
        foreach (var variable in block.Variables)
        {
            context.Blueprint.PubVarNames.Add(variable.Name);
        }
    }


    public void ProcessMainBlock(BlockDefinition block, ConversionContext context, INodeCreationService nodeCreationService)
    {
        var entryNode = new EntryNode
        {
            X = 50,
            Y = 50
        };
        context.Blueprint.AddNode(entryNode);
        context.EntryNode = entryNode;
        Log.Debug("Created EntryNode: Name={NodeName}, Id={NodeId}", entryNode.Name, entryNode.Id);

        var mainFlowNodes = new List<BlueprintNode>();
        var currentY = 150.0;

        for (int i = 0; i < block.Statements.Count; i++)
        {
            var statement = block.Statements[i];
            var node = ProcessStatement(statement, context, nodeCreationService, 50, currentY);
            if (node != null)
            {
                context.Blueprint.AddNode(node);
                mainFlowNodes.Add(node);
                currentY += 120;
            }
        }

        _connectionCreationService.LinkNodesWithExec(mainFlowNodes, context.Blueprint);

        var flowNode = mainFlowNodes.LastOrDefault(n => n is BranchNode || n is LoopNode);
        FlowControlStatement? flowCtrl = null;

        if (flowNode == null)
        {
            if (context.Script.LoopBlocks.TryGetValue("MainBlock", out var loopBlock))
            {
                flowCtrl = loopBlock.Statements.FirstOrDefault(s => s is FlowControlStatement) as FlowControlStatement;
                if (flowCtrl != null)
                {
                    context.CurrentParentBlock = "MainBlock";
                    var loopNodeInLoopBlock = ProcessFlowControlStatement(flowCtrl, context, nodeCreationService, 350, 150);
                    if (loopNodeInLoopBlock is LoopNode loopFromBlock)
                    {
                        flowNode = loopFromBlock;
                        context.LoopNodesByParentBlock["MainBlock"] = loopFromBlock;
                    }
                }
            }
        }
        else
        {
            flowCtrl = block.Statements.LastOrDefault(s => s is FlowControlStatement) as FlowControlStatement;
        }

        if (flowNode != null && flowCtrl != null)
        {
            if (flowNode is BranchNode branch)
            {
                ProcessBranchNodeConnectionsInMain(branch, flowCtrl, context);
            }
            else if (flowNode is LoopNode loop)
            {
                ProcessLoopNodeConnectionsInMain(loop, flowCtrl, context);
            }
        }

        if (mainFlowNodes.Count > 0)
        {
            var firstNode = mainFlowNodes[0];
            var entryExecPin = entryNode.OutputPins.First(p => p.Name == "Exec");
            var firstExecPin = firstNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
            if (entryExecPin != null && firstExecPin != null)
            {
                context.Blueprint.AddConnection(new BlueprintConnection
                {
                    SourceNodeId = entryNode.Id,
                    SourcePinId = entryExecPin.Id,
                    TargetNodeId = firstNode.Id,
                    TargetPinId = firstExecPin.Id
                });
            }
        }
    }


    public BlueprintNode? ProcessStatement(BlockStatement statement, ConversionContext context, INodeCreationService nodeCreationService, double x, double y)
    {
        switch (statement)
        {
            case FlowControlStatement flowCtrl:
                if ((flowCtrl.ControlType == FlowControlType.Branch || flowCtrl.ControlType == FlowControlType.Loop) &&
                    !string.IsNullOrEmpty(flowCtrl.TrueBlockName) &&
                    !string.IsNullOrEmpty(flowCtrl.FalseBlockName))
                {
                    if (flowCtrl.ControlType == FlowControlType.Loop &&
                        context.CurrentParentBlock != null &&
                        context.LoopNodesByParentBlock.TryGetValue(context.CurrentParentBlock, out var existingLoop))
                    {
                        context.LastProcessedNode = existingLoop;
                        return existingLoop;
                    }

                    var flowNode = ProcessFlowControlStatement(flowCtrl, context, nodeCreationService, x, y);
                    if (flowNode != null)
                    {
                        context.LastProcessedNode = flowNode;
                    }
                    return flowNode;
                }
                if (flowCtrl.ControlType == FlowControlType.LoopBodyEnd)
                {
                    HandleLoopBodyEnd(flowCtrl, context);
                    return null;
                }
                var otherFlowNode = ProcessFlowControlStatement(flowCtrl, context, nodeCreationService, x, y);
                if (otherFlowNode != null)
                {
                    context.LastProcessedNode = otherFlowNode;
                }
                return otherFlowNode;

            case ExpressionStatement expr:
                if (expr.Expression.StartsWith("NextBlock = "))
                    return null;
                var actionNode = CreateActionNodeFromExpression(expr.Expression, context);
                if (actionNode != null)
                {
                    context.LastProcessedNode = actionNode;
                }
                return actionNode;

            default:
                return null;
        }
    }

    public BlueprintNode? ProcessFlowControlStatement(FlowControlStatement flowCtrl, ConversionContext context, INodeCreationService nodeCreationService, double x, double y)
    {
        var node = CreateControlFlowNode(flowCtrl);
        if (node == null) return null;

        node.X = x;
        node.Y = y;

        if (!string.IsNullOrEmpty(flowCtrl.ConditionExpression))
        {
            _connectionCreationService.CreateDataConnectionsForExpression(flowCtrl.ConditionExpression, node, context);
        }

        return node;
    }


    public BlueprintNode? ProcessBlockRecursive(BlockDefinition block, ConversionContext context, INodeCreationService nodeCreationService, double x, double y)
    {
        if (context.VisitedBlocks.Contains(block.Name))
        {
            return context.BlockFirstNodes.GetValueOrDefault(block.Name);
        }
        context.VisitedBlocks.Add(block.Name);

        var nodes = new List<BlueprintNode>();
        var currentY = y;

        foreach (var statement in block.Statements)
        {
            var node = ProcessStatement(statement, context, nodeCreationService, x, currentY);
            if (node != null)
            {
                if (!context.Blueprint.Nodes.Any(n => n.Id == node.Id))
                {
                    context.Blueprint.AddNode(node);
                }
                nodes.Add(node);
                currentY += 120;

                if (context.PendingNodesForExecChain.Count > 0)
                {
                    var parentNodeIndex = nodes.Count - 1;
                    foreach (var pendingNode in context.PendingNodesForExecChain)
                    {
                        if (!nodes.Any(n => n.Id == pendingNode.Id))
                        {
                            nodes.Insert(parentNodeIndex, pendingNode);
                            parentNodeIndex++;
                        }
                    }
                    context.PendingNodesForExecChain.Clear();
                }
            }
        }

        if (nodes.Count == 0 && block.Statements.Count > 0)
        {
            var firstStatement = block.Statements[0];
            if (firstStatement is FlowControlStatement flowCtrl &&
                (flowCtrl.ControlType == FlowControlType.Branch || flowCtrl.ControlType == FlowControlType.Loop))
            {
                var routingNode = ProcessFlowControlStatement(flowCtrl, context, nodeCreationService, x, y);
                if (routingNode != null)
                {
                    context.Blueprint.AddNode(routingNode);
                    nodes.Add(routingNode);
                }
            }
        }

        _connectionCreationService.LinkNodesWithExec(nodes, context.Blueprint);

        context.VisitedBlocks.Add(block.Name);

        var flowNode = nodes.LastOrDefault(n => n is BranchNode || n is LoopNode);
        if (flowNode != null)
        {
            if (flowNode is BranchNode branch)
            {
                ProcessBranchNodeConnections(branch, block, context, x, y);
            }
            else if (flowNode is LoopNode loop)
            {
                ProcessLoopNodeConnections(loop, block, context, x, y);
            }
        }

        if (!string.IsNullOrEmpty(block.NextBlockName) &&
            block.Type != BlockType.LoopBlock)
        {
            var nextBlock = context.Script.GetBlockByName(block.NextBlockName);
            if (nextBlock != null && !context.VisitedBlocks.Contains(nextBlock.Name))
            {
                if (nodes.Count > 0)
                {
                    var lastNode = nodes.Last();
                    var lastExecOut = lastNode.OutputPins.FirstOrDefault(p => p.Name == "Exec");

                    // Skip cross-block connection if lastNode's Exec output already has
                    // an external connection (e.g. from HandleLoopBodyEnd or branch/loop routing).
                    // This prevents duplicate connections like BLE → Print(猜大了)
                    // when the next block is also reachable via Branch True/False.
                    var lastExecAlreadyConnected = lastExecOut != null &&
                        context.Blueprint.Connections.Any(c =>
                            c.SourceNodeId == lastNode.Id &&
                            c.SourcePinId == lastExecOut.Id &&
                            c.TargetNodeId != lastNode.Id);

                    if (!lastExecAlreadyConnected)
                    {
                        var nextFirstNode = ProcessBlockRecursive(nextBlock, context, nodeCreationService, x + 300, y);
                        if (nextFirstNode != null)
                        {
                            var nextExecIn = nextFirstNode.InputPins.FirstOrDefault(p => p.Name == "Exec");

                            if (lastExecOut == null || lastExecOut.Direction != PinDirection.Output)
                            {
                                Log.Warning("Cannot create cross-block Exec connection: {Node}.Exec is not a valid output pin",
                                    lastNode.Name);
                            }
                            else if (lastNode.Id == nextFirstNode.Id)
                            {
                                Log.Warning("Skipping self-loop cross-block Exec connection");
                            }
                            else if (lastExecOut != null && nextExecIn != null)
                            {
                                context.Blueprint.AddConnection(new BlueprintConnection
                                {
                                    SourceNodeId = lastNode.Id,
                                    SourcePinId = lastExecOut.Id,
                                    TargetNodeId = nextFirstNode.Id,
                                    TargetPinId = nextExecIn.Id
                                });
                            }
                        }
                    }
                }
            }
        }

        var firstNode = nodes.FirstOrDefault();
        if (firstNode != null)
        {
            context.BlockFirstNodes[block.Name] = firstNode;
        }

        return firstNode;
    }


    public void ProcessBranchNodeConnections(BranchNode branch, BlockDefinition block, ConversionContext context, double x, double y)
    {
        var flowCtrl = block.Statements.FirstOrDefault(s => s is FlowControlStatement) as FlowControlStatement;
        if (flowCtrl == null) return;

        double targetY = y + 150;

        if (!string.IsNullOrEmpty(flowCtrl.TrueBlockName))
        {
            var trueBlock = context.Script.GetBlockByName(flowCtrl.TrueBlockName);
            if (trueBlock != null && !context.VisitedBlocks.Contains(trueBlock.Name))
            {
                var firstTrueNode = ProcessBlockRecursive(trueBlock, context, null, x + 300, targetY);
                if (firstTrueNode != null && firstTrueNode.Id != branch.Id)
                {
                    var truePin = branch.OutputPins.FirstOrDefault(p => p.Name == "True");
                    var firstExecIn = firstTrueNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                    if (truePin != null && firstExecIn != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = branch.Id,
                            SourcePinId = truePin.Id,
                            TargetNodeId = firstTrueNode.Id,
                            TargetPinId = firstExecIn.Id
                        });
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(flowCtrl.FalseBlockName))
        {
            var falseBlock = context.Script.GetBlockByName(flowCtrl.FalseBlockName);
            if (falseBlock != null)
            {
                BlueprintNode? firstFalseNode;
                if (context.VisitedBlocks.Contains(falseBlock.Name))
                {
                    firstFalseNode = context.BlockFirstNodes.GetValueOrDefault(falseBlock.Name);
                }
                else
                {
                    firstFalseNode = ProcessBlockRecursive(falseBlock, context, null, x + 300, targetY + 200);
                }

                if (firstFalseNode != null && firstFalseNode.Id != branch.Id && context.Blueprint.Nodes.Any(n => n.Id == firstFalseNode.Id))
                {
                    var actualTarget = GetFirstConnectableNode(firstFalseNode, context);
                    var falsePin = branch.OutputPins.FirstOrDefault(p => p.Name == "False");
                    var actualExecIn = actualTarget?.InputPins.FirstOrDefault(p => p.Name == "Exec");

                    if (falsePin != null && actualExecIn != null && actualTarget != null && actualTarget.Id != branch.Id)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = branch.Id,
                            SourcePinId = falsePin.Id,
                            TargetNodeId = actualTarget.Id,
                            TargetPinId = actualExecIn.Id
                        });
                    }
                }
            }
        }
    }

    public void ProcessBranchNodeConnectionsInMain(BranchNode branch, FlowControlStatement flowCtrl, ConversionContext context)
    {
        double x = 350;
        double targetY = 300;

        if (!string.IsNullOrEmpty(flowCtrl.TrueBlockName))
        {
            var trueBlock = context.Script.GetBlockByName(flowCtrl.TrueBlockName);
            if (trueBlock != null)
            {
                var firstTrueNode = ProcessBlockRecursive(trueBlock, context, null, x, targetY);
                if (firstTrueNode != null)
                {
                    var truePin = branch.OutputPins.FirstOrDefault(p => p.Name == "True");
                    var firstExecIn = firstTrueNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                    if (truePin != null && firstExecIn != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = branch.Id,
                            SourcePinId = truePin.Id,
                            TargetNodeId = firstTrueNode.Id,
                            TargetPinId = firstExecIn.Id
                        });
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(flowCtrl.FalseBlockName))
        {
            var falseBlock = context.Script.GetBlockByName(flowCtrl.FalseBlockName);
            if (falseBlock != null)
            {
                if (!context.VisitedBlocks.Contains(falseBlock.Name))
                {
                    var firstFalseNode = ProcessBlockRecursive(falseBlock, context, null, x, targetY + 200);
                    if (firstFalseNode != null && firstFalseNode.Id != branch.Id)
                    {
                        var falsePin = branch.OutputPins.FirstOrDefault(p => p.Name == "False");
                        var firstExecIn = firstFalseNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                        if (falsePin != null && firstExecIn != null)
                        {
                            context.Blueprint.AddConnection(new BlueprintConnection
                            {
                                SourceNodeId = branch.Id,
                                SourcePinId = falsePin.Id,
                                TargetNodeId = firstFalseNode.Id,
                                TargetPinId = firstExecIn.Id
                            });
                        }
                    }
                }
            }
        }
    }


    public void ProcessLoopNodeConnections(LoopNode loop, BlockDefinition block, ConversionContext context, double x, double y)
    {
        var flowCtrl = block.Statements.FirstOrDefault(s => s is FlowControlStatement) as FlowControlStatement;
        if (flowCtrl == null) return;

        double targetY = y + 150;

        if (!string.IsNullOrEmpty(flowCtrl.TrueBlockName))
        {
            var loopBodyBlock = context.Script.GetBlockByName(flowCtrl.TrueBlockName);
            if (loopBodyBlock != null)
            {
                context.LoopNodesByParentBlock[flowCtrl.TrueBlockName] = loop;

                var firstLoopNode = ProcessBlockRecursive(loopBodyBlock, context, null, x + 300, targetY);
                if (firstLoopNode != null)
                {
                    var loopBodyPin = loop.OutputPins.FirstOrDefault(p => p.Name == "LoopBody");
                    var firstExecIn = firstLoopNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                    if (loopBodyPin != null && firstExecIn != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = loop.Id,
                            SourcePinId = loopBodyPin.Id,
                            TargetNodeId = firstLoopNode.Id,
                            TargetPinId = firstExecIn.Id
                        });
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(flowCtrl.FalseBlockName))
        {
            var afterLoopBlock = context.Script.GetBlockByName(flowCtrl.FalseBlockName);
            if (afterLoopBlock != null)
            {
                var firstAfterLoopNode = ProcessBlockRecursive(afterLoopBlock, context, null, x + 300, targetY + 200);
                if (firstAfterLoopNode != null)
                {
                    var loopEndPin = loop.OutputPins.FirstOrDefault(p => p.Name == "LoopEnd");
                    var firstExecIn = firstAfterLoopNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                    if (loopEndPin != null && firstExecIn != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = loop.Id,
                            SourcePinId = loopEndPin.Id,
                            TargetNodeId = firstAfterLoopNode.Id,
                            TargetPinId = firstExecIn.Id
                        });
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(flowCtrl.LoopBodyEndReturnTo))
        {
            context.LoopNodesByParentBlock[flowCtrl.LoopBodyEndReturnTo] = loop;
        }
    }

    public void ProcessLoopNodeConnectionsInMain(LoopNode loop, FlowControlStatement flowCtrl, ConversionContext context)
    {
        double x = 350;
        double targetY = 300;

        if (!string.IsNullOrEmpty(flowCtrl.TrueBlockName))
        {
            var loopBodyBlock = context.Script.GetBlockByName(flowCtrl.TrueBlockName);
            if (loopBodyBlock != null)
            {
                context.LoopNodesByParentBlock[flowCtrl.TrueBlockName] = loop;
                if (!string.IsNullOrEmpty(flowCtrl.LoopBodyEndReturnTo))
                {
                    context.LoopNodesByParentBlock[flowCtrl.LoopBodyEndReturnTo] = loop;
                }

                var firstLoopNode = ProcessBlockRecursive(loopBodyBlock, context, null, x, targetY);
                if (firstLoopNode != null)
                {
                    var loopBodyPin = loop.OutputPins.FirstOrDefault(p => p.Name == "LoopBody");
                    var firstExecIn = firstLoopNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                    if (loopBodyPin != null && firstExecIn != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = loop.Id,
                            SourcePinId = loopBodyPin.Id,
                            TargetNodeId = firstLoopNode.Id,
                            TargetPinId = firstExecIn.Id
                        });
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(flowCtrl.FalseBlockName))
        {
            var afterLoopBlock = context.Script.GetBlockByName(flowCtrl.FalseBlockName);
            if (afterLoopBlock != null)
            {
                var firstAfterLoopNode = ProcessBlockRecursive(afterLoopBlock, context, null, x, targetY + 200);
                if (firstAfterLoopNode != null)
                {
                    var loopEndPin = loop.OutputPins.FirstOrDefault(p => p.Name == "LoopEnd");
                    var firstExecIn = firstAfterLoopNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                    if (loopEndPin != null && firstExecIn != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = loop.Id,
                            SourcePinId = loopEndPin.Id,
                            TargetNodeId = firstAfterLoopNode.Id,
                            TargetPinId = firstExecIn.Id
                        });
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(flowCtrl.LoopBodyEndReturnTo))
        {
            context.LoopNodesByParentBlock[flowCtrl.LoopBodyEndReturnTo] = loop;
        }
    }


    public void ProcessLoopBlock(BlockDefinition block, ConversionContext context, INodeCreationService nodeCreationService)
    {
        if (context.VisitedBlocks.Contains(block.Name)) return;
        context.VisitedBlocks.Add(block.Name);

        var parentBlockName = block.ParentBlockName ?? block.Name;
        context.CurrentParentBlock = parentBlockName;

        var nodes = new List<BlueprintNode>();
        var currentY = 50.0;

        foreach (var statement in block.Statements)
        {
            var node = ProcessStatement(statement, context, nodeCreationService, 50, currentY);
            if (node != null)
            {
                context.Blueprint.AddNode(node);
                nodes.Add(node);
                currentY += 120;
            }
        }

        _connectionCreationService.LinkNodesWithExec(nodes, context.Blueprint);

        foreach (var statement in block.Statements)
        {
            if (statement is FlowControlStatement flowCtrl)
            {
                if (!string.IsNullOrEmpty(flowCtrl.TrueBlockName))
                {
                    var trueBlock = context.Script.GetBlockByName(flowCtrl.TrueBlockName);
                    if (trueBlock != null && !context.VisitedBlocks.Contains(trueBlock.Name))
                        ProcessBlockRecursive(trueBlock, context, nodeCreationService, 350, 50);
                }
                if (!string.IsNullOrEmpty(flowCtrl.FalseBlockName))
                {
                    var falseBlock = context.Script.GetBlockByName(flowCtrl.FalseBlockName);
                    if (falseBlock != null && !context.VisitedBlocks.Contains(falseBlock.Name))
                        ProcessBlockRecursive(falseBlock, context, nodeCreationService, 350, 200);
                }
            }
        }
    }

    public void HandleLoopBodyEnd(FlowControlStatement flowCtrl, ConversionContext context)
    {
        if (!string.IsNullOrEmpty(flowCtrl.LoopBodyEndReturnTo))
        {
            if (context.LoopNodesByParentBlock.TryGetValue(flowCtrl.LoopBodyEndReturnTo, out var loopNode))
            {
                var lastNode = context.LastProcessedNode;

                if (lastNode != null)
                {
                    var loopConditionIn = loopNode.InputPins.FirstOrDefault(p => p.Name == "Condition");
                    BlueprintNode? bleNode = null;

                    if (loopConditionIn != null)
                    {
                        var conditionConn = context.Blueprint.Connections
                            .FirstOrDefault(c => c.TargetNodeId == loopNode.Id &&
                                                   c.TargetPinId == loopConditionIn.Id);
                        if (conditionConn != null)
                        {
                            bleNode = context.Blueprint.Nodes.FirstOrDefault(n => n.Id == conditionConn.SourceNodeId);
                        }
                    }

                    if (bleNode == null && !string.IsNullOrEmpty(flowCtrl.ConditionExpression))
                    {
                        var conditionVarName = flowCtrl.ConditionExpression.Trim();
                        var source = context.VariableSources.Values
                            .FirstOrDefault(s => s.PubVarName == conditionVarName);
                        if (source != null)
                        {
                            bleNode = source.Node;
                        }
                    }

                    if (bleNode != null)
                    {
                        var lastExecOut = lastNode.OutputPins.FirstOrDefault(p => p.Name == "Exec");
                        var bleExecIn = bleNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
                        var bleExecOut = bleNode.OutputPins.FirstOrDefault(p => p.Name == "Exec");
                        var loopExecIn = loopNode.InputPins.FirstOrDefault(p => p.Name == "Exec");

                        if (lastExecOut != null && bleExecIn != null && lastNode.Id != bleNode.Id)
                        {
                            var hasConnection = context.Blueprint.Connections
                                .Any(c => c.SourceNodeId == lastNode.Id &&
                                           c.SourcePinId == lastExecOut.Id &&
                                           c.TargetNodeId == bleNode.Id &&
                                           c.TargetPinId == bleExecIn.Id);
                            if (!hasConnection)
                            {
                                context.Blueprint.AddConnection(new BlueprintConnection
                                {
                                    SourceNodeId = lastNode.Id,
                                    SourcePinId = lastExecOut.Id,
                                    TargetNodeId = bleNode.Id,
                                    TargetPinId = bleExecIn.Id
                                });
                            }
                        }

                        if (bleExecOut != null && loopExecIn != null)
                        {
                            var hasConnection = context.Blueprint.Connections
                                .Any(c => c.SourceNodeId == bleNode.Id &&
                                           c.SourcePinId == bleExecOut.Id &&
                                           c.TargetNodeId == loopNode.Id &&
                                           c.TargetPinId == loopExecIn.Id);
                            if (!hasConnection)
                            {
                                context.Blueprint.AddConnection(new BlueprintConnection
                                {
                                    SourceNodeId = bleNode.Id,
                                    SourcePinId = bleExecOut.Id,
                                    TargetNodeId = loopNode.Id,
                                    TargetPinId = loopExecIn.Id
                                });
                            }
                        }
                    }
                }

                context.LoopNodesByParentBlock[flowCtrl.LoopBodyEndReturnTo] = loopNode;
            }
        }
    }

    public BlueprintNode? GetFirstConnectableNode(BlueprintNode node, ConversionContext context)
    {
        return node;
    }


    private BlueprintNode? CreateControlFlowNode(FlowControlStatement statement)
    {
        switch (statement.ControlType)
        {
            case FlowControlType.Branch:
                return new BranchNode();
            case FlowControlType.Loop:
                return new LoopNode();
            case FlowControlType.Break:
                return new BreakNode();
            default:
                return null;
        }
    }

    private BlueprintNode? CreateActionNodeFromExpression(string expression, ConversionContext context)
    {
        var parsed = TryParseInvocationExpression(expression);
        if (parsed == null)
            return null;

        var (funcName, args, assignment) = parsed.Value;

        if (funcName == "Get")
        {
            var varName = args?.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty;

            var getNode = new GetNode
            {
                VarName = varName,
                Name = "Get:" + varName
            };
            Log.Debug("Created GetNode: VarName={VarName}, Id={NodeId}", varName, getNode.Id);

            var getValuePin = getNode.OutputPins.First(p => p.Name == "Value");

            if (assignment != null)
            {
                var assignedVarName = assignment.Left.ToString();
                if (assignedVarName != "_")
                {
                    var isPubVar = context.Script.PubVarBlock?.Variables.Any(v => v.Name == assignedVarName) == true;
                    context.VariableSources["__get_" + assignedVarName + "_" + Guid.NewGuid().ToString("N") + "__"] = new VariableSource
                    {
                        Node = getNode,
                        Pin = getValuePin,
                        PubVarName = isPubVar ? assignedVarName : null
                    };
                    Log.Debug("Registered GetNode output as VariableSource: {VarName}, IsPubVar={IsPubVar}",
                        assignedVarName, isPubVar);
                }
            }

            return getNode;
        }

        if (funcName == "Set")
        {
            var argList = args?.Arguments.ToList() ?? new List<ArgumentSyntax>();
            if (argList.Count >= 2)
            {
                var varName = argList[0].Expression.ToString();
                var valueExpr = argList[1].Expression;
                var setNode = new SetNode
                {
                    VarName = varName,
                    Name = "Set:" + varName
                };
                Log.Debug("Created SetNode: VarName={VarName}, Id={NodeId}", varName, setNode.Id);

                var setValuePin = setNode.InputPins.FirstOrDefault(p => p.Name == "Value");
                if (setValuePin != null && valueExpr != null)
                {
                    _connectionCreationService.ProcessArgumentExpression(valueExpr, setValuePin, setNode, context);
                }

                return setNode;
            }
            return null;
        }

        if (funcName == "Print")
        {
            var printNode = new PrintNode();
            var arg = args?.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty;
            _connectionCreationService.CreateDataConnectionsForExpression(arg, printNode, context);
            return printNode;
        }

        if (funcName == "Pause")
        {
            var pauseNode = new PauseNode();
            var arg = args?.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty;
            _connectionCreationService.CreateDataConnectionsForExpression(arg, pauseNode, context);
            return pauseNode;
        }

        if (assignment != null)
        {
            var varName = assignment.Left.ToString();

            if (funcName is "Loop" or "Branch" or "LoopBodyEnd" or "Break")
            {
                Log.Debug("Skipped creating CallNode for flow control function: {FuncName}", funcName);
                return null;
            }

            var existingSource = context.VariableSources.Values
                .FirstOrDefault(s => s.PubVarName == varName);

            BlueprintNode callNode;
            if (existingSource != null)
            {
                callNode = existingSource.Node;
                Log.Debug("Reused existing source node {NodeName} for {VarName}", callNode.Name, varName);
            }
            else
            {
                var isHelper = context.Script.HelperFunctions.Any(h => h.Name == funcName);
                if (isHelper)
                    callNode = new CallHelperNode { HelperFunctionName = funcName, Name = "Helper:" + funcName };
                else
                    callNode = new CallNode { FunctionName = funcName, Name = "Call:" + funcName };

                _connectionCreationService.AddParameterPins(callNode, args?.Arguments);
                _connectionCreationService.CreateDataConnectionsForArguments(args?.Arguments, callNode, context);

                var returnPin = callNode.OutputPins.FirstOrDefault(p => p.Name == "Return");
                if (returnPin != null)
                {
                    var isPubVar = context.Script.PubVarBlock?.Variables.Any(v => v.Name == varName) == true;
                    context.VariableSources[varName] = new VariableSource
                    {
                        Node = callNode,
                        Pin = returnPin,
                        PubVarName = isPubVar ? varName : null
                    };
                }
            }

            return callNode;
        }

        if (funcName is "Loop" or "Branch" or "LoopBodyEnd" or "Break")
        {
            Log.Debug("Skipped creating CallNode for flow control/builtin function: {FuncName}", funcName);
            return null;
        }

        BlueprintNode callNode2;
        var isHelper2 = context.Script.HelperFunctions.Any(h => h.Name == funcName);
        if (isHelper2)
            callNode2 = new CallHelperNode { HelperFunctionName = funcName, Name = "Helper:" + funcName };
        else
            callNode2 = new CallNode { FunctionName = funcName, Name = "Call:" + funcName };

        _connectionCreationService.AddParameterPins(callNode2, args?.Arguments);
        _connectionCreationService.CreateDataConnectionsForArguments(args?.Arguments, callNode2, context);

        return callNode2;
    }

    private (string? funcName, ArgumentListSyntax? args, AssignmentExpressionSyntax? assignment)? TryParseInvocationExpression(string expression)
    {
        var wrappedCode = "_ = " + expression + ";";
        var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, cancellationToken: CancellationToken.None);
        var root = syntaxTree.GetCompilationUnitRoot();

        var globalStmt = root.Members.FirstOrDefault() as GlobalStatementSyntax;
        var stmt = globalStmt?.Statement as ExpressionStatementSyntax;

        if (stmt?.Expression is AssignmentExpressionSyntax outerAssignment)
        {
            ExpressionSyntax rightExpr = outerAssignment.Right;
            AssignmentExpressionSyntax? innermostAssignment = null;
            while (rightExpr is AssignmentExpressionSyntax nestedAssignment)
            {
                innermostAssignment = nestedAssignment;
                rightExpr = nestedAssignment.Right;
            }

            if (rightExpr is InvocationExpressionSyntax invoke)
            {
                var methodName = _connectionCreationService.GetMethodNameFromExpression(invoke);
                var assignmentToReturn = innermostAssignment ?? outerAssignment;
                return (methodName, invoke.ArgumentList, assignmentToReturn);
            }
        }
        else if (stmt?.Expression is InvocationExpressionSyntax directInvoke)
        {
            var methodName = _connectionCreationService.GetMethodNameFromExpression(directInvoke);
            return (methodName, directInvoke.ArgumentList, null);
        }
        return null;
    }
}
