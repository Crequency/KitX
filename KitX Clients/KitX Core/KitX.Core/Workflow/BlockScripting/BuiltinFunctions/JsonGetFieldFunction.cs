using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    public class JsonGetFieldFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonGetField";
        public string DisplayName => "JSON Get Field";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => null;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 160;
        public double NodeHeight => 80;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Json", PinType.String, 35),
            new("FieldPath", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.String, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<CFGStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();
            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Expression,
                FunctionName = FunctionName,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
        {
            if (node is BuiltinFunctionNode bfn && stmt.Arguments?.Count > 1)
                bfn.Properties["FieldPath"] = StripQuotes(stmt.Arguments[1]);
            return node;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var jsonValue = helper.GetInputValue(node, "Json");
            var fieldPath = node is BuiltinFunctionNode bfn
                ? bfn.Properties.GetValueOrDefault("FieldPath", "") ?? ""
                : "";
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}({jsonValue}, \"{fieldPath}\")",
                SourceCode = $"{FunctionName}({jsonValue}, \"{fieldPath}\");",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];

        private static string StripQuotes(string s)
        {
            s = s?.Trim() ?? "";
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                return s[1..^1];
            return s;
        }
    }
}

namespace KitX.Core.Workflow.BlockScripting
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
