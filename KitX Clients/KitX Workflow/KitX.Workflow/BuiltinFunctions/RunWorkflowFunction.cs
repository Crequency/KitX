using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class RunWorkflowFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "RunWorkflow";
        public string DisplayName => "Run Workflow";
        public bool IsNonExtractable => false; // Value-producing (has Return pin) â€?can be nested as an expression

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("WorkflowId", PinType.String, 35)
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
        public bool RunWorkflow(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId)) return false;
            if (!ServiceLocator.IsInitialized) return false;
            try
            {
                var wfService = ServiceLocator.GetRequiredService<IWorkflowManagementService>();
                return wfService.RunWorkflowAsync(workflowId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] RunWorkflow failed for {Id}", workflowId);
                return false;
            }
        }
    }
}
