using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
using KitX.Workflow.Services;
using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// PluginCall 内置函数 �?调用本机插件函数�?    /// </summary>
    public class PluginCallFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "PluginCall";
        public string DisplayName => "PluginCall";
        // PluginCall has a return value (OutputPin "Return") and is meant to be used as
        // an expression in argument position (e.g. Set("x", PluginCall("P","M",arg))).
        // IsNonExtractable=true would make BS2CFGConverter.ExpandExpression keep it inline
        // as a raw string instead of expanding it into a temp PubVar + standalone call,
        // which then fails to compile (CS0103 'PluginCall' undefined in generated C#).
        // false matches PluginCallWithTarget/Get/TryGetDevice �?value-producing builtins
        // that CAN be nested. Set/Print/Pause stay true (pure side-effects, no return).
        public bool IsNonExtractable => false;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35),
            new("MethodName", PinType.String, 35),
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Any, 40)
        ];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
            => ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.PluginCallExpression(stmt));
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// 调用插件函数。所有分发策略由 RealPluginManager.CallAuto() 内部自动完成�?        /// </summary>
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
