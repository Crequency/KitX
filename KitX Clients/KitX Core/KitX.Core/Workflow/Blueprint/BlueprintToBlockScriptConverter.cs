using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint to BlockScript
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter
{
    public string Convert(Contract.Workflow.Blueprint blueprint)
    {
        return ConvertToBlockScript(blueprint).SourceCode;
    }

    public BlockScript ConvertToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        var context = new ReverseConversionContext
        {
            Blueprint = blueprint,
            Script = new BlockScript(),
            PubVarIndex = 0,
            BlockIndex = 0
        };

        // 1. Generate #PubVarBlock with all PubVars
        GeneratePubVarBlock(context);

        // 2. Generate #ConstBlock
        GenerateConstBlock(context);

        // 3. Find Entry node and process main execution flow
        var entryNode = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entryNode != null)
        {
            // Get nodes in main execution flow (from Entry to first control flow)
            var mainFlow = GetMainExecutionFlow(blueprint, entryNode);
            var mainBlock = GenerateBlock(context, mainFlow, "MainBlock");
            context.Script.MainBlock = mainBlock;
        }

        // 4. Process sub-graphs (branches, loops) as NamedBlocks
        ProcessSubGraphs(blueprint, context);

        // 5. Generate source code
        GenerateSourceCode(context.Script);

        return context.Script;
    }

    private void GeneratePubVarBlock(ReverseConversionContext context)
    {
        var pubVarBlock = new BlockDefinition
        {
            Type = BlockType.PubVarBlock,
            Name = "PubVarBlock"
        };

        // Also create PubVars for data connections that don't have explicit PubVar
        var dataConnections = context.Blueprint.Connections
            .Where(c => !IsExecConnection(context.Blueprint, c))
            .ToList();

        foreach (var conn in dataConnections)
        {
            if (string.IsNullOrEmpty(conn.PubVarName))
            {
                // Create a new PubVar for this connection
                var pubVarName = $"temp_{context.PubVarIndex++}";
                conn.PubVarName = pubVarName;
                pubVarBlock.Variables.Add(new VariableDeclaration
                {
                    Name = pubVarName,
                    Type = "object"
                });
                context.Blueprint.PubVarNames.Add(pubVarName);
            }
        }

        if (pubVarBlock.Variables.Count > 0)
        {
            context.Script.PubVarBlock = pubVarBlock;
        }
    }

    private void GenerateConstBlock(ReverseConversionContext context)
    {
        var constBlock = new BlockDefinition
        {
            Type = BlockType.ConstBlock,
            Name = "ConstBlock"
        };

        foreach (var node in context.Blueprint.Nodes)
        {
            if (node is ConstNode constNode)
            {
                constBlock.Variables.Add(new VariableDeclaration
                {
                    Name = constNode.ConstName,
                    Type = constNode.ConstType,
                    DefaultValue = constNode.ConstValue
                });
            }
        }

        if (constBlock.Variables.Count > 0)
        {
            context.Script.ConstBlock = constBlock;
        }
    }

    private List<BlueprintNode> GetMainExecutionFlow(Contract.Workflow.Blueprint blueprint, BlueprintNode entryNode)
    {
        var flow = new List<BlueprintNode>();
        var visited = new HashSet<string>();
        CollectMainFlow(blueprint, entryNode, flow, visited, out _);
        return flow;
    }

    private void CollectMainFlow(Contract.Workflow.Blueprint blueprint, BlueprintNode node, List<BlueprintNode> flow,
        HashSet<string> visited, out bool hitControlFlow)
    {
        if (visited.Contains(node.Id))
        {
            hitControlFlow = false;
            return;
        }
        visited.Add(node.Id);

        flow.Add(node);

        // Check if this is a control flow node
        if (node is BranchNode || node is LoopNode)
        {
            hitControlFlow = true;
            return;
        }

        // Get next Exec connection
        var execOut = node.OutputPins.FirstOrDefault(p => p.Name == "Exec");
        if (execOut == null)
        {
            hitControlFlow = false;
            return;
        }

        var execConn = blueprint.Connections
            .FirstOrDefault(c => c.SourcePinId == execOut.Id);

        if (execConn == null)
        {
            hitControlFlow = false;
            return;
        }

        var nextNode = blueprint.GetNodeById(execConn.TargetNodeId);
        if (nextNode == null)
        {
            hitControlFlow = false;
            return;
        }

        CollectMainFlow(blueprint, nextNode, flow, visited, out hitControlFlow);
    }

    private void ProcessSubGraphs(Contract.Workflow.Blueprint blueprint, ReverseConversionContext context)
    {
        var processedTargets = new HashSet<string>();

        foreach (var node in blueprint.Nodes)
        {
            if (node is BranchNode branch)
            {
                ProcessBranchSubGraphs(blueprint, branch, context, processedTargets);
            }
            else if (node is LoopNode loop)
            {
                ProcessLoopSubGraphs(blueprint, loop, context, processedTargets);
            }
        }
    }

    private void ProcessBranchSubGraphs(Contract.Workflow.Blueprint blueprint, BranchNode branch,
        ReverseConversionContext context, HashSet<string> processedTargets)
    {
        ProcessOutputArm(blueprint, branch, "True", context, processedTargets, null,
            (flow, blockName) => flow.TrueBlockName = blockName);
        ProcessOutputArm(blueprint, branch, "False", context, processedTargets, null,
            (flow, blockName) => flow.FalseBlockName = blockName);
    }

    private void ProcessLoopSubGraphs(Contract.Workflow.Blueprint blueprint, LoopNode loop,
        ReverseConversionContext context, HashSet<string> processedTargets)
    {
        ProcessOutputArm(blueprint, loop, "LoopBody", context, processedTargets, loop.Id,
            (flow, blockName) => flow.TrueBlockName = blockName);
        ProcessOutputArm(blueprint, loop, "LoopEnd", context, processedTargets, null,
            (flow, blockName) => flow.FalseBlockName = blockName);
    }

    /// <summary>
    /// Processes a single output arm (True/False for Branch, LoopBody/LoopEnd for Loop).
    /// Extracted to eliminate duplication between Branch and Loop sub-graph processing.
    /// </summary>
    private void ProcessOutputArm(
        Contract.Workflow.Blueprint blueprint,
        BlueprintNode controlNode,
        string outputPinName,
        ReverseConversionContext context,
        HashSet<string> processedTargets,
        string? loopbackTargetId,
        Action<FlowControlStatement, string> setBlockName)
    {
        var pin = controlNode.OutputPins.FirstOrDefault(p => p.Name == outputPinName);
        if (pin == null) return;

        var conn = blueprint.Connections.FirstOrDefault(c => c.SourcePinId == pin.Id);
        if (conn == null || processedTargets.Contains(conn.TargetNodeId)) return;

        var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
        if (targetNode == null) return;

        var blockName = $"Block_{context.BlockIndex++}";
        var block = CollectSubGraph(blueprint, targetNode, context, processedTargets, loopbackTargetId);
        context.Script.NamedBlocks[blockName] = block;
        processedTargets.Add(conn.TargetNodeId);

        if (context.ControlFlowMap.TryGetValue(controlNode.Id, out var flow))
        {
            setBlockName(flow, blockName);
        }
    }

    private BlockDefinition CollectSubGraph(Contract.Workflow.Blueprint blueprint, BlueprintNode startNode,
        ReverseConversionContext context, HashSet<string> processedTargets, string? loopbackTargetId = null)
    {
        var block = new BlockDefinition
        {
            Type = BlockType.NamedBlock,
            Name = startNode.NodeType.ToString()
        };

        var visited = new HashSet<string>();
        CollectNodesRecursive(blueprint, startNode, block, context, visited, processedTargets, loopbackTargetId);

        return block;
    }

    private void CollectNodesRecursive(Contract.Workflow.Blueprint blueprint, BlueprintNode node,
        BlockDefinition block, ReverseConversionContext context, HashSet<string> visited, HashSet<string> processedTargets,
        string? loopbackTargetId)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        // Add node to block
        var nodeStatement = ConvertNodeToStatement(node, blueprint);
        if (nodeStatement != null)
        {
            block.Statements.Add(nodeStatement);

            // Track FlowControlStatements for later setting target block names
            if (nodeStatement is FlowControlStatement flow)
            {
                context.ControlFlowMap[node.Id] = flow;
            }
        }

        // Process all output connections
        foreach (var outPin in node.OutputPins)
        {
            if (outPin.Name == "Exec")
            {
                var conn = blueprint.Connections.FirstOrDefault(c => c.SourcePinId == outPin.Id);
                if (conn != null)
                {
                    var nextNode = blueprint.GetNodeById(conn.TargetNodeId);
                    if (nextNode != null)
                    {
                        // Check if this is a loopback to the loop node
                        if (nextNode.Id == loopbackTargetId)
                        {
                            // Add LoopBodyEnd statement to return to loop
                            block.Statements.Add(new FlowControlStatement
                            {
                                ControlType = FlowControlType.LoopBodyEnd,
                                LoopBodyEndReturnTo = loopbackTargetId
                            });
                            return; // End of loop body
                        }

                        CollectNodesRecursive(blueprint, nextNode, block, context, visited, processedTargets, loopbackTargetId);
                    }
                }
            }
        }
    }

    private BlockStatement? ConvertNodeToStatement(BlueprintNode node, Contract.Workflow.Blueprint blueprint)
    {
        switch (node)
        {
            case PrintNode print:
                var printValue = GetInputValue(print, "Value", blueprint);
                return new ExpressionStatement
                {
                    Expression = $"Print({printValue});",
                    SourceCode = $"Print({printValue});",
                    LineNumber = 1
                };

            case PauseNode pause:
                var ms = GetInputValue(pause, "Milliseconds", blueprint);
                return new ExpressionStatement
                {
                    Expression = $"Pause({ms});",
                    SourceCode = $"Pause({ms});",
                    LineNumber = 1
                };

            case CallNode call:
                var callArgs = GetInputArgs(call, blueprint);
                return new ExpressionStatement
                {
                    Expression = $"{call.FunctionName}({callArgs});",
                    SourceCode = $"{call.FunctionName}({callArgs});",
                    LineNumber = 1
                };

            case CallHelperNode callHelper:
                var helperArgs = GetInputArgs(callHelper, blueprint);
                return new ExpressionStatement
                {
                    Expression = $"{callHelper.HelperFunctionName}({helperArgs});",
                    SourceCode = $"{callHelper.HelperFunctionName}({helperArgs});",
                    LineNumber = 1
                };

            case BreakNode:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Break,
                    SourceCode = "Break();",
                    LineNumber = 1
                };

            case BranchNode branch:
                var branchFlow = new FlowControlStatement
                {
                    ControlType = FlowControlType.Branch,
                    ConditionExpression = GetConditionExpression(branch, blueprint),
                    SourceCode = $"Branch({GetConditionExpression(branch, blueprint)}, \"\", \"\");",
                    LineNumber = 1
                };
                return branchFlow;

            case LoopNode loop:
                var loopFlow = new FlowControlStatement
                {
                    ControlType = FlowControlType.Loop,
                    ConditionExpression = GetConditionExpression(loop, blueprint),
                    SourceCode = $"Loop({GetConditionExpression(loop, blueprint)}, \"\", \"\");",
                    LineNumber = 1
                };
                return loopFlow;

            default:
                return null;
        }
    }

    private string GetConditionExpression(BlueprintNode node, Contract.Workflow.Blueprint blueprint)
        => GetInputValue(node, "Condition", blueprint);

    private string GetInputValue(BlueprintNode node, string pinName, Contract.Workflow.Blueprint blueprint)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return string.Empty;

        // Find the data connection to this pin
        var dataConn = blueprint.Connections.FirstOrDefault(c => c.TargetPinId == pin.Id);
        if (dataConn == null) return pin.DefaultValue ?? string.Empty;

        var sourceNode = blueprint.GetNodeById(dataConn.SourceNodeId);
        if (sourceNode is ConstNode constNode)
            return constNode.ConstName;

        // For other nodes, return the PubVar name if available
        return dataConn.PubVarName ?? pin.DefaultValue ?? string.Empty;
    }

    private string GetInputArgs(BlueprintNode node, Contract.Workflow.Blueprint blueprint)
    {
        var args = new List<string>();
        foreach (var pin in node.InputPins)
        {
            if (pin.Name != "Exec")
            {
                args.Add(GetInputValue(node, pin.Name, blueprint));
            }
        }
        return string.Join(", ", args);
    }

    private BlockDefinition GenerateBlock(ReverseConversionContext context,
        List<BlueprintNode> nodes, string blockName)
    {
        var block = new BlockDefinition
        {
            Type = blockName == "MainBlock" ? BlockType.MainBlock : BlockType.NamedBlock,
            Name = blockName
        };

        foreach (var node in nodes)
        {
            var statement = ConvertNodeToStatement(node, context.Blueprint);
            if (statement != null)
            {
                block.Statements.Add(statement);

                // Track FlowControlStatements for later setting target block names
                if (statement is FlowControlStatement flow)
                {
                    context.ControlFlowMap[node.Id] = flow;
                }
            }
        }

        return block;
    }

    private bool IsExecConnection(Contract.Workflow.Blueprint blueprint, BlueprintConnection conn)
    {
        var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
        var sourcePin = sourceNode?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
        return sourcePin?.Name == "Exec";
    }

    private void GenerateSourceCode(BlockScript script)
    {
        var sb = new StringBuilder();

        // #ConstBlock
        if (script.ConstBlock != null && script.ConstBlock.Variables.Count > 0)
        {
            sb.AppendLine("#ConstBlock");
            foreach (var variable in script.ConstBlock.Variables)
            {
                sb.AppendLine($"const {variable.Type} {variable.Name} = {variable.DefaultValue};");
            }
            sb.AppendLine();
        }

        // #PubVarBlock
        if (script.PubVarBlock != null && script.PubVarBlock.Variables.Count > 0)
        {
            sb.AppendLine("#PubVarBlock");
            foreach (var variable in script.PubVarBlock.Variables)
            {
                sb.AppendLine($"{variable.Type} {variable.Name};");
            }
            sb.AppendLine();
        }

        // #MainBlock
        if (script.MainBlock != null)
        {
            sb.AppendLine("#MainBlock");
            foreach (var statement in script.MainBlock.Statements)
            {
                sb.AppendLine(statement.SourceCode);
            }
            sb.AppendLine();
        }

        // NamedBlocks
        foreach (var kvp in script.NamedBlocks)
        {
            sb.AppendLine($"#Block {kvp.Key}");
            foreach (var statement in kvp.Value.Statements)
            {
                sb.AppendLine(statement.SourceCode);
            }
            sb.AppendLine();
        }

        script.SourceCode = sb.ToString();
    }

    private class ReverseConversionContext
    {
        public required Contract.Workflow.Blueprint Blueprint { get; set; }
        public required BlockScript Script { get; set; }
        public int PubVarIndex { get; set; }
        public int BlockIndex { get; set; }

        /// <summary>
        /// Maps node ID to its FlowControlStatement for setting target block names later
        /// </summary>
        public Dictionary<string, FlowControlStatement> ControlFlowMap { get; set; } = new();
    }
}
