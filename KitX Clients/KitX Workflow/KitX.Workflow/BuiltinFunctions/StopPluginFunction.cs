using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class StopPluginFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "StopPlugin";
        public string DisplayName => "Stop Plugin";
        public bool IsNonExtractable => true;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Boolean, 40)
        ];
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public bool StopPlugin(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) return false;
            if (!ServiceLocator.IsInitialized) return false;
            try
            {
                var pluginService = ServiceLocator.GetRequiredService<IPluginService>();
                var plugin = pluginService.GetInstalledPlugins()
                    .FirstOrDefault(p => p.PluginInfo?.Name == pluginName);
                if (plugin == null) return false;
                return pluginService.StopPluginAsync(plugin.Id).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] StopPlugin failed for {Name}", pluginName);
                return false;
            }
        }
    }
}
