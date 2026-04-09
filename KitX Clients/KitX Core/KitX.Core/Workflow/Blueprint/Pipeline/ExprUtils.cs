using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Shared expression-parsing utilities used across pipeline phases.
/// </summary>
public static class ExprUtils
{
    public static readonly HashSet<string> BuiltinFunctions = new()
    {
        BlockScriptWellKnown.Functions.Get, BlockScriptWellKnown.Functions.Set,
        BlockScriptWellKnown.Functions.Print, BlockScriptWellKnown.Functions.Pause,
        BlockScriptWellKnown.Functions.Branch, BlockScriptWellKnown.Functions.Loop,
        BlockScriptWellKnown.Functions.LoopBodyEnd, BlockScriptWellKnown.Functions.Break
    };

    public static readonly HashSet<string> NonExtractableFunctions = new()
    {
        BlockScriptWellKnown.Functions.Set, BlockScriptWellKnown.Functions.Print,
        BlockScriptWellKnown.Functions.Pause
    };

    public static readonly HashSet<string> FlowControlFunctions = new()
    {
        BlockScriptWellKnown.Functions.Branch, BlockScriptWellKnown.Functions.Loop,
        BlockScriptWellKnown.Functions.LoopBodyEnd, BlockScriptWellKnown.Functions.Break
    };

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

    /// <summary>Checks if a token is a simple variable reference.</summary>
    public static bool IsVariableReference(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (bool.TryParse(token, out _)) return false;
        if (int.TryParse(token, out _) || double.TryParse(token, out _)) return false;
        if (token.StartsWith("\"")) return false;
        if (token.Contains("(")) return false;
        return Regex.IsMatch(token, @"^[a-zA-Z_]\w*$");
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
    /// Finds all nested invocation arguments in an invocation (non-builtin only).
    /// Returns them in depth-first order (deepest first).
    /// </summary>
    public static List<InvocationExpressionSyntax> FindNestedInvocations(InvocationExpressionSyntax invoke)
    {
        var result = new List<InvocationExpressionSyntax>();
        foreach (var arg in invoke.ArgumentList.Arguments)
            CollectNested(arg.Expression, result);
        return result;
    }

    private static void CollectNested(ExpressionSyntax expr, List<InvocationExpressionSyntax> result)
    {
        if (expr is InvocationExpressionSyntax invoke)
        {
            var funcName = GetMethodName(invoke);
            // Recurse into arguments first (depth-first)
            foreach (var arg in invoke.ArgumentList.Arguments)
                CollectNested(arg.Expression, result);
            // Only extract non-builtin, non-flow-control functions
            if (!NonExtractableFunctions.Contains(funcName) && !FlowControlFunctions.Contains(funcName))
                result.Add(invoke);
        }
        else if (expr is ParenthesizedExpressionSyntax paren)
            CollectNested(paren.Expression, result);
    }

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

    /// <summary>Gets the integer value of a literal, or null.</summary>
    public static int? GetIntLiteralValue(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.NumericLiteralToken)
            && lit.Token.Value is int val)
            return val;
        return null;
    }

    /// <summary>Checks if an expression is a string literal.</summary>
    public static bool IsStringLiteral(ExpressionSyntax expr)
        => expr is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.StringLiteralToken);

    /// <summary>Checks if an expression is a numeric literal.</summary>
    public static bool IsNumericLiteral(ExpressionSyntax expr)
        => expr is LiteralExpressionSyntax lit && lit.Token.IsKind(SyntaxKind.NumericLiteralToken);

    /// <summary>Gets the value of any literal expression (string, int, double, bool, null).</summary>
    public static object? GetLiteralValue(LiteralExpressionSyntax literal)
        => literal.Token.Value;
}
