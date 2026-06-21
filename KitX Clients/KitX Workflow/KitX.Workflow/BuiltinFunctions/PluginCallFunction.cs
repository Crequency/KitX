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
    /// PluginCall 内置函数 — 调用本机插件函数。
    /// </summary>
    public class PluginCallFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "PluginCall";
        public string DisplayName => "PluginCall";
        public bool IsFlowControl => false;
        // PluginCall has a return value (OutputPin "Return") and is meant to be used as
        // an expression in argument position (e.g. Set("x", PluginCall("P","M",arg))).
        // IsNonExtractable=true would make BS2CFGConverter.ExpandExpression keep it inline
        // as a raw string instead of expanding it into a temp PubVar + standalone call,
        // which then fails to compile (CS0103 'PluginCall' undefined in generated C#).
        // false matches PluginCallWithTarget/Get/TryGetDevice — value-producing builtins
        // that CAN be nested. Set/Print/Pause stay true (pure side-effects, no return).
        public bool IsNonExtractable => false;
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

        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
            => ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.PluginCallExpression(stmt));

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
        {
            // PluginCall has variable arguments: PluginName, MethodName, plus optional data args.
            // The static descriptor declares 2 data pins (PluginName, MethodName); if the
            // statement carries more arguments (e.g. PluginCall(ui, "M", data)), add extra
            // input pins so DataEdgeBuilder can wire them into the node and the round-trip
            // preserves all arguments.
            var existingDataPins = node.InputPins.Count(p => p.Type != PinType.Execution);
            var needed = stmt.Arguments?.Count ?? 0;
            for (int i = existingDataPins; i < needed; i++)
            {
                node.InputPins.Add(new BlueprintPin
                {
                    Name = $"arg{i}",
                    Direction = PinDirection.Input,
                    Type = PinType.Any
                });
            }
            return node;
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            // Collect all non-Exec input pin values in pin order → PluginCall(arg0, arg1, ...)
            // Reverses the ConfigureNode + DataEdgeBuilder wiring to reconstruct the original
            // expression. Handles PubVar assignment when the Return output is consumed.
            var argPins = node.InputPins
                .Where(p => p.Type != PinType.Execution)
                .ToList();
            if (argPins.Count < 2) return null;

            var args = argPins
                .Select(p => helper.GetInputValue(node, p.Name))
                .ToList();

            var expr = $"{FunctionName}({string.Join(", ", args)})";

            var pubVar = helper.GetOutputPubVar(node, Return);
            var sourceCode = !string.IsNullOrEmpty(pubVar)
                ? $"{pubVar} = {expr};"
                : $"{expr};";

            return new ExpressionStatement
            {
                Expression = expr,
                SourceCode = sourceCode,
                LineNumber = 1
            };
        }

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
