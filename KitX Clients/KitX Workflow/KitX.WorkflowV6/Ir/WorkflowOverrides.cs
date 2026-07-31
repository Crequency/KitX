using System.Collections.Immutable;

namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowOverrides — user constant/global overrides applied before execution.
//
// Extracted from the Dashboard VM (P4-α) so that the run-by-id path
// (WorkflowSessionManager → ITriggerManager routing) can reuse the exact same
// override semantics as the in-editor Run/DebugRun. Because v6 codegen inlines
// InitialValueExpression directly into the generated C# source (CodegenBase
// RenderIdentifier), an override must be a *valid C# literal expression* for the
// declared type — hence the type-aware RenderLiteral below.
// ─────────────────────────────────────────────────────────────────────────────

public static class WorkflowOverrides
{
    /// <summary>
    /// Renders a user-entered value as a valid C# literal expression for the given
    /// KS type. Strings/characters are quoted+escaped; bool/int/long/double/float pass
    /// through verbatim; unknown types (e.g. dict, which uses DictInitializer) pass
    /// through untouched.
    /// </summary>
    public static string RenderLiteral(string? text, string type)
    {
        if (text is null) return "default";
        if (string.IsNullOrEmpty(type)) return text;
        return type.ToLowerInvariant() switch
        {
            "string" => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                   .Replace("\n", "\\n").Replace("\t", "\\t") + "\"",
            "char" => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'") + "'",
            "bool" or "int" or "long" or "double" or "float" => text,
            _ => text   // dict / unknown: pass through (dict uses DictInitializer, not text)
        };
    }

    /// <summary>
    /// Applies name → value overrides to the IR's <see cref="Workflow.Constants"/> and
    /// <see cref="Workflow.GlobalVars"/> via with-expressions, rewriting
    /// <c>InitialValueExpression</c> (compile-time text inlining). Returns the input
    /// unchanged when there are no overrides or no matching names.
    /// </summary>
    public static Workflow ApplyConstantOverrides(
        Workflow ir, IReadOnlyDictionary<string, string?>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return ir;

        var cBuilder = ir.Constants.ToBuilder();
        var gBuilder = ir.GlobalVars.ToBuilder();
        var changed = false;

        foreach (var (name, userText) in overrides)
        {
            if (cBuilder.TryGetValue(name, out var c))
            {
                cBuilder[name] = c with { InitialValueExpression = RenderLiteral(userText, c.Type) };
                changed = true;
            }
            else if (gBuilder.TryGetValue(name, out var g))
            {
                gBuilder[name] = g with { InitialValueExpression = RenderLiteral(userText, g.Type) };
                changed = true;
            }
        }

        return changed
            ? ir with { Constants = cBuilder.ToImmutable(), GlobalVars = gBuilder.ToImmutable() }
            : ir;
    }
}
