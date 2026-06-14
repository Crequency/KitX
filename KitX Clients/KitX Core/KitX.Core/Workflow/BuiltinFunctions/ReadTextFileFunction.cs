using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions
{
    public class ReadTextFileFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ReadTextFile";
        public string DisplayName => "Read Text File";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 140;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Path", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.String, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<CFGStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();
            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Expression,
                FunctionName = FunctionName,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var value = helper.GetInputValue(node, "Path");
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}({value})",
                SourceCode = $"{FunctionName}({value});",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public string ReadTextFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                if (!File.Exists(path)) return "";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] ReadTextFile failed for {Path}", path);
                return "";
            }
        }
    }
}
