using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.DI;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    public class ListWorkflowsFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ListWorkflows";
        public string DisplayName => "List Workflows";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => null;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 140;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20)
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
            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Expression,
                FunctionName = FunctionName,
                Arguments = [],
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}()",
                SourceCode = $"{FunctionName}();",
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
        public string ListWorkflows()
        {
            if (!DI.ServiceHost.IsInitialized)
            {
                Log.Warning("[BlockScriptGlobals] ListWorkflows: ServiceHost not initialized");
                return "[]";
            }
            try
            {
                var storage = DI.ServiceHost.GetRequiredService<IWorkflowStorageService>();
                var workflows = storage.DiscoverWorkflowsAsync().GetAwaiter().GetResult();
                var info = workflows.Select(w => new
                {
                    w.Id,
                    w.Name,
                    w.Description,
                    w.TriggerType
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
