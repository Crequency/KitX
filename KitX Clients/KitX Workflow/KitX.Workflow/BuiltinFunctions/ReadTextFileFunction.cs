using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
{
    public class ReadTextFileFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ReadTextFile";
        public string DisplayName => "Read Text File";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => false; // Value-producing (has Return pin) — can be nested as an expression
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

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var value = helper.GetInputValue(node, "Path");
            var expr = $"{FunctionName}({value})";
            var pubVar = helper.GetOutputPubVar(node, "Return");
            return new ExpressionStatement
            {
                Expression = expr,
                SourceCode = string.IsNullOrEmpty(pubVar) ? $"{expr};" : $"{pubVar} = {expr};",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Workflow.BlockScripting
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
