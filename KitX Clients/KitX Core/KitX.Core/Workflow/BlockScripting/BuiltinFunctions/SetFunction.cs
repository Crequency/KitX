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
    /// Set 内置函数 — 设置变量值。
    /// </summary>
    public class SetFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Set";
        public string DisplayName => "Set";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => BlueprintNodeType.Set;
        public CFGStatementKind StatementKind => CFGStatementKind.Set;
        public double NodeWidth => 120;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Value", PinType.Any, 40)
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

            string? setVarName = null;
            if (invoke.ArgumentList.Arguments.Count > 0)
            {
                var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
                var varNameLiteral = ExprUtils.GetStringLiteralValue(firstArgExpr);
                setVarName = varNameLiteral ?? currentArgExprs[0];
                currentArgExprs.RemoveAt(0);
            }

            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Set,
                FunctionName = FunctionName,
                SetVarName = setVarName,
                Arguments = currentArgExprs,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
        {
            var varName = stmt.SetVarName ?? "";
            if (node is SetNode sn)
                sn.VarName = varName;
            else if (node is BuiltinFunctionNode bfn)
                bfn.Properties["VarName"] = varName;
            return node;
        }

        public (string?, string?, string?) ExtractStatementFields(
            InvocationExpressionSyntax invoke, List<string> expandedArgs,
            string? assignedVar, PipelineContext context)
        {
            string? setVarName = null;
            if (expandedArgs.Count > 0)
            {
                var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
                setVarName = ExprUtils.GetStringLiteralValue(firstArgExpr) ?? expandedArgs[0];
                expandedArgs.RemoveAt(0);
            }
            return (setVarName, null, null);
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var varName = node switch
            {
                SetNode sn => sn.VarName,
                BuiltinFunctionNode bfn => bfn.Properties.GetValueOrDefault("VarName", ""),
                _ => ""
            };
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
