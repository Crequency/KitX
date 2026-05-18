using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Core.Workflow.Pipeline;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions
{
    public class StartPluginFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "StartPlugin";
        public string DisplayName => "Start Plugin";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
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
            var value = helper.GetInputValue(node, "PluginName");
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
        public bool StartPlugin(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) return false;
            if (!DI.ServiceHost.IsInitialized) return false;
            try
            {
                var pluginService = DI.ServiceHost.GetRequiredService<IPluginService>();
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
