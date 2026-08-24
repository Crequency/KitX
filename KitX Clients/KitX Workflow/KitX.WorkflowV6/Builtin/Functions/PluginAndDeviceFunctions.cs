namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// Plugin and device invocation builtins.
//
// These functions bridge the workflow to the KitX plugin ecosystem via
// IPluginHost (injected into ExecutionGlobals at runtime). When PluginHost
// is null, all calls return defaults (null/false) — the workflow runs without
// a host, plugin calls simply produce no results.
//
// PluginCall returns PinType.Json (JsonElement) so the result can be directly
// consumed by the JSON function family (JsonAsString/JsonAsInt/JsonGetField/...).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// PluginCall — invokes a method on a local plugin. SideEffect: produces a Json result.
/// Extra pipeline arguments are appended as params object[] args.
/// </summary>
public sealed class PluginCallFunction : BuiltinFunctionBase
{
    public PluginCallFunction() : base("PluginCall", FunctionKind.SideEffect,
        [new("PluginName", PinType.String, 20), new("MethodName", PinType.String, 35)],
        [new("Return", PinType.Json, 50)],
        new("Param ", 3, PinType.Any)) { }
}

/// <summary>
/// PluginNotify — sends a plugin method invocation WITHOUT waiting for a response.
/// Intended for void/side-effect plugin functions (popups, notifications, device
/// actions) so the workflow continues immediately instead of blocking on the
/// plugin's response channel.
/// </summary>
public sealed class PluginNotifyFunction : BuiltinFunctionBase
{
    public PluginNotifyFunction() : base("PluginNotify", FunctionKind.SideEffect,
        [new("PluginName", PinType.String, 20), new("MethodName", PinType.String, 35)],
        [],
        new("Param ", 3, PinType.Any)) { }
}

/// <summary>
/// PluginCallWithTarget — invokes a method on a plugin running on a target device.
/// SideEffect: produces a Json result.
/// </summary>
public sealed class PluginCallWithTargetFunction : BuiltinFunctionBase
{
    public PluginCallWithTargetFunction() : base("PluginCallWithTarget", FunctionKind.SideEffect,
        [new("PluginName", PinType.String, 20), new("MethodName", PinType.String, 35), new("TargetDevice", PinType.Any, 50)],
        [new("Return", PinType.Json, 50)],
        new("Param ", 4, PinType.Any)) { }
}

/// <summary>
/// TryGetDevice — finds an online device by name. Pure: returns the device handle (Any)
/// or null if not found.
/// </summary>
public sealed class TryGetDeviceFunction : BuiltinFunctionBase
{
    public TryGetDeviceFunction() : base("TryGetDevice", FunctionKind.Pure,
        [new("DeviceName", PinType.String, 20)],
        [new("Return", PinType.Any, 50)]) { }
}
