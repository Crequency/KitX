using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class PauseNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Pause;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var ms = helper.GetInputValue(node, "Milliseconds");
        return new ExpressionStatement
        {
            Expression = $"Pause({ms});",
            SourceCode = $"Pause({ms});",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
