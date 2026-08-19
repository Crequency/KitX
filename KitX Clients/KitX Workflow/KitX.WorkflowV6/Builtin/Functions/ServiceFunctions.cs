namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// Service management builtins — plugin lifecycle, plugin installation, and
// ecosystem queries.
//
// The v5 workflow-lifecycle builtins (StopWorkflow / CreateWorkflow /
// RunWorkflow / ListWorkflows) were retired in the B5+B6+B7 cleanup — the v6 IR
// architecture has no run-by-id service, so those four descriptors were removed
// from the palette along with their IPluginHost members.
//
// All functions delegate to IPluginHost (injected into ExecutionGlobals). When
// PluginHost is null, bool functions return false and string functions return "".
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>StartPlugin — starts a plugin by name. SideEffect.</summary>
public sealed class StartPluginFunction : IBuiltinFunction
{
    public string Name => "StartPlugin";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("PluginName", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];
}

/// <summary>StopPlugin — stops a plugin by name. SideEffect.</summary>
public sealed class StopPluginFunction : IBuiltinFunction
{
    public string Name => "StopPlugin";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("PluginName", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];
}

/// <summary>InstallPlugin — installs a plugin from a .kxp file. SideEffect.</summary>
public sealed class InstallPluginFunction : IBuiltinFunction
{
    public string Name => "InstallPlugin";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("KxpPath", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];
}

/// <summary>GetPluginInfoByName — gets plugin info as JSON string. Pure.</summary>
public sealed class GetPluginInfoByNameFunction : IBuiltinFunction
{
    public string Name => "GetPluginInfoByName";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("PluginName", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.String, 50),
    ];
}

/// <summary>ListPluginNames — lists all plugin names as JSON array string. Pure.</summary>
public sealed class ListPluginNamesFunction : IBuiltinFunction
{
    public string Name => "ListPluginNames";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.String, 50),
    ];
}
