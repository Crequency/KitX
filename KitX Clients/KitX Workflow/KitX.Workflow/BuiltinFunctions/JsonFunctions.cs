using System.Linq;
using System.Text.Json;
using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    // ── Scalar extraction (land a JsonElement scalar into a typed PubVar) ──────────

    /// <summary>JsonAsString — coerce a JsonElement scalar to string (Package/List-Port §4.5).</summary>
    public class JsonAsStringFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonAsString";
        public string DisplayName => "JSON As String";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.String, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonAsString", args));
        }
    }

    /// <summary>JsonAsInt — coerce a JsonElement scalar to int.</summary>
    public class JsonAsIntFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonAsInt";
        public string DisplayName => "JSON As Int";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.Integer, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonAsInt", args));
        }
    }

    /// <summary>JsonAsBool — coerce a JsonElement scalar to bool.</summary>
    public class JsonAsBoolFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonAsBool";
        public string DisplayName => "JSON As Bool";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.Boolean, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonAsBool", args));
        }
    }

    // ── Array/object navigation (stay in Json-land) ───────────────────────────────

    /// <summary>JsonArrayLength — length of a JSON array (Package/List-Port §4.2).</summary>
    public class JsonArrayLengthFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonArrayLength";
        public string DisplayName => "JSON Array Length";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.Integer, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonArrayLength", args));
        }
    }

    /// <summary>JsonArrayAt — element at index of a JSON array, returned as Json (Package/List-Port §4.3).</summary>
    public class JsonArrayAtFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonArrayAt";
        public string DisplayName => "JSON Array At";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35), new("Index", PinType.Integer, 55)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.Json, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonArrayAt", args));
        }
    }

    /// <summary>JsonObjectKeys — the keys of a JSON object, returned as a JSON array of strings (§4.4).</summary>
    public class JsonObjectKeysFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonObjectKeys";
        public string DisplayName => "JSON Object Keys";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.Json, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonObjectKeys", args));
        }
    }

    /// <summary>JsonContains — whether a dotted path exists in a JSON value (§4.6).</summary>
    public class JsonContainsFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "JsonContains";
        public string DisplayName => "JSON Contains";
        public bool IsNonExtractable => false;
        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20), new("Json", PinType.Any, 35), new("Path", PinType.String, 55)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 20), new("Return", PinType.Boolean, 40)];
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var args = (stmt.Arguments ?? new List<string>()).Select(a => ctx.ResolveArgument(a)).ToArray();
            return ctx.EmitValueAssignment(stmt.PubVarTarget, ctx.GInvoke("JsonContains", args));
        }
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public string JsonAsString(object? json)
        {
            var el = json.AsJsonElement();
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "",
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => el.GetRawText()
            };
        }

        public int JsonAsInt(object? json)
        {
            var el = json.AsJsonElement();
            return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) ? i : 0;
        }

        public bool JsonAsBool(object? json)
        {
            var el = json.AsJsonElement();
            return el.ValueKind == JsonValueKind.True;
        }

        public int JsonArrayLength(object? json)
        {
            var el = json.AsJsonElement();
            return el.ValueKind == JsonValueKind.Array ? el.GetArrayLength() : 0;
        }

        public JsonElement JsonArrayAt(object? json, int index)
        {
            var el = json.AsJsonElement();
            if (el.ValueKind != JsonValueKind.Array) return default;
            var arr = el.EnumerateArray().ToList();
            return (index >= 0 && index < arr.Count) ? arr[index].Clone() : default;
        }

        public JsonElement JsonObjectKeys(object? json)
        {
            var el = json.AsJsonElement();
            if (el.ValueKind != JsonValueKind.Object) return default;
            var names = el.EnumerateObject().Select(p => p.Name).ToArray();
            return JsonSerializer.SerializeToElement(names);
        }

        public bool JsonContains(object? json, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var found = JsonGetField(json, path);
            return found.ValueKind != JsonValueKind.Undefined;
        }
    }
}
