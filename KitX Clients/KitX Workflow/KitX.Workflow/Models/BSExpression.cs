using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
// BSExpression is the single bridge: Roslyn is used exactly once, at the parse
// boundary in BSExpressionAdapter.FromRoslyn, to build these parser-agnostic
// nodes. Everything downstream consumes BSExpression and never touches Roslyn.
//
// Roslyn retains its legitimate duties: BlockSyntaxValidator (syntax checking +
// diagnostics), helper-function bodies (full C#), and the compilation backend
// (CFG2CSConverter). It is no longer involved in AST representation or any
// BS↔Blueprint translation step.
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
/// Flattening (BS→CFG): each Source becomes a PubVar assignment; each Target becomes a call
/// statement whose arguments are filled from Sources (first segment) or the previous segment's
/// single result (subsequent segments). All statements share a <c>PipelineId</c> for round-trip
/// reconstruction. The CFG's flat semantics are unchanged — the pipeline is purely a text-side
/// sugar whose structure is captured as provenance metadata.
/// </para>
/// </summary>
public sealed class BSPipeline : BSExpression
{
    /// <summary>The comma-separated source expressions (the left side of the first <c>\-</c>).</summary>
    public IReadOnlyList<BSExpression> Sources { get; set; } = Array.Empty<BSExpression>();

    /// <summary>The ordered pipeline segments (each <c>\- Target</c>).</summary>
    public IReadOnlyList<BSCall> Targets { get; set; } = Array.Empty<BSCall>();
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
    /// Uses Roslyn token parsing once at the boundary (cheaper than a full expression tree);
    /// consumers call this on a string rather than re-parsing an expression tree.
    /// </summary>
    public static bool IsCharacterLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        // Fast pre-check: C# char literals always start and end with single quote.
        if (value.Length < 3 || value[0] != '\'' || value[^1] != '\'') return false;
        // Validate with Roslyn — parse as a single token (far cheaper than a full expression
        // tree parse). A char literal parses as a CharacterLiteralToken.
        var token = SyntaxFactory.ParseToken(value);
        return token.IsKind(SyntaxKind.CharacterLiteralToken);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BSExpressionAdapter — the only place Roslyn's C# parser feeds BSExpression.
// Cover every Roslyn node type the converters previously walked directly:
// Invocation / Literal / IdentifierName / Assignment / Binary / Parenthesized.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Builds <see cref="BSExpression"/> trees from Roslyn syntax nodes. Called once per
/// parsed statement/expression at the extract boundary; the result is then shared by
/// every downstream consumer, eliminating the re-parse-the-string pattern entirely.
/// </summary>
public static class BSExpressionAdapter
{
    /// <summary>
    /// Adapts a Roslyn <see cref="ExpressionSyntax"/> into a <see cref="BSExpression"/>.
    /// Returns null for expression kinds BS does not model (should not occur for valid BS).
    /// </summary>
    public static BSExpression? FromRoslyn(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case InvocationExpressionSyntax invoke:
                return FromInvocation(invoke);
            case LiteralExpressionSyntax lit:
                return FromLiteral(lit);
            case IdentifierNameSyntax id:
                // The lone underscore is the pipeline placeholder, not a variable reference.
                return id.Identifier.Text == "_"
                    ? new BSPlaceholder { SourceText = "_" }
                    : new BSIdentifier { Name = id.Identifier.Text, SourceText = id.ToString() };
            case AssignmentExpressionSyntax assign:
                return FromAssignment(assign);
            case BinaryExpressionSyntax binary:
                return FromBinary(binary);
            case ParenthesizedExpressionSyntax paren:
                return new BSParenthesized
                {
                    Inner = FromRoslyn(paren.Expression) ?? new BSIdentifier(),
                    SourceText = paren.ToString()
                };
            case MemberAccessExpressionSyntax member:
                // A bare member access (not a call) — treat as an identifier-like reference
                // carrying the full dotted path. Rare in BS (PluginName.MethodName appears as
                // the receiver of a call), but handled so it is never lost.
                return new BSIdentifier { Name = member.ToString(), SourceText = member.ToString() };
            default:
                return null;
        }
    }

    /// <summary>
    /// Parses a free-form expression string (e.g. a Branch/Loop condition text) into a
    /// <see cref="BSExpression"/>. This is a one-shot Roslyn parse at the boundary — used by
    /// the few sites that carry an expression as a string (e.g. <c>FlowControlStatement.ConditionExpression</c>)
    /// and need structural analysis. Returns null when the string is not a parseable expression.
    /// </summary>
    public static BSExpression? Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        try
        {
            var tree = CSharpSyntaxTree.ParseText("_ = " + expression + ";");
            var root = tree.GetCompilationUnitRoot();
            var globalStmt = root.Members.FirstOrDefault() as GlobalStatementSyntax;
            var stmt = globalStmt?.Statement as ExpressionStatementSyntax;
            if (stmt?.Expression is AssignmentExpressionSyntax assignment)
                return FromRoslyn(assignment.Right);
            return stmt?.Expression is null ? null : FromRoslyn(stmt.Expression);
        }
        catch
        {
            return null;
        }
    }

    private static BSCall FromInvocation(InvocationExpressionSyntax invoke)
    {
        var methodName = GetShortName(invoke);
        var fullMethodName = GetFullName(invoke);
        var args = new List<BSExpression>(invoke.ArgumentList.Arguments.Count);
        var rawArgs = new List<string>(invoke.ArgumentList.Arguments.Count);
        foreach (var arg in invoke.ArgumentList.Arguments)
        {
            rawArgs.Add(arg.Expression.ToString());
            args.Add(FromRoslyn(arg.Expression) ?? new BSIdentifier { SourceText = arg.Expression.ToString() });
        }
        return new BSCall
        {
            MethodName = methodName,
            FullMethodName = fullMethodName,
            Args = args,
            RawArgs = rawArgs,
            SourceText = invoke.ToString()
        };
    }

    private static BSLiteral FromLiteral(LiteralExpressionSyntax lit)
    {
        var token = lit.Token;
        var kind = token.Kind() switch
        {
            SyntaxKind.StringLiteralToken => BSLiteralKind.String,
            SyntaxKind.NumericLiteralToken => token.Value is double or float ? BSLiteralKind.Double : BSLiteralKind.Integer,
            SyntaxKind.TrueKeyword => BSLiteralKind.Boolean,
            SyntaxKind.FalseKeyword => BSLiteralKind.Boolean,
            SyntaxKind.CharacterLiteralToken => BSLiteralKind.Char,
            SyntaxKind.NullKeyword => BSLiteralKind.Null,
            _ => BSLiteralKind.String
        };
        return new BSLiteral { Kind = kind, Value = token.Value, SourceText = lit.ToString() };
    }

    private static BSAssignment FromAssignment(AssignmentExpressionSyntax assign)
    {
        var target = FromRoslyn(assign.Left) as BSIdentifier
                     ?? new BSIdentifier { Name = assign.Left.ToString(), SourceText = assign.Left.ToString() };
        var value = FromRoslyn(assign.Right) ?? new BSIdentifier { SourceText = assign.Right.ToString() };
        return new BSAssignment
        {
            Target = target,
            Value = value,
            SourceText = assign.ToString()
        };
    }

    private static BSBinary FromBinary(BinaryExpressionSyntax binary)
    {
        var left = FromRoslyn(binary.Left) ?? new BSIdentifier { SourceText = binary.Left.ToString() };
        var right = FromRoslyn(binary.Right) ?? new BSIdentifier { SourceText = binary.Right.ToString() };
        return new BSBinary
        {
            Left = left,
            Right = right,
            Operator = binary.OperatorToken.Text,
            SourceText = binary.ToString()
        };
    }

    // Mirrors the former ExprUtils.GetMethodName / GetFullMethodName — kept here as the
    // single source of truth for the Roslyn→name extraction, used only while building a BSCall.

    private static string GetShortName(InvocationExpressionSyntax invoke) => invoke.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => string.Empty
    };

    private static string GetFullName(InvocationExpressionSyntax invoke) => invoke.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Expression + "." + member.Name.Identifier.Text,
        _ => string.Empty
    };
}
