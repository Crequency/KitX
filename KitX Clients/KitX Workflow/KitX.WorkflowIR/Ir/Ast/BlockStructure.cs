namespace KitX.Workflow.Ir.Ast;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// BS block structure — the parse-tree representation of a BlockScript document.
//
// Migrated (semantics preserved) from the legacy KitX.Workflow.Models:
// BlockType / BlockDefinition / BlockScript / VariableDeclaration / Statements/*.
//
// These are produced by the BlockStructureRecognizer + BSParser (pure scanning +
// Superpower combinators, A-grade) and consumed by BsLowerer. They mirror the BS
// source 1:1 and are NOT the canonical IR (the IR normalises block structure and
// carries pipelines as structured AST). Records now, so value-comparable.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Block type enumeration (v5.0): no LoopBlock anymore.</summary>
public enum BlockType
{
    /// <summary>Constants block — read-only constants (must have initial value, §3.1).</summary>
    ConstBlock,

    /// <summary>Main block — entry point.</summary>
    MainBlock,

    /// <summary>Named block — jumped to by Branch/ForLoop/Switch/Goto. May carry ##BlockVars.</summary>
    NamedBlock,

    /// <summary>Public variable block — global mutable variables, cross-block read/write (§3.2).</summary>
    PubVarBlock,
}

/// <summary>A variable declaration (used in ConstBlock, PubVarBlock, and ##BlockVars).</summary>
public sealed record VariableDeclaration
{
    public required string Name { get; init; }
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
    public object? DefaultValue { get; init; }
}

/// <summary>Base of statements within a block of the BS parse tree.</summary>
public abstract record BlockStatement
{
    /// <summary>Debug statement id — preserved through BP⇄BS round-trip. Empty = unset.</summary>
    public string StatementId { get; init; } = string.Empty;

    /// <summary>1-based line number in source.</summary>
    public int LineNumber { get; init; }

    /// <summary>Original source text for this statement.</summary>
    public string SourceCode { get; init; } = string.Empty;

    /// <summary>Comment attached to this statement (v5.0 §9 bidirectional retention). Null = none.</summary>
    public string? Comment { get; init; }
}

/// <summary>An expression statement (a call, assignment, or pipeline).</summary>
public sealed record ExpressionStatement : BlockStatement
{
    /// <summary>The expression text to execute.</summary>
    public string Expression { get; init; } = string.Empty;

    /// <summary>
    /// The pre-parsed BS expression when extracted from source by the statement extractor,
    /// consumed by BsLowerer to walk the structured AST. Null when the statement has no
    /// analyzable body.
    /// </summary>
    public BSExpression? ParsedExpression { get; init; }

    /// <summary>
    /// The assigned variable when ParsedExpression came from <c>x = Func(...)</c>; null for
    /// bare expression statements.
    /// </summary>
    public string? AssignedVariable { get; init; }
}

/// <summary>A flow-control statement (Branch/ForLoop/Switch/Goto/Break/Exit).</summary>
public sealed record FlowControlStatement : BlockStatement
{
    /// <summary>Function name in BS source (e.g. "Branch", "ForLoop", "Exit").</summary>
    public string? FunctionName { get; init; }

    /// <summary>Condition expression text (for Branch/Switch).</summary>
    public string ConditionExpression { get; init; } = string.Empty;

    /// <summary>
    /// Extra positional arguments for variadic control-flow forms. ForLoop stores
    /// [from, to, step, indexName] here.
    /// </summary>
    public IReadOnlyList<string> FlowArguments { get; init; } = [];

    /// <summary>Outgoing arms (replaces the legacy TrueBlock/FalseBlock/Loopback trio).</summary>
    public IReadOnlyList<BranchArm> Arms { get; init; } = [];
}

/// <summary>A single block definition in the BS parse tree.</summary>
public sealed record BlockDefinition
{
    public required BlockType Type { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Variable declarations; semantics depend on Type (const vs pubvar vs blockvar).</summary>
    public IReadOnlyList<VariableDeclaration> Variables { get; init; } = [];

    /// <summary>Block-local variable declarations (##BlockVars, v5.0 §3.3). MainBlock/NamedBlock only.</summary>
    public IReadOnlyList<VariableDeclaration> BlockVars { get; init; } = [];

    /// <summary>Whether the body was introduced by an explicit ##BlockBody marker.</summary>
    public bool HasExplicitBlockBody { get; init; }

    /// <summary>Block-level comment (v5.0 §9.3) — <c>// ...</c> above or after the #Block marker.</summary>
    public string? Comment { get; init; }

    /// <summary>Statements in this block.</summary>
    public IReadOnlyList<BlockStatement> Statements { get; init; } = [];

    /// <summary>1-based line number where this block starts in source.</summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Next block to execute via sequential fall-through. v5.0: no implicit fall-through;
    /// a block must end with a control-flow statement. This carries the Goto target when the
    /// block ends with <c>Goto("name")</c>.
    /// </summary>
    public string? NextBlockName { get; init; }
}

/// <summary>The parsed BlockScript document (parse tree root).</summary>
public sealed record BlockScript
{
    public BlockDefinition? ConstBlock { get; init; }
    public BlockDefinition? PubVarBlock { get; init; }
    public BlockDefinition? MainBlock { get; init; }

    /// <summary>Named blocks keyed by name.</summary>
    public IReadOnlyDictionary<string, BlockDefinition> NamedBlocks { get; init; }
        = new Dictionary<string, BlockDefinition>();

    /// <summary>All blocks in order of appearance.</summary>
    public IReadOnlyList<BlockDefinition> AllBlocks { get; init; } = [];

    /// <summary>Raw source code (parsed input, may not include helper functions).</summary>
    public string SourceCode { get; init; } = string.Empty;

    /// <summary>Full source including merged helper functions (for execution).</summary>
    public string FullSourceCode { get; init; } = string.Empty;

    /// <summary>Helper functions available in the script execution context.</summary>
    public IReadOnlyList<HelperFunction> HelperFunctions { get; init; } = [];

    /// <summary>Gets a block by name (NamedBlocks first, then standard blocks).</summary>
    public BlockDefinition? GetBlockByName(string name)
    {
        if (NamedBlocks.TryGetValue(name, out var named)) return named;
        if (MainBlock?.Name == name) return MainBlock;
        if (ConstBlock?.Name == name) return ConstBlock;
        if (PubVarBlock?.Name == name) return PubVarBlock;
        return null;
    }
}
