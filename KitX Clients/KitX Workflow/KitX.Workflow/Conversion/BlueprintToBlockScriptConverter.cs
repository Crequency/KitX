using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;
using Serilog;

using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Converts Blueprint back to a fully-expanded BlockScript source code.
/// Thin orchestrator that delegates to CFGPipeline phases.
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter
{
    private readonly NodeExportHelper _exportHelper = new();
    private readonly Dictionary<string, IBuiltinFunctionDefinition> _builtinMap;

    // CFG pipeline components
    private readonly BP2CFGConverter _cfgBuilder;
    private readonly CFGConditionDuplicator _cfgConditionDuplicator = new();
    private readonly CFG2BSConverter _cfg2bs = new();
    private readonly BlockScriptSerializer _serializer = new();

    public BlueprintToBlockScriptConverter(IEnumerable<IBuiltinFunctionDefinition> definitions)
    {
        // Every node export need is satisfied directly by IBuiltinFunctionDefinition
        // (ToStatement/GetOutputArms/StatementKind/AutoSynthesizePubVar/IsFlowControl are all
        // on the interface). The former INodeExportStrategy + BuiltinFunctionExportStrategyAdapter
        // layer was a strict-subset adapter that only forwarded to these members, with the
        // dispatcher further special-casing the adapter type — pure indirection, removed.
        _builtinMap = definitions.ToDictionary(d => d.FunctionName);

        _cfgBuilder = new BP2CFGConverter(_builtinMap, _exportHelper);
    }

    internal ControlFlowGraph? LastCFG { get; private set; }

    public KitX.Core.Contract.Workflow.Blueprint Blueprint { get; private set; } = null!;

    /// <summary>
    /// User-facing diagnostics from the last BP→BS conversion. Empty when clean. Surface in the
    /// Dashboard editor output panel; backend-bug-class problems stay in Serilog logs.
    /// </summary>
    public ConversionDiagnostics? LastDiagnostics { get; private set; }

    public string Convert(KitX.Core.Contract.Workflow.Blueprint blueprint)
        => ConvertToBlockScript(blueprint).SourceCode;

    public BlockScript ConvertToBlockScript(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;

        var ctx = new ConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _exportHelper.SetContext(blueprint, ctx);
        _cfgBuilder.SetContext(blueprint, ctx);

        // Phase 1: BP → CFG via pipeline
        var cfg = CFGPipeline.BP2CFG(blueprint, _builtinMap, _exportHelper, prebuiltBuilder: _cfgBuilder);
        LastCFG = cfg;
        LastDiagnostics = ctx.Diagnostics;

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
