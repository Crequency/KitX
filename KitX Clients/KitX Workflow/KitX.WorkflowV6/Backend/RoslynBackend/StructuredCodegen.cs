namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Text;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// StructuredCodegen — IR → structured C# source string (discussion notes §5.3).
//
// Walks the structured Statement tree and emits C# that mirrors the v6 KS exactly:
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
    private Workflow _ir = null!;
    private HashSet<string> _helperNames = new(StringComparer.Ordinal);
    /// <summary>
    /// Names bound as local variables in the current scope (forEach item bindings,
    /// while-loop counters when allocated by codegen). These render as bare identifiers,
    /// NOT as `this.&lt;name&gt;` (PubVar field accesses). Populated by EmitBody when
    /// entering a forEach scope; cleared on scope exit.
    /// </summary>
    private readonly HashSet<string> _localNames = new();

    /// <summary>
    /// Maps a KS builtin function name to the C# method name on the G class (ExecutionGlobals).
    /// Most builtins keep their KS name verbatim. StringConcat gets a suffix to avoid a
    /// name clash with static string.Concat. User helper functions (Compare,
    /// Add, ...) keep their exact KS names — they are not renamed.
    /// </summary>
    private static readonly Dictionary<string, string> BuiltinToGMethod = new(StringComparer.Ordinal)
    {
        ["Print"] = "Print",
        ["Range"] = "Range",
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
        _ir = ir;
        _helperNames = new HashSet<string>(
            ir.HelperFunctions.Where(h => !string.IsNullOrEmpty(h.Name)).Select(h => h.Name!),
            StringComparer.Ordinal);
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

        // ── User-defined helper functions (§11.3) ──
        EmitHelperFunctions(ir);

        _indent--;
        EmitLine("}");
        return _sb.ToString();
    }

    /// <summary>
    /// Emits user-defined HelperFunction methods onto the G class. Each helper becomes
    /// a public instance method: <c>public ReturnType Name(params) { Code }</c>.
    /// The helper's Code string is embedded verbatim (it is C# code written by the
    /// workflow author). Adapted from v5.1 WorkflowIR's IrCodegen.GenerateHelperFunctions.
    /// </summary>
    private void EmitHelperFunctions(Workflow ir)
    {
        if (ir.HelperFunctions.IsDefault || ir.HelperFunctions.Length == 0) return;
        EmitLine("");
        foreach (var func in ir.HelperFunctions)
        {
            var paramList = string.Join(", ",
                func.Parameters.Select(p => $"{p.Type} {p.Name}"));
            EmitLine($"public {func.ReturnType} {func.Name}({paramList})");
            EmitLine("{");
            _indent++;
            // Embed the user's code verbatim, line by line, at the current indent.
            if (!string.IsNullOrWhiteSpace(func.Code))
            {
                foreach (var codeLine in func.Code.Split('\n'))
                    EmitLine(codeLine.TrimEnd());
            }
            else
            {
                EmitLine($"return default({func.ReturnType});");
            }
            _indent--;
            EmitLine("}");
            EmitLine("");
        }
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
            case SwitchStatement sw:
                EmitSwitch(sw);
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

    private void EmitSwitch(SwitchStatement sw)
    {
        EmitLine($"switch ({RenderBsNode(sw.Selector)})");
        EmitLine("{");
        _indent++;
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            EmitLine($"case {i}:");
            EmitLine("{");
            _indent++;
            EmitBody(sw.Arms[i]);
            EmitLine("break;");
            _indent--;
            EmitLine("}");
        }
        if (sw.Default.Length > 0)
        {
            EmitLine("default:");
            EmitLine("{");
            _indent++;
            EmitBody(sw.Default);
            EmitLine("break;");
            _indent--;
            EmitLine("}");
        }
        _indent--;
        EmitLine("}");
    }

    private void EmitPipeline(PipelineStatement p)
    {
        if (p.Segments.Length == 0)
        {
            if (p.Sources.Length == 1 && p.Sources[0] is KsCall call)
            {
                EmitLine($"{RenderCallStatement(call)};");
                return;
            }
            EmitLine($"/* bare expression: {RenderBsNode(p.Sources[0])} */");
            return;
        }

        string? currentExpr = null;
        bool lastWasAssignment = false;

        for (int segIdx = 0; segIdx < p.Segments.Length; segIdx++)
        {
            var seg = p.Segments[segIdx];
            lastWasAssignment = false;

            // Variable tap: assign current value to PubVar, then continue processing.
            bool isVarTap = seg.IsVariableTap
                         || (seg.Arguments.Length == 0 && !_registry.Contains(seg.Target) && !_helperNames.Contains(seg.Target));

            if (isVarTap)
            {
                var tapValue = currentExpr
                    ?? (p.Sources.Length > 0 ? RenderBsNode(p.Sources[0]) : "null");
                EmitLine($"this.{seg.Target} = {tapValue};");
                currentExpr = $"this.{seg.Target}";
                lastWasAssignment = true;
                continue;
            }

            // Function call segment: build arg list respecting `_` placeholder positions.
            // First segment uses pipeline sources as implicit inputs.
            // Subsequent segments use the previous segment's output as sole input.
            IEnumerable<string> inputArgs = segIdx == 0
                ? p.Sources.Select(RenderBsNode)
                : new[] { currentExpr ?? "null" };
            var allArgs = BuildArgList(seg.Arguments, inputArgs);
            currentExpr = $"this.{MapBuiltinToGMethod(seg.Target)}({allArgs})";
        }

        // Emit final expression as statement only if it's a function call,
        // not if the last segment was a variable assignment (already emitted).
        if (!lastWasAssignment && currentExpr is not null)
            EmitLine($"{currentExpr};");
    }

    private string RenderCallStatement(KsCall call)
    {
        var args = string.Join(", ", call.Args.Select(RenderBsNode));
        return $"this.{MapBuiltinToGMethod(call.MethodName)}({args})";
    }

    /// <summary>Renders a KsNode as a C# expression string.</summary>
    private string RenderBsNode(KsNode node) => node switch
    {
        KsLiteral lit => RenderLiteral(lit),
        KsIdentifier id => RenderIdentifier(id),
        KsCall call => call.Args.Length == 0
            ? $"this.{MapBuiltinToGMethod(call.MethodName)}()"
            : $"this.{MapBuiltinToGMethod(call.MethodName)}({string.Join(", ", call.Args.Select(RenderBsNode))})",
        KsPipeline pipe => RenderPipelineAsExpression(pipe),
        KsPipelineSegment seg => seg.IsVariableTap
            ? seg.Target
            : $"this.{MapBuiltinToGMethod(seg.Target)}({string.Join(", ", seg.Args.Select(RenderBsNode))})",
        KsPlaceholder => "_placeholder_",
        _ => $"/* {node.GetType().Name} */",
    };

    /// <summary>
    /// Renders a <see cref="KsPipeline"/> as a C# expression (for if/while condition
    /// positions and forEach sources). Chains segments as nested C# calls, respecting
    /// `_` placeholder positions: placeholders are replaced by pipeline inputs in order;
    /// remaining inputs are appended after explicit args.
    /// </summary>
    private string RenderPipelineAsExpression(KsPipeline pipe)
    {
        if (pipe.Segments.Length == 0)
        {
            return pipe.Sources.Length > 0 ? RenderBsNode(pipe.Sources[0]) : "true";
        }

        string currentExpr = "";
        for (int i = 0; i < pipe.Segments.Length; i++)
        {
            var seg = pipe.Segments[i];
            IEnumerable<string> inputArgs = i == 0
                ? pipe.Sources.Select(RenderBsNode)
                : new[] { currentExpr };
            var allArgs = BuildArgList(seg.Args, inputArgs);
            currentExpr = $"this.{MapBuiltinToGMethod(seg.Target)}({allArgs})";
        }
        return currentExpr;
    }

    /// <summary>
    /// Builds a C# argument list from explicit args + pipeline inputs. Placeholder (`_`)
    /// positions in the explicit args are replaced by pipeline inputs (in order); remaining
    /// inputs are appended after the explicit args. This correctly handles both:
    ///   <c>a, b &gt; Compare("BEQ")</c>  → Compare("BEQ", a, b)  (no placeholders, appended)
    ///   <c>x &gt; Range(0, _, 1)</c>     → Range(0, x, 1)        (placeholder replaced)
    /// </summary>
    private string BuildArgList(ImmutableArray<KsNode> args, IEnumerable<string> inputs)
    {
        var queue = new Queue<string>(inputs);
        var result = new List<string>();
        foreach (var arg in args)
        {
            if (arg is KsPlaceholder)
                result.Add(queue.Count > 0 ? queue.Dequeue() : "null");
            else
                result.Add(RenderBsNode(arg));
        }
        while (queue.Count > 0)
            result.Add(queue.Dequeue());
        return string.Join(", ", result);
    }

    /// <summary>
    /// Renders a KsIdentifier. If the name matches a constant declared in the IR,
    /// inlines its initial-value expression (e.g. loopMax → 3). Otherwise emits
    /// <c>this.&lt;name&gt;</c> (a PubVar field access).
    /// </summary>
    private string RenderIdentifier(KsIdentifier id)
    {
        if (_ir.Constants.TryGetValue(id.Name, out var c) && c.InitialValueExpression is not null)
            return c.InitialValueExpression;
        return _localNames.Contains(id.Name) ? id.Name : $"this.{id.Name}";
    }

    /// <summary>
    /// Renders a forEach source expression. If the source is a Range call, emit
    /// `G.Range(from, to, step)` directly so the foreach binds a real int. Otherwise
    /// fall back to the general RenderBsNode.
    /// </summary>
    private string RenderForEachSource(KsNode source)
    {
        if (source is KsCall call && call.MethodName == "Range")
        {
            return $"this.Range({string.Join(", ", call.Args.Select(RenderBsNode))})";
        }
        return RenderBsNode(source);
    }

    private string RenderLiteral(KsLiteral lit) => lit.Kind switch
    {
        KsLiteralKind.String => $"\"{lit.Value?.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        KsLiteralKind.Integer => lit.Value?.ToString() ?? "0",
        KsLiteralKind.Double => (lit.Value?.ToString() ?? "0.0") + "d",
        KsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        KsLiteralKind.Char => $"'{lit.Value}'",
        KsLiteralKind.Null => "null",
        _ => "null",
    };

    private void EmitLine(string line)
    {
        _sb.Append(new string(' ', _indent * 4));
        _sb.AppendLine(line);
    }

    private string NewTemp() => $"t{++_tempCounter}";
}