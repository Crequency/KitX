using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
{
    public class StartPluginFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "StartPlugin";
        public string DisplayName => "Start Plugin";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public double NodeWidth => 140;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Boolean, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var value = helper.GetInputValue(node, "PluginName");
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
        public bool StartPlugin(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) return false;
            if (!ServiceLocator.IsInitialized) return false;
            try
            {
                var pluginService = ServiceLocator.GetRequiredService<IPluginService>();
                var plugin = pluginService.GetInstalledPlugins()
                    .FirstOrDefault(p => p.PluginInfo?.Name == pluginName);
                if (plugin == null) return false;
                return pluginService.StartPluginAsync(plugin.Id).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] StartPlugin failed for {Name}", pluginName);
                return false;
            }
        }
    }
}
