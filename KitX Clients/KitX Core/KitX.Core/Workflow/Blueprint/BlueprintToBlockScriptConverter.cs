using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint back to a fully-expanded BlockScript source code.
/// Produces output equivalent to what ScriptFormatter (Phase 2 of the forward pipeline) generates.
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

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public string Convert(Contract.Workflow.Blueprint blueprint)
    {
        return ConvertToBlockScript(blueprint).SourceCode;
    }

    /// <inheritdoc/>
    public BlockScript ConvertToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        // Fast path: use stored block scopes when available
        if (blueprint.BlockScopes.Count > 0)
            return ConvertWithBlockScopes(blueprint);

        // Fallback: topology-based conversion
        return ConvertWithTopology(blueprint);
    }

    /// <summary>
    /// Fast-path conversion using stored block scope information.
    /// Block names and membership are read directly from the Blueprint.
    /// </summary>
    private BlockScript ConvertWithBlockScopes(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;
        var ctx = new ReverseConversionContext
        {
            Blueprint = blueprint,
            Script = new BlockScript()
        };

        // Phase 1: Analyze blueprint — build data indexes (same as topology path)
        AnalyzeBlueprint(ctx);

        // Build scopesByName lookup
        foreach (var scope in blueprint.BlockScopes)
            ctx.ScopesByName[scope.Name] = scope;

        // Phase 2: Generate statements from stored block membership
        foreach (var scope in blueprint.BlockScopes)
        {
            BlockDefinition block;
            if (scope.IsMainBlock)
            {
                block = new BlockDefinition { Type = BlockType.MainBlock, Name = "MainBlock" };
                ctx.Script.MainBlock = block;
            }
            else
            {
                block = new BlockDefinition { Type = BlockType.NamedBlock, Name = scope.Name };
                ctx.Script.NamedBlocks[scope.Name] = block;
            }

            foreach (var nodeId in scope.NodeIds)
            {
                if (!ctx.NodeById.TryGetValue(nodeId, out var node)) continue;
                var stmt = GenerateStatement(node, ctx);
                if (stmt != null)
                {
                    block.Statements.Add(stmt);

                    // Track control flow nodes for block name resolution
                    if (stmt is FlowControlStatement flow)
                    {
                        ctx.ControlFlowMap[node.Id] = flow;
                        if (flow.ControlType == FlowControlType.Loop)
                            ctx.LoopNodes[node.Id] = node;
                    }
                }
            }

            // (LoopBodyEnd is NOT auto-inserted here — it's detected from exec connections below)
        }

        // Resolve control flow block names from stored ownership
        foreach (var scope in blueprint.BlockScopes)
        {
            if (scope.OwnerNodeId == null) continue;
            if (!ctx.ControlFlowMap.TryGetValue(scope.OwnerNodeId, out var flow)) continue;

            if (scope.OwnerArmName == "True" || scope.OwnerArmName == "LoopBody")
                flow.TrueBlockName = scope.Name;
            else if (scope.OwnerArmName == "False" || scope.OwnerArmName == "LoopEnd")
                flow.FalseBlockName = scope.Name;
        }

        // Regenerate flow control source code with resolved block names
        foreach (var flow in ctx.ControlFlowMap.Values)
        {
            if (flow.ControlType == FlowControlType.Branch)
                flow.SourceCode = $"NextBlock = Branch({flow.ConditionExpression}, \"{flow.TrueBlockName}\", \"{flow.FalseBlockName}\");";
            else if (flow.ControlType == FlowControlType.Loop)
                flow.SourceCode = $"NextBlock = Loop({flow.ConditionExpression}, \"{flow.TrueBlockName}\", \"{flow.FalseBlockName}\");";
        }

        // Detect LoopBodyEnd: scopes whose last node's Exec output connects back to a Loop node
        DetectAndInsertLoopBodyEnds(blueprint, ctx);

        // Phase 4: Assemble source code (same as topology path)
        AssembleBlockScript(ctx);

        return ctx.Script;
    }

    /// <summary>
    /// Fallback topology-based conversion for legacy blueprints without BlockScopes.
    /// </summary>
    private BlockScript ConvertWithTopology(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;
        var ctx = new ReverseConversionContext
        {
            Blueprint = blueprint,
            Script = new BlockScript()
        };

        // Phase 1: Analyze blueprint — build indexes
        AnalyzeBlueprint(ctx);

        // Phase 2: Walk execution flow — generate expanded statements
        WalkExecutionFlow(ctx);

        // Phase 3: Duplicate loop conditions before LoopBodyEnd
        DuplicateLoopConditions(ctx);

        // Phase 4: Assemble source code
        AssembleBlockScript(ctx);

        return ctx.Script;
    }

    // ──────────────────────────────────────────────
    // INodeExportHelper (backward compat for strategies)
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
    // Phase 1: Analyze Blueprint
    // ──────────────────────────────────────────────

    private void AnalyzeBlueprint(ReverseConversionContext ctx)
    {
        var bp = ctx.Blueprint;

        // Build node lookup
        foreach (var node in bp.Nodes)
            ctx.NodeById[node.Id] = node;

        // Classify connections and build data indexes
        foreach (var conn in bp.Connections)
        {
            var sourceNode = bp.GetNodeById(conn.SourceNodeId);
            if (sourceNode == null) continue;

            var sourcePin = sourceNode.GetPinById(conn.SourcePinId);
            if (sourcePin == null) continue;

            if (sourcePin.Type == PinType.Execution)
            {
                ctx.ExecConnections.Add(conn);
            }
            else
            {
                ctx.DataConnections.Add(conn);

                // Build input data map
                var targetNode = bp.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    var targetPin = targetNode.GetPinById(conn.TargetPinId);
                    if (targetPin != null)
                    {
                        ctx.InputDataMap[(conn.TargetNodeId, targetPin.Name)] = new DataEdgeInfo
                        {
                            SourceNode = sourceNode,
                            SourcePinName = sourcePin.Name,
                            SourcePin = sourcePin,
                            PubVarName = conn.PubVarName,
                            Connection = conn
                        };

                        // Track consumed outputs
                        ctx.ConsumedOutputs.Add((conn.SourceNodeId, sourcePin.Name));
                    }
                }
            }
        }

        // Assign PubVar names where needed but missing
        foreach (var conn in ctx.DataConnections)
        {
            if (!string.IsNullOrEmpty(conn.PubVarName)) continue;

            var sourceNode = bp.GetNodeById(conn.SourceNodeId);
            if (sourceNode == null) continue;

            // ConstNode references don't need PubVar
            if (sourceNode.NodeType == BlueprintNodeType.Const) continue;

            // GetNode, CallNode, CallHelperNode outputs need PubVar if consumed downstream
            if (sourceNode.NodeType is BlueprintNodeType.Get or BlueprintNodeType.Call
                or BlueprintNodeType.CallHelper)
            {
                var sourcePin = sourceNode.GetPinById(conn.SourcePinId);
                if (sourcePin == null) continue;

                var pubVar = GeneratePubVarName(ctx);
                conn.PubVarName = pubVar;
                ctx.AutoPubVars.Add(pubVar);

                // Update the InputDataMap entry
                var targetNode = bp.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    var targetPin = targetNode.GetPinById(conn.TargetPinId);
                    if (targetPin != null && ctx.InputDataMap.TryGetValue(
                        (conn.TargetNodeId, targetPin.Name), out var info))
                    {
                        info.PubVarName = pubVar;
                    }
                }

                Log.Debug("[BlueprintToScript] Auto-assigned PubVar {PubVar} for {NodeType}.{Pin}",
                    pubVar, sourceNode.NodeType, sourcePin.Name);
            }
        }

        // Collect all PubVar names
        foreach (var conn in ctx.DataConnections)
        {
            if (!string.IsNullOrEmpty(conn.PubVarName) && !ctx.AllPubVars.Contains(conn.PubVarName))
                ctx.AllPubVars.Add(conn.PubVarName);
        }

        Log.Debug("[BlueprintToScript] Phase 1 done: {Nodes} nodes, {Exec} exec conns, {Data} data conns, {PubVars} pubvars",
            ctx.NodeById.Count, ctx.ExecConnections.Count, ctx.DataConnections.Count, ctx.AllPubVars.Count);
    }

    // ──────────────────────────────────────────────
    // Phase 2: Walk Execution Flow
    // ──────────────────────────────────────────────

    private void WalkExecutionFlow(ReverseConversionContext ctx)
    {
        var bp = ctx.Blueprint;
        var entryNode = bp.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entryNode == null)
        {
            Log.Warning("[BlueprintToScript] No Entry node found");
            return;
        }

        // Main block
        var mainBlock = new BlockDefinition
        {
            Type = BlockType.MainBlock,
            Name = "MainBlock"
        };
        ctx.Script.MainBlock = mainBlock;

        // Walk from entry
        var visited = new HashSet<string>();
        WalkNode(entryNode, mainBlock, ctx, visited, loopbackTargetId: null);

        // Process control flow sub-graphs
        ProcessSubGraphs(ctx);
    }

    private void WalkNode(BlueprintNode node, BlockDefinition currentBlock,
        ReverseConversionContext ctx, HashSet<string> visited, string? loopbackTargetId)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        // If this node is the loopback target (we've looped back to the Loop node), insert LoopBodyEnd
        if (loopbackTargetId != null && node.Id == loopbackTargetId)
        {
            currentBlock.Statements.Add(new FlowControlStatement
            {
                ControlType = FlowControlType.LoopBodyEnd,
                SourceCode = "LoopBodyEnd();",
                LineNumber = 1
            });
            return;
        }

        // Generate statement for this node
        var stmt = GenerateStatement(node, ctx);
        if (stmt != null)
        {
            currentBlock.Statements.Add(stmt);

            // Track flow control for sub-graph processing
            if (stmt is FlowControlStatement flow)
            {
                ctx.ControlFlowMap[node.Id] = flow;

                // Record loop condition info for Phase 3
                if (flow.ControlType == FlowControlType.Loop)
                {
                    ctx.LoopNodes[node.Id] = node;
                }
            }
        }

        // Check if this is a control flow node — stop main flow walk
        if (node.NodeType is BlueprintNodeType.Branch or BlueprintNodeType.Loop)
        {
            ctx.PendingControlFlowNodes.Add(node);
            return;
        }

        // Follow exec output
        var execOut = node.OutputPins.FirstOrDefault(p => p.Name == "Exec");
        if (execOut == null)
        {
            // Dead end inside a loop body → insert LoopBodyEnd
            if (loopbackTargetId != null)
            {
                currentBlock.Statements.Add(new FlowControlStatement
                {
                    ControlType = FlowControlType.LoopBodyEnd,
                    SourceCode = "LoopBodyEnd();",
                    LineNumber = 1
                });
            }
            return;
        }

        var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOut.Id);
        if (execConn == null)
        {
            // Dead end inside a loop body → insert LoopBodyEnd
            if (loopbackTargetId != null)
            {
                currentBlock.Statements.Add(new FlowControlStatement
                {
                    ControlType = FlowControlType.LoopBodyEnd,
                    SourceCode = "LoopBodyEnd();",
                    LineNumber = 1
                });
            }
            return;
        }

        var nextNode = ctx.Blueprint.GetNodeById(execConn.TargetNodeId);
        if (nextNode == null) return;

        // Check for loopback (next node is the loop we're looping back to)
        if (loopbackTargetId != null && nextNode.Id == loopbackTargetId)
        {
            currentBlock.Statements.Add(new FlowControlStatement
            {
                ControlType = FlowControlType.LoopBodyEnd,
                SourceCode = "LoopBodyEnd();",
                LineNumber = 1
            });
            return;
        }

        WalkNode(nextNode, currentBlock, ctx, visited, loopbackTargetId);
    }

    /// <summary>
    /// Generates a BlockStatement for the given node in expanded format.
    /// </summary>
    private BlockStatement? GenerateStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        switch (node.NodeType)
        {
            case BlueprintNodeType.Entry:
                return null; // Entry nodes produce no statement

            case BlueprintNodeType.Print:
                return GeneratePrintStatement(node, ctx);

            case BlueprintNodeType.Pause:
                return GeneratePauseStatement(node, ctx);

            case BlueprintNodeType.Set:
                return GenerateSetStatement(node, ctx);

            case BlueprintNodeType.Get:
                return GenerateGetStatement(node, ctx);

            case BlueprintNodeType.Call:
                return GenerateCallStatement(node, ctx);

            case BlueprintNodeType.CallHelper:
                return GenerateCallHelperStatement(node, ctx);

            case BlueprintNodeType.Branch:
                return GenerateBranchStatement(node, ctx);

            case BlueprintNodeType.Loop:
                return GenerateLoopStatement(node, ctx);

            case BlueprintNodeType.Break:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Break,
                    SourceCode = "Break();",
                    LineNumber = 1
                };

            default:
                Log.Warning("[BlueprintToScript] Unhandled node type: {NodeType}", node.NodeType);
                return null;
        }
    }

    private BlockStatement? GeneratePrintStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        var value = ResolveInputValue(node, "Value", ctx);
        var sourceCode = $"Print({value});";
        return new ExpressionStatement
        {
            Expression = $"Print({value})",
            SourceCode = sourceCode,
            LineNumber = 1
        };
    }

    private BlockStatement? GeneratePauseStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        var ms = ResolveInputValue(node, "Milliseconds", ctx);
        var sourceCode = $"Pause({ms});";
        return new ExpressionStatement
        {
            Expression = $"Pause({ms})",
            SourceCode = sourceCode,
            LineNumber = 1
        };
    }

    private BlockStatement? GenerateSetStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        if (node is not SetNode setNode) return null;
        var value = ResolveInputValue(node, "Value", ctx);
        var sourceCode = $"Set(\"{setNode.VarName}\", {value});";
        return new ExpressionStatement
        {
            Expression = $"Set(\"{setNode.VarName}\", {value})",
            SourceCode = sourceCode,
            LineNumber = 1
        };
    }

    private BlockStatement? GenerateGetStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        if (node is not GetNode getNode) return null;

        // Only generate statement if Value output is consumed
        var valuePin = node.OutputPins.FirstOrDefault(p => p.Name == "Value");
        if (valuePin == null || !ctx.ConsumedOutputs.Contains((node.Id, "Value")))
        {
            // Not consumed — skip (invisible in exec flow)
            return null;
        }

        // Find the PubVar assigned to this Get's output
        var pubVar = FindOutputPubVar(node, "Value", ctx);
        if (pubVar == null) return null;

        var sourceCode = $"{pubVar} = Get(\"{getNode.VarName}\");";
        return new ExpressionStatement
        {
            Expression = $"Get(\"{getNode.VarName}\")",
            SourceCode = sourceCode,
            LineNumber = 1
        };
    }

    private BlockStatement? GenerateCallStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        if (node is not CallNode callNode) return null;

        // Resolve arguments (all non-Exec input pins)
        var args = ResolveAllArgs(node, ctx);
        var funcRef = string.IsNullOrEmpty(callNode.PluginName)
            ? callNode.FunctionName
            : $"{callNode.PluginName}.{callNode.FunctionName}";

        // Check if Return output is consumed → needs PubVar assignment
        var returnPin = node.OutputPins.FirstOrDefault(p => p.Name == "Return");
        bool hasReturn = returnPin != null && ctx.ConsumedOutputs.Contains((node.Id, "Return"));

        if (hasReturn)
        {
            var pubVar = FindOutputPubVar(node, "Return", ctx);
            var sourceCode = $"{pubVar} = {funcRef}({args});";
            return new ExpressionStatement
            {
                Expression = $"{funcRef}({args})",
                SourceCode = sourceCode,
                LineNumber = 1
            };
        }
        else
        {
            var sourceCode = $"{funcRef}({args});";
            return new ExpressionStatement
            {
                Expression = $"{funcRef}({args})",
                SourceCode = sourceCode,
                LineNumber = 1
            };
        }
    }

    private BlockStatement? GenerateCallHelperStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        if (node is not CallHelperNode helperNode) return null;

        // Resolve arguments
        var args = ResolveAllArgs(node, ctx);
        var funcName = helperNode.HelperFunctionName;

        // Check if Return output is consumed → needs PubVar assignment
        var returnPin = node.OutputPins.FirstOrDefault(p => p.Name == "Return");
        bool hasReturn = returnPin != null && ctx.ConsumedOutputs.Contains((node.Id, "Return"));

        if (hasReturn)
        {
            var pubVar = FindOutputPubVar(node, "Return", ctx);
            var sourceCode = $"{pubVar} = {funcName}({args});";
            return new ExpressionStatement
            {
                Expression = $"{funcName}({args})",
                SourceCode = sourceCode,
                LineNumber = 1
            };
        }
        else
        {
            var sourceCode = $"{funcName}({args});";
            return new ExpressionStatement
            {
                Expression = $"{funcName}({args})",
                SourceCode = sourceCode,
                LineNumber = 1
            };
        }
    }

    private BlockStatement GenerateBranchStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        var condition = ResolveInputValue(node, "Condition", ctx);

        // Block names will be filled during sub-graph processing
        return new FlowControlStatement
        {
            ControlType = FlowControlType.Branch,
            ConditionExpression = condition,
            TrueBlockName = string.Empty, // filled later
            FalseBlockName = string.Empty, // filled later
            SourceCode = $"NextBlock = Branch({condition}, \"\", \"\");",
            LineNumber = 1
        };
    }

    private BlockStatement GenerateLoopStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        var condition = ResolveInputValue(node, "Condition", ctx);

        return new FlowControlStatement
        {
            ControlType = FlowControlType.Loop,
            ConditionExpression = condition,
            TrueBlockName = string.Empty, // filled later
            FalseBlockName = string.Empty, // filled later
            SourceCode = $"NextBlock = Loop({condition}, \"\", \"\");",
            LineNumber = 1
        };
    }

    // ──────────────────────────────────────────────
    // Input Resolution
    // ──────────────────────────────────────────────

    /// <summary>
    /// Resolves the value for an input pin by tracing data connections.
    /// </summary>
    private string ResolveInputValue(BlueprintNode node, string pinName, ReverseConversionContext ctx)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return string.Empty;

        // Check if there's a data connection to this pin
        if (ctx.InputDataMap.TryGetValue((node.Id, pin.Name), out var info))
        {
            // Source is ConstNode → use ConstName
            if (info.SourceNode is ConstNode constNode)
                return constNode.ConstName;

            // Has PubVarName → use it
            if (!string.IsNullOrEmpty(info.PubVarName))
                return info.PubVarName;
        }

        // No connection → use default value
        var defaultValue = pin.DefaultValue ?? string.Empty;
        return FormatLiteralValue(defaultValue, ctx);
    }

    /// <summary>
    /// Formats a literal value for output, adding quotes to strings when needed.
    /// </summary>
    private string FormatLiteralValue(string value, ReverseConversionContext? ctx = null)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Already quoted
        if (value.StartsWith("\"")) return value;

        // PubVar reference
        if (value.StartsWith("vaaa") && value.Length >= 8) return value;

        // Numeric literal
        if (int.TryParse(value, out _) || double.TryParse(value, out _)) return value;

        // Boolean literal
        if (value == "true" || value == "false") return value;

        // Get/Set/Helper expressions
        if (value.Contains("(")) return value;

        // Known ConstName reference
        if (ctx != null && ctx.Blueprint.Nodes.OfType<ConstNode>().Any(c => c.ConstName == value))
            return value;

        // Default: treat as string, add quotes
        return $"\"{value}\"";
    }

    /// <summary>
    /// Resolves all non-Exec input arguments, returning comma-separated string.
    /// </summary>
    private string ResolveAllArgs(BlueprintNode node, ReverseConversionContext ctx)
    {
        var args = new List<string>();
        foreach (var pin in node.InputPins)
        {
            if (pin.Name != "Exec")
            {
                args.Add(ResolveInputValue(node, pin.Name, ctx));
            }
        }
        return string.Join(", ", args);
    }

    /// <summary>
    /// Finds the PubVar name assigned to a node's output pin.
    /// Looks through all data connections from this pin.
    /// </summary>
    private string? FindOutputPubVar(BlueprintNode node, string pinName, ReverseConversionContext ctx)
    {
        var pin = node.OutputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return null;

        var conn = ctx.DataConnections.FirstOrDefault(c => c.SourcePinId == pin.Id);
        return conn?.PubVarName;
    }

    // ──────────────────────────────────────────────
    // Sub-graph Processing
    // ──────────────────────────────────────────────

    private void ProcessSubGraphs(ReverseConversionContext ctx)
    {
        var processedTargets = new HashSet<string>();
        var processedNodes = new HashSet<string>();

        // Keep processing until no new control flow nodes are discovered
        while (ctx.PendingControlFlowNodes.Count > 0)
        {
            // Snapshot current pending nodes
            var pendingNodes = ctx.PendingControlFlowNodes.ToList();
            ctx.PendingControlFlowNodes.Clear();

            foreach (var node in pendingNodes)
            {
                if (processedNodes.Contains(node.Id)) continue;
                processedNodes.Add(node.Id);

                switch (node.NodeType)
                {
                    case BlueprintNodeType.Branch:
                        ProcessBranchSubGraphs(node, ctx, processedTargets);
                        break;
                    case BlueprintNodeType.Loop:
                        ProcessLoopSubGraphs(node, ctx, processedTargets);
                        break;
                }
            }
        }

        // Update flow control statements with actual block names
        foreach (var kvp in ctx.ControlFlowMap)
        {
            var flow = kvp.Value;
            if (ctx.BlockNameAssignments.TryGetValue(kvp.Key, out var assignments))
            {
                flow.TrueBlockName = assignments.TrueBlockName;
                flow.FalseBlockName = assignments.FalseBlockName;

                // Regenerate source code
                if (flow.ControlType == FlowControlType.Branch)
                    flow.SourceCode = $"NextBlock = Branch({flow.ConditionExpression}, \"{flow.TrueBlockName}\", \"{flow.FalseBlockName}\");";
                else if (flow.ControlType == FlowControlType.Loop)
                    flow.SourceCode = $"NextBlock = Loop({flow.ConditionExpression}, \"{flow.TrueBlockName}\", \"{flow.FalseBlockName}\");";
            }
        }
    }

    private void ProcessBranchSubGraphs(BlueprintNode branchNode, ReverseConversionContext ctx,
        HashSet<string> processedTargets)
    {
        var arms = new[] { ("True", false), ("False", false) };

        var trueBlockName = string.Empty;
        var falseBlockName = string.Empty;

        // Determine if this branch is inside a loop body (inherit loopback target)
        var currentLoopback = ctx.CurrentLoopbackTargetId;

        foreach (var (pinName, _) in arms)
        {
            var pin = branchNode.OutputPins.FirstOrDefault(p => p.Name == pinName);
            if (pin == null) continue;

            var conn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null || processedTargets.Contains(conn.TargetNodeId)) continue;

            var targetNode = ctx.Blueprint.GetNodeById(conn.TargetNodeId);
            if (targetNode == null) continue;

            var blockName = GenerateBlockName(ctx);
            var block = new BlockDefinition
            {
                Type = BlockType.NamedBlock,
                Name = blockName
            };

            var visited = new HashSet<string>();
            WalkNode(targetNode, block, ctx, visited, loopbackTargetId: currentLoopback);

            ctx.Script.NamedBlocks[blockName] = block;
            processedTargets.Add(conn.TargetNodeId);

            if (pinName == "True") trueBlockName = blockName;
            else falseBlockName = blockName;
        }

        ctx.BlockNameAssignments[branchNode.Id] = (trueBlockName, falseBlockName);
    }

    private void ProcessLoopSubGraphs(BlueprintNode loopNode, ReverseConversionContext ctx,
        HashSet<string> processedTargets)
    {
        var loopBodyBlockName = string.Empty;
        var loopEndBlockName = string.Empty;

        // LoopBody arm (loops back to this LoopNode)
        var loopBodyPin = loopNode.OutputPins.FirstOrDefault(p => p.Name == "LoopBody");
        if (loopBodyPin != null)
        {
            var conn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == loopBodyPin.Id);
            if (conn != null && !processedTargets.Contains(conn.TargetNodeId))
            {
                var targetNode = ctx.Blueprint.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    loopBodyBlockName = GenerateBlockName(ctx);
                    var block = new BlockDefinition
                    {
                        Type = BlockType.NamedBlock,
                        Name = loopBodyBlockName
                    };

                    // Set loopback context for nested sub-blocks
                    ctx.CurrentLoopbackTargetId = loopNode.Id;

                    var visited = new HashSet<string>();
                    WalkNode(targetNode, block, ctx, visited, loopbackTargetId: loopNode.Id);

                    ctx.Script.NamedBlocks[loopBodyBlockName] = block;
                    processedTargets.Add(conn.TargetNodeId);

                    // Record loop body block for Phase 3 condition duplication
                    ctx.LoopBodyBlocks[loopNode.Id] = block;
                }
            }
        }

        // LoopEnd arm
        var loopEndPin = loopNode.OutputPins.FirstOrDefault(p => p.Name == "LoopEnd");
        if (loopEndPin != null)
        {
            var conn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == loopEndPin.Id);
            if (conn != null && !processedTargets.Contains(conn.TargetNodeId))
            {
                var targetNode = ctx.Blueprint.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    loopEndBlockName = GenerateBlockName(ctx);
                    var block = new BlockDefinition
                    {
                        Type = BlockType.NamedBlock,
                        Name = loopEndBlockName
                    };

                    var visited = new HashSet<string>();
                    WalkNode(targetNode, block, ctx, visited, loopbackTargetId: null);

                    ctx.Script.NamedBlocks[loopEndBlockName] = block;
                    processedTargets.Add(conn.TargetNodeId);
                }
            }
        }

        ctx.BlockNameAssignments[loopNode.Id] = (loopBodyBlockName, loopEndBlockName);
    }

    // ──────────────────────────────────────────────
    // Phase 3: Loop Condition Duplication
    // ──────────────────────────────────────────────

    private void DuplicateLoopConditions(ReverseConversionContext ctx)
    {
        foreach (var kvp in ctx.LoopBodyBlocks)
        {
            var loopNodeId = kvp.Key;
            var loopBodyBlock = kvp.Value;

            if (!ctx.LoopNodes.TryGetValue(loopNodeId, out var loopNode)) continue;

            // Get the condition expression for this loop
            var flow = ctx.ControlFlowMap.TryGetValue(loopNodeId, out var f) ? f : null;
            if (flow == null) continue;

            var conditionExpr = flow.ConditionExpression;
            if (string.IsNullOrEmpty(conditionExpr)) continue;

            // Find the statements in the main block that evaluate the condition
            // These are the statements between the last non-related statement and the Loop statement
            // For simplicity: find the condition evaluation chain (Get + CallHelper statements before the Loop)
            var condStmts = FindConditionStatements(loopNode, ctx);
            if (condStmts.Count == 0) continue;

            // Insert duplicates before each LoopBodyEnd in the loop body block
            var insertions = new List<(int index, List<BlockStatement> stmts)>();

            for (int i = 0; i < loopBodyBlock.Statements.Count; i++)
            {
                var stmt = loopBodyBlock.Statements[i];
                if (stmt is FlowControlStatement flowStmt
                    && flowStmt.ControlType == FlowControlType.LoopBodyEnd)
                {
                    var dupStmts = condStmts.Select(CloneStatement).ToList();
                    insertions.Add((i, dupStmts.Cast<BlockStatement>().ToList()));
                }
            }

            // Apply insertions in reverse order to preserve indices
            foreach (var (index, stmts) in insertions.OrderByDescending(x => x.index))
                loopBodyBlock.Statements.InsertRange(index, stmts);
        }
    }

    /// <summary>
    /// Finds the condition evaluation statements for a Loop node.
    /// These are the Get/CallHelper statements that compute the condition value.
    /// </summary>
    private List<ExpressionStatement> FindConditionStatements(BlueprintNode loopNode,
        ReverseConversionContext ctx)
    {
        var result = new List<ExpressionStatement>();

        // The loop's condition comes from a data input pin.
        // Trace back through the data chain to find all evaluation statements.
        var conditionPin = loopNode.InputPins.FirstOrDefault(p => p.Name == "Condition");
        if (conditionPin == null) return result;

        // Find which node feeds the condition
        if (!ctx.InputDataMap.TryGetValue((loopNode.Id, "Condition"), out var condInfo))
            return result;

        // The condition is evaluated by some chain of nodes.
        // We need to find all statements in the main block that correspond to this chain.
        // Strategy: find statements whose PubVar names appear in the condition expression chain.

        var chainPubVars = new HashSet<string>();
        CollectConditionChain(condInfo, ctx, chainPubVars);

        if (chainPubVars.Count == 0) return result;

        // Find matching statements in the main block
        if (ctx.Script.MainBlock == null) return result;

        var loopFlowStmt = ctx.ControlFlowMap.TryGetValue(loopNode.Id, out var lf) ? lf : null;
        if (loopFlowStmt == null) return result;

        // Collect statements before the Loop that match the condition chain
        var loopIndex = ctx.Script.MainBlock.Statements.IndexOf(loopFlowStmt);
        if (loopIndex < 0) return result;

        for (int i = loopIndex - 1; i >= 0; i--)
        {
            var stmt = ctx.Script.MainBlock.Statements[i];
            if (stmt is ExpressionStatement exprStmt)
            {
                // Check if this statement assigns to one of the chain PubVars
                foreach (var pv in chainPubVars)
                {
                    if (exprStmt.SourceCode.StartsWith($"{pv} = "))
                    {
                        result.Insert(0, exprStmt);
                        break;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Recursively collects all PubVar names in the data chain leading to a condition.
    /// </summary>
    private void CollectConditionChain(DataEdgeInfo info, ReverseConversionContext ctx,
        HashSet<string> pubVars)
    {
        if (!string.IsNullOrEmpty(info.PubVarName))
            pubVars.Add(info.PubVarName);

        // If the source is a CallHelper/Call, trace its argument inputs
        if (info.SourceNode.NodeType is BlueprintNodeType.CallHelper or BlueprintNodeType.Call)
        {
            foreach (var pin in info.SourceNode.InputPins)
            {
                if (pin.Name == "Exec") continue;
                if (ctx.InputDataMap.TryGetValue((info.SourceNode.Id, pin.Name), out var argInfo))
                {
                    CollectConditionChain(argInfo, ctx, pubVars);
                }
            }
        }
    }

    private static ExpressionStatement CloneStatement(ExpressionStatement source) => new()
    {
        Expression = source.Expression,
        SourceCode = source.SourceCode,
        LineNumber = source.LineNumber
    };

    // ──────────────────────────────────────────────
    // Phase 4: Assemble Source Code
    // ──────────────────────────────────────────────

    private void AssembleBlockScript(ReverseConversionContext ctx)
    {
        var sb = new StringBuilder();

        // #ConstBlock
        var constNodes = ctx.Blueprint.Nodes.OfType<ConstNode>().ToList();
        if (constNodes.Count > 0)
        {
            sb.AppendLine("#ConstBlock");
            foreach (var cn in constNodes)
            {
                if (string.IsNullOrEmpty(cn.ConstValue))
                {
                    // Variable without initial value
                    sb.AppendLine($"{cn.ConstType} {cn.ConstName};");
                }
                else
                {
                    var value = cn.ConstType == "string" ? $"\"{cn.ConstValue}\"" : cn.ConstValue;
                    sb.AppendLine($"const {cn.ConstType} {cn.ConstName} = {value};");
                }
            }
            sb.AppendLine();
        }

        // #PubVarBlock
        if (ctx.AllPubVars.Count > 0)
        {
            sb.AppendLine("#PubVarBlock");
            foreach (var pv in ctx.AllPubVars)
            {
                sb.AppendLine($"object {pv};");
            }
            sb.AppendLine();
        }

        // #MainBlock
        if (ctx.Script.MainBlock != null)
        {
            sb.AppendLine("#MainBlock");
            foreach (var stmt in ctx.Script.MainBlock.Statements)
            {
                sb.AppendLine(stmt.SourceCode);
            }
            sb.AppendLine();
        }

        // Named blocks
        foreach (var kvp in ctx.Script.NamedBlocks)
        {
            sb.AppendLine($"#Block {kvp.Key}");
            foreach (var stmt in kvp.Value.Statements)
            {
                sb.AppendLine(stmt.SourceCode);
            }
            sb.AppendLine();
        }

        ctx.Script.SourceCode = sb.ToString().TrimEnd();

        Log.Debug("[BlueprintToScript] Phase 4 done. Source code length: {Len}", ctx.Script.SourceCode.Length);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Detects which block scopes end with a LoopBodyEnd by checking if their
    /// last node's Exec output connects back to a Loop node.
    /// </summary>
    private void DetectAndInsertLoopBodyEnds(Contract.Workflow.Blueprint bp, ReverseConversionContext ctx)
    {
        foreach (var scope in bp.BlockScopes)
        {
            if (scope.NodeIds.Count == 0) continue;

            var lastNodeId = scope.NodeIds[scope.NodeIds.Count - 1];
            if (!ctx.NodeById.TryGetValue(lastNodeId, out var lastNode)) continue;

            // Check if last node's Exec output connects to a Loop node
            var execOutPin = lastNode.OutputPins.FirstOrDefault(p => p.Name == "Exec");
            if (execOutPin == null) continue;

            var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOutPin.Id);
            if (execConn == null) continue;

            if (!ctx.NodeById.TryGetValue(execConn.TargetNodeId, out var targetNode)) continue;
            if (targetNode.NodeType != BlueprintNodeType.Loop) continue;

            // Find the block containing the Loop node
            var returnToBlock = FindScopeContainingNode(targetNode.Id, bp);
            if (returnToBlock == null) continue;

            // Find the block definition for this scope
            BlockDefinition? blockDef = scope.IsMainBlock
                ? ctx.Script.MainBlock
                : ctx.Script.NamedBlocks.GetValueOrDefault(scope.Name);
            if (blockDef == null) continue;

            blockDef.Statements.Add(new FlowControlStatement
            {
                ControlType = FlowControlType.LoopBodyEnd,
                SourceCode = $"NextBlock = LoopBodyEnd(\"{returnToBlock}\");",
                LineNumber = 1,
                LoopBodyEndReturnTo = returnToBlock
            });
        }
    }

    /// <summary>
    /// Finds the block scope name containing a given node ID.
    /// </summary>
    private static string? FindScopeContainingNode(string nodeId, Contract.Workflow.Blueprint bp)
    {
        foreach (var scope in bp.BlockScopes)
        {
            if (scope.NodeIds.Contains(nodeId))
                return scope.Name;
        }
        return null;
    }

    private string GeneratePubVarName(ReverseConversionContext ctx)
    {
        return $"vaaa{ctx.PubVarCounter++:D4}";
    }

    private string GenerateBlockName(ReverseConversionContext ctx)
    {
        return $"Block_{ctx.BlockCounter++}";
    }

    // ──────────────────────────────────────────────
    // Context classes
    // ──────────────────────────────────────────────

    private class ReverseConversionContext
    {
        public required Contract.Workflow.Blueprint Blueprint { get; set; }
        public required BlockScript Script { get; set; }

        // Phase 1: Analysis
        public Dictionary<string, BlueprintNode> NodeById { get; set; } = new();
        public List<BlueprintConnection> ExecConnections { get; set; } = new();
        public List<BlueprintConnection> DataConnections { get; set; } = new();
        public Dictionary<(string nodeId, string pinName), DataEdgeInfo> InputDataMap { get; set; } = new();
        public HashSet<(string nodeId, string pinName)> ConsumedOutputs { get; set; } = new();
        public List<string> AllPubVars { get; set; } = new();
        public List<string> AutoPubVars { get; set; } = new();
        public int PubVarCounter { get; set; } = 1;
        public int BlockCounter { get; set; } = 0;

        // Phase 2: Execution walk
        public Dictionary<string, FlowControlStatement> ControlFlowMap { get; set; } = new();
        public List<BlueprintNode> PendingControlFlowNodes { get; set; } = new();
        public Dictionary<string, BlueprintNode> LoopNodes { get; set; } = new();
        public Dictionary<string, BlockDefinition> LoopBodyBlocks { get; set; } = new();
        public Dictionary<string, (string TrueBlockName, string FalseBlockName)> BlockNameAssignments { get; set; } = new();

        // Block scope fast path
        public Dictionary<string, BlueprintBlockScope> ScopesByName { get; set; } = new();

        /// <summary>
        /// Current loopback target ID when walking inside a loop body.
        /// Set by ProcessLoopSubGraphs, read by ProcessBranchSubGraphs.
        /// </summary>
        public string? CurrentLoopbackTargetId { get; set; }
    }

    private class DataEdgeInfo
    {
        public BlueprintNode SourceNode { get; set; } = null!;
        public string SourcePinName { get; set; } = string.Empty;
        public BlueprintPin SourcePin { get; set; } = null!;
        public string? PubVarName { get; set; }
        public BlueprintConnection Connection { get; set; } = null!;
    }
}
