using System.Collections.Generic;
using System.Linq;
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
    public class InstallPluginFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "InstallPlugin";
        public string DisplayName => "Install Plugin";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => null;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 140;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("KxpPath", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Boolean, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<FormattedStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();
            return [new FormattedStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Expression,
                FunctionName = FunctionName,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var value = helper.GetInputValue(node, "KxpPath");
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}({value})",
                SourceCode = $"{FunctionName}({value});",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public bool InstallPlugin(string kxpPath)
        {
            if (string.IsNullOrEmpty(kxpPath)) return false;
            if (!DI.ServiceHost.IsInitialized) return false;
            try
            {
                var pluginService = DI.ServiceHost.GetRequiredService<IPluginService>();
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
