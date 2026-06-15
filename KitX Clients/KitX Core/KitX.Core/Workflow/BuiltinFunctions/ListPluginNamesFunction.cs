using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions
{
    public class ListPluginNamesFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ListPluginNames";
        public string DisplayName => "List Plugin Names";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 140;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.String, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}()",
                SourceCode = $"{FunctionName}();",
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
        public string ListPluginNames()
        {
            if (!DI.ServiceHost.IsInitialized)
            {
                Log.Warning("[BlockScriptGlobals] ListPluginNames: ServiceHost not initialized");
                return "[]";
            }
            try
            {
                var pluginService = DI.ServiceHost.GetRequiredService<IPluginService>();
                var plugins = pluginService.GetInstalledPlugins();
                var names = plugins
                    .Where(p => p.PluginInfo != null)
                    .Select(p => p.PluginInfo!.Name)
                    .ToList();
                return JsonSerializer.Serialize(names);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] ListPluginNames failed");
                return "[]";
            }
        }
    }
}
