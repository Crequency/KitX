using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
{
    public class InstallPluginFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "InstallPlugin";
        public string DisplayName => "Install Plugin";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => false; // Value-producing (has Return pin) — can be nested as an expression
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 140;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("KxpPath", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Boolean, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var value = helper.GetInputValue(node, "KxpPath");
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
        public bool InstallPlugin(string kxpPath)
        {
            if (string.IsNullOrEmpty(kxpPath)) return false;
            if (!ServiceLocator.IsInitialized) return false;
            try
            {
                var pluginService = ServiceLocator.GetRequiredService<IPluginService>();
                return pluginService.ImportPluginAsync(kxpPath).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] InstallPlugin failed for {Path}", kxpPath);
                return false;
            }
        }
    }
}
