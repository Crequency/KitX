namespace KitX.WorkflowIR.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// JSON + StringConcat builtins.
//
// The 7 JSON functions share an identical emit shape — G.<Name>(args) assigned to a
// PubVar — so they derive from a tiny JsonBuiltinBase that implements ICodeGenHandler
// once. Each subclass only declares its identity + ports (a data-driven table would
// also work, but seven distinct classes keep each function individually addressable
// in the registry and individually documentable, matching the task's guidance).
//
// StringConcat is the input-variadic builtin (N string inputs → one string). Its
// emit is the same value-assignment shape; the variadic growth is declared via
// InputVariadic so the editor's generic logic drives pin expansion.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared emit logic for the JSON builtins: <c>G.&lt;Name&gt;(args)</c> assigned to the
/// statement's PubVar target. All seven JSON functions reuse this.
/// </summary>
public abstract class JsonBuiltinBase : IBuiltinFunction, ICodeGenHandler
{
    public abstract string Name { get; }
    public FunctionKind Kind => FunctionKind.Pure;
    public abstract IReadOnlyList<PortSpec> InputPorts { get; }
    public abstract IReadOnlyList<PortSpec> OutputPorts { get; }

    /// <summary>The C# return type of the G.&lt;Name&gt;(...) call, used for typed locals.</summary>
    protected abstract string ReturnTypeName { get; }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke(Name, args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, ReturnTypeName))
            yield return s;
    }
}

/// <summary>JsonAsString — coerce a JsonElement scalar to string.</summary>
public sealed class JsonAsStringFunction : JsonBuiltinBase
{
    public override string Name => "JsonAsString";
    protected override string ReturnTypeName => "string";
    public override IReadOnlyList<PortSpec> InputPorts =>
        [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.String, 40)];
}

/// <summary>JsonAsInt — coerce a JsonElement scalar to int.</summary>
public sealed class JsonAsIntFunction : JsonBuiltinBase
{
    public override string Name => "JsonAsInt";
    protected override string ReturnTypeName => "int";
    public override IReadOnlyList<PortSpec> InputPorts =>
        [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Integer, 40)];
}

/// <summary>JsonAsBool — coerce a JsonElement scalar to bool.</summary>
public sealed class JsonAsBoolFunction : JsonBuiltinBase
{
    public override string Name => "JsonAsBool";
    protected override string ReturnTypeName => "bool";
    public override IReadOnlyList<PortSpec> InputPorts =>
        [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Boolean, 40)];
}

/// <summary>JsonArrayLength — length of a JSON array.</summary>
public sealed class JsonArrayLengthFunction : JsonBuiltinBase
{
    public override string Name => "JsonArrayLength";
    protected override string ReturnTypeName => "int";
    public override IReadOnlyList<PortSpec> InputPorts =>
        [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Integer, 40)];
}

/// <summary>JsonArrayAt — element at index of a JSON array, returned as Json.</summary>
public sealed class JsonArrayAtFunction : JsonBuiltinBase
{
    public override string Name => "JsonArrayAt";
    protected override string ReturnTypeName => "object";
    public override IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Json", PinType.Any, 35),
        new("Index", PinType.Integer, 55),
    ];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Json, 40)];
}

/// <summary>JsonObjectKeys — the keys of a JSON object, returned as a JSON array of strings.</summary>
public sealed class JsonObjectKeysFunction : JsonBuiltinBase
{
    public override string Name => "JsonObjectKeys";
    protected override string ReturnTypeName => "object";
    public override IReadOnlyList<PortSpec> InputPorts =>
        [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Json, 40)];
}

/// <summary>JsonGetField — navigate a JSON value by dotted path, returning a Json element.</summary>
public sealed class JsonGetFieldFunction : JsonBuiltinBase
{
    public override string Name => "JsonGetField";
    protected override string ReturnTypeName => "object";
    public override IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Json", PinType.Any, 35),
        new("FieldPath", PinType.String, 35),
    ];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Json, 40)];
}

/// <summary>JsonContains — whether a dotted path exists in a JSON value.</summary>
public sealed class JsonContainsFunction : JsonBuiltinBase
{
    public override string Name => "JsonContains";
    protected override string ReturnTypeName => "bool";
    public override IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Json", PinType.Any, 35),
        new("Path", PinType.String, 55),
    ];
    public override IReadOnlyList<PortSpec> OutputPorts =>
        [new("Exec", PinType.Execution, 20), new("Return", PinType.Boolean, 40)];
}

/// <summary>
/// StringConcat builtin — concatenate N string inputs into one. Input-variadic: the
/// editor auto-appends a new "Input {N}" String pin when the last is connected.
/// BlockScript: <c>StringConcat(part1, part2, ...)</c>.
/// </summary>
public sealed class StringConcatFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "StringConcat";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("A", PinType.String, 35),
        new("B", PinType.String, 55),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Result", PinType.String, 40),
    ];

    /// <summary>Input variadic: append "Input 3", "Input 4", ... when the last is connected.</summary>
    public VariadicPinSpec? InputVariadic => new("Input ", 3, PinType.String);

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        // G.StringConcat(arg1, arg2, ...) — argument count is dynamic.
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var concatExpr = ctx.GInvoke("StringConcat", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, concatExpr, "string"))
            yield return s;
    }
}
