namespace KitX.WorkflowIR.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// Service-side builtins: plugin lifecycle, workflow lifecycle, and queries.
//
// These all call into host services and produce a status/JSON return. Per the
// unified rule for the C-level set: they are SideEffect (mutating or service-touching)
// but keep a Return output pin when they yield a meaningful status value, because the
// Kind (SideEffect) now drives the non-extractable semantic — the legacy
// IsNonExtractable flag is gone. Value-producing query builtins (CreateWorkflow,
// GetPluginInfoByName, ListPluginNames, ListWorkflows, RunWorkflow, InstallPlugin)
// are Pure where they only read/return a value.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>StartPlugin builtin — start an installed plugin by name. SideEffect.</summary>
public sealed class StartPluginFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "StartPlugin";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("PluginName", PinType.String, 35),
    ];

    // Return pin kept: it carries the start success status (a real value the program reads).
    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.Boolean, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("StartPlugin", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "bool"))
            yield return s;
    }
}

/// <summary>StopPlugin builtin — stop a running plugin by name. SideEffect.</summary>
public sealed class StopPluginFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "StopPlugin";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("PluginName", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.Boolean, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("StopPlugin", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "bool"))
            yield return s;
    }
}

/// <summary>StopWorkflow builtin — stop a running workflow by id. SideEffect.</summary>
public sealed class StopWorkflowFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "StopWorkflow";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("WorkflowId", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.Boolean, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("StopWorkflow", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "bool"))
            yield return s;
    }
}

/// <summary>CreateWorkflow builtin — persist a new workflow, return its id. Pure.</summary>
public sealed class CreateWorkflowFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "CreateWorkflow";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Name", PinType.String, 35),
        new("Source", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.String, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("CreateWorkflow", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "string"))
            yield return s;
    }
}

/// <summary>RunWorkflow builtin — start a workflow by id, return success. Pure.</summary>
public sealed class RunWorkflowFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "RunWorkflow";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("WorkflowId", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.Boolean, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("RunWorkflow", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "bool"))
            yield return s;
    }
}

/// <summary>InstallPlugin builtin — import a .kxp plugin package. Pure.</summary>
public sealed class InstallPluginFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "InstallPlugin";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("KxpPath", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.Boolean, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("InstallPlugin", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "bool"))
            yield return s;
    }
}

/// <summary>GetPluginInfoByName builtin — return a plugin's info as JSON. Pure.</summary>
public sealed class GetPluginInfoByNameFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "GetPluginInfoByName";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("PluginName", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.String, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("GetPluginInfoByName", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "string"))
            yield return s;
    }
}

/// <summary>ListPluginNames builtin — return installed plugin names as a JSON array. Pure.</summary>
public sealed class ListPluginNamesFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "ListPluginNames";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [new("Exec", PinType.Execution, 20)];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.String, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("ListPluginNames", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "string"))
            yield return s;
    }
}

/// <summary>ListWorkflows builtin — return known workflows as JSON. Pure.</summary>
public sealed class ListWorkflowsFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "ListWorkflows";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts => [new("Exec", PinType.Execution, 20)];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.String, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("ListWorkflows", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "string"))
            yield return s;
    }
}
