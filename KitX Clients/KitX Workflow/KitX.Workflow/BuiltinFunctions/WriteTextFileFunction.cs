using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class WriteTextFileFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "WriteTextFile";
        public string DisplayName => "Write Text File";
        public bool IsNonExtractable => true;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Path", PinType.String, 35),
            new("Content", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20)
        ];
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public void WriteTextFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] WriteTextFile failed for {Path}", path);
            }
        }
    }
}
