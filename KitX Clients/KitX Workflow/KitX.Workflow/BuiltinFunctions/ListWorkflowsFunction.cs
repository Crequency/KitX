using System.Text.Json;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class ListWorkflowsFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ListWorkflows";
        public string DisplayName => "List Workflows";
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
        public string ListWorkflows()
        {
            if (!ServiceLocator.IsInitialized)
            {
                Log.Warning("[BlockScriptGlobals] ListWorkflows: ServiceLocator not initialized");
                return "[]";
            }
            try
            {
                var storage = ServiceLocator.GetRequiredService<IWorkflowStorageService>();
                var workflows = storage.DiscoverWorkflowsAsync().GetAwaiter().GetResult();
                var info = workflows.Select(w => new
                {
                    w.Id,
                    w.Name,
                    w.Description,
                    TriggerType = w.TriggerConfig?.TriggerType ?? "Manual"
                });
                return JsonSerializer.Serialize(info);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] ListWorkflows failed");
                return "[]";
            }
        }
    }
}
