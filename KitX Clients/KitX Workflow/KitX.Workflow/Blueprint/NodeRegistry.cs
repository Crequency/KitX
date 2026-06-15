using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using Serilog;

namespace KitX.Workflow.Blueprint;

/// <summary>
/// Unified registry for node type creation and metadata.
/// Each node type is self-describing via GetDescriptor(), eliminating
/// the need for external switch statements when adding new node types.
/// All builtin function nodes are created via CreateBuiltinFunctionNode(),
/// driven by IBuiltinFunctionDefinition.
/// </summary>
public class NodeRegistry : INodeRegistry
{
    private readonly Dictionary<BlueprintNodeType, Type> _typeMap;
    private readonly Dictionary<BlueprintNodeType, NodeDescriptor> _descriptorCache;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public NodeRegistry()
    {
        _typeMap = new Dictionary<BlueprintNodeType, Type>
        {
            [BlueprintNodeType.Entry] = typeof(EntryNode),
            [BlueprintNodeType.PluginTrigger] = typeof(PluginTriggerNode),
            [BlueprintNodeType.Const] = typeof(ConstNode),
            [BlueprintNodeType.Call] = typeof(CallNode),
            [BlueprintNodeType.CallHelper] = typeof(CallHelperNode),
            [BlueprintNodeType.Variable] = typeof(VariableNode),
            [BlueprintNodeType.BuiltinFunction] = typeof(BuiltinFunctionNode),
        };

        _descriptorCache = new Dictionary<BlueprintNodeType, NodeDescriptor>();
        foreach (var kvp in _typeMap)
        {
            var instance = (BlueprintNode)Activator.CreateInstance(kvp.Value)!;
            _descriptorCache[kvp.Key] = instance.GetDescriptor();
        }

        Log.Information("NodeRegistry initialized with {Count} node types", _typeMap.Count);
    }

    /// <summary>
    /// Creates NodeRegistry with a BuiltinFunctionRegistry for dynamic node creation.
    /// </summary>
    public NodeRegistry(BuiltinFunctionRegistry functionRegistry) : this()
    {
        _functionRegistry = functionRegistry;
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

    /// <inheritdoc/>
    public BlueprintNode CreateBuiltinFunctionNode(string functionName)
    {
        if (_functionRegistry == null)
            throw new InvalidOperationException("BuiltinFunctionRegistry not configured. " +
                "Use NodeRegistry(BuiltinFunctionRegistry) constructor.");

        var def = _functionRegistry.Get(functionName)
            ?? throw new ArgumentException($"Unknown builtin function: {functionName}");

        var node = new BuiltinFunctionNode
        {
            NodeType = BlueprintNodeType.BuiltinFunction,
            FunctionName = functionName,
            Name = def.DisplayName
        };

        var descriptor = new NodeDescriptor(
            def.NodeWidth, def.NodeHeight,
            def.InputPins, def.OutputPins,
            def.DisplayName
        );
        node.SetDescriptor(descriptor);

        foreach (var pd in descriptor.InputPins)
            node.InputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Input, Type = pd.Type });
        foreach (var pd in descriptor.OutputPins)
            node.OutputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Output, Type = pd.Type });

        return node;
    }
}
