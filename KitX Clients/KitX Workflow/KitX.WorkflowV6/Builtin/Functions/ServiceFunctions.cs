namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// Service management builtins — plugin lifecycle, workflow lifecycle, plugin
// installation, and ecosystem queries.
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

/// <summary>StopWorkflow — stops a running workflow by ID. SideEffect.</summary>
public sealed class StopWorkflowFunction : IBuiltinFunction
{
    public string Name => "StopWorkflow";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("WorkflowId", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];
}

/// <summary>CreateWorkflow — creates a new workflow from source. Pure (value-producing).</summary>
public sealed class CreateWorkflowFunction : IBuiltinFunction
{
    public string Name => "CreateWorkflow";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Name", PinType.String, 20),
        new("Source", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.String, 50),
    ];
}

/// <summary>RunWorkflow — starts a workflow by ID. Pure (value-producing).</summary>
public sealed class RunWorkflowFunction : IBuiltinFunction
{
    public string Name => "RunWorkflow";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("WorkflowId", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.Boolean, 50),
    ];
}

/// <summary>InstallPlugin — installs a plugin from a .kxp file. Pure.</summary>
public sealed class InstallPluginFunction : IBuiltinFunction
{
    public string Name => "InstallPlugin";
    public FunctionKind Kind => FunctionKind.Pure;

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

/// <summary>ListWorkflows — lists all workflow IDs as JSON array string. Pure.</summary>
public sealed class ListWorkflowsFunction : IBuiltinFunction
{
    public string Name => "ListWorkflows";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Result", PinType.String, 50),
    ];
}
