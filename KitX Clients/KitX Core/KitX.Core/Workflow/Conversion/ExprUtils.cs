using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;
using KitX.Core.Workflow.Blueprint;
namespace KitX.Core.Workflow.Conversion;

/// <summary>
/// Shared expression-parsing utilities used across pipeline phases.
/// </summary>
public static class ExprUtils
{
    /// <summary>Parses an expression string using Roslyn. Returns null on failure.</summary>
    public static ExpressionSyntax? ParseExpression(string expression)
    {
        try
        {
            var wrappedCode = "_ = " + expression + ";";
            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, cancellationToken: CancellationToken.None);
            var root = syntaxTree.GetCompilationUnitRoot();
            var globalStmt = root.Members.FirstOrDefault() as GlobalStatementSyntax;
            var stmt = globalStmt?.Statement as ExpressionStatementSyntax;
            if (stmt?.Expression is AssignmentExpressionSyntax assignment)
                return assignment.Right;
            return stmt?.Expression;
        }
        catch { return null; }
    }

    /// <summary>Parses a full statement (may be assignment or plain expression).</summary>
    public static (ExpressionSyntax? rightExpr, string? assignedVar)? ParseStatement(string statement)
    {
        try
        {
            var wrappedCode = "_ = " + statement + ";";
            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, cancellationToken: CancellationToken.None);
            var root = syntaxTree.GetCompilationUnitRoot();
            var globalStmt = root.Members.FirstOrDefault() as GlobalStatementSyntax;
            var stmt = globalStmt?.Statement as ExpressionStatementSyntax;
            if (stmt?.Expression == null) return (null, null);

            if (stmt.Expression is AssignmentExpressionSyntax outerAssignment)
            {
                ExpressionSyntax rightExpr = outerAssignment.Right;
                AssignmentExpressionSyntax? innermost = null;
                while (rightExpr is AssignmentExpressionSyntax nested)
                {
                    innermost = nested;
                    rightExpr = nested.Right;
                }
                var assignedVar = (innermost ?? outerAssignment).Left.ToString().Trim();
                return (rightExpr, assignedVar);
            }
            return (stmt.Expression, null);
        }
        catch { return (null, null); }
    }

    /// <summary>Extracts the short method name from an invocation expression.</summary>
    public static string GetMethodName(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is IdentifierNameSyntax id) return id.Identifier.Text;
        if (invoke.Expression is GenericNameSyntax generic) return generic.Identifier.Text;
        if (invoke.Expression is MemberAccessExpressionSyntax member) return member.Name.Identifier.Text;
        return string.Empty;
    }

    /// <summary>
    /// Extracts the full dotted method path from an invocation expression.
    /// For "TestPlugin.WPF.Core.HelloKitX()" returns "TestPlugin.WPF.Core.HelloKitX".
    /// For simple calls like "Get(...)" returns just "Get".
    /// </summary>
    public static string GetFullMethodName(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is IdentifierNameSyntax id) return id.Identifier.Text;
        if (invoke.Expression is GenericNameSyntax generic) return generic.Identifier.Text;
        if (invoke.Expression is MemberAccessExpressionSyntax member)
            return member.Expression.ToString() + "." + member.Name.Identifier.Text;
        return string.Empty;
    }

    /// <summary>
    /// Generates a PubVar name from a linear counter, cycling from vaaa0001 to vzzz9999.
    /// Format: 'v' + 3 lowercase letters + 4 digits. Total capacity: 26^3 * 10000 = 175,760,000.
    /// </summary>
    public static string GeneratePubVarName(int counter)
    {
        int letterPart = counter / 10000;  // 0 = aaa, 1 = aab, ...
        int digitPart = counter % 10000;
        char c3 = (char)('a' + letterPart % 26);
        char c2 = (char)('a' + (letterPart / 26) % 26);
        char c1 = (char)('a' + (letterPart / 676) % 26);
        return $"v{c1}{c2}{c3}{digitPart:D4}";
    }

    /// <summary>
    /// Tries to extract the linear counter from an auto-generated PubVar name.
    /// Returns null if the name doesn't match the auto-generation format.
    /// </summary>
    public static int? TryExtractPubVarCounter(string name)
    {
        // Format: v + 3 lowercase letters + 4 digits
        if (name == null || name.Length != 8 || name[0] != 'v')
            return null;
        for (int i = 1; i <= 3; i++)
            if (name[i] < 'a' || name[i] > 'z') return null;
        if (!int.TryParse(name[4..], out var digitPart)) return null;
        int letterPart = (name[1] - 'a') * 676 + (name[2] - 'a') * 26 + (name[3] - 'a');
        return letterPart * 10000 + digitPart;
    }

    /// <summary>
    /// <summary>Computes a fingerprint string for a call expression for reuse detection.</summary>
    public static string ComputeFingerprint(string funcName, List<string> args)
        => $"{funcName}({string.Join(",", args.Select(a => a.Trim().Replace(" ", "")))})";

    /// <summary>Gets the string value of a string literal, or null.</summary>
    public static string? GetStringLiteralValue(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.StringLiteralToken))
            return lit.Token.ValueText;
        return null;
    }

    /// <summary>Gets the value of any literal expression (string, int, double, bool, null).</summary>
    public static object? GetLiteralValue(LiteralExpressionSyntax literal)
        => literal.Token.Value;

    /// <summary>
    /// Determines whether a value string represents a C# character literal (e.g. '\0', 'a', '\n').
    /// Uses Roslyn parsing for reliable detection — avoids string-pattern heuristics.
    /// Returns true only when the expression parses as a CharacterLiteralExpression.
    /// </summary>
    public static bool IsCharacterLiteral(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        // Fast pre-check: C# char literals always start and end with single quote
        if (value.Length < 3 || value[0] != '\'' || value[^1] != '\'') return false;
        // Validate with Roslyn
        var expr = ParseExpression(value);
        return expr is LiteralExpressionSyntax lit
               && lit.Token.IsKind(SyntaxKind.CharacterLiteralToken);
    }
}
