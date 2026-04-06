using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class PrintNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Print;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var value = helper.GetInputValue(node, "Value");
        return new ExpressionStatement
        {
            Expression = $"Print({value});",
            SourceCode = $"Print({value});",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
