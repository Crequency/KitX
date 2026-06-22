using KitX.Workflow.Models;

namespace KitX.Workflow.Conversion.Arguments;

/// <summary>
/// Classifies a raw argument string token into its source kind. Shared by DataEdgeBuilder
/// (BS→BP) and NodeExportHelper (BP→BS) so the string/char/numeric/identifier predicates are
/// not duplicated. Site #3 (BS2CFGConverter.ExpandExpression) is AST-driven and does not use
/// this — its classification is structural (BSLiteral/BSIdentifier/BSCall), which is cleaner.
/// </summary>
public enum ArgumentSourceKind
{
    /// <summary>A double-quoted string literal (e.g. <c>"hello"</c>).</summary>
    StringLiteral,

    /// <summary>A single-quoted char literal (e.g. <c>'a'</c>).</summary>
    CharLiteral,

    /// <summary>An integer or floating-point numeric literal.</summary>
    NumericLiteral,

    /// <summary>A boolean literal (<c>true</c> / <c>false</c>).</summary>
    BooleanLiteral,

    /// <summary>A bare identifier — PubVar, ConstBlock variable, or VariableNode name.
    /// The caller resolves which via its own name tables.</summary>
    Identifier,

    /// <summary>A call expression (contains <c>(</c>) — treated as opaque text by string-based
    /// resolvers; BS2CFG handles it structurally via ExpandExpression.</summary>
    CallExpression,

    /// <summary>Unrecognised token.</summary>
    Unknown,
}

/// <summary>
/// The result of classifying an argument string: its kind plus the raw and (for string
/// literals) quote-stripped value.
/// </summary>
public readonly record struct ArgumentSource(ArgumentSourceKind Kind, string RawValue, string? StrippedValue)
{
    /// <summary>Convenience: true when this is any literal kind.</summary>
    public bool IsLiteral => Kind is ArgumentSourceKind.StringLiteral
        or ArgumentSourceKind.CharLiteral
        or ArgumentSourceKind.NumericLiteral
        or ArgumentSourceKind.BooleanLiteral;
}

/// <summary>
/// Stateless classifier for argument strings. Mirrors the predicates that were duplicated in
/// DataEdgeBuilder.ProcessArgument and NodeExportHelper.FormatLiteralValue.
/// </summary>
public static class ArgumentSourceClassifier
{
    /// <summary>Classifies a raw argument string into its source kind.</summary>
    public static ArgumentSource Classify(string arg)
    {
        var trimmed = arg.Trim();

        // String literal: starts and ends with double quote.
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            return new(ArgumentSourceKind.StringLiteral, trimmed, trimmed[1..^1]);

        // Char literal: validated via the shared Roslyn-aware helper.
        if (BSExpressionExtensions.IsCharacterLiteral(trimmed))
            return new(ArgumentSourceKind.CharLiteral, trimmed, trimmed);

        // Numeric literal.
        if (int.TryParse(trimmed, out _) || double.TryParse(trimmed, out _))
            return new(ArgumentSourceKind.NumericLiteral, trimmed, trimmed);

        // Boolean literal.
        if (trimmed == "true" || trimmed == "false")
            return new(ArgumentSourceKind.BooleanLiteral, trimmed, trimmed);

        // Call expression: contains an opening paren (e.g. Get("x"), HelperFuncAdd(_, 1)).
        if (trimmed.Contains('('))
            return new(ArgumentSourceKind.CallExpression, trimmed, trimmed);

        // Anything else is an identifier (PubVar / ConstBlock / VariableNode).
        return new(ArgumentSourceKind.Identifier, trimmed, trimmed);
    }
}