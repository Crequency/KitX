using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.Blueprint.ExportStrategies;

public class SetNodeExportStrategy : INodeExportStrategy
{
    public BlueprintNodeType NodeType => BlueprintNodeType.Set;
    public bool IsControlFlow => false;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        if (node is not SetNode setNode) return null;
        var value = helper.GetInputValue(node, BlockScriptWellKnown.Pins.Value);
        var sourceCode = $"{BlockScriptWellKnown.Functions.Set}(\"{setNode.VarName}\", {value})";
        return new ExpressionStatement
        {
            Expression = sourceCode,
            SourceCode = sourceCode + ";",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node) => [];
}
