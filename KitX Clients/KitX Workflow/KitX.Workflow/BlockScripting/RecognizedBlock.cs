using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Represents a recognized block with its type, name, and source position
/// </summary>
internal class RecognizedBlock
{
    public BlockType BlockType { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int ContentStart { get; set; }
    public int ContentEnd { get; set; }
    public string Content { get; set; } = string.Empty;
}