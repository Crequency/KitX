using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

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
//
// SECURITY (W-2): the rendered text is spliced verbatim into generated C# source
// (e.g. `public int counter = <text>;`). Non-string types are therefore validated
// with a strict lexical grammar before they are allowed through — a payload like
// `0; File.WriteAllText(...) //` must be rejected, never inlined. String values
// are safe because they are quoted + escaped by the shared codec. The validation
// lives here (the single chokepoint for run-by-id overrides); the same semantics
// apply to the in-editor path, which funnels through ApplyConstantOverrides.
// ─────────────────────────────────────────────────────────────────────────────

public static class WorkflowOverrides
{
    // ── Strict lexical grammars (W-2) ──
    //
    // These accept ONLY the character classes that can appear in the corresponding
    // C# numeric/bool literal. Anything else (;, (, ), /, *, =, hex 0x, ...) fails
    // the lexeme check and is rejected with a diagnostic. Parsing with the exact
    // CLR type then guards the RANGE (an overflow like "99999999999999999999"
    // would otherwise pass the lexeme check but fail to compile).

    /// <summary>Optional sign + decimal digits (int/long).</summary>
    private static readonly Regex IntegralLexeme = new(
        @"^[+-]?[0-9]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Optional sign + digits + optional single fraction + optional exponent (double/float).</summary>
    private static readonly Regex FloatLexeme = new(
        @"^[+-]?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Renders a user-entered value as a valid C# literal expression for the given
    /// KS type. Strings/chars are quoted+escaped via the shared
    /// <see cref="KsScalarLiteralCodec"/> (W-7); bool/int/long/double/float pass
    /// through ONLY after strict lexical validation (W-2) — invalid values throw
    /// <see cref="InvalidOperationException"/> with the offending value/type instead
    /// of producing injectable source; unknown types (e.g. dict, which uses
    /// DictInitializer) pass through untouched.
    /// </summary>
    public static string RenderLiteral(string? text, string type)
    {
        if (text is null) return "default";
        if (string.IsNullOrEmpty(type)) return text;
        return type.ToLowerInvariant() switch
        {
            "string" => KsScalarLiteralCodec.EncodeStringLiteral(text),
            "char" => text.Length == 1
                ? KsScalarLiteralCodec.EncodeCharLiteral(text[0])
                // Multi-char overrides for a char-typed const are a user error; keep
                // the old tolerant quoting (quotes are for the C# char literal, so the
                // char-side escape set — \ and ' — applies, not the string one).
                : "'" + text.Replace("\\", "\\\\").Replace("'", "\\'") + "'",
            "bool" => ParseBool(text),
            "int" => ParseNumeric(text, "int", IntegralLexeme, static s =>
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)),
            "long" => ParseNumeric(text, "long", IntegralLexeme, static s =>
                long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)),
            "double" => ParseNumeric(text, "double", FloatLexeme, static s =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)),
            "float" => ParseNumeric(text, "float", FloatLexeme, static s =>
                float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)),
            _ => text   // dict / unknown: pass through (dict uses DictInitializer, not text)
        };
    }

    /// <summary>Validates a bool override; returns the canonical lowercase C# literal.</summary>
    private static string ParseBool(string text)
    {
        if (!bool.TryParse(text, out var value))
            throw InvalidValue(text, "bool");
        return value ? "true" : "false";
    }

    /// <summary>
    /// Validates a numeric override: lexical grammar first (rejects injection), then
    /// range via the exact CLR parse. The original lexeme is returned verbatim when
    /// valid — it is already canonical C#.
    /// </summary>
    private static string ParseNumeric(
        string text, string type, Regex lexeme, Func<string, bool> rangeCheck)
    {
        if (!lexeme.IsMatch(text) || !rangeCheck(text))
            throw InvalidValue(text, type);
        return text;
    }

    private static InvalidOperationException InvalidValue(string text, string type)
        => new($"Override value '{text}' is not a valid {type} literal (injected code is rejected; "
             + "expected: true/false, a signed integer, or a signed decimal/exponent number)");

    /// <summary>
    /// Applies name → value overrides to the IR's <see cref="Workflow.Constants"/> and
    /// <see cref="Workflow.GlobalVars"/> via with-expressions, rewriting
    /// <c>InitialValueExpression</c> (compile-time text inlining). Returns the input
    /// unchanged when there are no overrides or no matching names. Throws
    /// <see cref="InvalidOperationException"/> (with the offending name attached) when
    /// an override value fails <see cref="RenderLiteral"/>'s strict validation.
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
            try
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
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"Constant override '{name}' is invalid: {ex.Message}", ex);
            }
        }

        return changed
            ? ir with { Constants = cBuilder.ToImmutable(), GlobalVars = gBuilder.ToImmutable() }
            : ir;
    }
}
