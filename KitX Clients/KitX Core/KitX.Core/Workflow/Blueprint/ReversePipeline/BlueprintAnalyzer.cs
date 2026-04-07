using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

namespace KitX.Core.Workflow.Blueprint.ReversePipeline;

/// <summary>
/// Phase 1 of reverse conversion: analyzes Blueprint to build data indexes,
/// classify connections, and auto-assign PubVar names where missing.
/// </summary>
internal class BlueprintAnalyzer
{
    public void Analyze(ReverseConversionContext ctx)
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

                var pubVar = ExprUtils.GeneratePubVarName(ctx.PubVarCounter++);
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
}
