using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.CFG;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint back to a fully-expanded BlockScript source code.
/// Thin orchestrator that delegates to CFGPipeline phases.
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter
{
    private readonly NodeExportHelper _exportHelper = new();
    private readonly Dictionary<BlueprintNodeType, INodeExportStrategy> _strategyMap;
    private readonly Dictionary<string, INodeExportStrategy> _builtinMap;

    // CFG pipeline components
    private readonly BP2CFGConverter _cfgBuilder;
    private readonly CFGConditionDuplicator _cfgConditionDuplicator = new();
    private readonly CFG2BSConverter _cfg2bs = new();
    private readonly BlockScriptSerializer _serializer = new();

    public BlueprintToBlockScriptConverter(IEnumerable<INodeExportStrategy> strategies)
    {
        _strategyMap = new Dictionary<BlueprintNodeType, INodeExportStrategy>();
        _builtinMap = new Dictionary<string, INodeExportStrategy>();

        foreach (var s in strategies)
        {
            if (s is BuiltinFunctionExportStrategyAdapter adapter)
            {
                _builtinMap[adapter.FunctionName] = s;
                if (adapter.LegacyNodeType != null)
                    _strategyMap[adapter.LegacyNodeType.Value] = s;
            }
            else
            {
                _strategyMap[s.NodeType] = s;
            }
        }

        _cfgBuilder = new BP2CFGConverter(_strategyMap, _builtinMap, _exportHelper);
    }

    internal ControlFlowGraph? LastCFG { get; private set; }

    public Contract.Workflow.Blueprint Blueprint { get; private set; } = null!;

    public string Convert(Contract.Workflow.Blueprint blueprint)
        => ConvertToBlockScript(blueprint).SourceCode;

    public BlockScript ConvertToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;

        var ctx = new ConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _exportHelper.SetContext(blueprint, ctx);
        _cfgBuilder.SetContext(blueprint, ctx);

        // Phase 1: BP → CFG via pipeline
        var cfg = CFGPipeline.BP2CFG(blueprint, _strategyMap, _builtinMap, _exportHelper, prebuiltBuilder: _cfgBuilder);
        LastCFG = cfg;

        Log.Debug("[BlueprintToScript] CFG: {BlockCount} blocks, {EdgeCount} edges",
            cfg.Blocks.Count, cfg.Blocks.Sum(b => b.Successors.Count));

        // Phase 2: Duplicate loop conditions
        _cfgConditionDuplicator.Duplicate(cfg);

        // Phase 3: CFG → BS via pipeline
        var script = CFGPipeline.CFG2BS(cfg);

        // Phase 4: Serialize to source code
        script.SourceCode = _serializer.Serialize(script);

        script.HelperFunctions = blueprint.HelperFunctions ?? [];

        if (cfg.DebugContext != null)
            script.DebugNodeMapping = new Dictionary<string, string>(cfg.DebugContext.StatementToNodeId);

        Log.Debug("[BlueprintToScript] Done. Source code length: {Len}, HelperFunctions: {Count}, DebugMapping: {Map}",
            script.SourceCode?.Length ?? 0, script.HelperFunctions?.Count ?? 0,
            script.DebugNodeMapping?.Count ?? 0);

        return script;
    }
}
