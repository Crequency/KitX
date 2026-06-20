using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Validates pure C# code within recognized blocks using Roslyn.
/// Phase 2 of BlockScriptParser: validates syntax and block-type-specific rules.
/// </summary>
internal class BlockSyntaxValidator
{
    /// <summary>
    /// Validates pure C# code within a block using Roslyn
    /// </summary>
    public BlockValidationResult Validate(RecognizedBlock recognized)
    {
        var result = new BlockValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(recognized.Content))
        {
            return result; // Empty blocks are OK
        }

        try
        {
            // Parse the pure C# code
            var syntaxTree = CSharpSyntaxTree.ParseText(recognized.Content);
            var root = syntaxTree.GetRoot();
            result.ParsedRoot = root;  // Save for reuse by statement extraction
            var diagnostics = syntaxTree.GetDiagnostics();

            // Check for syntax errors
            foreach (var diag in diagnostics)
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"[{recognized.BlockType}] Syntax error: {diag.GetMessage()}";
                    result.ErrorLine = recognized.StartLine + (int)diag.Location.GetLineSpan().StartLinePosition.Line;
                    return result;
                }
            }

            // Validate block-type-specific rules
            ValidateBlockSpecificRules(recognized, root, result);

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = $"[{recognized.BlockType}] Parse error: {ex.Message}";
            result.ErrorLine = recognized.StartLine;
            return result;
        }
    }

    /// <summary>
    /// Validates block-type-specific rules
    /// </summary>
    private void ValidateBlockSpecificRules(RecognizedBlock recognized, SyntaxNode root, BlockValidationResult result)
    {
        // Get all statements in the block
        var statements = root.DescendantNodes()
            .Where(n => n is StatementSyntax)
            .Cast<StatementSyntax>()
            .ToList();

        foreach (var stmt in statements)
        {
            switch (recognized.BlockType)
            {
                case BlockType.ConstBlock:
                    // ConstBlock: only variable declarations allowed, initializers are OK (const values)
                    if (stmt is not LocalDeclarationStatementSyntax)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"[{recognized.BlockType}] Only variable declarations are allowed. Found: {stmt.Kind()}";
                        result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                        return;
                    }
                    break;

                case BlockType.PubVarBlock:
                    // PubVarBlock: only variable declarations allowed, but NO initializers
                    if (stmt is not LocalDeclarationStatementSyntax)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"[{recognized.BlockType}] Only variable declarations are allowed. Found: {stmt.Kind()}";
                        result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                        return;
                    }

                    // PubVarBlock declarations must NOT have initializers
                    if (stmt is LocalDeclarationStatementSyntax varDecl)
                    {
                        foreach (var variable in varDecl.Declaration.Variables)
                        {
                            if (variable.Initializer != null)
                            {
                                result.IsValid = false;
                                result.ErrorMessage = $"[{recognized.BlockType}] PubVarBlock variable declarations cannot have initializers. Variable '{variable.Identifier.Text}' has an initializer.";
                                result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                                return;
                            }
                        }
                    }

                    // PubVarBlock must not contain assignment operations
                    if (stmt is ExpressionStatementSyntax exprStmt)
                    {
                        var hasAssignment = exprStmt.DescendantNodes().Any(n => n is AssignmentExpressionSyntax);
                        if (hasAssignment)
                        {
                            result.IsValid = false;
                            result.ErrorMessage = $"[{recognized.BlockType}] PubVarBlock cannot contain assignment operations.";
                            result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                            return;
                        }
                    }
                    break;

                case BlockType.MainBlock:
                case BlockType.NamedBlock:
                    // MainBlock and NamedBlock: no variable declarations allowed
                    if (stmt is LocalDeclarationStatementSyntax)
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"[{recognized.BlockType}] Variable declarations are not allowed. Use ConstBlock or PubVarBlock instead.";
                        result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                        return;
                    }

                    // Check for prohibited syntax (if/else/for/while/try/catch)
                    if (ContainsProhibitedSyntax(stmt))
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"[{recognized.BlockType}] Prohibited syntax found: if/else/for/while/try/catch are not allowed";
                        result.ErrorLine = recognized.StartLine + stmt.GetLineNumber();
                        return;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Checks if a statement contains prohibited syntax (if/else/for/while/try/catch)
    /// </summary>
    private static bool ContainsProhibitedSyntax(StatementSyntax statement)
    {
        // Check the statement itself
        if (statement is IfStatementSyntax ||
            statement is ForStatementSyntax ||
            statement is ForEachStatementSyntax ||
            statement is WhileStatementSyntax ||
            statement is DoStatementSyntax ||
            statement is TryStatementSyntax)
        {
            return true;
        }

        // Recursively check child statements
        foreach (var child in statement.DescendantNodes())
        {
            if (child is IfStatementSyntax ||
                child is ForStatementSyntax ||
                child is ForEachStatementSyntax ||
                child is WhileStatementSyntax ||
                child is DoStatementSyntax ||
                child is TryStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
