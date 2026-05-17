using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.CFG;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint back to a fully-expanded BlockScript source code.
/// Thin orchestrator that delegates to CFG pipeline phases:
///   CFGBuilderFromBlueprint → CFGConditionDuplicator → ScriptGenerator → ScriptSerializer
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter
{
    private readonly NodeExportHelper _exportHelper = new();

    // CFG pipeline components
    private readonly CFGBuilderFromBlueprint _cfgBuilder;
    private readonly CFGConditionDuplicator _cfgConditionDuplicator = new();
    private readonly ScriptGenerator _scriptGenerator = new();
    private readonly ScriptSerializer _scriptSerializer = new();

    public BlueprintToBlockScriptConverter(IEnumerable<INodeExportStrategy> strategies)
    {
        var strategyMap = new Dictionary<BlueprintNodeType, INodeExportStrategy>();
        var builtinMap = new Dictionary<string, INodeExportStrategy>();

        foreach (var s in strategies)
        {
            if (s is BuiltinFunctionExportStrategyAdapter adapter)
            {
                builtinMap[adapter.FunctionName] = s;
                // Standard functions also register by their legacy NodeType for reverse conversion
                if (adapter.LegacyNodeType != null)
                    strategyMap[adapter.LegacyNodeType.Value] = s;
            }
            else
            {
                strategyMap[s.NodeType] = s;
            }
        }

        _cfgBuilder = new CFGBuilderFromBlueprint(strategyMap, builtinMap, _exportHelper);
    }

    /// <summary>Last CFG built, for diagnostics</summary>
    internal ControlFlowGraph? LastCFG { get; private set; }

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public Contract.Workflow.Blueprint Blueprint { get; private set; } = null!;

    /// <inheritdoc/>
    public string Convert(Contract.Workflow.Blueprint blueprint)
        => ConvertToBlockScript(blueprint).SourceCode;

    /// <inheritdoc/>
    public BlockScript ConvertToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;

        // Set up context for NodeExportHelper and CFG builder (needed for statement generation)
        var ctx = new ConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _exportHelper.SetContext(blueprint, ctx);
        _cfgBuilder.SetContext(blueprint, ctx);

        // Phase 1: Build CFG from Blueprint (unified algorithm)
        var cfg = _cfgBuilder.Build(blueprint);
        LastCFG = cfg;

        Log.Debug("[BlueprintToScript] CFG: {BlockCount} blocks, {EdgeCount} edges",
            cfg.Blocks.Count, cfg.Blocks.Sum(b => b.Successors.Count));

        // Phase 2: Duplicate loop conditions
        _cfgConditionDuplicator.Duplicate(cfg);

        // Phase 3: Generate BlockScript from CFG
        var script = _scriptGenerator.Generate(cfg);

        // Phase 4: Serialize to source code
        script.SourceCode = _scriptSerializer.Serialize(script);

        // Transfer helper functions from Blueprint to BlockScript
        script.HelperFunctions = blueprint.HelperFunctions ?? [];

        // Preserve debug mapping for the execution pipeline
        if (cfg.DebugContext != null)
            script.DebugNodeMapping = new Dictionary<string, string>(cfg.DebugContext.StatementToNodeId);

        Log.Debug("[BlueprintToScript] Done. Source code length: {Len}, HelperFunctions: {Count}, DebugMapping: {Map}",
            script.SourceCode?.Length ?? 0, script.HelperFunctions?.Count ?? 0,
            script.DebugNodeMapping?.Count ?? 0);

        return script;
    }
}