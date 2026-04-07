using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class CallHelperNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.CallHelper;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        if (node is not CallHelperNode callHelper) return null;
        var helperArgs = helper.GetInputArgs(callHelper);
        var expression = $"{callHelper.HelperFunctionName}({helperArgs})";
        return new ExpressionStatement
        {
            Expression = expression,
            SourceCode = expression + ";",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
