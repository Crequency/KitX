using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Device;
using KitX.Core.Device;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;
using KitX.Shared.CSharp.Device;
using Serilog;
using Microsoft.Extensions.DependencyInjection;

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
        public CFGStatementKind StatementKind => CFGStatementKind.TryGetDevice;
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

        public List<CFGStatement> FormatInvocation(
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

            return [new CFGStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.TryGetDevice,
                FunctionName = FunctionName,
                PubVarTarget = pubVarTarget,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
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
        /// 使用 IDeviceServer.GetSignedInDevices() 查找匹配的 DeviceLocator。
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
                if (!DI.ServiceHost.IsInitialized)
                {
                    Log.Warning("[BlockScriptGlobals] TryGetDevice: ServiceHost not initialized");
                    return null;
                }

                var deviceServer = DI.ServiceHost.GetRequiredService<IDeviceServer>();
                var discoveryService = DI.ServiceHost.GetRequiredService<IDeviceDiscoveryService>();

                var signedInDevices = deviceServer.GetSignedInDevices();
                
                foreach (var locator in signedInDevices)
                {
                    if (locator.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("[BlockScriptGlobals] TryGetDevice: found device {DeviceName} (IPv4={IPv4})",
                            locator.DeviceName, locator.IPv4);

                        var defaultInfo = discoveryService.DefaultDeviceInfo;
                        if (defaultInfo != null && defaultInfo.Device.IsSameDevice(locator))
                        {
                            return defaultInfo;
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
