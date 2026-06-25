using System.Text.Json;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    public class JsonGetFieldFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonGetField";
        public string DisplayName => "JSON Get Field";
        public bool IsNonExtractable => false; // Value-producing (has Return pin) â€?can be nested as an expression

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Json", PinType.String, 35),
            new("FieldPath", PinType.String, 35)
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
        public string JsonGetField(string json, string fieldPath)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldPath))
                return "";

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var current = root;

                var segments = fieldPath.Split('.');
                foreach (var segment in segments)
                {
                    var bracketIdx = segment.IndexOf('[');
                    if (bracketIdx > 0)
                    {
                        var propName = segment[..bracketIdx];
                        var idxStr = segment[(bracketIdx + 1)..^1];

                        if (!string.IsNullOrEmpty(propName))
                        {
                            if (!current.TryGetProperty(propName, out current))
                                return "";
                        }

                        if (int.TryParse(idxStr, out var idx) &&
                            current.ValueKind == JsonValueKind.Array)
                        {
                            var arr = current.EnumerateArray().ToList();
                            if (idx >= 0 && idx < arr.Count)
                                current = arr[idx];
                            else
                                return "";
                        }
                        else return "";
                    }
                    else
                    {
                        if (!current.TryGetProperty(segment, out current))
                            return "";
                    }
                }

                return current.ValueKind switch
                {
                    JsonValueKind.String => current.GetString() ?? "",
                    JsonValueKind.Number => current.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    JsonValueKind.Object => current.GetRawText(),
                    JsonValueKind.Array => current.GetRawText(),
                    _ => current.GetRawText()
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BlockScriptGlobals] JsonGetField failed for path {Path}", fieldPath);
                return "";
            }
        }
    }
}
