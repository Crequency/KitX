namespace KitX.WorkflowV6.Ir;

using System.Globalization;
using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// KsScalarLiteralCodec — the single shared implementation of scalar-literal
// text encoding/decoding across all Lens/Backend sites.
//
// Before this type existed, every site (Tokenizer, Parser, KsRenderer, BpRenderer,
// BpReverseTranslator, CodegenBase, Fingerprint) had its own ad-hoc text form for
// literals, which caused two data-correctness bugs:
//
//   1. Missing escapes — strings containing `"` or `\` were re-wrapped as `"`+value+`"`
//      without escaping, so `s = "a\"b"` decoded to `a"b` and re-rendering produced
//      corrupt source.
//   2. Culture dependence — double values were formatted/parsed with the current
//      culture, so under a comma-decimal culture (e.g. de-DE) `3.14` became `3,14`
//      and re-parsing drifted the type (or failed entirely).
//
// Everything here is invariant-culture and the escape rules are exactly symmetric
// with the tokenizer's string/char decoding (see Tokenizer.ReadString/ReadChar).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared scalar-literal text codec. Two text conventions exist in the codebase:
/// <list type="bullet">
/// <item><b>KS text</b> — quoted and escaped (<c>"a\"b"</c>, <c>'\n'</c>), see <see cref="Encode(KsLiteral)"/>.</item>
/// <item><b>BP pin text</b> — bare value with no quotes (<c>a"b</c>, <c>3.14</c>), see <see cref="EncodeBareValue(KsLiteral)"/>.</item>
/// </list>
/// All numeric text is invariant-culture in both directions.
/// </summary>
internal static class KsScalarLiteralCodec
{
    // ── Escape characters (symmetric with Tokenizer.ReadString / ReadChar) ──

    /// <summary>Decodes one escape-sequence character; unknown escapes pass through verbatim (tokenizer semantics).</summary>
    public static char DecodeEscapeChar(char esc) => esc switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '\\' => '\\',
        '"' => '"',
        '\'' => '\'',
        '0' => '\0',
        _ => esc,
    };

    /// <summary>Escapes a string value for inclusion inside double quotes (KS text and C# string semantics agree).</summary>
    public static string EscapeStringValue(string value)
        => value.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("\0", "\\0");

    /// <summary>Escapes a char value for inclusion inside single quotes (must escape <c>'</c> and <c>\</c>).</summary>
    public static string EscapeCharValue(char value) => value switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        _ => value.ToString(),
    };

    /// <summary>Renders a string value as a quoted, escaped KS/C# string literal (<c>"a\"b"</c>).</summary>
    public static string EncodeStringLiteral(string? value)
        => $"\"{EscapeStringValue(value ?? string.Empty)}\"";

    /// <summary>Renders a char value as a quoted, escaped KS char literal (<c>'\n'</c>).</summary>
    public static string EncodeCharLiteral(char value)
        => $"'{EscapeCharValue(value)}'";

    // ── KS text encoding (quoted, escaped) ──

    /// <summary>
    /// Renders a <see cref="KsLiteral"/> as KS source text: quoted strings/chars with
    /// symmetric escaping, invariant-culture numbers, lowercase <c>true/false</c>, <c>null</c>.
    /// </summary>
    public static string Encode(KsLiteral lit) => lit.Kind switch
    {
        KsLiteralKind.String => EncodeStringLiteral(lit.Value as string),
        KsLiteralKind.Char => lit.Value is char c ? EncodeCharLiteral(c) : "''",
        KsLiteralKind.Integer => lit.Value is int i ? i.ToString(CultureInfo.InvariantCulture) : "0",
        KsLiteralKind.Double => lit.Value is double d ? FormatDouble(d) : "0.0",
        KsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        KsLiteralKind.Null => "null",
        _ => lit.SourceText,
    };

    // ── BP pin text encoding (bare value, no quotes) ──

    /// <summary>
    /// Renders a <see cref="KsLiteral"/> as BP pin text: the raw value with no quotes
    /// (the BP convention — the reverse translator re-parses the text by type).
    /// Strings carry no escaping because BP pin text has no quoting context; numbers
    /// are invariant-culture so the text is machine-independent.
    /// </summary>
    public static string EncodeBareValue(KsLiteral lit) => lit.Kind switch
    {
        KsLiteralKind.Null => "null",
        KsLiteralKind.Boolean => lit.Value is true ? "true" : "false",
        KsLiteralKind.String => lit.Value as string ?? string.Empty,
        KsLiteralKind.Char => lit.Value is char c ? c.ToString() : "null",
        KsLiteralKind.Integer => lit.Value is int i ? i.ToString(CultureInfo.InvariantCulture) : "null",
        KsLiteralKind.Double => lit.Value is double d ? FormatDouble(d) : "null",
        _ => "null",
    };

    // ── BP pin text decoding (heuristic type recovery) ──

    /// <summary>
    /// Parses BP pin text back into a scalar literal. Type order: null → bool → int →
    /// double → string. Invariant-culture throughout. This is the function-argument
    /// convention (single-character strings stay strings).
    /// </summary>
    public static (KsLiteralKind Kind, object? Value) Decode(string? text)
    {
        if (text is null || text == "null")
            return (KsLiteralKind.Null, null);
        if (bool.TryParse(text, out var b))
            return (KsLiteralKind.Boolean, b);
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return (KsLiteralKind.Integer, i);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return (KsLiteralKind.Double, d);
        return (KsLiteralKind.String, text);
    }

    /// <summary>
    /// Parses a DictNew Value pin's text back into a scalar literal. Same order as
    /// <see cref="Decode(string?)"/> plus the documented T8 behavior: a single-character
    /// text resolves to a char (needed for <c>'x'</c> literal round-trips; single-char
    /// STRING values drift to char on the BP side — a known, documented limitation).
    /// </summary>
    public static (KsLiteralKind Kind, object? Value) DecodeDictValue(string? text)
    {
        var (kind, value) = Decode(text);
        if (kind == KsLiteralKind.String && text is { Length: 1 })
            return (KsLiteralKind.Char, text[0]);
        return (kind, value);
    }

    // ── Helpers ──

    /// <summary>
    /// Invariant-culture double text that re-parses as a double: integral values get a
    /// trailing <c>.0</c> so the text does not drift to an integer on re-parse.
    /// </summary>
    private static string FormatDouble(double d)
    {
        var s = d.ToString(CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e')
            && !double.IsNaN(d) && !double.IsInfinity(d))
            s += ".0";
        return s;
    }
}
