using System.Text.Json;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class GetPluginInfoByNameFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "GetPluginInfoByName";
        public string DisplayName => "Get Plugin Info";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => false; // Value-producing (has Return pin) — can be nested as an expression
        public double NodeWidth => 160;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.String, 40)
        ];

        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
        {
            if (node is BuiltinFunctionNode bfn && stmt.Arguments?.Count > 0)
                bfn.Properties["PluginName"] = StripQuotes(stmt.Arguments[0]);
            return node;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var pluginName = node is BuiltinFunctionNode bfn
                ? bfn.Properties.GetValueOrDefault("PluginName", "") ?? ""
                : "";
            var expr = $"{FunctionName}(\"{pluginName}\")";
            var pubVar = helper.GetOutputPubVar(node, "Return");
            return new ExpressionStatement
            {
                Expression = expr,
                SourceCode = string.IsNullOrEmpty(pubVar) ? $"{expr};" : $"{pubVar} = {expr};",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];

        private static string StripQuotes(string s)
        {
            s = s?.Trim() ?? "";
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                return s[1..^1];
            return s;
        }
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public string GetPluginInfoByName(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) return "{}";
            if (!ServiceLocator.IsInitialized) return "{}";
            try
            {
                var pluginService = ServiceLocator.GetRequiredService<IPluginService>();
                var plugin = pluginService.GetInstalledPlugins()
                    .FirstOrDefault(p => p.PluginInfo?.Name == pluginName);
                if (plugin?.PluginInfo == null) return "{}";

                return JsonSerializer.Serialize(new
                {
                    plugin.PluginInfo.Name,
                    plugin.PluginInfo.Version,
                    IsRunning = true,
                    Functions = plugin.PluginInfo.Functions?.Select(f => new
                    {
                        f.Name,
                        f.ReturnValueType,
                        Parameters = f.Parameters?.Select(p => new { p.Name, p.Type })
                    })
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] GetPluginInfoByName failed for {Name}", pluginName);
                return "{}";
            }
        }
    }
}
