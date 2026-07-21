namespace KitX.WorkflowV6.Ir.Ast;

using System.Text.Json.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// BS AST — BlockScript source tree (lossless), distinct from the structured IR.
//
// Inherited split from KitX.WorkflowIR: the AST mirrors BS source 1:1 (so BS round-
// trip is lossless and the indented parser can carry verbatim text on every node),
// while the IR is the canonical lowered form. Lowering is a one-way transform
// (AST → IR); rendering IR → BS text does not need the AST.
//
// The v6 BS grammar (indented, Python-style, see discussion notes §4.1 + §十二-A:
// 4-space indent, no tabs, +4 per level) is parsed into the node types below. Every
// node remembers its verbatim source text so the renderer never re-parses.
//
// Node family overview (closed set, mirrors the 9 control-flow primitives in §3.3 +
// the const/var declaration blocks in §十二-C):
//
//   Expression nodes (BsNode subclasses, reusable as args / sources / conditions):
//     BsLiteral       — string/int/double/bool/char/null literal
//     BsIdentifier    — variable / PubVar / ConstBlock name reference
//     BsCall          — function invocation (bare, member, or nested-as-arg)
//     BsPipeline       — the `>` / `=` data-flow syntax tree
//     BsPipelineSegment — one `> Target` of a BsPipeline (call OR variable tap)
//     BsPlaceholder    — `_`, the pipeline-value insertion marker
//
//   Declaration nodes (top-level only, from `const { ... }` / `var { ... }` blocks):
//     BsConstDecl     — one row inside a const block
//     BsVarDecl       — one row inside a var block
//     BsConstBlock    — the `const { ... }` block (list of BsConstDecl)
//     BsVarBlock      — the `var { ... }` block (list of BsVarDecl)
//
//   Control-flow nodes (statement-level; bodies are child BsStatement lists):
//     BsIf            — `if cond { body } else { body }` (else-if nests via BsIf in else body)
//     BsSwitch        — `switch sel { 0: A; 1: B; default: C }`
//     BsForEach       — `forEach source as item { body }`
//     BsWhile         — `while cond { body }`
//     BsBreak         — `break`
//     BsContinue      — `continue`
//     BsExit          — `exit()` (may carry reason arg)
//
//   Program root:
//     BsProgram       — the whole document (optional const/var blocks + top-level body)
//
// Per discussion notes §十二-K, control-flow keywords do NOT route through
// IBuiltinFunction — they are parsed directly into their own AST node kinds by the
// indented parser, and lowered into their own IR Statement kinds (IfStatement,
// ForEachStatement, ...) by BsLowerer.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Root of the BS source AST. Every node may carry verbatim source text for lossless rendering.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$bsNodeKind")]
[JsonDerivedType(typeof(BsLiteral), "Literal")]
[JsonDerivedType(typeof(BsIdentifier), "Identifier")]
[JsonDerivedType(typeof(BsCall), "Call")]
[JsonDerivedType(typeof(BsPipeline), "Pipeline")]
[JsonDerivedType(typeof(BsPipelineSegment), "PipelineSegment")]
[JsonDerivedType(typeof(BsPlaceholder), "Placeholder")]
public abstract record BsNode
{
    /// <summary>
    /// Verbatim source text this node was parsed from. Set at the parse boundary.
    /// </summary>
    /// <remarks>
    /// Kept as a property (not excluded from record equality) so JSON serialisation
    /// round-trips it correctly. Two ASTs that differ only in SourceText/SourceLine
    /// are semantically equal WHEN those values are the same — which is always true
    /// in the parse-to-render-to-reparse path (the same source text produces the same
    /// SourceText values). The field-vs-property distinction would only matter if we
    /// expected two structurally identical ASTs with different SourceText to be equal,
    /// which is not a goal: the canonical source of truth is the structured IR, and
    /// SourceText is a lossless round-trip aid that happens to be deterministic from
    /// the source.
    /// </remarks>
    public string SourceText { get; set; } = string.Empty;
    public int SourceLine { get; set; }
}

// ── Expression nodes ──

/// <summary>Discriminated literal kinds mirroring BlockScript's supported types.</summary>
public enum BsLiteralKind { String, Integer, Double, Boolean, Char, Null }

/// <summary>A literal value (string/int/bool/double/char/null) with its typed value.</summary>
public sealed record BsLiteral : BsNode
{
    public required BsLiteralKind Kind { get; init; }

    /// <summary>
    /// The literal value (string/int/bool/double/char/null). Stored as object? so the
    /// AST carries the typed value (not just source text). The JsonConverter attribute
    /// ensures System.Text.Json round-trips the boxed value as its runtime type (string
    /// stays string, int stays int) rather than collapsing to JsonElement.
    /// </summary>
    [property: System.Text.Json.Serialization.JsonConverter(typeof(Serialization.BsLiteralValueConverter))]
    public object? Value { get; init; }
}

/// <summary>An identifier reference (variable / PubVar / ConstBlock name).</summary>
public sealed record BsIdentifier : BsNode
{
    public required string Name { get; init; }
}

/// <summary>
/// A function invocation. Covers bare calls (<c>Print(x)</c>), member-access calls
/// (<c>Plugin.Method(args)</c>), and nested calls used as arguments.
/// <see cref="MethodName"/> is the short name (last segment);
/// <see cref="FullMethodName"/> the full dotted path. Args are the structured argument
/// expressions (may themselves be BsCalls); RawArgs preserves each argument's source
/// text for string-based lowering paths that haven't been migrated yet.
/// </summary>
public sealed record BsCall : BsNode
{
    public required string MethodName { get; init; }
    public string FullMethodName { get; init; } = string.Empty;
    public ImmutableArray<BsNode> Args { get; init; } = [];
    public ImmutableArray<string> RawArgs { get; init; } = [];

    public bool Equals(BsCall? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MethodName == other.MethodName
            && FullMethodName == other.FullMethodName
            && SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Args.SequenceEqual(other.Args)
            && RawArgs.SequenceEqual(other.RawArgs);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(MethodName);
        h.Add(FullMethodName);
        h.Add(SourceText);
        h.Add(SourceLine);
        foreach (var a in Args) h.Add(a);
        return h.ToHashCode();
    }
}

/// <summary>
/// A pipeline expression (<c>a, b &gt; F > G > x</c>). <see cref="Sources"/> is the
/// comma-separated LHS; <see cref="Segments"/> is the ordered list of
/// <see cref="BsPipelineSegment"/> (each a call or a variable tap).
/// Lowered to a <see cref="KitX.WorkflowV6.Ir.Statements.PipelineStatement"/>.
/// Inherits from <see cref="BsStatement"/> so it can sit in a statement body (the
/// pipeline is the only data-flow construct, and at the statement level it IS the
/// statement — there's no separate "expression statement" wrapper).
/// </summary>
public sealed record BsPipeline : BsStatement
{
    public required ImmutableArray<BsNode> Sources { get; init; }
    public required ImmutableArray<BsPipelineSegment> Segments { get; init; }

    public string RenderPipelineSource()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Join(", ", Sources.Select(s => s.SourceText)));
        foreach (var seg in Segments)
            sb.Append(" > ").Append(seg.SourceText);
        return sb.ToString();
    }

    public bool Equals(BsPipeline? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Sources.SequenceEqual(other.Sources)
            && Segments.SequenceEqual(other.Segments);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText);
        h.Add(SourceLine);
        foreach (var s in Sources) h.Add(s);
        foreach (var s in Segments) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// One segment of a <see cref="BsPipeline"/> (one <c>&gt; Target</c>). Either a function
/// call (with optional arguments, which may include <see cref="BsPlaceholder"/>s for
/// pipeline-value insertion) or a variable tap (<c>&gt; x</c> with no parens).
/// </summary>
public sealed record BsPipelineSegment : BsNode
{
    public required string Target { get; init; }
    public ImmutableArray<BsNode> Args { get; init; } = [];
    public ImmutableArray<string> RawArgs { get; init; } = [];
    public bool IsVariableTap { get; init; }

    public bool Equals(BsPipelineSegment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Target == other.Target
            && IsVariableTap == other.IsVariableTap
            && SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Args.SequenceEqual(other.Args)
            && RawArgs.SequenceEqual(other.RawArgs);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Target);
        h.Add(IsVariableTap);
        h.Add(SourceText);
        h.Add(SourceLine);
        foreach (var a in Args) h.Add(a);
        return h.ToHashCode();
    }
}

/// <summary>
/// A pipeline placeholder (<c>_</c>) — marks where a pipeline value inserts during
/// lowering. <see cref="Index"/> is the ordinal among multiple placeholders in the
/// same call (0-based).
/// </summary>
public sealed record BsPlaceholder : BsNode
{
    public int Index { get; init; }
}

// ── Declaration nodes (top-level only) ──

/// <summary>A single constant declaration row inside a <see cref="BsConstBlock"/>.</summary>
public sealed record BsConstDecl : BsNode
{
    public required string Name { get; init; }
    /// <summary>Declared C# type (e.g. "int", "string"). Open: may become a typed enum later.</summary>
    public string Type { get; init; } = "object";
    /// <summary>Verbatim initialiser expression source text (e.g. <c>42</c>, <c>"hi"</c>).</summary>
    public string? InitialValueExpression { get; init; }
}

/// <summary>A single mutable variable declaration row inside a <see cref="BsVarBlock"/>.</summary>
public sealed record BsVarDecl : BsNode
{
    public required string Name { get; init; }
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
}

/// <summary>
/// The <c>const { ... }</c> block (discussion notes §十二-C). Top-level only; a list of
/// <see cref="BsConstDecl"/> rows. Lowered to the <see cref="KitX.WorkflowV6.Ir.Workflow.Constants"/>
/// dictionary.
/// </summary>
public sealed record BsConstBlock : BsNode
{
    public ImmutableArray<BsConstDecl> Declarations { get; init; } = [];

    public bool Equals(BsConstBlock? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Declarations.SequenceEqual(other.Declarations);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText);
        h.Add(SourceLine);
        foreach (var d in Declarations) h.Add(d);
        return h.ToHashCode();
    }
}

/// <summary>
/// The <c>var { ... }</c> block (discussion notes §十二-C). Top-level only; a list of
/// <see cref="BsVarDecl"/> rows. Lowered to the <see cref="KitX.WorkflowV6.Ir.Workflow.GlobalVars"/>
/// dictionary.
/// </summary>
public sealed record BsVarBlock : BsNode
{
    public ImmutableArray<BsVarDecl> Declarations { get; init; } = [];

    public bool Equals(BsVarBlock? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Declarations.SequenceEqual(other.Declarations);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText);
        h.Add(SourceLine);
        foreach (var d in Declarations) h.Add(d);
        return h.ToHashCode();
    }
}

// ── Control-flow statement nodes ──

/// <summary>Base of statement-level BS AST nodes (anything that can sit in a body).</summary>
public abstract record BsStatement : BsNode;

/// <summary>
/// An <c>if &lt;condition&gt; { then-body } else { else-body }</c> statement.
/// <see cref="Condition"/> is a <see cref="BsNode"/> expression (typically a BsCall to
/// <c>HelperFuncCompare</c>, or a BsIdentifier referencing a bool PubVar — comparison
/// operators are disabled per §十二-B so conditions are always function calls or ids).
/// <c>else if</c> nests a <see cref="BsIf"/> inside <see cref="ElseBody"/>.
/// </summary>
public sealed record BsIf : BsStatement
{
    public required BsNode Condition { get; init; }
    public required ImmutableArray<BsStatement> ThenBody { get; init; } = [];
    public ImmutableArray<BsStatement> ElseBody { get; init; } = [];

    public bool Equals(BsIf? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Condition == other.Condition
            && ThenBody.SequenceEqual(other.ThenBody)
            && ElseBody.SequenceEqual(other.ElseBody);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText);
        h.Add(SourceLine);
        h.Add(Condition);
        foreach (var s in ThenBody) h.Add(s);
        foreach (var s in ElseBody) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// A <c>switch &lt;selector&gt; { 0: A; 1: B; default: C }</c> statement.
/// <see cref="Selector"/> is a <see cref="BsNode"/> expression yielding an integer index.
/// <see cref="Arms"/> carries arms 0..N-1 in source order; <see cref="Default"/> is the
/// fallback body (may be empty).
/// </summary>
public sealed record BsSwitch : BsStatement
{
    public required BsNode Selector { get; init; }
    public required ImmutableArray<ImmutableArray<BsStatement>> Arms { get; init; } = [];
    public ImmutableArray<BsStatement> Default { get; init; } = [];

    public bool Equals(BsSwitch? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (SourceText != other.SourceText || SourceLine != other.SourceLine) return false;
        if (Selector != other.Selector) return false;
        if (Arms.Length != other.Arms.Length) return false;
        for (int i = 0; i < Arms.Length; i++)
            if (!Arms[i].SequenceEqual(other.Arms[i])) return false;
        return Default.SequenceEqual(other.Default);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText); h.Add(SourceLine); h.Add(Selector);
        foreach (var a in Arms) foreach (var s in a) h.Add(s);
        foreach (var s in Default) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// A <c>forEach &lt;source&gt; as &lt;item&gt; { body }</c> statement (discussion notes
/// §3.3 #4, §十二-G). The source is a <see cref="BsNode"/> expression producing a
/// collection (typically <c>Range(...)</c> or a Json array). The body sees the current
/// element bound to <see cref="ItemName"/> as a real input — NOT the v5.1 string-name
/// injection anti-pattern.
/// </summary>
public sealed record BsForEach : BsStatement
{
    public required BsNode Source { get; init; }
    public required string ItemName { get; init; }
    public required ImmutableArray<BsStatement> Body { get; init; } = [];

    public bool Equals(BsForEach? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Source == other.Source
            && ItemName == other.ItemName
            && Body.SequenceEqual(other.Body);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText); h.Add(SourceLine); h.Add(Source); h.Add(ItemName);
        foreach (var s in Body) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>A <c>while &lt;condition&gt; { body }</c> statement (discussion notes §3.3 #5, §十二-E).</summary>
public sealed record BsWhile : BsStatement
{
    public required BsNode Condition { get; init; }
    public required ImmutableArray<BsStatement> Body { get; init; } = [];

    public bool Equals(BsWhile? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Condition == other.Condition
            && Body.SequenceEqual(other.Body);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText); h.Add(SourceLine); h.Add(Condition);
        foreach (var s in Body) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// A <c>break</c> statement (discussion notes §3.3 #6, §十二-D: no label — escapes the
/// nearest enclosing loop only).
/// </summary>
public sealed record BsBreak : BsStatement;

/// <summary>
/// A <c>continue</c> statement (§3.3 #7, §十二-D: no label — continues the nearest
/// enclosing loop only).
/// </summary>
public sealed record BsContinue : BsStatement;

/// <summary>
/// An <c>exit()</c> statement (§3.3 #8). Terminates the workflow (maps to <c>return</c>
/// in the generated structured C#). The v5 "Break" builtin renamed to avoid the
/// loop-break name clash. May carry an optional reason argument.
/// </summary>
public sealed record BsExit : BsStatement
{
    public BsNode? Reason { get; init; }
}

// ── Program root ──

/// <summary>
/// The root of a BS document: optional <see cref="BsConstBlock"/> + optional
/// <see cref="BsVarBlock"/> + an ordered top-level <see cref="Body"/> of statements.
/// Produced by the indented parser; lowered to a <see cref="KitX.WorkflowV6.Ir.Workflow"/>.
/// </summary>
public sealed record BsProgram : BsNode
{
    public BsConstBlock? ConstBlock { get; init; }
    public BsVarBlock? VarBlock { get; init; }
    public required ImmutableArray<BsStatement> Body { get; init; } = [];

    public bool Equals(BsProgram? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SourceText == other.SourceText
            && SourceLine == other.SourceLine
            && Equals(ConstBlock, other.ConstBlock)
            && Equals(VarBlock, other.VarBlock)
            && Body.SequenceEqual(other.Body);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SourceText); h.Add(SourceLine);
        h.Add(ConstBlock); h.Add(VarBlock);
        foreach (var s in Body) h.Add(s);
        return h.ToHashCode();
    }
}

// ── Extension helpers over BsNode (replaces the old ExprUtils Roslyn helpers) ──

/// <summary>Extension helpers over <see cref="BsNode"/>.</summary>
public static class BsNodeExtensions
{
    /// <summary>The string value when the node is a string literal, else null.</summary>
    public static string? AsStringLiteral(this BsNode? node)
        => node is BsLiteral { Kind: BsLiteralKind.String } lit ? lit.Value as string : null;

    /// <summary>The typed literal value (string/int/double/bool/char/null), else null.</summary>
    public static object? LiteralValue(this BsNode? node)
        => node is BsLiteral lit ? lit.Value : null;

    /// <summary>
    /// True when the source text is a C# character literal (e.g. <c>'\0'</c>, <c>'a'</c>).
    /// Lightweight structural test: char literals start/end with single quote, content is
    /// either one char or a backslash-escape pair. Inherited from v5.
    /// </summary>
    public static bool IsCharacterLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length < 3 || value[0] != '\'' || value[^1] != '\'') return false;
        var inner = value[1..^1];
        return inner.Length == 1 || (inner.Length == 2 && inner[0] == '\\');
    }
}