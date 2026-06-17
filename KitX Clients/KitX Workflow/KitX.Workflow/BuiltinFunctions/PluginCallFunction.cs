using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
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

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => ctx.EmitValueAssignment(stmt.PubVarTarget, CFG2CSGenerator.BuildPluginCallExpression(stmt, ctx.PubVarTypes));

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper) => null;

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Workflow.BlockScripting
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
