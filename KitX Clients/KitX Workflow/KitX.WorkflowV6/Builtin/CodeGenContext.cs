namespace KitX.WorkflowV6.Builtin;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

// ─────────────────────────────────────────────────────────────────────────────
// CodeGenContext — the Roslyn builder surface passed to ICodeGenHandler.EmitCSharp.
//
// Inherited contract from KitX.WorkflowIR.Builtin.CodeGenContext: self-contained
// Roslyn builders (Literal, GInvoke, ...) plus type-aware helpers (ResolveArgument,
// EmitValueAssignment, PluginCallExpression, ...) injected by the backend so the
// Builtin assembly stays free of the codegen God-module.
//
// The v6 structured-C# target (discussion notes §5.3) means the helper that emitted
/// v5's <c>G.NextBlock = G.Member(args); break;</c> trampoline step is gone. Instead,
/// control-flow emission is now structural (if/foreach/while/break/continue in the
/// generated C# itself); only side-effect entry points (Print, plugin dispatch, PubVar
/// Get/Set) remain on the <c>G</c> surface. The builders below preserve the v5 surface
/// that is still meaningful; the rest is filled in during implementation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Roslyn emission context handed to <see cref="ICodeGenHandler.EmitCSharp"/>. Provides
/// self-contained SyntaxFactory builders; type-aware helpers are injected by the backend.
/// </summary>
public sealed class CodeGenContext
{
    /// <summary>Inferred C# type name per PubVar/Const identifier (set by backend).</summary>
    public IReadOnlyDictionary<string, string> PubVarTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Return type per helper function name (set by backend).</summary>
    public IReadOnlyDictionary<string, string> HelperReturnTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Variable names injected at runtime (e.g. forEach element bindings).</summary>
    public IReadOnlySet<string> InjectedVariableNames { get; init; } = new HashSet<string>();

    /// <summary>Resolves a formatted argument string into a Roslyn expression. Injected by backend.</summary>
    public Func<string, ExpressionSyntax> ResolveArgument { get; init; } = _ =>
        throw new InvalidOperationException("CodeGenContext.ResolveArgument not wired (v6 backend not yet implemented).");

    /// <summary>Emits value-assignment statements for a value-producing expression. Injected by backend.</summary>
    public Func<string?, ExpressionSyntax, string, List<StatementSyntax>> EmitValueAssignment { get; init; } = (_, _, _) =>
        throw new InvalidOperationException("CodeGenContext.EmitValueAssignment not wired (v6 backend not yet implemented).");

    /// <summary>Builds a G.PluginCall(...) expression for a dotted plugin-method call. Injected by backend.</summary>
    public Func<string, string, IReadOnlyList<string>, ExpressionSyntax>? PluginCallExpression { get; init; }

    // ── Self-contained Roslyn builders (pure SyntaxFactory, no backend dependency). ──

    /// <summary>A string literal expression.</summary>
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

    /// <summary>Emits <c>var target = init;</c>.</summary>
    public List<StatementSyntax> EmitVarLocal(string target, ExpressionSyntax init)
        => [LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .AddVariables(VariableDeclarator(Identifier(target))
                    .WithInitializer(EqualsValueClause(init))))];

    /// <summary>Parses a C# expression from source text.</summary>
    public ExpressionSyntax Parse(string code) => ParseExpression(code);

    /// <summary>A <c>return;</c> statement. Used by exit()'s structured-C# lowering.</summary>
    public StatementSyntax Return() => ReturnStatement();
}
