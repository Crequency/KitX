namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Result of executing a block - used by state machine for flow control
/// </summary>
public class BlockExecutionResult
{
    /// <summary>
    /// Whether execution should continue to next block
    /// </summary>
    public bool ShouldContinue { get; set; } = true;

    /// <summary>
    /// Name of the next block to execute (null means end of script)
    /// </summary>
    public string? NextBlockName { get; set; }

    /// <summary>
    /// Whether this is a return (end of entire script)
    /// </summary>
    public bool IsReturn { get; set; }

    /// <summary>
    /// Return value if IsReturn is true
    /// </summary>
    public object? ReturnValue { get; set; }

    /// <summary>
    /// Create a result for continuing to next block
    /// </summary>
    public static BlockExecutionResult ContinueTo(string? nextBlockName) => new()
    {
        ShouldContinue = true,
        NextBlockName = nextBlockName,
        IsReturn = false
    };

    /// <summary>
    /// Create a result for end of script
    /// </summary>
    public static BlockExecutionResult Return(object? value = null) => new()
    {
        ShouldContinue = false,
        NextBlockName = null,
        IsReturn = true,
        ReturnValue = value
    };
}