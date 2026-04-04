using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Classifies blueprint connections into Exec (execution flow) and Data (data flow)
/// by inspecting the source pin type of each connection.
/// </summary>
public class BlueprintRenderDataService : IBlueprintRenderDataService
{
    /// <inheritdoc />
    public BlueprintRenderData GetRenderData(Contract.Workflow.Blueprint blueprint)
    {
        var exec = new List<BlueprintConnection>();
        var data = new List<BlueprintConnection>();

        Log.Debug("[RenderData] === Classifying {ConnCount} connections from blueprint '{BpName}' ===",
            blueprint.Connections.Count, blueprint.Name);

        foreach (var conn in blueprint.Connections)
        {
            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
            var sourcePin = sourceNode?.OutputPins
                .FirstOrDefault(p => p.Id == conn.SourcePinId);
            var targetPin = targetNode?.InputPins
                .FirstOrDefault(p => p.Id == conn.TargetPinId);

            var srcPinType = sourcePin?.Type.ToString() ?? "null";
            var tgtPinType = targetPin?.Type.ToString() ?? "null";

            // [DIAG] Log both classification results for comparison
            var classifyBySourceOnly = sourcePin?.Type == PinType.Execution;
            var classifyByEither = sourcePin?.Type == PinType.Execution
                || targetPin?.Type == PinType.Execution;

            if (classifyBySourceOnly)
            {
                exec.Add(conn);
                Log.Debug("[RenderData]   EXEC  {SrcName}.{SrcPin} [{SrcType}] -> {TgtName}.{TgtPin} [{TgtType}]  (bySource={BySrc}, byEither={ByEither})",
                    sourceNode?.Name ?? "NULL", sourcePin?.Name ?? "?",
                    targetNode?.Name ?? "NULL", targetPin?.Name ?? "?",
                    srcPinType, tgtPinType, classifyBySourceOnly, classifyByEither);
            }
            else
            {
                data.Add(conn);
                Log.Debug("[RenderData]   DATA  {SrcName}.{SrcPin} [{SrcType}] -> {TgtName}.{TgtPin} [{TgtType}]  (bySource={BySrc}, byEither={ByEither})",
                    sourceNode?.Name ?? "NULL", sourcePin?.Name ?? "?",
                    targetNode?.Name ?? "NULL", targetPin?.Name ?? "?",
                    srcPinType, tgtPinType, classifyBySourceOnly, classifyByEither);
            }
        }

        Log.Debug("[RenderData] === Result: {ExecCount} exec, {DataCount} data (total {Total}) ===",
            exec.Count, data.Count, exec.Count + data.Count);

        return new BlueprintRenderData
        {
            AllNodes = blueprint.Nodes.ToList(),
            ExecConnections = exec,
            DataConnections = data
        };
    }
}
