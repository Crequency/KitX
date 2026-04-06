using System.Collections.Generic;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Helper service providing data resolution utilities for node export strategies.
/// Implemented by BlueprintToBlockScriptConverter.
/// </summary>
public interface INodeExportHelper
{
    /// <summary>
    /// Resolves the input value for a pin by tracing data connections.
    /// Returns ConstName for const references, PubVarName for pubvar references,
    /// or the pin's default value.
    /// </summary>
    string GetInputValue(BlueprintNode node, string pinName);

    /// <summary>
    /// Resolves all non-Exec input arguments for a node, returning them as a comma-separated string.
    /// </summary>
    string GetInputArgs(BlueprintNode node);

    /// <summary>
    /// The blueprint being converted.
    /// </summary>
    Contract.Workflow.Blueprint Blueprint { get; }
}

/// <summary>
/// Strategy for converting a specific node type to a BlockScript statement.
/// Each node type that participates in reverse conversion provides an implementation,
/// eliminating the need for switch-based dispatch in the converter.
/// </summary>
public interface INodeExportStrategy
{
    /// <summary>
    /// The node type this strategy handles.
    /// </summary>
    BlueprintNodeType NodeType { get; }

    /// <summary>
    /// Whether this node type represents a control flow construct (Branch, Loop, etc.).
    /// Used by the converter to determine main flow termination and sub-graph processing.
    /// </summary>
    bool IsControlFlow { get; }

    /// <summary>
    /// Converts the node to a BlockScript statement, or null if the node should be skipped.
    /// </summary>
    BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper);

    /// <summary>
    /// For control flow nodes: returns the output arm configuration.
    /// Each arm defines an output pin name and whether it represents a loopback.
    /// Non-control-flow strategies return an empty collection.
    /// </summary>
    IEnumerable<OutputArmDescriptor> GetOutputArms(BlueprintNode node);
}

/// <summary>
/// Describes a single output arm of a control flow node (e.g., Branch's True/False, Loop's LoopBody/LoopEnd).
/// </summary>
public struct OutputArmDescriptor
{
    /// <summary>
    /// The output pin name (e.g., "True", "False", "LoopBody", "LoopEnd")
    /// </summary>
    public string PinName { get; set; }

    /// <summary>
    /// Whether this arm loops back to a parent node (LoopBody loops back to the Loop node)
    /// </summary>
    public bool IsLoopback { get; set; }
}
