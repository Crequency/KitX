using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class CallNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Call;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        if (node is not CallNode call) return null;
        var callArgs = helper.GetInputArgs(call);
        return new ExpressionStatement
        {
            Expression = $"{call.FunctionName}({callArgs});",
            SourceCode = $"{call.FunctionName}({callArgs});",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
