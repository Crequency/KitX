using System.Linq;
using System.Text.Json;
using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// JsonGetField ¡ª navigate a JSON value by dotted path (with optional [index] array access),
    /// returning a <see cref="PinType.Json"/> element so the result stays a structured value that
    /// can be chained into further Json functions or landed into a Json PubVar.
    /// v5.2 refactor (Package/List-Port design ¡ì4.1): accepts Any (JsonElement or JSON string),
    /// returns Json. Scalar leaves come back as JsonElement (use JsonAsString/Int/Bool to land them).
    /// </summary>
    public class JsonGetFieldFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonGetField";
        public string DisplayName => "JSON Get Field";
        public bool IsNonExtractable => false;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Json", PinType.Any, 35),
            new("FieldPath", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Json, 40)
        ];

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonGetField", args));
        }
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>Navigate a JSON value (JsonElement or JSON string) by dotted path.
        /// Returns a JsonElement (object/array subtree or scalar). Empty/missing ¡ú null element.</summary>
        public JsonElement JsonGetField(object? json, string fieldPath)
        {
            if (string.IsNullOrEmpty(fieldPath)) return default;
            var root = json.AsJsonElement();
            if (root.ValueKind == JsonValueKind.Undefined) return default;

            try
            {
                var current = root;
                foreach (var segment in fieldPath.Split('.'))
                {
                    var bracketIdx = segment.IndexOf('[');
                    if (bracketIdx > 0)
                    {
                        var propName = segment[..bracketIdx];
                        var idxStr = segment[(bracketIdx + 1)..^1];
                        if (!string.IsNullOrEmpty(propName)
                            && !current.TryGetProperty(propName, out current)) return default;
                        if (int.TryParse(idxStr, out var idx) && current.ValueKind == JsonValueKind.Array)
                        {
                            var arr = current.EnumerateArray().ToList();
                            current = (idx >= 0 && idx < arr.Count) ? arr[idx] : default;
                        }
                        else return default;
                    }
                    else if (!current.TryGetProperty(segment, out current)) return default;
                }
                return current.Clone();  // detach from any disposable JsonDocument
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BlockScriptGlobals] JsonGetField failed for path {Path}", fieldPath);
                return default;
            }
        }
    }
}
