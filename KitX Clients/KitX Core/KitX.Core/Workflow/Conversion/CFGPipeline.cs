using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using KitX.Core.Workflow.Blueprint;

using KitX.Core.Workflow.CFG;
namespace KitX.Core.Workflow.Conversion;

/// <summary>
/// Canonical CFG pipeline: five sub-path functions that compose into four main paths.
///
/// Sub-paths:
///   BS2CFG(BlockScript)        → ControlFlowGraph    (parse, expand syntax sugar, allocate IDs)
///   BP2CFG(Blueprint)          → ControlFlowGraph    (build from nodes, StatementId = node.Id)
///   CFG2BS(ControlFlowGraph)   → BlockScript         (serialize, preserve StatementId)
///   CFG2BP(ControlFlowGraph)   → (via PipelineContext) (build visual nodes)
///   CFG2CS(ControlFlowGraph, …) → CompilationUnitSyntax (generate C#, emit debug checkpoints)
///
/// Main paths:
///   BS→BP  =  CFG2BP(BS2CFG(bs))
///   BP→BS  =  CFG2BS(BP2CFG(bp))
///   BS→CS  =  compile(CFG2CS(BS2CFG(bs)))
///   BP→CS  =  compile(CFG2CS(BP2CFG(bp)))
///
/// StatementId flow:
///   BS2CFG — empty BlockStatement.StatementId → generates new Guid
///   BP2CFG — sets StatementId = node.Id ✓
///   CFG2BS — copies StatementId to BlockStatement ✓
///   BS2CFG (from BP→BS) — preserves non-empty StatementId ✓
///   CFG2CS — uses stmt.StatementId for debug checkpoints ✓
///   CFG2BP — uses stmt.StatementId for NodeByStatementId mapping ✓
/// </summary>
internal static class CFGPipeline
{
    /// <summary>
    /// BS → CFG: parse source code, expand syntax sugar, allocate IDs.
    /// Equivalent to BS2CFGConverter.Format().
    /// </summary>
    internal static ControlFlowGraph BS2CFG(
        BlockScript script,
        List<HelperFunction> helpers,
        BuiltinFunctionRegistry? functionRegistry,
        PipelineContext? context = null)
    {
        var ctx = context ?? new PipelineContext { Script = script };
        var formatter = new BS2CFGConverter(helpers, functionRegistry);
        var cfg = formatter.Format(script, ctx);
        cfg.DebugContext = new BlueprintDebugContext();
        return cfg;
    }

    /// <summary>
    /// BP → CFG: build from Blueprint nodes, StatementId = node.Id.
    /// Equivalent to BP2CFGConverter.Build().
    /// </summary>
    internal static ControlFlowGraph BP2CFG(
        Contract.Workflow.Blueprint blueprint,
        Dictionary<BlueprintNodeType, INodeExportStrategy> strategyMap,
        Dictionary<string, INodeExportStrategy> builtinMap,
        NodeExportHelper exportHelper,
        BP2CFGConverter? prebuiltBuilder = null)
    {
        var builder = prebuiltBuilder ?? new BP2CFGConverter(strategyMap, builtinMap, exportHelper);
        if (prebuiltBuilder == null)
        {
            builder.SetContext(blueprint, new ConversionContext
            {
                Blueprint = blueprint,
                Script = new BlockScript()
            });
        }
        return builder.Build(blueprint);
    }

    /// <summary>
    /// CFG → BS: serialize to BlockScript, preserving StatementId.
    /// Equivalent to CFG2BSConverter.Generate().
    /// </summary>
    internal static BlockScript CFG2BS(ControlFlowGraph cfg)
    {
        var generator = new CFG2BSConverter();
        var script = generator.Generate(cfg);
        return script;
    }

    /// <summary>
    /// CFG → BP: build visual Blueprint nodes.
    /// Equivalent to CFG2BPConverter.Build().
    /// </summary>
    internal static void CFG2BP(
        ControlFlowGraph cfg,
        PipelineContext context,
        INodeRegistry registry,
        List<HelperFunction> helpers,
        BuiltinFunctionRegistry? functionRegistry = null)
    {
        var builder = new CFG2BPConverter(registry, helpers, functionRegistry);
        builder.Build(cfg, context);
    }
}
