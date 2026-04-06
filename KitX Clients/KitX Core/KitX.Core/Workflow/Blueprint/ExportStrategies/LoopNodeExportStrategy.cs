using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class LoopNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Loop;
    public bool IsControlFlow => true;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var condition = helper.GetInputValue(node, "Condition");
        return new FlowControlStatement
        {
            ControlType = FlowControlType.Loop,
            ConditionExpression = condition,
            SourceCode = $"Loop({condition}, \"\", \"\");",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node)
    {
        return
        [
            new OutputArmDescriptor { PinName = "LoopBody", IsLoopback = true },
            new OutputArmDescriptor { PinName = "LoopEnd", IsLoopback = false }
        ];
    }
}
