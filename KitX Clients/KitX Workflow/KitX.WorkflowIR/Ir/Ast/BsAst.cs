namespace KitX.WorkflowIR.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// BSExpression AST — KitX's own BlockScript expression tree.
//
// Migrated verbatim (semantics) from the legacy KitX.Workflow.Models.BSExpression
// hierarchy. This is the output of the Superpower BSParser (a near-pure functional
// combinator library, A-grade code) and the input to BsLowerer. The legacy
// classes were mutable; here they are records so the AST is value-comparable and
// safe to share.
//
// Why a separate AST from the IR: the AST mirrors BS source 1:1 (lossless), while
// the IR is the canonical lowered form (pipelines carried as structured AST, but
// block structure normalised). Lowering is a one-way transform (AST → IR);
// rendering IR → BS text does not need the AST.
//
// Every node carries its verbatim source text so rendering never re-parses.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Root of the BS expression AST. Every node remembers its exact source text.</summary>
public abstract record BSExpression
{
    /// <summary>Verbatim source text (e.g. <c>Get(currentLoop)</c>).</summary>
    public string SourceText { get; init; } = string.Empty;
}

/// <summary>Discriminated literal kinds mirroring BlockScript's supported types.</summary>
public enum BSLiteralKind { String, Integer, Double, Boolean, Char, Null }

/// <summary>A literal value (string/int/bool/double/char/null) with its typed value.</summary>
public sealed record BSLiteral : BSExpression
{
    public required BSLiteralKind Kind { get; init; }
    public object? Value { get; init; }
}

/// <summary>An identifier reference (variable / PubVar / ConstBlock name).</summary>
public sealed record BSIdentifier : BSExpression
{
    public required string Name { get; init; }
}

/// <summary>
/// A function invocation — the central node of BS. Covers bare calls
/// (<c>Print(x)</c>), member-access calls (<c>Plugin.Method(args)</c>), and nested
/// calls used as arguments. <see cref="MethodName"/> is the short name (last
/// segment); <see cref="FullMethodName"/> the full dotted path. Args are the
/// structured argument expressions (may themselves be BSCalls); RawArgs preserves
/// each argument's source text for string-based lowering paths.
/// </summary>
public sealed record BSCall : BSExpression
{
    public required string MethodName { get; init; }
    public string FullMethodName { get; init; } = string.Empty;
    public IReadOnlyList<BSExpression> Args { get; init; } = [];
    public IReadOnlyList<string> RawArgs { get; init; } = [];
}

/// <summary>An assignment <c>target = value</c>. Target is always an identifier in BS.</summary>
public sealed record BSAssignment : BSExpression
{
    public required BSIdentifier Target { get; init; }
    public required BSExpression Value { get; init; }
}

/// <summary>
/// A binary expression. BS only meaningfully uses <c>+</c> (lowered to StringConcat);
/// other operators are preserved structurally so lowering can rebuild text.
/// </summary>
public sealed record BSBinary : BSExpression
{
    public required BSExpression Left { get; init; }
    public required BSExpression Right { get; init; }
    public required string Operator { get; init; }
}

/// <summary>A parenthesized sub-expression; only the inner expression matters.</summary>
public sealed record BSParenthesized : BSExpression
{
    public required BSExpression Inner { get; init; }
}

/// <summary>
/// A pipeline statement: <c>SourceList &gt; Target { &gt; Target }</c>. Sources is the
/// comma-separated LHS; Targets is the ordered list of pipeline segments (each a
/// BSCall, possibly containing BSPlaceholder args). This is the lowering input for
/// IrPipelineStatement.
/// </summary>
public sealed record BSPipeline : BSExpression
{
    /// <summary>The comma-separated source expressions (left of the first <c>&gt;</c>).</summary>
    public required IReadOnlyList<BSExpression> Sources { get; init; }

    /// <summary>The ordered pipeline segments (each <c>&gt; Target</c>).</summary>
    public required IReadOnlyList<BSCall> Targets { get; init; }

    /// <summary>
    /// Renders the pipeline back to its <c>&gt;</c> source form from the structured AST.
    /// Each Source/Target/Arg renders its own <see cref="BSExpression.SourceText"/>,
    /// which is set losslessly at the parse boundary.
    /// </summary>
    public string RenderPipelineSource()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Join(", ", Sources.Select(s => s.SourceText)));
        foreach (var target in Targets)
            sb.Append(" > ").Append(target.SourceText);
        return sb.ToString();
    }
}

/// <summary>
/// A pipeline placeholder (<c>_</c>) — marks where a pipeline value inserts during
/// lowering. <see cref="Index"/> is the ordinal among multiple placeholders in the
/// same call (0-based).
/// </summary>
public sealed record BSPlaceholder : BSExpression
{
    public int Index { get; init; }
}

/// <summary>Extension helpers over BSExpression (replaces the old ExprUtils Roslyn helpers).</summary>
public static class BSExpressionExtensions
{
    /// <summary>The string value when the expression is a string literal, else null.</summary>
    public static string? AsStringLiteral(this BSExpression? expr)
        => expr is BSLiteral { Kind: BSLiteralKind.String } lit ? lit.Value as string : null;

    /// <summary>The typed literal value (string/int/double/bool/char/null), else null.</summary>
    public static object? LiteralValue(this BSExpression? expr)
        => expr is BSLiteral lit ? lit.Value : null;

    /// <summary>
    /// True when the source text is a C# character literal (e.g. <c>'\0'</c>, <c>'a'</c>).
    /// Lightweight structural test: char literals start/end with single quote, content is
    /// either one char or a backslash-escape pair.
    /// </summary>
    public static bool IsCharacterLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length < 3 || value[0] != '\'' || value[^1] != '\'') return false;
        var inner = value[1..^1];
        return inner.Length == 1 || (inner.Length == 2 && inner[0] == '\\');
    }
}
