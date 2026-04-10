using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.ReversePipeline;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint back to a fully-expanded BlockScript source code.
/// Thin orchestrator that delegates to pipeline phases:
///   Phase 1: BlueprintAnalyzer — data index construction
///   Phase 2: ExecutionFlowWalker — walk execution flow (BlockScopes or topology path)
///   Phase 3: ConditionDuplicator — loop condition duplication (topology path only)
///   Phase 4: BlockScriptAssembler — source code assembly
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter
{
    private readonly BlueprintAnalyzer _analyzer = new();
    private readonly ConditionDuplicator _conditionDuplicator = new();
    private readonly BlockScriptAssembler _assembler = new();
    private readonly NodeExportHelper _exportHelper = new();

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

        Walker = new ExecutionFlowWalker(strategyMap, builtinMap, _exportHelper);
    }

    /// <summary>Execution flow walker — accessible for testing</summary>
    internal ExecutionFlowWalker Walker { get; }

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
        if (blueprint.BlockScopes.Count > 0)
            return ConvertWithBlockScopes(blueprint);
        return ConvertWithTopology(blueprint);
    }

    // ──────────────────────────────────────────────
    // Conversion paths
    // ──────────────────────────────────────────────

    private BlockScript ConvertWithBlockScopes(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;
        var ctx = new ReverseConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _exportHelper.SetContext(blueprint, ctx);

        // Phase 1
        _analyzer.Analyze(ctx);

        // Phase 2: Generate statements from stored block membership
        Walker.WalkFromBlockScopes(blueprint, ctx);

        // Phase 4
        _assembler.Assemble(ctx);
        return ctx.Script;
    }

    private BlockScript ConvertWithTopology(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;
        var ctx = new ReverseConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _exportHelper.SetContext(blueprint, ctx);

        // Phase 1
        _analyzer.Analyze(ctx);

        // Phase 2: Walk execution flow
        Walker.WalkExecutionFlow(ctx);

        // Phase 3: Duplicate loop conditions
        _conditionDuplicator.Duplicate(ctx);

        // Phase 4: Assemble source code
        _assembler.Assemble(ctx);
        return ctx.Script;
    }
}
