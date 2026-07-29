namespace KitX.WorkflowV6.Ir.Ast;

using System.Text.Json.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// KS AST — KScript source tree (lossless), distinct from the structured IR.
//
// Inherited split from KitX.WorkflowIR: the AST mirrors KS source 1:1 (so KS round-
// trip is lossless and the indented parser can carry verbatim text on every node),
// while the IR is the canonical lowered form. Lowering is a one-way transform
// (AST → IR); rendering IR → KS text does not need the AST.
//
// The v6 KS grammar (indented, Python-style, see discussion notes §4.1 + §十二-A:
// 4-space indent, no tabs, +4 per level) is parsed into the node types below. Every
// node remembers its verbatim source text so the renderer never re-parses.
//
// Node family overview (closed set, mirrors the 9 control-flow primitives in §3.3 +
// the const/var declaration blocks in §十二-C):
//
//   Expression nodes (KsNode subclasses, reusable as args / sources / conditions):
//     KsLiteral       — string/int/double/bool/char/null literal
//     KsIdentifier    — variable / PubVar / ConstBlock name reference
//     KsCall          — function invocation (bare, member, or nested-as-arg)
//     KsPipeline       — the `>` / `=` data-flow syntax tree
//     KsPipelineSegment — one `> Target` of a KsPipeline (call OR variable tap)
//     KsPlaceholder    — `_`, the pipeline-value insertion marker
//
//   Declaration nodes (top-level only, from `const { ... }` / `var { ... }` blocks):
//     KsConstDecl     — one row inside a const block
//     KsVarDecl       — one row inside a var block
//     KsConstBlock    — the `const { ... }` block (list of KsConstDecl)
//     KsVarBlock      — the `var { ... }` block (list of KsVarDecl)
//
//   Control-flow nodes (statement-level; bodies are child KsStatement lists):
//     KsIf            — `if cond { body } else { body }` (else-if nests via KsIf in else body)
//     KsSwitch        — `switch sel { 0: A; 1: B; default: C }`
//     KsForEach       — `forEach source as item { body }`
//     KsWhile         — `while cond { body }`
//     KsBreak         — `break`
//     KsContinue      — `continue`
//
//   Program root:
//     KsProgram       — the whole document (optional const/var blocks + top-level body)
//
// Per discussion notes §十二-K, control-flow keywords do NOT route through
// IBuiltinFunction — they are parsed directly into their own AST node kinds by the
// indented parser, and lowered into their own IR Statement kinds (IfStatement,
// ForEachStatement, ...) by KsLowerer.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Root of the KS source AST. Every node may carry verbatim source text for lossless rendering.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$ksNodeKind")]
[JsonDerivedType(typeof(KsLiteral), "Literal")]
[JsonDerivedType(typeof(KsIdentifier), "Identifier")]
[JsonDerivedType(typeof(KsCall), "Call")]
[JsonDerivedType(typeof(KsPipeline), "Pipeline")]
[JsonDerivedType(typeof(KsPipelineSegment), "PipelineSegment")]
[JsonDerivedType(typeof(KsPlaceholder), "Placeholder")]
[JsonDerivedType(typeof(KsDictLiteral), "DictLiteral")]
public abstract record KsNode
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

/// <summary>Discriminated literal kinds mirroring KScript's supported types.</summary>
public enum KsLiteralKind { String, Integer, Double, Boolean, Char, Null }

/// <summary>A literal value (string/int/bool/double/char/null) with its typed value.</summary>
public sealed record KsLiteral : KsNode
{
    public required KsLiteralKind Kind { get; init; }

    /// <summary>
    /// The literal value (string/int/bool/double/char/null). Stored as object? so the
    /// AST carries the typed value (not just source text). The JsonConverter attribute
    /// ensures System.Text.Json round-trips the boxed value as its runtime type (string
    /// stays string, int stays int) rather than collapsing to JsonElement.
    /// </summary>
    [property: System.Text.Json.Serialization.JsonConverter(typeof(Serialization.KsLiteralValueConverter))]
    public object? Value { get; init; }

    public bool Equals(KsLiteral? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && Equals(Value, other.Value);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Kind);
        h.Add(Value);
        return h.ToHashCode();
    }
}

/// <summary>
/// One key→value entry of a <see cref="KsDictLiteral"/>. Key is a string literal;
/// Value is a scalar literal or a const identifier reference (flat — no nesting).
/// </summary>
public sealed record KsDictEntry
{
    public required KsNode Key { get; init; }
    public required KsNode Value { get; init; }

    public bool Equals(KsDictEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Key, other.Key) && Equals(Value, other.Value);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Key);
        h.Add(Value);
        return h.ToHashCode();
    }
}

/// <summary>
/// A dict literal <c>{k: v, ...}</c>. Only valid as a const/var declaration initialiser
/// (Package/Dict-Type-Design.md §2.1) — not a general expression, never appears in pipeline
/// sources or function arguments. Values are flat scalars; nesting is rejected at parse time.
/// </summary>
public sealed record KsDictLiteral : KsNode
{
    public required ImmutableArray<KsDictEntry> Entries { get; init; }

    public bool Equals(KsDictLiteral? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Entries.SequenceEqual(other.Entries);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var e in Entries) h.Add(e);
        return h.ToHashCode();
    }
}

/// <summary>An identifier reference (variable / PubVar / ConstBlock name).</summary>
public sealed record KsIdentifier : KsNode
{
    public required string Name { get; init; }

    public bool Equals(KsIdentifier? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name;
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        return h.ToHashCode();
    }
}

/// <summary>
/// A function invocation. Covers bare calls (<c>Print(x)</c>), member-access calls
/// (<c>Plugin.Method(args)</c>), and nested calls used as arguments.
/// <see cref="MethodName"/> is the short name (last segment);
/// <see cref="FullMethodName"/> the full dotted path. Args are the structured argument
/// expressions (may themselves be KsCalls); RawArgs preserves each argument's source
/// text for string-based lowering paths that haven't been migrated yet.
/// </summary>
public sealed record KsCall : KsNode
{
    public required string MethodName { get; init; }
    public string FullMethodName { get; init; } = string.Empty;
    public ImmutableArray<KsNode> Args { get; init; } = [];
    public ImmutableArray<string> RawArgs { get; init; } = [];

    public bool Equals(KsCall? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MethodName == other.MethodName
            && FullMethodName == other.FullMethodName
            && Args.SequenceEqual(other.Args)
            && RawArgs.SequenceEqual(other.RawArgs);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(MethodName);
        h.Add(FullMethodName);
        foreach (var a in Args) h.Add(a);
        return h.ToHashCode();
    }
}

/// <summary>
/// A pipeline expression (<c>a, b &gt; F > G > x</c>). <see cref="Sources"/> is the
/// comma-separated LHS; <see cref="Segments"/> is the ordered list of
/// <see cref="KsPipelineSegment"/> (each a call or a variable tap).
/// Lowered to a <see cref="KitX.WorkflowV6.Ir.Statements.PipelineStatement"/>.
/// Inherits from <see cref="KsStatement"/> so it can sit in a statement body (the
/// pipeline is the only data-flow construct, and at the statement level it IS the
/// statement — there's no separate "expression statement" wrapper).
/// </summary>
public sealed record KsPipeline : KsStatement
{
    public required ImmutableArray<KsNode> Sources { get; init; }
    public required ImmutableArray<KsPipelineSegment> Segments { get; init; }

    public string RenderPipelineSource()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Join(", ", Sources.Select(s => s.SourceText)));
        foreach (var seg in Segments)
            sb.Append(" > ").Append(seg.SourceText);
        return sb.ToString();
    }

    public bool Equals(KsPipeline? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Sources.SequenceEqual(other.Sources)
            && Segments.SequenceEqual(other.Segments);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var s in Sources) h.Add(s);
        foreach (var s in Segments) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// One segment of a <see cref="KsPipeline"/> (one <c>&gt; Target</c>). Either a function
/// call (with optional arguments, which may include <see cref="KsPlaceholder"/>s for
/// pipeline-value insertion) or a variable tap (<c>&gt; x</c> with no parens).
/// </summary>
public sealed record KsPipelineSegment : KsNode
{
    public required string Target { get; init; }
    public ImmutableArray<KsNode> Args { get; init; } = [];
    public ImmutableArray<string> RawArgs { get; init; } = [];
    public bool IsVariableTap { get; init; }

    /// <summary>Inline <c>//</c> comment on this segment's line (multi-line pipelines only).</summary>
    public string? Comment { get; set; }

    public bool Equals(KsPipelineSegment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Target == other.Target
            && IsVariableTap == other.IsVariableTap
            && Args.SequenceEqual(other.Args)
            && RawArgs.SequenceEqual(other.RawArgs);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Target);
        h.Add(IsVariableTap);
        foreach (var a in Args) h.Add(a);
        return h.ToHashCode();
    }
}

/// <summary>
/// A pipeline placeholder (<c>_</c>) — marks where a pipeline value inserts during
/// lowering.
/// </summary>
public sealed record KsPlaceholder : KsNode
{
    public bool Equals(KsPlaceholder? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return true;
    }

    public override int GetHashCode()
    {
        return typeof(KsPlaceholder).GetHashCode();
    }
}

// ── Declaration nodes (top-level only) ──

/// <summary>A single constant declaration row inside a <see cref="KsConstBlock"/>.</summary>
public sealed record KsConstDecl : KsNode
{
    public required string Name { get; init; }
    /// <summary>Declared C# type (e.g. "int", "string"). Open: may become a typed enum later.</summary>
    public string Type { get; init; } = "object";
    /// <summary>Verbatim initialiser expression source text (e.g. <c>42</c>, <c>"hi"</c>).</summary>
    public string? InitialValueExpression { get; init; }
    /// <summary>
    /// Structured dict-literal initialiser, set only when <see cref="Type"/> == "dict" and the
    /// row has a <c>{k: v, ...}</c> initialiser (Package/Dict-Type-Design.md §2.1). Null otherwise.
    /// </summary>
    public KsDictLiteral? DictInitializer { get; init; }

    public bool Equals(KsConstDecl? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && Type == other.Type
            && InitialValueExpression == other.InitialValueExpression
            && Equals(DictInitializer, other.DictInitializer);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        h.Add(Type);
        h.Add(InitialValueExpression);
        h.Add(DictInitializer);
        return h.ToHashCode();
    }
}

/// <summary>A single mutable variable declaration row inside a <see cref="KsVarBlock"/>.</summary>
public sealed record KsVarDecl : KsNode
{
    public required string Name { get; init; }
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
    /// <summary>
    /// Structured dict-literal initialiser, set only when <see cref="Type"/> == "dict" and the
    /// row has a <c>{k: v, ...}</c> initialiser (Package/Dict-Type-Design.md §2.1). Null otherwise.
    /// </summary>
    public KsDictLiteral? DictInitializer { get; init; }

    public bool Equals(KsVarDecl? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && Type == other.Type
            && InitialValueExpression == other.InitialValueExpression
            && Equals(DictInitializer, other.DictInitializer);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        h.Add(Type);
        h.Add(InitialValueExpression);
        h.Add(DictInitializer);
        return h.ToHashCode();
    }
}

/// <summary>
/// The <c>const { ... }</c> block (discussion notes §十二-C). Top-level only; a list of
/// <see cref="KsConstDecl"/> rows. Lowered to the <see cref="KitX.WorkflowV6.Ir.Workflow.Constants"/>
/// dictionary.
/// </summary>
public sealed record KsConstBlock : KsNode
{
    public ImmutableArray<KsConstDecl> Declarations { get; init; } = [];

    public bool Equals(KsConstBlock? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Declarations.SequenceEqual(other.Declarations);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var d in Declarations) h.Add(d);
        return h.ToHashCode();
    }
}

/// <summary>
/// The <c>var { ... }</c> block (discussion notes §十二-C). Top-level only; a list of
/// <see cref="KsVarDecl"/> rows. Lowered to the <see cref="KitX.WorkflowV6.Ir.Workflow.GlobalVars"/>
/// dictionary.
/// </summary>
public sealed record KsVarBlock : KsNode
{
    public ImmutableArray<KsVarDecl> Declarations { get; init; } = [];

    public bool Equals(KsVarBlock? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Declarations.SequenceEqual(other.Declarations);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var d in Declarations) h.Add(d);
        return h.ToHashCode();
    }
}

// ── Control-flow statement nodes ──

/// <summary>Base of statement-level KS AST nodes (anything that can sit in a body).</summary>
public abstract record KsStatement : KsNode
{
    /// <summary>Leading full-line <c>//</c> comment(s) above this statement (joined by <c>\n</c>).</summary>
    public string? LeadingComment { get; set; }

    /// <summary>Inline <c>//</c> comment on this statement's header line.</summary>
    public string? TrailingComment { get; set; }
}

/// <summary>
/// An <c>if &lt;condition&gt; { then-body } else { else-body }</c> statement.
/// <see cref="Condition"/> is a <see cref="KsNode"/> expression (typically a KsCall to
/// <c>Compare</c>, or a KsIdentifier referencing a bool PubVar — comparison
/// operators are disabled per §十二-B so conditions are always function calls or ids).
/// <c>else if</c> nests a <see cref="KsIf"/> inside <see cref="ElseBody"/>.
/// </summary>
public sealed record KsIf : KsStatement
{
    public required KsNode Condition { get; init; }
    public required ImmutableArray<KsStatement> ThenBody { get; init; } = [];
    public ImmutableArray<KsStatement> ElseBody { get; init; } = [];

    public bool Equals(KsIf? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Condition == other.Condition
            && ThenBody.SequenceEqual(other.ThenBody)
            && ElseBody.SequenceEqual(other.ElseBody);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Condition);
        foreach (var s in ThenBody) h.Add(s);
        foreach (var s in ElseBody) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// A <c>switch &lt;selector&gt; { 0: A; 1: B; default: C }</c> statement.
/// <see cref="Selector"/> is a <see cref="KsNode"/> expression yielding an integer index.
/// <see cref="Arms"/> carries arms 0..N-1 in source order; <see cref="Default"/> is the
/// fallback body (may be empty). <see cref="ArmLabels"/> stores the integer label for
/// each arm (value-match semantics: selector value is compared against labels, not used
/// as a 0-based index).
/// </summary>
public sealed record KsSwitch : KsStatement
{
    public required KsNode Selector { get; init; }
    public required ImmutableArray<ImmutableArray<KsStatement>> Arms { get; init; } = [];
    public ImmutableArray<int> ArmLabels { get; init; } = [];
    public ImmutableArray<KsStatement> Default { get; init; } = [];

    public bool Equals(KsSwitch? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Selector != other.Selector) return false;
        if (Arms.Length != other.Arms.Length) return false;
        for (int i = 0; i < Arms.Length; i++)
            if (!Arms[i].SequenceEqual(other.Arms[i])) return false;
        if (!ArmLabels.SequenceEqual(other.ArmLabels)) return false;
        return Default.SequenceEqual(other.Default);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Selector);
        foreach (var a in Arms) foreach (var s in a) h.Add(s);
        foreach (var label in ArmLabels) h.Add(label);
        foreach (var s in Default) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// A <c>forEach &lt;source&gt; as &lt;item&gt; { body }</c> statement (discussion notes
/// §3.3 #4, §十二-G). The source is a <see cref="KsNode"/> expression producing a
/// collection (typically <c>Range(...)</c> or a Json array). The body sees the current
/// element bound to <see cref="ItemName"/> as a real input — NOT the v5.1 string-name
/// injection anti-pattern.
/// </summary>
public sealed record KsForEach : KsStatement
{
    public required KsNode Source { get; init; }
    public required string ItemName { get; init; }
    public required ImmutableArray<KsStatement> Body { get; init; } = [];

    public bool Equals(KsForEach? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Source == other.Source
            && ItemName == other.ItemName
            && Body.SequenceEqual(other.Body);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Source); h.Add(ItemName);
        foreach (var s in Body) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>A <c>while &lt;condition&gt; { body }</c> statement (discussion notes §3.3 #5, §十二-E).</summary>
public sealed record KsWhile : KsStatement
{
    public required KsNode Condition { get; init; }
    public required ImmutableArray<KsStatement> Body { get; init; } = [];

    public bool Equals(KsWhile? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Condition == other.Condition
            && Body.SequenceEqual(other.Body);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Condition);
        foreach (var s in Body) h.Add(s);
        return h.ToHashCode();
    }
}

/// <summary>
/// A <c>break</c> statement (discussion notes §3.3 #6, §十二-D: no label — escapes the
/// nearest enclosing loop only).
/// </summary>
public sealed record KsBreak : KsStatement
{
    public bool Equals(KsBreak? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return true;
    }

    public override int GetHashCode()
    {
        return typeof(KsBreak).GetHashCode();
    }
}

/// <summary>
/// A <c>continue</c> statement (§3.3 #7, §十二-D: no label — continues the nearest
/// enclosing loop only).
/// </summary>
public sealed record KsContinue : KsStatement
{
    public bool Equals(KsContinue? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return true;
    }

    public override int GetHashCode()
    {
        return typeof(KsContinue).GetHashCode();
    }
}

// ── Program root ──

/// <summary>
/// The root of a KS document: optional <see cref="KsConstBlock"/> + optional
/// <see cref="KsVarBlock"/> + an ordered top-level <see cref="Body"/> of statements.
/// Produced by the indented parser; lowered to a <see cref="KitX.WorkflowV6.Ir.Workflow"/>.
/// </summary>
public sealed record KsProgram : KsNode
{
    public KsConstBlock? ConstBlock { get; init; }
    public KsVarBlock? VarBlock { get; init; }
    public required ImmutableArray<KsStatement> Body { get; init; } = [];

    public bool Equals(KsProgram? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(ConstBlock, other.ConstBlock)
            && Equals(VarBlock, other.VarBlock)
            && Body.SequenceEqual(other.Body);
    }

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(ConstBlock); h.Add(VarBlock);
        foreach (var s in Body) h.Add(s);
        return h.ToHashCode();
    }
}

// ── Extension helpers over KsNode (replaces the old ExprUtils Roslyn helpers) ──

/// <summary>Extension helpers over <see cref="KsNode"/>.</summary>
public static class KsNodeExtensions
{
    /// <summary>The string value when the node is a string literal, else null.</summary>
    public static string? AsStringLiteral(this KsNode? node)
        => node is KsLiteral { Kind: KsLiteralKind.String } lit ? lit.Value as string : null;

    /// <summary>The typed literal value (string/int/double/bool/char/null), else null.</summary>
    public static object? LiteralValue(this KsNode? node)
        => node is KsLiteral lit ? lit.Value : null;

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