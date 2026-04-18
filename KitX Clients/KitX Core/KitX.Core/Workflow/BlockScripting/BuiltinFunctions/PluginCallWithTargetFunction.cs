using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Device;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.Pipeline;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    /// <summary>
    /// PluginCallWithTarget 内置函数 — 调用远程设备上的插件。
    /// 语法: PluginCallWithTarget("pluginName", "methodName", "targetDevice"[, arg1, arg2, ...]);
    /// targetDevice 可以是设备名（字符串）或 TryGetDevice("pattern") 的返回值（DeviceInfo）。
    /// </summary>
    public class PluginCallWithTargetFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "PluginCallWithTarget";
        public string DisplayName => "PluginCallWithTarget";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => BlueprintNodeType.Call;
        public FormattedStatementKind StatementKind => FormattedStatementKind.PluginCallWithTarget;
        public double NodeWidth => 140;
        public double NodeHeight => 80;

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

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<FormattedStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments
                .Select(a => a.Expression.ToString())
                .ToList();

            return [new FormattedStatement
            {
                BlockName = blockName,
                Kind = FormattedStatementKind.PluginCallWithTarget,
                FunctionName = FunctionName,
                PubVarTarget = null,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt)
        {
            if (node is CallNode call)
            {
                var args = stmt.Arguments;
                if (args?.Count > 0) call.PluginName = StripQuotes(args[0]);
                if (args?.Count > 1) call.FunctionName = StripQuotes(args[1]);
                if (args?.Count > 2) call.TargetDevice = StripQuotes(args[2]);
            }
            return node;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            if (node is not CallNode call) return null;

            var pluginNameLit = $"\"{call.PluginName}\"";
            var methodNameLit = $"\"{call.FunctionName}\"";
            var targetDeviceExpr = !string.IsNullOrEmpty(call.TargetDevice)
                ? $"\"{call.TargetDevice}\""
                : "null";

            var extraArgPins = call.InputPins
                .Where(p => p.Direction == PinDirection.Input && p.Name != "Exec")
                .Skip(3)
                .ToList();

            var extraArgs = new List<string>();
            foreach (var pin in extraArgPins)
            {
                extraArgs.Add(helper.GetInputValue(call, pin.Name));
            }

            string expression;
            if (extraArgs.Count > 0)
                expression = $"PluginCallWithTarget({pluginNameLit}, {methodNameLit}, {targetDeviceExpr}, {string.Join(", ", extraArgs)})";
            else
                expression = $"PluginCallWithTarget({pluginNameLit}, {methodNameLit}, {targetDeviceExpr})";

            return new ExpressionStatement
            {
                Expression = expression,
                SourceCode = expression + ";",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];

        private static string StripQuotes(string s)
        {
            s = s?.Trim() ?? "";
            if (s.Length >= 2 && s.StartsWith('"') && s.EndsWith('"'))
                return s[1..^1];
            return s;
        }
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// 调用远程设备上的插件函数。TargetDevice 参数指定目标设备。
        /// </summary>
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
