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
    public class ListPluginNamesFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ListPluginNames";
        public string DisplayName => "List Plugin Names";
        public bool IsNonExtractable => false; // Value-producing (has Return pin) â€?can be nested as an expression

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.String, 40)
        ];
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public string ListPluginNames()
        {
            if (!ServiceLocator.IsInitialized)
            {
                Log.Warning("[BlockScriptGlobals] ListPluginNames: ServiceLocator not initialized");
                return "[]";
            }
            try
            {
                var pluginService = ServiceLocator.GetRequiredService<IPluginService>();
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
