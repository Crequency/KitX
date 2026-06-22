using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Models;

// ─────────────────────────────────────────────────────────────────────────────
// BSExpression — KitX's own AST for BlockScript expressions.
//
// Why this exists: BS's block-interior is a restricted C# subset, and the
// previous implementation let Roslyn's SyntaxNode types (InvocationExpressionSyntax,
// BinaryExpressionSyntax, …) leak through the AST model
// (ExpressionStatement.ParsedInvocation), the builtin-function interface
// (IBuiltinFunctionDefinition.ExtractStatement/LowerToCFG), and the converters
// (BS2CFGConverter walked Roslyn trees directly). That leakage forced downstream
// code to re-parse expression strings (ExprUtils.ParseExpression/ParseStatement,
// each backed by a ConcurrentDictionary cache) — the "double parse" smell.
//
// BSExpression is the single AST surface: BSParser (Superpower) produces these
// parser-agnostic nodes at the parse boundary. Everything downstream consumes
// BSExpression and never touches Roslyn (or Superpower) parser internals.
//
// Roslyn retains its legitimate duties: helper-function bodies (full C#), and the
// compilation backend (CFG2CSConverter). It is no longer involved in BS parsing,
// AST representation, or any BS↔Blueprint translation step.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Root of the BlockScript expression AST. Every node remembers the exact source
/// text it was built from (lossless round-trip for SourceCode fields).
/// </summary>
public abstract class BSExpression
{
    /// <summary>Verbatim source text of this expression (e.g. <c>Get(currentLoop)</c>).</summary>
    public string SourceText { get; set; } = string.Empty;
}

/// <summary>
/// A literal value: string, integer, boolean, float/double, char, or null.
/// Carries the typed <see cref="Value"/> so consumers never re-parse the text.
/// </summary>
public sealed class BSLiteral : BSExpression
{
    public BSLiteralKind Kind { get; set; }
    public object? Value { get; set; }
}

/// <summary>Discriminated literal kinds mirroring BlockScript's supported types.</summary>
public enum BSLiteralKind
{
    String,
    Integer,
    Double,
    Boolean,
    Char,
    Null
}

/// <summary>An identifier reference (variable / PubVar / ConstBlock name).</summary>
public sealed class BSIdentifier : BSExpression
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A function invocation — the central node of BS. Covers bare calls (<c>Print(x)</c>),
/// member-access calls (<c>PluginName.MethodName(args)</c>), and nested calls used as
/// arguments (<c>Outer(Inner(...))</c>, which BS2CFGConverter expands into PubVar temps).
///
/// <see cref="MethodName"/> is the short name (last segment); <see cref="FullMethodName"/>
/// is the full dotted path (<c>TestPlugin.WPF.Core.HelloKitX</c>) for qualified calls.
/// <see cref="Args"/> are the structured argument expressions (may themselves be <see cref="BSCall"/>s).
/// <see cref="RawArgs"/> preserves each argument's source text for the converters' string-based
/// CFG emission path (preserved verbatim from source, no reformatting).
/// </summary>
public sealed class BSCall : BSExpression
{
    public string MethodName { get; set; } = string.Empty;
    public string FullMethodName { get; set; } = string.Empty;
    public IReadOnlyList<BSExpression> Args { get; set; } = Array.Empty<BSExpression>();
    public IReadOnlyList<string> RawArgs { get; set; } = Array.Empty<string>();
}

/// <summary>
/// An assignment: <c>target = value</c>. The target is always an identifier in BS
/// (PubVar / ConstBlock var). The value may be a call, literal, binary, etc.
/// <see cref="SourceText"/> is the whole <c>lhs = rhs</c> text.
/// </summary>
public sealed class BSAssignment : BSExpression
{
    public BSIdentifier Target { get; set; } = new();
    public BSExpression Value { get; set; } = new BSIdentifier();
}

/// <summary>
/// A binary expression. BS only meaningfully uses <c>+</c> (rewritten to StringConcat);
/// other operators may appear inside helper-adjacent expressions and are preserved
/// structurally so the converter can rebuild their text without re-parsing.
/// </summary>
public sealed class BSBinary : BSExpression
{
    public BSExpression Left { get; set; } = new BSIdentifier();
    public BSExpression Right { get; set; } = new BSIdentifier();
    public string Operator { get; set; } = string.Empty;
}

/// <summary>A parenthesized sub-expression; the inner expression is what matters.</summary>
public sealed class BSParenthesized : BSExpression
{
    public BSExpression Inner { get; set; } = new BSIdentifier();
}

/// <summary>
/// A pipeline statement: <c>SourceList \- Target { \- Target }</c>.
/// <para>
/// <see cref="Sources"/> is the comma-separated list of source expressions (1 or more).
/// <see cref="Targets"/> is the ordered list of pipeline segments (each a <see cref="BSCall"/>,
/// which may contain <see cref="BSPlaceholder"/> arguments marking where pipeline values insert).
/// </para>
/// <para>
/// Flattening (BS→CFG): the BSPipeline is carried as a first-class <c>PipelineStatement</c>
/// whose <c>FlattenedStatements</c> view expands to the imperative PubVar-assignment sequence
/// consumed by CFG2BP/CFG2CS/executor. CFG2BS renders back from the AST directly.
/// </para>
/// </summary>
public sealed class BSPipeline : BSExpression
{
    /// <summary>The comma-separated source expressions (the left side of the first <c>\-</c>).</summary>
    public IReadOnlyList<BSExpression> Sources { get; set; } = Array.Empty<BSExpression>();

    /// <summary>The ordered pipeline segments (each <c>\- Target</c>).</summary>
    public IReadOnlyList<BSCall> Targets { get; set; } = Array.Empty<BSCall>();

    /// <summary>
    /// Renders the pipeline back to its <c>&gt;</c> source form from the structured AST.
    /// Used by CFG2BS to reconstruct pipeline text without relying on verbatim source (which may
    /// be a __pipe sentinel from the Roslyn prescanner). Each Source/Target/Arg renders its own
    /// <see cref="BSExpression.SourceText"/>, which is set losslessly at the parse boundary.
    /// </summary>
    public string RenderPipelineSource()
    {
        var sb = new System.Text.StringBuilder();
        // Sources: comma-joined (each keeps its verbatim text, e.g. "a", "Get(x)", "\"lit\"").
        sb.Append(string.Join(", ", Sources.Select(s => s.SourceText)));
        // Targets: " > TargetText" each.
        foreach (var target in Targets)
            sb.Append(" > ").Append(target.SourceText);
        return sb.ToString();
    }
}

/// <summary>
/// A pipeline placeholder (<c>_</c>) — marks a parameter position where a pipeline value
/// (from Sources or the previous segment's result) should be inserted during flattening.
/// <see cref="Index"/> is the ordinal among multiple placeholders in the same call (0-based),
/// used to match pipeline values to positions when a target has more than one <c>_</c>.
/// </summary>
public sealed class BSPlaceholder : BSExpression
{
    public int Index { get; set; }
}

/// <summary>Extension methods over BSExpression, replacing the Roslyn-coupled ExprUtils helpers.</summary>
public static class BSExpressionExtensions
{
    /// <summary>
    /// Returns the string value when the expression is a string literal, else null.
    /// Replaces <c>ExprUtils.GetStringLiteralValue</c>.
    /// </summary>
    public static string? AsStringLiteral(this BSExpression? expr)
        => expr is BSLiteral { Kind: BSLiteralKind.String } lit ? lit.Value as string : null;

    /// <summary>
    /// Returns the typed literal value (string/int/double/bool/char/null), else null.
    /// Replaces <c>ExprUtils.GetLiteralValue</c>.
    /// </summary>
    public static object? LiteralValue(this BSExpression? expr)
        => expr is BSLiteral lit ? lit.Value : null;

    /// <summary>
    /// True when the source text is a C# character literal (e.g. <c>'\0'</c>, <c>'a'</c>).
    /// Replaces the former Roslyn <c>SyntaxFactory.ParseToken</c> check with a lightweight
    /// structural test (char literals always start/end with single quote, content is either
    /// one char or a backslash-escape pair).
    /// </summary>
    public static bool IsCharacterLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        // C# char literals always start and end with single quote.
        if (value.Length < 3 || value[0] != '\'' || value[^1] != '\'') return false;
        var inner = value[1..^1];
        // Valid: single char ('a'), or backslash-escape pair ('\n', '\'', '\\', '\0', ...).
        return inner.Length == 1 || (inner.Length == 2 && inner[0] == '\\');
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BSExpressionAdapter has been removed. BS parsing is now owned by BSParser
// (Superpower token-driven parser). BSExpression trees are produced directly by
// BSParser's combinators; no Roslyn SyntaxNode→BSExpression bridge is needed.
// ─────────────────────────────────────────────────────────────────────────────

