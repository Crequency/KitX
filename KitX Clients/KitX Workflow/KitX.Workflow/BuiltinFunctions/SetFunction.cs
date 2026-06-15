using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
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

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
    {
        var result = new List<StatementSyntax>();
        var varName = stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "";
        if (stmt.Arguments.Count > 1)
            result.Add(ctx.GInvokeStatement("Set", ctx.Literal(varName), ctx.ResolveArgument(stmt.Arguments[1])));
        else if (stmt.Arguments.Count > 0)
            result.Add(ctx.GInvokeStatement("Set", ctx.Literal(varName), ctx.ResolveArgument(stmt.Arguments[0])));
        return result;
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
