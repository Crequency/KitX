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

    protected string RenderKsNode(KsNode node) => node switch
    {
        KsLiteral lit => RenderLiteral(lit),
        KsIdentifier id => RenderIdentifier(id),
        KsCall call => call.Args.Length == 0
            ? $"this.{call.MethodName}()"
            : $"this.{call.MethodName}({string.Join(", ", call.Args.Select(RenderKsNode))})",
        KsPipeline pipe => RenderPipelineAsExpression(pipe),
        KsPipelineSegment seg => seg.IsVariableTap
            ? seg.Target
            : $"this.{seg.Target}({string.Join(", ", seg.Args.Select(RenderKsNode))})",
        KsPlaceholder => "_placeholder_",
        _ => throw new InvalidOperationException($"Unknown KS node type: {node.GetType().Name}"),
    };

    protected string RenderLiteral(KsLiteral lit)
    {
        // KS text and C# literal syntax agree for strings/chars (same escape table), so
        // the shared codec renders them; doubles additionally carry the C# `d` suffix.
        var text = KsScalarLiteralCodec.Encode(lit);
        return lit.Kind == KsLiteralKind.Double ? text + "d" : text;
    }

    protected string RenderIdentifier(KsIdentifier id)
    {
        if (_ir.Constants.TryGetValue(id.Name, out var c))
        {
            // A dict const carries a structured DictInitializer — inline its C# Dictionary
            // construction. This MUST take priority over InitialValueExpression: after a BP
            // round-trip the text field may hold the JSON payload, not a valid C# expression.
            if (c.DictInitializer is not null)
                return RenderDictInitializer(c.DictInitializer);
            if (c.InitialValueExpression is not null)
                return c.InitialValueExpression;
        }
        return IsLocal(id.Name) ? id.Name : $"this.{id.Name}";
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
            currentExpr = $"this.{seg.Target}({allArgs})";
        }
        return currentExpr;
    }

    protected string RenderCallStatement(KsCall call)
    {
        var args = string.Join(", ", call.Args.Select(RenderKsNode));
        return $"this.{call.MethodName}({args})";
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

    /// <summary>
    /// Emits the user-defined helper functions as public methods on the generated G
    /// class (shared by both codegen paths — Run and Debug must produce the same G
    /// surface, otherwise debug runs fail with CS1061 for every helper call).
    /// </summary>
    protected void EmitHelperFunctions(Workflow ir)
    {
        if (ir.HelperFunctions.IsDefault || ir.HelperFunctions.Length == 0) return;
        EmitLine("");
        foreach (var func in ir.HelperFunctions)
        {
            var paramList = string.Join(", ",
                func.Parameters.Select(p => $"{p.Type} {p.Name}"));
            EmitLine($"public {func.ReturnType} {func.Name}({paramList})");
            EmitLine("{");
            Indent();
            if (!string.IsNullOrWhiteSpace(func.Code))
            {
                foreach (var codeLine in func.Code.Split('\n'))
                    EmitLine(codeLine.TrimEnd());
            }
            else
            {
                EmitLine($"return default({func.ReturnType});");
            }
            Dedent();
            EmitLine("}");
            EmitLine("");
        }
    }

    protected abstract void EmitPipeline(PipelineStatement p, string stmtPath);

    protected virtual void EmitCheckpoint(string stmtId, string lexicalPath) { }

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

        // Generate strongly-typed fields. PubVarTypes (from TypeInferer) is the
        // primary source, but it may be incomplete when lowering is null (BP mode)
        // or when ScriptCompiler's fallback path is used. Fall back to ir.Constants
        // and ir.GlobalVars directly so no declared variable is ever missing.
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        if (lowering is not null)
        {
            foreach (var (name, type) in lowering.PubVarTypes)
            {
                if (emitted.Add(name))
                    EmitLine($"public {KsTypeToCSharp(type)} {name}{RenderDeclInitializer(name, ir)};");
            }
        }
        foreach (var (name, c) in ir.Constants)
        {
            if (emitted.Add(name))
                EmitLine($"public {KsTypeToCSharp(c.Type)} {name}{RenderDeclInitializer(name, ir)};");
        }
        foreach (var (name, g) in ir.GlobalVars)
        {
            if (emitted.Add(name))
                EmitLine($"public {KsTypeToCSharp(g.Type)} {name}{RenderDeclInitializer(name, ir)};");
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
    /// Renders the C# field-initialiser fragment for a declared PubVar/Const. Dict decls use the
    /// structured <see cref="KsDictLiteral"/>; scalar decls use their verbatim literal text
    /// (Package/Dict-Type-Design.md §2.1 — initialisers are literals only, so the text is valid
    /// C#). Returns "" when there is no initialiser.
    /// </summary>
    protected string RenderDeclInitializer(string name, Workflow ir)
    {
        if (ir.GlobalVars.TryGetValue(name, out var g))
        {
            if (g.DictInitializer is { } gdl) return " = " + RenderDictInitializer(gdl);
            if (g.InitialValueExpression is { Length: > 0 } gie) return " = " + gie;
        }
        if (ir.Constants.TryGetValue(name, out var c))
        {
            if (c.DictInitializer is { } cdl) return " = " + RenderDictInitializer(cdl);
            if (c.InitialValueExpression is { Length: > 0 } cie) return " = " + cie;
        }
        return "";
    }

    /// <summary>Renders a KsDictLiteral as a C# Dictionary collection initialiser.</summary>
    protected string RenderDictInitializer(KsDictLiteral dict)
    {
        var sb = new StringBuilder();
        // Explicit type (not `new()`): when inlined into an object?-typed argument position
        // (e.g. DictGetValue(dictRef, key) where dictRef is a const dict), target-type inference
        // would resolve `new()` to object — which doesn't support [] indexing (CS0021).
        sb.Append("new Dictionary<string, object?>() {");
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
