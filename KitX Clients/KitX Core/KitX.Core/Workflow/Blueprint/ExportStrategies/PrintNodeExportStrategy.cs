using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class PrintNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Print;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var value = helper.GetInputValue(node, BlockScriptWellKnown.Pins.Value);
        return new ExpressionStatement
        {
            Expression = $"{BlockScriptWellKnown.Functions.Print}({value});",
            SourceCode = $"{BlockScriptWellKnown.Functions.Print}({value});",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
