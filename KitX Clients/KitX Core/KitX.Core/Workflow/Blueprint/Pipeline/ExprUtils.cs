using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Shared expression-parsing utilities used across pipeline phases.
/// </summary>
public static class ExprUtils
{
    public static readonly HashSet<string> BuiltinFunctions = new()
    {
        "Get", "Set", "Print", "Pause", "Branch", "Loop", "LoopBodyEnd", "Break"
    };

    public static readonly HashSet<string> NonExtractableFunctions = new()
    {
        "Set", "Print", "Pause"
    };

    public static readonly HashSet<string> FlowControlFunctions = new()
    {
        "Branch", "Loop", "LoopBodyEnd", "Break"
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
    public static (ExpressionSyntax? rightExpr, string? assignedVar, bool isPubVar)? ParseStatement(string statement)
    {
        try
        {
            var wrappedCode = "_ = " + statement + ";";
            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, cancellationToken: CancellationToken.None);
            var root = syntaxTree.GetCompilationUnitRoot();
            var globalStmt = root.Members.FirstOrDefault() as GlobalStatementSyntax;
            var stmt = globalStmt?.Statement as ExpressionStatementSyntax;
            if (stmt?.Expression == null) return (null, null, false);

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
                return (rightExpr, assignedVar, IsPubVarName(assignedVar));
            }
            return (stmt.Expression, null, false);
        }
        catch { return (null, null, false); }
    }

    /// <summary>Extracts method name from an invocation expression.</summary>
    public static string GetMethodName(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is IdentifierNameSyntax id) return id.Identifier.Text;
        if (invoke.Expression is GenericNameSyntax generic) return generic.Identifier.Text;
        if (invoke.Expression is MemberAccessExpressionSyntax member) return member.Name.Identifier.Text;
        return string.Empty;
    }

    /// <summary>Checks if a name follows the PubVar convention (vaaaNNNN).</summary>
    public static bool IsPubVarName(string name)
        => !string.IsNullOrEmpty(name) && name.StartsWith("vaaa") && name.Length >= 8;

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

    /// <summary>Generates a PubVar name like vaaa0001.</summary>
    public static string GeneratePubVarName(int counter) => $"vaaa{counter:D4}";

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
}
