namespace KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// BS AST — BlockScript source tree (lossless), distinct from the structured IR.
//
// Inherited split from KitX.WorkflowIR: the AST mirrors BS source 1:1 (so BS round-
// trip is lossless and the indented parser can carry verbatim text on every node),
// while the IR is the canonical lowered form. Lowering is a one-way transform
// (AST → IR); rendering IR → BS text does not need the AST.
//
// The v6 BS grammar (indented, Python-style, see discussion notes §4.1) is the
// open design surface. The placeholder types here keep the parser/lowering/codegen
// handler interfaces (see Builtin.IFunctionHandlers) well-typed from day one; their
// concrete shape will be refined by the implementation plan.
//
// Two near-certain types are stubbed now:
//   • BsCall — a function invocation (covers bare calls, member calls, nested calls
//     used as arguments). The v6 pipeline form <c>a, b > F > G > x</c> parses into a
//     BsPipeline rooted on BsCalls; control-flow keywords (<c>if</c>, <c>forEach</c>,
//     <c>while</c>, ...) parse into BsControlFlow nodes that carry BsCall bodies.
//   • BsPipeline — the functional <c>&gt;</c> / <c>=</c> data-flow syntax tree.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Root of the BS source AST. Every node may carry verbatim source text for lossless rendering.</summary>
public abstract record BsNode
{
    /// <summary>Verbatim source text this node was parsed from.</summary>
    public string SourceText { get; init; } = string.Empty;

    /// <summary>1-based source line where this node starts.</summary>
    public int SourceLine { get; init; }
}

/// <summary>
/// A function invocation. Placeholder shape; refined when the v6 indented parser is
/// designed. Will cover bare calls (<c>Print(x)</c>), member calls (<c>Plugin.Method(args)</c>),
/// and nested calls used as arguments.
/// </summary>
public sealed record BsCall : BsNode
{
    public required string MethodName { get; init; }
    public string FullMethodName { get; init; } = string.Empty;
    public IReadOnlyList<BsNode> Args { get; init; } = [];
    public IReadOnlyList<string> RawArgs { get; init; } = [];
}

/// <summary>
/// A pipeline expression (<c>a, b > F > G > x</c>). Placeholder shape. The structured
/// IR's <c>PipelineStatement</c> is lowered from this; control-flow lowering is the
/// responsibility of the per-role handlers (see Builtin.IFunctionHandlers).
/// </summary>
public sealed record BsPipeline : BsNode
{
    public required IReadOnlyList<BsNode> Sources { get; init; }
    public required IReadOnlyList<BsPipelineSegment> Segments { get; init; }
}

/// <summary>One segment of a <see cref="BsPipeline"/> (one <c>&gt; Target</c>).</summary>
public sealed record BsPipelineSegment : BsNode
{
    public required string Target { get; init; }
    public IReadOnlyList<BsNode> Args { get; init; } = [];
    public bool IsVariableTap { get; init; }
}
