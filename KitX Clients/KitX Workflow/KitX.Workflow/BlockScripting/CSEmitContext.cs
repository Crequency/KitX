using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Context passed to <see cref="IBuiltinFunctionDefinition.EmitStatements"/> during
/// CFG→C# generation. Holds the per-script type map and shared emission helpers so each
/// builtin descriptor can emit its C# without reimplementing boilerplate, and so
/// <see cref="CFG2CSGenerator"/> dispatches without hardcoding function names.
/// </summary>
public sealed class CSEmitContext
{
    /// <summary>Inferred C# type name per PubVar/Const identifier.</summary>
    public Dictionary<string, string> PubVarTypes { get; }

    /// <summary>Return type per helper function name.</summary>
    public Dictionary<string, string> HelperReturnTypes { get; }

    public CSEmitContext(Dictionary<string, string> pubVarTypes, Dictionary<string, string> helperReturnTypes)
    {
        PubVarTypes = pubVarTypes;
        HelperReturnTypes = helperReturnTypes;
    }

    // ─── Primitive builders ───────────────────────────

    /// <summary>A string literal expression (quoted in generated source).</summary>
    public ExpressionSyntax Literal(string value)
        => LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));

    /// <summary>A <c>null</c> literal expression.</summary>
    public ExpressionSyntax NullLiteral()
        => LiteralExpression(SyntaxKind.NullLiteralExpression);

    /// <summary>Resolves a formatted argument string into a Roslyn expression.</summary>
    public ExpressionSyntax ResolveArgument(string arg)
        => CFG2CSGenerator.ResolveArgumentExpression(arg, PubVarTypes);

    /// <summary>Wraps an expression in <c>ConvertTo&lt;T&gt;(...)</c> for typed PubVar assignment.</summary>
    public ExpressionSyntax ConvertTo(string typeName, ExpressionSyntax expr)
        => CFG2CSGenerator.BuildConvertToInvocation(typeName, expr);

    /// <summary>Builds a <c>G.member(args...)</c> invocation expression.</summary>
    public InvocationExpressionSyntax GInvoke(string member, params ExpressionSyntax[] args)
        => CFG2CSGenerator.BuildGInvoke(member, args);

    /// <summary>Builds a <c>G.member(args...)</c> expression statement.</summary>
    public StatementSyntax GInvokeStatement(string member, params ExpressionSyntax[] args)
        => ExpressionStatement(GInvoke(member, args));

    // ─── Statement assemblers ─────────────────────────

    /// <summary>
    /// Emits the value-assignment statements for a value-producing expression:
    /// typed local declaration plus <c>G.Set</c> sync (when assigned to a PubVar),
    /// or a bare expression statement otherwise.
    /// </summary>
    public List<StatementSyntax> EmitValueAssignment(string? pubVarTarget, ExpressionSyntax rhs, string sourceType = "object")
        => CFG2CSGenerator.BuildValueAssignment(pubVarTarget, rhs, sourceType, PubVarTypes);

    /// <summary>Emits <c>var pubVarTarget = init;</c> (inferred-type local declaration).</summary>
    public List<StatementSyntax> EmitVarLocal(string pubVarTarget, ExpressionSyntax init)
        => new() {
            LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var"))
                    .AddVariables(VariableDeclarator(Identifier(pubVarTarget))
                        .WithInitializer(EqualsValueClause(init))))
        };

    /// <summary>Parses a C# expression from source text.</summary>
    public ExpressionSyntax Parse(string code) => ParseExpression(code);

    /// <summary>A <c>return;</c> statement.</summary>
    public StatementSyntax Return() => ReturnStatement();

    /// <summary>
    /// Emits the shared flow-control form: <c>G.NextBlock = G.{member}(args); break;</c>.
    /// Used by Branch/Loop/ToLoopCond/Flip descriptors.
    /// </summary>
    public List<StatementSyntax> EmitNextBlockAssignment(string member, params ExpressionSyntax[] args)
        => new() {
            ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("G"), IdentifierName("NextBlock")),
                GInvoke(member, args))),
            BreakStatement()
        };

    /// <summary>
    /// Default emission for a registered builtin with no custom override:
    /// <c>G.{Name}(args)</c> (or bare <c>{Name}(args)</c> for helpers) plus value assignment.
    /// </summary>
    public List<StatementSyntax> EmitDefault(CFGStatement stmt)
        => CFG2CSGenerator.EmitDefaultStatements(stmt, this);
}
