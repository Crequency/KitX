using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class ReadTextFileFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ReadTextFile";
        public string DisplayName => "Read Text File";
        public bool IsNonExtractable => false; // Value-producing (has Return pin) â€?can be nested as an expression

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Path", PinType.String, 35)
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
        public string ReadTextFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                if (!File.Exists(path)) return "";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] ReadTextFile failed for {Path}", path);
                return "";
            }
        }
    }
}
