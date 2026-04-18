using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// TryGetDevice 内置函数 — 根据设备名称查找已连接设备。
    /// 返回 DeviceInfo 对象（可用于 PluginCallWithTarget 的 targetDevice 参数）。
    /// 如果找不到设备，返回 null。
    /// 语法: TryGetDevice("DeviceName")
    /// </summary>
    public class TryGetDeviceFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "TryGetDevice";
        public string DisplayName => "TryGetDevice";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => false;
        public BlueprintNodeType? LegacyNodeType => null;
        public FormattedStatementKind StatementKind => FormattedStatementKind.TryGetDevice;
        public double NodeWidth => 120;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Pattern", PinType.String, 35),
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20),
            new("DeviceInfo", PinType.Any, 40)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<FormattedStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments
                .Select(a => a.Expression.ToString())
                .ToList();

            string? pubVarTarget;
            if (!string.IsNullOrEmpty(assignedVar))
            {
                pubVarTarget = assignedVar;
            }
            else
            {
                pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
                if (!context.PubVarNames.Contains(pubVarTarget))
                    context.PubVarNames.Add(pubVarTarget);
            }

            return [new FormattedStatement
            {
                BlockName = blockName,
                Kind = FormattedStatementKind.TryGetDevice,
                FunctionName = FunctionName,
                PubVarTarget = pubVarTarget,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt)
        {
            if (node is BuiltinFunctionNode bfn && stmt.Arguments?.Count > 0)
            {
                bfn.Properties["Pattern"] = StripQuotes(stmt.Arguments[0]);
            }
            return node;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            string pattern;
            if (node is BuiltinFunctionNode bfn)
                pattern = bfn.Properties.GetValueOrDefault("Pattern", "") ?? "";
            else
                pattern = "";

            var patternLit = $"\"{pattern}\"";
            var value = helper.GetInputValue(node, "DeviceInfo");
            return new ExpressionStatement
            {
                Expression = $"TryGetDevice({patternLit})",
                SourceCode = $"{value} = TryGetDevice({patternLit});",
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
        /// 根据设备名称查找已连接设备的 DeviceInfo。
        /// 内部使用 DevicesServer._signedDeviceTokens 查找匹配的 DeviceLocator。
        /// </summary>
        public object? TryGetDevice(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                Log.Warning("[BlockScriptGlobals] TryGetDevice: deviceName is empty");
                return null;
            }

            try
            {
                var tokensField = typeof(DevicesServer)
                    .GetField("_signedDeviceTokens",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                if (tokensField == null)
                {
                    Log.Warning("[BlockScriptGlobals] TryGetDevice: _signedDeviceTokens field not found");
                    return null;
                }

                var tokens = tokensField.GetValue(DevicesServer.Instance) as System.Collections.IDictionary;
                if (tokens == null)
                {
                    Log.Warning("[BlockScriptGlobals] TryGetDevice: _signedDeviceTokens is null");
                    return null;
                }

                foreach (System.Collections.DictionaryEntry entry in tokens)
                {
                    if (entry.Key is DeviceLocator locator &&
                        locator.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("[BlockScriptGlobals] TryGetDevice: found device {DeviceName} (IPv4={IPv4})",
                            locator.DeviceName, locator.IPv4);

                        var discoveryInstance = typeof(DevicesDiscoveryServer)
                            .GetProperty("Instance",
                                BindingFlags.NonPublic | BindingFlags.Static)?
                            .GetValue(null);

                        if (discoveryInstance != null)
                        {
                            var defaultInfoProperty = discoveryInstance.GetType()
                                .GetProperty("DefaultDeviceInfo");
                            if (defaultInfoProperty?.GetValue(discoveryInstance) is DeviceInfo info &&
                                info.Device.IsSameDevice(locator))
                            {
                                return info;
                            }
                        }

                        return new DeviceInfo
                        {
                            Device = locator,
                            SendTime = DateTime.UtcNow
                        };
                    }
                }

                Log.Warning("[BlockScriptGlobals] TryGetDevice: device not found: {DeviceName}", deviceName);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] TryGetDevice failed for {DeviceName}", deviceName);
                return null;
            }
        }
    }
}
