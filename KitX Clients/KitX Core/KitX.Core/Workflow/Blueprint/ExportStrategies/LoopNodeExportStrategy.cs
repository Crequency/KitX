using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class LoopNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Loop;
    public bool IsControlFlow => true;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var condition = helper.GetInputValue(node, BlockScriptWellKnown.Pins.Condition);
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
            new OutputArmDescriptor { PinName = BlockScriptWellKnown.Pins.LoopBody, IsLoopback = true },
            new OutputArmDescriptor { PinName = BlockScriptWellKnown.Pins.LoopEnd, IsLoopback = false }
        ];
    }
}
