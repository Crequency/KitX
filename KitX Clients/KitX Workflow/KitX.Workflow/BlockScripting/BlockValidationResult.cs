using Microsoft.CodeAnalysis;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Result of validating a block's C# code
/// </summary>
internal class BlockValidationResult
{
    public bool IsValid { get; set; } = true;
    public string ErrorMessage { get; set; } = string.Empty;
    public int ErrorLine { get; set; }
    public SyntaxNode? ParsedRoot { get; set; }
}