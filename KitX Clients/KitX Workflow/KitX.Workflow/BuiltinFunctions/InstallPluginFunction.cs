using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class InstallPluginFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "InstallPlugin";
        public string DisplayName => "Install Plugin";
        public bool IsNonExtractable => false; // Value-producing (has Return pin) â€?can be nested as an expression

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("KxpPath", PinType.String, 35)
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
