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

        foreach (var conn in blueprint.Connections)
        {
            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            var sourcePin = sourceNode?.OutputPins
                .FirstOrDefault(p => p.Id == conn.SourcePinId);

            if (sourcePin?.Type == PinType.Execution)
                exec.Add(conn);
            else
                data.Add(conn);
        }

        Log.Debug("[RenderData] Classified {Total} connections: {ExecCount} exec, {DataCount} data",
            blueprint.Connections.Count, exec.Count, data.Count);

        return new BlueprintRenderData
        {
            AllNodes = blueprint.Nodes.ToList(),
            ExecConnections = exec,
            DataConnections = data
        };
    }
}
