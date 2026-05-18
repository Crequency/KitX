using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using KitX.Core.DI;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    public class GetPluginInfoByNameFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "GetPluginInfoByName";
        public string DisplayName => "Get Plugin Info";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 160;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35)
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
            if (node is BuiltinFunctionNode bfn && stmt.Arguments?.Count > 0)
                bfn.Properties["PluginName"] = StripQuotes(stmt.Arguments[0]);
            return node;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var pluginName = node is BuiltinFunctionNode bfn
                ? bfn.Properties.GetValueOrDefault("PluginName", "") ?? ""
                : "";
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}(\"{pluginName}\")",
                SourceCode = $"{FunctionName}(\"{pluginName}\");",
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
        public string GetPluginInfoByName(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName)) return "{}";
            if (!DI.ServiceHost.IsInitialized) return "{}";
            try
            {
                var pluginService = DI.ServiceHost.GetRequiredService<IPluginService>();
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
