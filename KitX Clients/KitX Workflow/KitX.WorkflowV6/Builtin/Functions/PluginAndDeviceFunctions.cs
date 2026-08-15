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
public sealed class PluginCallFunction : IBuiltinFunction
{
    public string Name => "PluginCall";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("PluginName", PinType.String, 20),
        new("MethodName", PinType.String, 35),
    ];

    /// <summary>Extra pipeline args append as variadic params (`PluginCall(p, m, a, b, ...)`).</summary>
    public VariadicPinSpec? InputVariadic => new("Param ", 3, PinType.Any);

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>
/// PluginNotify — sends a plugin method invocation WITHOUT waiting for a response.
/// Intended for void/side-effect plugin functions (popups, notifications, device
/// actions) so the workflow continues immediately instead of blocking on the
/// plugin's response channel.
/// </summary>
public sealed class PluginNotifyFunction : IBuiltinFunction
{
    public string Name => "PluginNotify";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("PluginName", PinType.String, 20),
        new("MethodName", PinType.String, 35),
    ];

    /// <summary>Extra pipeline args append as variadic params (`PluginNotify(p, m, a, b, ...)`).</summary>
    public VariadicPinSpec? InputVariadic => new("Param ", 3, PinType.Any);

    public IReadOnlyList<PortSpec> OutputPorts => [];
}

/// <summary>
/// PluginCallWithTarget — invokes a method on a plugin running on a target device.
/// SideEffect: produces a Json result.
/// </summary>
public sealed class PluginCallWithTargetFunction : IBuiltinFunction
{
    public string Name => "PluginCallWithTarget";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("PluginName", PinType.String, 20),
        new("MethodName", PinType.String, 35),
        new("TargetDevice", PinType.Any, 50),
    ];

    /// <summary>Extra pipeline args append as variadic params.</summary>
    public VariadicPinSpec? InputVariadic => new("Param ", 4, PinType.Any);

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Json, 50),
    ];
}

/// <summary>
/// TryGetDevice — finds an online device by name. Pure: returns the device handle (Any)
/// or null if not found.
/// </summary>
public sealed class TryGetDeviceFunction : IBuiltinFunction
{
    public string Name => "TryGetDevice";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("DeviceName", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Return", PinType.Any, 50),
    ];
}
