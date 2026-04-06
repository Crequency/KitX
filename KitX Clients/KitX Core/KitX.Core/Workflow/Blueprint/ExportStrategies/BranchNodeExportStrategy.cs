using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class BranchNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Branch;
    public bool IsControlFlow => true;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var condition = helper.GetInputValue(node, "Condition");
        return new FlowControlStatement
        {
            ControlType = FlowControlType.Branch,
            ConditionExpression = condition,
            SourceCode = $"Branch({condition}, \"\", \"\");",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node)
    {
        return
        [
            new OutputArmDescriptor { PinName = "True", IsLoopback = false },
            new OutputArmDescriptor { PinName = "False", IsLoopback = false }
        ];
    }
}
