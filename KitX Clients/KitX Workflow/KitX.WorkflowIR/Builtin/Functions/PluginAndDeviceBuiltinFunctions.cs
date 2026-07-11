namespace KitX.Workflow.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Ast;
using KitX.Workflow.Ir.Lowering;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// C-level builtins: plugin call, device lookup, and service-side effects.
//
// PluginCall / PluginCallWithTarget: their emit delegates to the type-aware helpers
// on CodeGenContext (PluginCallExpression / PluginCallWithTargetExpression) that the
// Phase 7 backend injects. The legacy runtime branched on `is RealPluginManager`
// type-sniffing; that is gone here — the runtime half is deferred to Phase 7, where
// it will be expressed as an abstract CallAuto on IPluginManager (or a new
// dispatch interface), eliminating the cast probe entirely.
//
// TryGetDevice: the only builtin with a custom ILoweringHandler — it must mint its
// own PubVar target when the call appears bare (not assigned), so its result is
// addressable. The new design does this through LoweringContext.Allocator instead
// of the legacy ForwardConversionState's exposed counter/list.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// PluginCall builtin — call a local plugin function. Value-producing (nestable).
/// BlockScript: <c>PluginCall("pluginName", "methodName"[, arg1, ...])</c>.
/// </summary>
public sealed class PluginCallFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "PluginCall";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("PluginName", PinType.String, 35),
        new("MethodName", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        // Return is PinType.Json (not Any): plugin methods return JSON payloads that
        // arrive as System.Text.Json.JsonElement (normalized by ExecutionGlobals.PluginCall
        // via AsJsonElement). This gives the return value a first-class type identity so
        // TypeInferer types the target PubVar as JsonElement, enabling the JSON builtin
        // family (JsonArrayAt/JsonGetField/...) to consume it without ad-hoc casts.
        // (List-Port-And-Json-Functions-Design.md §2.1, §3.1)
        new("Return", PinType.Json, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.FlatArguments(stmt);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var pluginName = args.Count > 0 ? args[0] : "";
        var methodName = args.Count > 1 ? args[1] : "";
        var rest = args.Skip(2).ToList();

        if (ctx.PluginCallExpression is { } build)
        {
            var call = build(pluginName, methodName, rest);
            // sourceType is "object": the runtime PluginCall returns object? (a boxed
            // JsonElement). When the target PubVar is typed JsonElement (via the return
            // pin's PinType.Json above), BuildValueAssignment emits ConvertTo<JsonElement>,
            // whose AsJsonElement branch normalizes the boxed value.
            foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "object"))
                yield return s;
        }
        // When PluginCallExpression is not wired (pre-Phase-7), emit nothing — the
        // backend is responsible for wiring it before emission runs.
    }
}

/// <summary>
/// PluginCallWithTarget builtin — call a plugin on a remote device. Value-producing.
/// BlockScript: <c>PluginCallWithTarget("plugin", "method", "targetDevice"[, args])</c>.
/// </summary>
public sealed class PluginCallWithTargetFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "PluginCallWithTarget";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("PluginName", PinType.String, 35),
        new("MethodName", PinType.String, 35),
        new("TargetDevice", PinType.Any, 40),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        // Return is PinType.Json — see PluginCallFunction.OutputPorts for rationale.
        new("Return", PinType.Json, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.FlatArguments(stmt);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var pluginName = args.Count > 0 ? args[0] : "";
        var methodName = args.Count > 1 ? args[1] : "";
        var targetDevice = args.Count > 2 ? args[2] : "";
        var rest = args.Skip(3).ToList();

        if (ctx.PluginCallWithTargetExpression is { } build)
        {
            var call = build(pluginName, methodName, rest, targetDevice);
            foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "object"))
                yield return s;
        }
    }
}

/// <summary>
/// TryGetDevice builtin — look up a connected device by name, returning a DeviceInfo.
/// Value-producing (the result feeds PluginCallWithTarget's targetDevice arg).
/// BlockScript: <c>TryGetDevice("DeviceName")</c>.
/// </summary>
/// <remarks>
/// The only builtin with a custom ILoweringHandler: a bare <c>TryGetDevice("x")</c>
/// (no assignment target) still needs an addressable result PubVar, so the lowering
/// mints one via the LoweringContext allocator. This replaces the legacy path that
/// reached into ForwardConversionState's exposed NextPubVarCounter / PubVarNames.
/// </remarks>
public sealed class TryGetDeviceFunction : IBuiltinFunction, ILoweringHandler, ICodeGenHandler
{
    public string Name => "TryGetDevice";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Pattern", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("DeviceInfo", PinType.Any, 40),
    ];

    public IReadOnlyList<IrStatement> LowerToIr(
        BSCall call, IReadOnlyList<string> expandedArgs, string? assignedVar, LoweringContext ctx)
    {
        // Mint a PubVar target when the call is bare, so the result is addressable.
        var pubVarTarget = !string.IsNullOrEmpty(assignedVar)
            ? assignedVar
            : ctx.Allocator.AllocateCapacitor();

        return
        [
            new IrPipelineStatement
            {
                Fingerprint = IrFingerprint.Compute("TryGetDevice", expandedArgs, pubVarTarget),
                Sources = [call.SourceText],
                Segments =
                [
                    new IrSegment
                    {
                        Kind = IrSegmentKind.FunctionCall,
                        FunctionName = "TryGetDevice",
                        Arguments = expandedArgs.Select(IrPipelineArgument.Lit).ToImmutableArray(),
                    },
                    new IrSegment { Kind = IrSegmentKind.Variable, VariableName = pubVarTarget },
                ],
            },
        ];
    }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.FlatArguments(stmt);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        if (args.Count == 0 || string.IsNullOrEmpty(assignedVar)) yield break;

        var call = ctx.GInvoke("TryGetDevice", ctx.ResolveArgument(args[0]));
        foreach (var s in ctx.EmitVarLocal(assignedVar, call))
            yield return s;
    }
}
