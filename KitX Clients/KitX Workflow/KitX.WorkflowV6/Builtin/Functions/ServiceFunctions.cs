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
public sealed class StartPluginFunction : BuiltinFunctionBase
{
    public StartPluginFunction() : base("StartPlugin", FunctionKind.SideEffect,
        [new("PluginName", PinType.String, 20)],
        [new("Result", PinType.Boolean, 50)]) { }
}

/// <summary>StopPlugin — stops a plugin by name. SideEffect.</summary>
public sealed class StopPluginFunction : BuiltinFunctionBase
{
    public StopPluginFunction() : base("StopPlugin", FunctionKind.SideEffect,
        [new("PluginName", PinType.String, 20)],
        [new("Result", PinType.Boolean, 50)]) { }
}

/// <summary>InstallPlugin — installs a plugin from a .kxp file. SideEffect.</summary>
public sealed class InstallPluginFunction : BuiltinFunctionBase
{
    public InstallPluginFunction() : base("InstallPlugin", FunctionKind.SideEffect,
        [new("KxpPath", PinType.String, 20)],
        [new("Result", PinType.Boolean, 50)]) { }
}

/// <summary>GetPluginInfoByName — gets plugin info as JSON string. Pure.</summary>
public sealed class GetPluginInfoByNameFunction : BuiltinFunctionBase
{
    public GetPluginInfoByNameFunction() : base("GetPluginInfoByName", FunctionKind.Pure,
        [new("PluginName", PinType.String, 20)],
        [new("Result", PinType.String, 50)]) { }
}

/// <summary>ListPluginNames — lists all plugin names as JSON array string. Pure.</summary>
public sealed class ListPluginNamesFunction : BuiltinFunctionBase
{
    public ListPluginNamesFunction() : base("ListPluginNames", FunctionKind.Pure,
        [],
        [new("Result", PinType.String, 50)]) { }
}
