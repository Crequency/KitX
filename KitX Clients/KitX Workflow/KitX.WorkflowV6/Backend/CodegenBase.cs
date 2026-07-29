namespace KitX.WorkflowV6.Backend;

using System.Text;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

internal abstract class CodegenBase
{
    protected readonly StringBuilder _sb = new();
    protected int _indent;
    protected Workflow _ir = null!;
    protected HashSet<string> _helperNames = new(StringComparer.Ordinal);
    protected int _pipeCounter;
    protected readonly BuiltinFunctionRegistry _registry;

    protected static readonly Dictionary<string, string> BuiltinToGMethod = new(StringComparer.Ordinal)
    {
        ["Print"] = "Print",
        ["Range"] = "Range",
        ["StringConcat"] = "StringConcatMethod",
    };

    protected CodegenBase(BuiltinFunctionRegistry registry)
    {
        _registry = registry;
    }

    protected void EmitLine(string line)
    {
        _sb.Append(' ', _indent * 4);
        _sb.AppendLine(line);
    }

    protected void Emit(string text) => _sb.Append(text);

    protected void Indent() => _indent++;
    protected void Dedent() => _indent--;

    protected string MapMethodName(string name)
        => BuiltinToGMethod.GetValueOrDefault(name, name);

    protected string RenderKsNode(KsNode node) => node switch
    {
        KsLiteral lit => RenderLiteral(lit),
        KsIdentifier id => RenderIdentifier(id),
        KsCall call => call.Args.Length == 0
            ? $"this.{MapMethodName(call.MethodName)}()"
            : $"this.{MapMethodName(call.MethodName)}({string.Join(", ", call.Args.Select(RenderKsNode))})",
        KsPipeline pipe => RenderPipelineAsExpression(pipe),
        KsPipelineSegment seg => seg.IsVariableTap
            ? seg.Target
            : $"this.{MapMethodName(seg.Target)}({string.Join(", ", seg.Args.Select(RenderKsNode))})",
        KsPlaceholder => "_placeholder_",
        _ => $"/* {node.GetType().Name} */",
    };

    protected string RenderLiteral(KsLiteral lit) => lit.Kind switch
    {
        KsLiteralKind.String => $"\"{EscapeString(lit.Value?.ToString() ?? "")}\"",
        KsLiteralKind.Integer => lit.Value?.ToString() ?? "0",
        KsLiteralKind.Double => (lit.Value?.ToString() ?? "0.0") + "d",
        KsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        KsLiteralKind.Char => $"'{lit.Value}'",
        KsLiteralKind.Null => "null",
        _ => "null",
    };

    protected static string EscapeString(string s)
        => s.Replace("\\", "\\\\")
             .Replace("\"", "\\\"")
             .Replace("\n", "\\n")
             .Replace("\r", "\\r")
             .Replace("\t", "\\t")
             .Replace("\0", "\\0");

    protected string RenderIdentifier(KsIdentifier id)
    {
        if (_ir.Constants.TryGetValue(id.Name, out var c))
        {
            if (c.InitialValueExpression is not null)
                return c.InitialValueExpression;
            // A const declared with a dict literal carries DictInitializer instead of text.
            // Inline its structured initialiser so a reference (e.g. as another decl's value)
            // does not emit `this.name` — which is invalid in a C# field-initialiser context.
            if (c.DictInitializer is not null)
                return RenderDictInitializer(c.DictInitializer);
        }
        return IsLocal(id.Name) ? id.Name : $"this.{id.Name}";
    }

    protected virtual string RenderForEachSource(KsNode source)
    {
        if (source is KsCall call && call.MethodName == "Range")
        {
            return $"this.Range({string.Join(", ", call.Args.Select(RenderKsNode))})";
        }
        return RenderKsNode(source);
    }

    protected string RenderPipelineAsExpression(KsPipeline pipe)
    {
        if (pipe.Segments.Length == 0)
        {
            return pipe.Sources.Length > 0 ? RenderKsNode(pipe.Sources[0]) : "true";
        }

        string currentExpr = "";
        for (int i = 0; i < pipe.Segments.Length; i++)
        {
            var seg = pipe.Segments[i];
            IEnumerable<string> inputArgs = i == 0
                ? pipe.Sources.Select(RenderKsNode)
                : [currentExpr];
            var allArgs = BuildArgList(seg.Args, inputArgs);
            currentExpr = $"this.{MapMethodName(seg.Target)}({allArgs})";
        }
        return currentExpr;
    }

    protected string RenderCallStatement(KsCall call)
    {
        var args = string.Join(", ", call.Args.Select(RenderKsNode));
        return $"this.{MapMethodName(call.MethodName)}({args})";
    }

    protected string BuildArgList(ImmutableArray<KsNode> args, IEnumerable<string> inputs)
    {
        var queue = new Queue<string>(inputs);
        var result = new List<string>();
        foreach (var arg in args)
        {
            if (arg is KsPlaceholder)
                result.Add(queue.Count > 0 ? queue.Dequeue() : "null");
            else
                result.Add(RenderKsNode(arg));
        }
        while (queue.Count > 0)
            result.Add(queue.Dequeue());
        return string.Join(", ", result);
    }

    // Local-name tracking uses reference counting so that nested scopes binding the
    // same name (e.g. `forEach ... as i:` inside another `forEach ... as i:`) push/pop
    // correctly — the outer binding survives the inner scope's pop.
    private readonly Dictionary<string, int> _localNameCounts = new(StringComparer.Ordinal);

    protected void PushLocal(string name)
    {
        _localNameCounts[name] = _localNameCounts.GetValueOrDefault(name) + 1;
    }
    protected void PopLocal(string name)
    {
        if (!_localNameCounts.TryGetValue(name, out var count)) return;
        if (count <= 1) _localNameCounts.Remove(name);
        else _localNameCounts[name] = count - 1;
    }
    protected bool IsLocal(string name) => _localNameCounts.ContainsKey(name);

    public abstract string Generate(Workflow ir, LoweringResult? lowering, bool hasDebugger = false);

    protected abstract void EmitPipeline(PipelineStatement p, int ordinal, int depth);

    protected virtual void EmitCheckpoint(string stmtId, string lexicalPath, int ordinal) { }

    protected virtual void EmitClassHeader(Workflow ir, LoweringResult? lowering)
    {
        EmitLine("using System;");
        EmitLine("using System.Collections.Generic;");
        EmitLine("using KitX.WorkflowV6.Backend.Runtime;");
        EmitLine("");
        EmitLine("namespace KitX.WorkflowV6.Generated;");
        EmitLine("");
        EmitLine("public sealed class G : ExecutionGlobals");
        EmitLine("{");
        _indent++;

        if (lowering is not null)
        {
            foreach (var (name, type) in lowering.PubVarTypes)
            {
                var csharpType = KsTypeToCSharp(type);
                var init = RenderDeclInitializer(name, ir);
                EmitLine($"public {csharpType} {name}{init};");
            }
        }
        EmitLine("");
    }

    /// <summary>Maps a KS type keyword to its C# type name. Non-mapped types pass through.</summary>
    protected static string KsTypeToCSharp(string ksType) => ksType switch
    {
        "dict" => "Dictionary<string, object?>",
        _ => ksType,
    };

    /// <summary>
    /// Renders the C# field-initialiser fragment for a declared PubVar/Const, when it carries
    /// a structured dict-literal initialiser (Package/Dict-Type-Design.md §2.1). Returns ""
    /// when there is no dict initialiser (the legacy <c>InitialValueExpression</c> text path is
    /// not emitted as a C# field initialiser — it is consumed elsewhere).
    /// </summary>
    protected string RenderDeclInitializer(string name, Workflow ir)
    {
        KsDictLiteral? dictInit = null;
        if (ir.GlobalVars.TryGetValue(name, out var g) && g.DictInitializer is not null)
            dictInit = g.DictInitializer;
        else if (ir.Constants.TryGetValue(name, out var c) && c.DictInitializer is not null)
            dictInit = c.DictInitializer;

        return dictInit is { } dl ? " = " + RenderDictInitializer(dl) : "";
    }

    /// <summary>Renders a KsDictLiteral as a C# Dictionary collection initialiser.</summary>
    protected string RenderDictInitializer(KsDictLiteral dict)
    {
        var sb = new StringBuilder();
        sb.Append("new() {");
        bool first = true;
        foreach (var entry in dict.Entries)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(" [");
            sb.Append(RenderKsNode(entry.Key));
            sb.Append("] = ");
            sb.Append(RenderKsNode(entry.Value));
        }
        sb.Append(" }");
        return sb.ToString();
    }

    protected virtual void EmitClassFooter()
    {
        _indent--;
        EmitLine("}");
    }
}
