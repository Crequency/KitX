using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class BreakNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Break;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        return new FlowControlStatement
        {
            ControlType = FlowControlType.Break,
            SourceCode = "Break();",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
