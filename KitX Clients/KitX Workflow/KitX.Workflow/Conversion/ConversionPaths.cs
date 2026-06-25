using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Canonical conversion paths for the CFG-as-truth architecture (v5.1).
///
/// Paths:
///   BS2CFG(BlockScript) → ControlFlowGraph   (parse BS, build CFG)
///   CFG2BS(ControlFlowGraph) → BlockScript   (render CFG to BS text)
///   CFG2CS(ControlFlowGraph, …) → CompilationUnitSyntax (codegen)
///
/// Removed (v5.1): CFG2BP, BP2CFG — BP is now a rendered view,
/// not a separate persistence model. See CFGGraphRenderer (G-3) for
/// CFG → BP graph rendering.
/// </summary>
internal static class ConversionPaths
{
    /// <summary>
    /// BS → CFG: parse source code, expand syntax sugar, allocate IDs.
    /// </summary>
    internal static ControlFlowGraph BS2CFG(
        BlockScript script,
        List<HelperFunction> helpers,
        BuiltinFunctionRegistry? functionRegistry,
        ForwardConversionState? context = null)
    {
        var ctx = context ?? new ForwardConversionState { Script = script };
        var formatter = new BS2CFGConverter(helpers, functionRegistry);
        var cfg = formatter.Format(script, ctx);
        return cfg;
    }

    /// <summary>
    /// CFG → BS: render to BlockScript text.
    /// </summary>
    internal static BlockScript CFG2BS(ControlFlowGraph cfg)
    {
        var renderer = new CFGRenderer();
        return renderer.Generate(cfg);
    }
}
