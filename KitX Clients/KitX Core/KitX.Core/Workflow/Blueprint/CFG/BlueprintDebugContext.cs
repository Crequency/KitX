using System.Collections.Generic;

namespace KitX.Core.Workflow.Blueprint.CFG;

public class BlueprintDebugContext
{
    public Dictionary<string, string> StatementToNodeId { get; } = new();
}
