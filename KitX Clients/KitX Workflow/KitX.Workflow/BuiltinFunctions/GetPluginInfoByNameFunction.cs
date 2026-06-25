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
        public bool IsNonExtractable => false; // Value-producing (has Return pin) â€?can be nested as an expression

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35)
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
