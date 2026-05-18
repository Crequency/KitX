using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    /// <summary>
    /// Set builtin function — writes a value to a global variable.
    /// BlockScript syntax: Set("varName", value)
    /// VarName is passed as the first input pin (String type), Value as the second.
    /// </summary>
    public class SetFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Set";
        public string DisplayName => "Set";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Set;
        public double NodeWidth => 160;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("VarName", PinType.String, 35),
            new("Value", PinType.Any, 50)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<CFGStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var currentArgExprs = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();

            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Set,
                FunctionName = FunctionName,
                Arguments = currentArgExprs,
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
                var varPin = node.InputPins.FirstOrDefault(p => p.Name == "VarName");
                if (varPin != null)
                    varPin.DefaultValue = varName;
            }
            return node;
        }

        public (string?, string?, string?) ExtractStatementFields(
            InvocationExpressionSyntax invoke, List<string> expandedArgs,
            string? assignedVar, PipelineContext context)
        {
            // Don't strip first arg — it stays as the VarName pin argument
            return (null, null, null);
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var varPin = node.InputPins.FirstOrDefault(p => p.Name == "VarName");
            var varName = varPin?.DefaultValue ?? "";
            var value = helper.GetInputValue(node, "Value");
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}(\"{varName}\", {value})",
                SourceCode = $"{FunctionName}(\"{varName}\", {value});",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}
