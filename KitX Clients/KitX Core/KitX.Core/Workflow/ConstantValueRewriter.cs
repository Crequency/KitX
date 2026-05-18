using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KitX.Core.Workflow;

/// <summary>
/// Rewriter for injecting constant values into variable declarations using CSharpSyntaxRewriter
/// </summary>
/// <remarks>
/// This approach is more reliable than regex-based matching as it understands the actual
/// syntax structure and won't accidentally match content in comments or strings.
/// </remarks>
internal class ConstantValueRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, object?> _constantValues;

    public ConstantValueRewriter(Dictionary<string, object?> constantValues)
    {
        _constantValues = constantValues;
    }

    /// <summary>
    /// Visits variable declarators to replace their initializer values
    /// </summary>
    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        // Check if this variable declarator has an initializer and matches a constant name
        if (node.Initializer != null && _constantValues.TryGetValue(node.Identifier.Text, out var newValue))
        {
            var newInitializer = CreateNewInitializer(node.Initializer, newValue);
            if (newInitializer != null)
            {
                return node.WithInitializer(newInitializer);
            }
        }

        return base.VisitVariableDeclarator(node);
    }

    /// <summary>
    /// Creates a new EqualsValueClauseSyntax with the specified value
    /// </summary>
    private EqualsValueClauseSyntax? CreateNewInitializer(EqualsValueClauseSyntax oldInitializer, object? value)
    {
        ExpressionSyntax? newExpression = null;

        if (value == null)
        {
            newExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (value is int intVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(intVal));
        }
        else if (value is long longVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(longVal));
        }
        else if (value is double doubleVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(doubleVal));
        }
        else if (value is float floatVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(floatVal));
        }
        else if (value is decimal decimalVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(decimalVal));
        }
        else if (value is bool boolVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                boolVal ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
        }
        else if (value is string stringVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(stringVal));
        }
        else if (value is char charVal)
        {
            newExpression = SyntaxFactory.LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                SyntaxFactory.Literal(charVal));
        }

        if (newExpression != null)
        {
            return SyntaxFactory.EqualsValueClause(newExpression)
                .WithLeadingTrivia(oldInitializer.GetLeadingTrivia())
                .WithTrailingTrivia(oldInitializer.GetTrailingTrivia());
        }

        return null;
    }
}
