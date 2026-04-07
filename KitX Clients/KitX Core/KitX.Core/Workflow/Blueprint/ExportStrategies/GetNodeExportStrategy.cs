using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class GetNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Get;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        if (node is not GetNode getNode) return null;
        var varName = getNode.VarName;
        var sourceCode = $"Get(\"{varName}\")";
        return new ExpressionStatement
        {
            Expression = sourceCode,
            SourceCode = sourceCode + ";",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
