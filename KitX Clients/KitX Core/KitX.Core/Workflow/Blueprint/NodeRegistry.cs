using System;
using System.Collections.Generic;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Unified registry for node type creation and metadata.
/// Each node type is self-describing via GetDescriptor(), eliminating
/// the need for external switch statements when adding new node types.
/// </summary>
public class NodeRegistry : INodeRegistry
{
    private readonly Dictionary<BlueprintNodeType, Type> _typeMap;
    private readonly Dictionary<BlueprintNodeType, NodeDescriptor> _descriptorCache;

    public NodeRegistry()
    {
        // Map each BlueprintNodeType enum value to its concrete class
        _typeMap = new Dictionary<BlueprintNodeType, Type>
        {
            [BlueprintNodeType.Entry] = typeof(EntryNode),
            [BlueprintNodeType.Branch] = typeof(BranchNode),
            [BlueprintNodeType.Loop] = typeof(LoopNode),
            [BlueprintNodeType.Break] = typeof(BreakNode),
            [BlueprintNodeType.Const] = typeof(ConstNode),
            [BlueprintNodeType.Call] = typeof(CallNode),
            [BlueprintNodeType.CallHelper] = typeof(CallHelperNode),
            [BlueprintNodeType.Get] = typeof(GetNode),
            [BlueprintNodeType.Set] = typeof(SetNode),
            [BlueprintNodeType.Print] = typeof(PrintNode),
            [BlueprintNodeType.Pause] = typeof(PauseNode),
        };

        // Pre-cache descriptors from each node type
        _descriptorCache = new Dictionary<BlueprintNodeType, NodeDescriptor>();
        foreach (var kvp in _typeMap)
        {
            var instance = (BlueprintNode)Activator.CreateInstance(kvp.Value)!;
            _descriptorCache[kvp.Key] = instance.GetDescriptor();
        }

        Log.Information("NodeRegistry initialized with {Count} node types", _typeMap.Count);
    }

    /// <inheritdoc/>
    public BlueprintNode Create(BlueprintNodeType type)
    {
        if (!_typeMap.TryGetValue(type, out var nodeType))
            throw new ArgumentException($"Unknown node type: {type}");

        return (BlueprintNode)Activator.CreateInstance(nodeType)!;
    }

    /// <inheritdoc/>
    public NodeDescriptor GetDescriptor(BlueprintNodeType type)
    {
        if (!_descriptorCache.TryGetValue(type, out var descriptor))
            throw new ArgumentException($"No descriptor for node type: {type}");

        return descriptor;
    }

    /// <inheritdoc/>
    public IReadOnlySet<BlueprintNodeType> RegisteredTypes => _typeMap.Keys.ToHashSet();
}
