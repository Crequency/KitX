using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint to BlockScript using strategy pattern for node type dispatch.
/// Each node type's conversion logic lives in a dedicated INodeExportStrategy implementation.
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter, INodeExportHelper
{
    private readonly Dictionary<BlueprintNodeType, INodeExportStrategy> _strategies;

    public BlueprintToBlockScriptConverter(IEnumerable<INodeExportStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.NodeType);
    }

    /// <inheritdoc/>
    public Contract.Workflow.Blueprint Blueprint { get; private set; } = null!;

    /// <summary>
    /// Converts a blueprint to BlockScript source code string.
    /// </summary>
    public string Convert(Contract.Workflow.Blueprint blueprint)
    {
        return ConvertToBlockScript(blueprint).SourceCode;
    }

    /// <summary>
    /// Converts a blueprint to a BlockScript object.
    /// </summary>
    public BlockScript ConvertToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;

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

    // ──────────────────────────────────────────────
    // INodeExportHelper implementation
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public string GetInputValue(BlueprintNode node, string pinName)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return string.Empty;

        var dataConn = Blueprint.Connections.FirstOrDefault(c => c.TargetPinId == pin.Id);
        if (dataConn == null) return pin.DefaultValue ?? string.Empty;

        var sourceNode = Blueprint.GetNodeById(dataConn.SourceNodeId);
        if (sourceNode is ConstNode constNode)
            return constNode.ConstName;

        return dataConn.PubVarName ?? pin.DefaultValue ?? string.Empty;
    }

    /// <inheritdoc/>
    public string GetInputArgs(BlueprintNode node)
    {
        var args = new List<string>();
        foreach (var pin in node.InputPins)
        {
            if (pin.Name != "Exec")
            {
                args.Add(GetInputValue(node, pin.Name));
            }
        }
        return string.Join(", ", args);
    }

    // ──────────────────────────────────────────────
    // Core conversion logic (strategy-driven)
    // ──────────────────────────────────────────────

    private BlockStatement? ConvertNodeToStatement(BlueprintNode node, Contract.Workflow.Blueprint blueprint)
    {
        if (_strategies.TryGetValue(node.NodeType, out var strategy))
            return strategy.ToStatement(node, this);

        return null;
    }

    private void GeneratePubVarBlock(ReverseConversionContext context)
    {
        var pubVarBlock = new BlockDefinition
        {
            Type = BlockType.PubVarBlock,
            Name = "PubVarBlock"
        };

        var dataConnections = context.Blueprint.Connections
            .Where(c => !IsExecConnection(context.Blueprint, c))
            .ToList();

        foreach (var conn in dataConnections)
        {
            if (string.IsNullOrEmpty(conn.PubVarName))
            {
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

        // Use strategy to check if this is a control flow node
        if (_strategies.TryGetValue(node.NodeType, out var strategy) && strategy.IsControlFlow)
        {
            hitControlFlow = true;
            return;
        }

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
            if (!_strategies.TryGetValue(node.NodeType, out var strategy) || !strategy.IsControlFlow)
                continue;

            foreach (var arm in strategy.GetOutputArms(node))
            {
                ProcessOutputArm(blueprint, node, arm.PinName, arm.IsLoopback,
                    context, processedTargets);
            }
        }
    }

    private void ProcessOutputArm(
        Contract.Workflow.Blueprint blueprint,
        BlueprintNode controlNode,
        string outputPinName,
        bool isLoopback,
        ReverseConversionContext context,
        HashSet<string> processedTargets)
    {
        var pin = controlNode.OutputPins.FirstOrDefault(p => p.Name == outputPinName);
        if (pin == null) return;

        var conn = blueprint.Connections.FirstOrDefault(c => c.SourcePinId == pin.Id);
        if (conn == null || processedTargets.Contains(conn.TargetNodeId)) return;

        var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
        if (targetNode == null) return;

        var blockName = $"Block_{context.BlockIndex++}";
        var loopbackTargetId = isLoopback ? controlNode.Id : null;
        var block = CollectSubGraph(blueprint, targetNode, context, processedTargets, loopbackTargetId);
        context.Script.NamedBlocks[blockName] = block;
        processedTargets.Add(conn.TargetNodeId);

        if (context.ControlFlowMap.TryGetValue(controlNode.Id, out var flow))
        {
            // Determine which block name property to set based on arm index
            var arms = _strategies[controlNode.NodeType].GetOutputArms(controlNode).ToList();
            var armIndex = arms.FindIndex(a => a.PinName == outputPinName);
            if (armIndex == 0)
                flow.TrueBlockName = blockName;
            else
                flow.FalseBlockName = blockName;
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

        var nodeStatement = ConvertNodeToStatement(node, blueprint);
        if (nodeStatement != null)
        {
            block.Statements.Add(nodeStatement);

            if (nodeStatement is FlowControlStatement flow)
            {
                context.ControlFlowMap[node.Id] = flow;
            }
        }

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
                        if (nextNode.Id == loopbackTargetId)
                        {
                            block.Statements.Add(new FlowControlStatement
                            {
                                ControlType = FlowControlType.LoopBodyEnd,
                                SourceCode = "LoopBodyEnd();",
                                LineNumber = 1
                            });
                            return;
                        }

                        CollectNodesRecursive(blueprint, nextNode, block, context, visited, processedTargets, loopbackTargetId);
                    }
                }
            }
        }
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

        if (script.ConstBlock != null && script.ConstBlock.Variables.Count > 0)
        {
            sb.AppendLine("#ConstBlock");
            foreach (var variable in script.ConstBlock.Variables)
            {
                sb.AppendLine($"const {variable.Type} {variable.Name} = {variable.DefaultValue};");
            }
            sb.AppendLine();
        }

        if (script.PubVarBlock != null && script.PubVarBlock.Variables.Count > 0)
        {
            sb.AppendLine("#PubVarBlock");
            foreach (var variable in script.PubVarBlock.Variables)
            {
                sb.AppendLine($"{variable.Type} {variable.Name};");
            }
            sb.AppendLine();
        }

        if (script.MainBlock != null)
        {
            sb.AppendLine("#MainBlock");
            foreach (var statement in script.MainBlock.Statements)
            {
                sb.AppendLine(statement.SourceCode);
            }
            sb.AppendLine();
        }

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
        public Dictionary<string, FlowControlStatement> ControlFlowMap { get; set; } = new();
    }
}
