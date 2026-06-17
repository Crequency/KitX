using System.Collections.Generic;

namespace KitX.Workflow.Contract;

using ContractWorkflow = KitX.Core.Contract.Workflow;

/// <summary>
/// Helper service providing data resolution utilities for node export strategies.
/// Implemented by BlueprintToBlockScriptConverter.
/// Internal to the workflow pipeline.
/// </summary>
public interface INodeExportHelper
{
    /// <summary>
    /// Resolves the input value for a pin by tracing data connections.
    /// Returns ConstName for const references, PubVarName for pubvar references,
    /// or the pin's default value.
    /// </summary>
    string GetInputValue(ContractWorkflow.BlueprintNode node, string pinName);

    /// <summary>
    /// Resolves all non-Exec input arguments for a node, returning them as a comma-separated string.
    /// </summary>
    string GetInputArgs(ContractWorkflow.BlueprintNode node);

    /// <summary>
    /// The blueprint being converted.
    /// </summary>
    ContractWorkflow.Blueprint Blueprint { get; }

    /// <summary>
    /// Returns the PubVar name assigned to the given output pin, or null if no data connection exists.
    /// </summary>
    string? GetOutputPubVar(ContractWorkflow.BlueprintNode node, string pinName);

    /// <summary>
    /// Returns true if the given output pin is consumed by at least one data connection.
    /// </summary>
    bool IsOutputConsumed(ContractWorkflow.BlueprintNode node, string pinName);
}

/// <summary>
/// Strategy for converting a specific node type to a BlockScript statement.
/// Each node type that participates in reverse conversion provides an implementation,
/// eliminating the need for switch-based dispatch in the converter.
/// Internal to the workflow pipeline.
/// </summary>
public interface INodeExportStrategy
{
    /// <summary>
    /// The node type this strategy handles.
    /// </summary>
    ContractWorkflow.BlueprintNodeType NodeType { get; }

    /// <summary>
    /// Whether this node type represents a control flow construct (Branch, Loop, etc.).
    /// Used by the converter to determine main flow termination and sub-graph processing.
    /// </summary>
    bool IsControlFlow { get; }

    /// <summary>
    /// Converts the node to a BlockScript statement, or null if the node should be skipped.
    /// </summary>
    KitX.Workflow.Contract.Models.BlockStatement? ToStatement(ContractWorkflow.BlueprintNode node, INodeExportHelper helper);

    /// <summary>
    /// For control flow nodes: returns the output arm configuration.
    /// Each arm defines an output pin name and whether it represents a loopback.
    /// Non-control-flow strategies return an empty collection.
    /// </summary>
    IEnumerable<OutputArmDescriptor> GetOutputArms(ContractWorkflow.BlueprintNode node);
}

/// <summary>
/// Describes a single output arm of a control flow node (e.g., Branch's True/False, Loop's LoopBody/LoopEnd).
/// Internal to the workflow pipeline.
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
