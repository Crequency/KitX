using Microsoft.CodeAnalysis;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Shared Roslyn syntax extensions for workflow parsing
/// </summary>
internal static class RoslynExtensions
{
    /// <summary>
    /// Gets the 1-based line number for a syntax node
    /// </summary>
    public static int GetLineNumber(this SyntaxNode node)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        return lineSpan.StartLinePosition.Line + 1;
    }
}