namespace KitX.Workflow.Abstractions;

/// <summary>
/// Describes a single output arm of a control flow node (e.g., Branch's True/False, Loop's
/// LoopBody/LoopEnd). Internal to the workflow pipeline. Returned by
/// <see cref="BlockScripting.IBuiltinFunctionDefinition.GetOutputArms"/>.
/// </summary>
public struct OutputArmDescriptor
{
    /// <summary>
    /// The output pin name (e.g., Pins.True, Pins.False, Pins.LoopBody, Pins.LoopEnd)
    /// </summary>
    public string PinName { get; set; }

    /// <summary>
    /// Whether this arm loops back to a parent node (LoopBody loops back to the Loop node)
    /// </summary>
    public bool IsLoopback { get; set; }
}