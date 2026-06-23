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
    /// PluginCallWithTarget 内置函数 — 调用远程设备上的插件。
    /// 语法: PluginCallWithTarget("pluginName", "methodName", "targetDevice"[, arg1, arg2, ...]);
    /// targetDevice 可以是设备名（字符串）或 TryGetDevice("pattern") 的返回值（DeviceInfo）。
    /// </summary>
    public class PluginCallWithTargetFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "PluginCallWithTarget";
        public string DisplayName => "PluginCallWithTarget";
        public bool IsNonExtractable => false;
        public BuiltinNodeKind NodeKind => BuiltinNodeKind.Call;
        // Preserve prior early-route behavior: identical cross-device calls dedup to one node.
        public string? GetReuseKey(CFGStatement stmt) => stmt.Fingerprint;

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

        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
            => ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.PluginCallWithTargetExpression(stmt));

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
        {
            if (node is CallNode call)
            {
                // Use stmt.Arguments directly — it contains the properly expanded argument strings
                // from the BS2CFGConverter's ExpandArguments step. First 3 args are
                // plugin name, method name, target device; remaining are extra args.
                var allArgs = stmt.Arguments;
                if (allArgs != null && allArgs.Count >= 3)
                {
                    call.PluginName = StripQuotes(allArgs[0]);
                    call.FunctionName = StripQuotes(allArgs[1]);
                    call.TargetDevice = StripQuotes(allArgs[2]);
                    if (allArgs.Count > 3)
                        call.ExtraArguments = allArgs.Skip(3).ToList();
                }
            }
            return node;
        }

        /// <summary>
        /// Parses PluginCallWithTarget argument string to extract the first 3 string literal arguments.
        /// Handles nested parentheses and quoted strings.
        /// </summary>
        private static List<string> ParsePluginCallArguments(string argsContent)
        {
            var result = new List<string>();
            int i = 0;
            int argCount = 0;

            while (i < argsContent.Length && argCount < 3)
            {
                // Skip whitespace
                while (i < argsContent.Length && char.IsWhiteSpace(argsContent[i])) i++;
                if (i >= argsContent.Length) break;

                char c = argsContent[i];
                if (c == '"')
                {
                    // String literal
                    int start = i;
                    i++;
                    while (i < argsContent.Length)
                    {
                        if (argsContent[i] == '\\' && i + 1 < argsContent.Length)
                            i += 2; // Skip escaped char
                        else if (argsContent[i] == '"')
                        {
                            i++;
                            break;
                        }
                        else
                            i++;
                    }
                    result.Add(argsContent[(start + 1)..(i - 1)]);  // Extract content between quotes (exclude both '"')
                    argCount++;
                }
                else if (c == '(')
                {
                    // Nested call - skip to matching ')'
                    int depth = 1;
                    i++;
                    while (i < argsContent.Length && depth > 0)
                    {
                        if (argsContent[i] == '(') depth++;
                        else if (argsContent[i] == ')') depth--;
                        i++;
                    }
                }
                else
                {
                    // Identifier or other - skip to comma or end
                    while (i < argsContent.Length && argsContent[i] != ',') i++;
                    // Don't count as an argument (not a string literal)
                }

                // Skip to next comma
                while (i < argsContent.Length && argsContent[i] != ',') i++;
                if (i < argsContent.Length && argsContent[i] == ',') i++;
            }

            return result;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            if (node is not CallNode call) return null;

            var pluginNameLit = $"\"{call.PluginName}\"";
            var methodNameLit = $"\"{call.FunctionName}\"";
            var targetDeviceExpr = !string.IsNullOrEmpty(call.TargetDevice)
                ? $"\"{call.TargetDevice}\""
                : "null";

            // Use ExtraArguments if available (set by ConfigureNode), otherwise fall back to InputPins
            List<string> extraArgs;
            if (call.ExtraArguments.Count > 0)
            {
                extraArgs = call.ExtraArguments;
            }
            else
            {
                extraArgs = new List<string>();
                var extraArgPins = call.InputPins
                    .Where(p => p.Direction == PinDirection.Input && p.Name != "Exec")
                    .Skip(3)
                    .ToList();
                foreach (var pin in extraArgPins)
                {
                    extraArgs.Add(helper.GetInputValue(call, pin.Name));
                }
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

namespace KitX.Workflow.BlockScripting
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
