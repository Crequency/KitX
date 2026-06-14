using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions;

/// <summary>
/// Get builtin function — reads a variable value from global scope.
/// BlockScript syntax: Get("varName")
/// VarName is passed as the first input pin (String type), allowing connections.
/// </summary>
public class GetFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Get";
    public string DisplayName => "Get";
    public bool IsFlowControl => false;
    public bool IsNonExtractable => false;
    public CFGStatementKind StatementKind => CFGStatementKind.Assignment;
    public double NodeWidth => 140;
    public double NodeHeight => 60;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20),
        new("VarName", PinType.String, 40)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [
        new("Exec", PinType.Execution, 20),
        new("Value", PinType.Any, 40)
    ];

    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<CFGStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string blockName,
        PipelineContext context, string? assignedVar)
    {
        var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();

        // Auto-generate PubVar if not already assigned
        string? pubVarTarget;
        if (string.IsNullOrEmpty(assignedVar) || !context.PubVarNames.Contains(assignedVar))
        {
            pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarTarget))
                context.PubVarNames.Add(pubVarTarget);
        }
        else
        {
            pubVarTarget = assignedVar;
        }

        return [new CFGStatement
        {
            BlockName = blockName,
            Kind = CFGStatementKind.Assignment,
            FunctionName = FunctionName,
            PubVarTarget = pubVarTarget,
            Arguments = args,
            OriginalExpression = invoke.ToString(),
            SourceLine = 0,
        }];
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
    {
        // Set default value on VarName pin from first argument
        if (stmt.Arguments?.Count > 0)
        {
            var varName = stmt.Arguments[0].Trim('"');
            var pin = node.InputPins.FirstOrDefault(p => p.Name == "VarName");
            if (pin != null)
                pin.DefaultValue = varName;
        }
        return node;
    }

    public (string?, string?, string?) ExtractStatementFields(
        InvocationExpressionSyntax invoke, List<string> expandedArgs,
        string? assignedVar, PipelineContext context)
    {
        string? pubVarTarget;
        if (string.IsNullOrEmpty(assignedVar) || !context.PubVarNames.Contains(assignedVar))
        {
            pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarTarget))
                context.PubVarNames.Add(pubVarTarget);
        }
        else
        {
            pubVarTarget = assignedVar;
        }

        return (null, null, pubVarTarget);
    }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        if (!helper.IsOutputConsumed(node, "Value"))
            return null;

        var pubVar = helper.GetOutputPubVar(node, "Value");
        if (pubVar == null) return null;

        var varPin = node.InputPins.FirstOrDefault(p => p.Name == "VarName");
        var varName = varPin?.DefaultValue ?? "";
        if (string.IsNullOrEmpty(varName)) return null;

        return new ExpressionStatement
        {
            Expression = $"Get(\"{varName}\")",
            SourceCode = $"{pubVar} = Get(\"{varName}\");",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
