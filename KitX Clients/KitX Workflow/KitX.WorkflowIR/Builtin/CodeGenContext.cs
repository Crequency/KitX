namespace KitX.Workflow.Builtin;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

// ─────────────────────────────────────────────────────────────────────────────
// CodeGenContext — the Roslyn builder surface passed to ICodeGenHandler.EmitCSharp.
//
// Migrated from the legacy CSEmitContext (an A-grade fluent builder). The legacy
// version delegated its non-trivial helpers (ResolveArgument / EmitValueAssignment /
// PluginCallExpression) to CFG2CSConverter static methods — a God module that the
// new library splits into focused emitters (Phase 7).
//
// Here, CodeGenContext is split into two layers:
//   1. Self-contained Roslyn builders (Literal, GInvoke, EmitNextBlockAssignment,
//      Return, EmitVarLocal, GInvokeStatement) — pure SyntaxFactory calls, no
//      dependencies, implemented right here.
//   2. Type-aware helpers (ResolveArgument, EmitValueAssignment, PluginCall*)
//      — these need the type-inference map + helper-return-type map, which live
//      in the Roslyn backend (Phase 7). They are exposed as injectable delegates
//      so Phase 7 wires them without CodeGenContext knowing about the backend.
//
// Functions emit against this surface; the backend supplies the delegates at
// emission time. This keeps the Builtin assembly free of the CFG2CSConverter God
// module while preserving the fluent builder ergonomics.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Roslyn emission context handed to <see cref="ICodeGenHandler.EmitCSharp"/>. Provides
/// self-contained SyntaxFactory builders plus type-aware helpers injected by the
/// Roslyn backend (Phase 7).
/// </summary>
public sealed class CodeGenContext
{
    /// <summary>Inferred C# type name per PubVar/Const identifier (set by backend).</summary>
    public IReadOnlyDictionary<string, string> PubVarTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Return type per helper function name (set by backend).</summary>
    public IReadOnlyDictionary<string, string> HelperReturnTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Variable names injected at runtime via G.Set (e.g. ForLoop indexName). References
    /// to these resolve to <c>G.Get("name")</c> instead of bare identifiers.
    /// </summary>
    public IReadOnlySet<string> InjectedVariableNames { get; init; } = new HashSet<string>();

    /// <summary>
    /// Resolves a formatted argument string (PubVar / literal / identifier) into a Roslyn
    /// expression. Injected by the backend (it needs the type map + literal-parsing logic).
    /// </summary>
    public Func<string, ExpressionSyntax> ResolveArgument { get; init; } = _ =>
        throw new InvalidOperationException("CodeGenContext.ResolveArgument not wired (Phase 7 backend not built).");

    /// <summary>
    /// Emits value-assignment statements for a value-producing expression (typed local +
    /// G.Set sync, or bare expression statement). Injected by the backend.
    /// </summary>
    public Func<string?, ExpressionSyntax, string, List<StatementSyntax>> EmitValueAssignment { get; init; } = (_, _, _) =>
        throw new InvalidOperationException("CodeGenContext.EmitValueAssignment not wired (Phase 7 backend not built).");

    /// <summary>Builds a G.PluginCall(...) expression for a dotted plugin-method call. Injected by backend.</summary>
    public Func<string, string, IReadOnlyList<string>, ExpressionSyntax>? PluginCallExpression { get; init; }

    /// <summary>Builds a G.PluginCallWithTarget(...) expression for a cross-device call. Injected by backend.</summary>
    public Func<string, string, IReadOnlyList<string>, string, ExpressionSyntax>? PluginCallWithTargetExpression { get; init; }

    /// <summary>
    /// Resolves a formatted argument string into a Roslyn expression, treating it as a
    /// typed PubVar read (<c>G.Get&lt;typeName&gt;("arg")</c>). Injected by the backend
    /// (the generic-Get construction lives with the type-inference code).
    /// </summary>
    public Func<string, string, ExpressionSyntax> GetInvocation { get; init; } = (_, _) =>
        throw new InvalidOperationException("CodeGenContext.GetInvocation not wired (Phase 7 backend not built).");

    /// <summary>Wraps an expression in <c>ConvertTo&lt;T&gt;(...)</c>. Injected by the backend.</summary>
    public Func<string, ExpressionSyntax, ExpressionSyntax> ConvertTo { get; init; } = (_, _) =>
        throw new InvalidOperationException("CodeGenContext.ConvertTo not wired (Phase 7 backend not built).");

    // ── Self-contained Roslyn builders (pure SyntaxFactory, no backend dependency). ──

    /// <summary>A string literal expression (quoted in generated source).</summary>
    public ExpressionSyntax Literal(string value)
        => LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));

    /// <summary>Builds a <c>G.member(args...)</c> invocation expression.</summary>
    public InvocationExpressionSyntax GInvoke(string member, params ExpressionSyntax[] args)
        => InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("G"), IdentifierName(member)),
            ArgumentList(SeparatedList(args.Select(Argument))));

    /// <summary>Builds a <c>G.member(args...)</c> expression statement.</summary>
    public StatementSyntax GInvokeStatement(string member, params ExpressionSyntax[] args)
        => ExpressionStatement(GInvoke(member, args));

    /// <summary>Emits <c>var pubVarTarget = init;</c> (inferred-type local declaration).</summary>
    public List<StatementSyntax> EmitVarLocal(string pubVarTarget, ExpressionSyntax init)
        => [LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .AddVariables(VariableDeclarator(Identifier(pubVarTarget))
                    .WithInitializer(EqualsValueClause(init))))];

    /// <summary>Parses a C# expression from source text.</summary>
    public ExpressionSyntax Parse(string code) => ParseExpression(code);

    /// <summary>A <c>return;</c> statement.</summary>
    public StatementSyntax Return() => ReturnStatement();

    /// <summary>
    /// Emits the shared flow-control form: <c>G.NextBlock = G.{member}(args); break;</c>.
    /// Used by Branch/ForLoop/Switch/Goto/Flip descriptors.
    /// </summary>
    public List<StatementSyntax> EmitNextBlockAssignment(string member, params ExpressionSyntax[] args)
        =>
        [
            ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("NextBlock")),
                GInvoke(member, args))),
            BreakStatement()
        ];
}
