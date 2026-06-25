using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Services;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// PluginCallWithTarget 内置函数 �?调用远程设备上的插件�?    /// 语法: PluginCallWithTarget("pluginName", "methodName", "targetDevice"[, arg1, arg2, ...]);
    /// targetDevice 可以是设备名（字符串）或 TryGetDevice("pattern") 的返回值（DeviceInfo）�?    /// </summary>
    public class PluginCallWithTargetFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "PluginCallWithTarget";
        public string DisplayName => "PluginCallWithTarget";
        public bool IsNonExtractable => false;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("PluginName", PinType.String, 35),
            new("MethodName", PinType.String, 35),
            new("TargetDevice", PinType.Any, 40),
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("Return", PinType.Any, 40)
        ];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
            => ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.PluginCallWithTargetExpression(stmt));
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// 调用远程设备上的插件函数。TargetDevice 参数指定目标设备�?        /// </summary>
        public object? PluginCallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
        {
            if (_pluginManager == null)
            {
                Log.Warning("[BlockScriptGlobals] PluginCallWithTarget: no plugin manager available, " +
                    "cannot call {PluginName}.{MethodName}@{Target}", pluginName, methodName, targetDevice);
                return null;
            }

            var callInfo = new PluginCallInfo
            {
                PluginName = pluginName,
                MethodName = methodName,
                Parameters = args ?? Array.Empty<object>(),
                TargetDevice = targetDevice
            };

            try
            {
                if (_pluginManager is RealPluginManager realManager)
                    return realManager.CallAuto(callInfo);

                return _pluginManager.Call<object?>(callInfo);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] PluginCallWithTarget failed: {PluginName}.{MethodName}@{Target}",
                    pluginName, methodName, targetDevice);
                return null;
            }
        }
    }
}
