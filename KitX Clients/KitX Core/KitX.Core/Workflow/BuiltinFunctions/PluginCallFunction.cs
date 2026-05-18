using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Core.Workflow.Pipeline;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions
{
    /// <summary>
    /// PluginCall 内置函数 — 调用本机插件函数。
    /// </summary>
    public class PluginCallFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "PluginCall";
        public string DisplayName => "PluginCall";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 140;
        public double NodeHeight => 80;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35),
            new("MethodName", PinType.String, 35),
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Any, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<CFGStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments
                .Select(a => a.Expression.ToString())
                .ToList();
            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Expression,
                FunctionName = FunctionName,
                PubVarTarget = null,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper) => null;

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// 调用插件函数。所有分发策略由 RealPluginManager.CallAuto() 内部自动完成。
        /// </summary>
        public object? PluginCall(string pluginName, string methodName, params object[] args)
        {
            if (_pluginManager == null)
            {
                Log.Warning("[BlockScriptGlobals] PluginCall: no plugin manager available, " +
                    "cannot call {PluginName}.{MethodName}", pluginName, methodName);
                return null;
            }

            var callInfo = new PluginCallInfo
            {
                PluginName = pluginName,
                MethodName = methodName,
                Parameters = args ?? Array.Empty<object>()
            };

            try
            {
                if (_pluginManager is RealPluginManager realManager)
                    return realManager.CallAuto(callInfo);

                return _pluginManager.Call<object?>(callInfo);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] PluginCall failed: {PluginName}.{MethodName}",
                    pluginName, methodName);
                return null;
            }
        }
    }
}
