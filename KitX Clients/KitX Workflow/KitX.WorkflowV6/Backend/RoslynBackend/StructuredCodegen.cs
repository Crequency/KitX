namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Text;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// StructuredCodegen — IR → structured C# source string (discussion notes §5.3).
//
// Walks the structured Statement tree and emits C# that mirrors the v6 BS exactly:
//   IfStatement      →  if (cond) { thenBody } else { elseBody }
//   ForEachStatement →  foreach (var item in source) { body }
//   WhileStatement   →  while (cond) { body }
//   BreakStatement   →  break;
//   ContinueStatement→  continue;
//   ExitStatement    →  return;
//   PipelineStatement →  side-effect: G.Print(value); / pure: var tmp = G.Range(...);
//
// No switch-case trampoline (v5's G.NextBlock), no block-name addressing. The
// generated C# is therefore optimisable by the C# compiler (inline, branch
// prediction, loop optimisation) — discussion notes §5.3.
//
// The generated class subclasses <see cref="Runtime.ExecutionGlobals"/> so strong-typed
// PubVars can be added as fields on it (§十二-F). The entry point is `RunAsync` which
// runs the top-level body; exit() maps to <c>return</c>.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// IR → structured C# source string. Pure: the same IR always yields the same C#.
/// </summary>
internal sealed class StructuredCodegen
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly StringBuilder _sb = new();
    private int _indent;
    private int _tempCounter;
    /// <summary>
    /// Names bound as local variables in the current scope (forEach item bindings,
    /// while-loop counters when allocated by codegen). These render as bare identifiers,
    /// NOT as `this.&lt;name&gt;` (PubVar field accesses). Populated by EmitBody when
    /// entering a forEach scope; cleared on scope exit.
    /// </summary>
    private readonly HashSet<string> _localNames = new();

    /// <summary>
    /// Maps a BS builtin function name to the C# method name on the G class (ExecutionGlobals).
    /// Most builtins keep their BS name (Print, Range); a few are renamed to keep the C# side
    /// terse (HelperFuncCompare → Compare, HelperFuncAdd → Add). Add to this map when a new
    /// builtin's BS name differs from its C# method name.
    /// </summary>
    private static readonly Dictionary<string, string> BuiltinToGMethod = new(StringComparer.Ordinal)
    {
        ["Print"] = "Print",
        ["Range"] = "Range",
        ["HelperFuncCompare"] = "Compare",
        ["HelperFuncAdd"] = "Add",
        ["StringConcat"] = "StringConcatMethod",  // avoid string.Concat static name clash
    };

    private static string MapBuiltinToGMethod(string bsName)
        => BuiltinToGMethod.TryGetValue(bsName, out var gName) ? gName : bsName;

    public StructuredCodegen(BuiltinFunctionRegistry registry) => _registry = registry;

    /// <summary>
    /// Generates a complete C# class source string from the IR. The class subclasses
    /// <see cref="Runtime.ExecutionGlobals"/> (named <c>G_Workflow</c>) and exposes a
    /// <c>RunAsync</c> entry point that runs the top-level body.
    /// </summary>
    public string Generate(Workflow ir, LoweringResult? lowering)
    {
        _sb.Clear();
        _indent = 0;
        _tempCounter = 0;

        // ── Class header ──
        EmitLine("using System;");
        EmitLine("using System.Collections.Generic;");
        EmitLine("using KitX.WorkflowV6.Backend.Runtime;");
        EmitLine("");
        EmitLine("namespace KitX.WorkflowV6.Generated;");
        EmitLine("");
        EmitLine("public sealed class G : ExecutionGlobals");
        EmitLine("{");

        _indent++;
        // ── Strong-typed PubVar fields (§十二-F) ──
        if (lowering is not null)
        {
            foreach (var (name, type) in lowering.PubVarTypes)
            {
                EmitLine($"public {type} {name};");
            }
        }
        EmitLine("");

        // ── RunAsync entry point ──
        EmitLine("public void RunAsync()");
        EmitLine("{");
        _indent++;
        EmitBody(ir.Body);
        _indent--;
        EmitLine("}");

        _indent--;
        EmitLine("}");
        return _sb.ToString();
    }

    private void EmitBody(ImmutableArray<Statement> body)
    {
        foreach (var s in body)
            EmitStatement(s);
    }

    private void EmitStatement(Statement stmt)
    {
        switch (stmt)
        {
            case PipelineStatement p:
                EmitPipeline(p);
                break;
            case IfStatement iff:
                EmitLine($"if ({RenderBsNode(iff.Condition)})");
                EmitLine("{");
                _indent++;
                EmitBody(iff.ThenBody);
                _indent--;
                if (iff.ElseBody.Length > 0)
                {
                    EmitLine("} else {");
                    _indent++;
                    EmitBody(iff.ElseBody);
                    _indent--;
                }
                EmitLine("}");
                break;
            case ForEachStatement fe:
                EmitLine($"foreach (var {fe.ItemName} in {RenderForEachSource(fe.Source)})");
                EmitLine("{");
                _indent++;
                _localNames.Add(fe.ItemName);
                EmitBody(fe.Body);
                _localNames.Remove(fe.ItemName);
                _indent--;
                EmitLine("}");
                break;
            case WhileStatement ws:
                EmitLine($"while ({RenderBsNode(ws.Condition)})");
                EmitLine("{");
                _indent++;
                EmitBody(ws.Body);
                _indent--;
                EmitLine("}");
                break;
            case BreakStatement:
                EmitLine("break;");
                break;
            case ContinueStatement:
                EmitLine("continue;");
                break;
            case ExitStatement:
                EmitLine("return;");
                break;
            default:
                EmitLine($"/* unknown statement kind: {stmt.Kind} */");
                break;
        }
    }

    private void EmitPipeline(PipelineStatement p)
    {
        // For MVP: emit the pipeline as a sequence of G.<Name>(args) calls.
        // Pure segments produce intermediate temp variables; the last segment's value
        // (if any) goes into a variable-tap target.
        // The simplest MVP path: emit `G.Print(value)` for bare Print pipelines, and
        // `var tmp = G.Range(...)` for pure-producing pipelines whose value is consumed
        // downstream (e.g. as the forEach source — but in that case the forEach wraps
        // the pipeline and emits its own source expression directly).
        //
        // For a bare call `Print(x)`: Sources=[BsCall Print, args=[x]], Segments=[]
        // → emit G.Print(x).
        // For `Range(0,10,1) > forEach as i`: handled by ForEachStatement which emits
        //   `foreach (var i in G.Range(0,10,1))` directly (see RenderForEachSource).
        // For `value > Print`: Sources=[value], Segments=[Print] → emit G.Print(value).

        if (p.Segments.Length == 0)
        {
            // Bare call: the source is a BsCall (e.g. Print("hello")). Emit G.Print(arg).
            if (p.Sources.Length == 1 && p.Sources[0] is BsCall call)
            {
                EmitLine($"{RenderCallStatement(call)};");
                return;
            }
            // Bare expression with no call: emit nothing (or a comment).
            EmitLine($"/* bare expression: {RenderBsNode(p.Sources[0])} */");
            return;
        }

        // Pipeline with segments: walk the chain.
        string? currentExpr = null;
        if (p.Sources.Length == 1)
            currentExpr = RenderBsNode(p.Sources[0]);
        else
            currentExpr = $"/* multi-source pipeline */ {string.Join(", ", p.Sources.Select(RenderBsNode))}";

        foreach (var seg in p.Segments)
        {
            if (seg.IsVariableTap)
            {
                // `= name` explicit assignment: assign the current expression to a PubVar.
                EmitLine($"this.{seg.Target} = {currentExpr};");
                return;
            }
            // A bare `> name` (no parens) is ambiguous: could be a builtin call OR a
            // variable tap. Resolve by checking the registry.
            if (seg.Arguments.Length == 0 && !_registry.Contains(seg.Target))
            {
                // Not a known builtin → treat as a variable tap assignment.
                EmitLine($"this.{seg.Target} = {currentExpr};");
                return;
            }
            // Call segment: invoke the builtin on the current pipeline value.
            var argList = seg.Arguments.Length == 0
                ? currentExpr  // bare `> Func`: the pipeline value is the implicit single arg.
                : string.Join(", ", seg.Arguments.Select(a => a is BsPlaceholder ? currentExpr : RenderBsNode(a)));
            currentExpr = $"this.{MapBuiltinToGMethod(seg.Target)}({argList})";
        }
        // The final expression is a side-effect or pure result; if it's a side-effect
        // (Print), emit it as a statement. If it's pure, assign to a temp or discard.
        EmitLine($"{currentExpr};");
    }

    private string RenderCallStatement(BsCall call)
    {
        var args = string.Join(", ", call.Args.Select(RenderBsNode));
        return $"this.{MapBuiltinToGMethod(call.MethodName)}({args})";
    }

    /// <summary>Renders a BsNode as a C# expression string.</summary>
    private string RenderBsNode(BsNode node) => node switch
    {
        BsLiteral lit => RenderLiteral(lit),
        BsIdentifier id => _localNames.Contains(id.Name) ? id.Name : $"this.{id.Name}",
        BsCall call => call.Args.Length == 0
            ? $"this.{MapBuiltinToGMethod(call.MethodName)}()"
            : $"this.{MapBuiltinToGMethod(call.MethodName)}({string.Join(", ", call.Args.Select(RenderBsNode))})",
        BsPipeline pipe => pipe.RenderPipelineSource(),  // fallback — should be pre-lowered
        BsPipelineSegment seg => seg.IsVariableTap
            ? seg.Target
            : $"this.{MapBuiltinToGMethod(seg.Target)}({string.Join(", ", seg.Args.Select(RenderBsNode))})",
        BsPlaceholder => "_placeholder_",
        _ => $"/* {node.GetType().Name} */",
    };

    /// <summary>
    /// Renders a forEach source expression. If the source is a Range call, emit
    /// `G.Range(from, to, step)` directly so the foreach binds a real int. Otherwise
    /// fall back to the general RenderBsNode.
    /// </summary>
    private string RenderForEachSource(BsNode source)
    {
        if (source is BsCall call && call.MethodName == "Range")
        {
            return $"this.Range({string.Join(", ", call.Args.Select(RenderBsNode))})";
        }
        return RenderBsNode(source);
    }

    private string RenderLiteral(BsLiteral lit) => lit.Kind switch
    {
        BsLiteralKind.String => $"\"{lit.Value?.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        BsLiteralKind.Integer => lit.Value?.ToString() ?? "0",
        BsLiteralKind.Double => (lit.Value?.ToString() ?? "0.0") + "d",
        BsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        BsLiteralKind.Char => $"'{lit.Value}'",
        BsLiteralKind.Null => "null",
        _ => "null",
    };

    private void EmitLine(string line)
    {
        _sb.Append(new string(' ', _indent * 4));
        _sb.AppendLine(line);
    }

    private string NewTemp() => $"t{++_tempCounter}";
}