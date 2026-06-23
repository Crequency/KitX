using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.CFG;
using Serilog;

using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Converts BlockScript to Blueprint via a clean 6-phase pipeline.
/// Acts as a thin orchestrator — all logic lives in individual pipeline phases.
/// </summary>
public class BlockScriptToBlueprintConverter : IBlockScriptToBlueprintConverter
{
    private readonly IBlockScriptParser _parser;
    private readonly INodeRegistry _nodeRegistry;
    private readonly ILayoutService _layoutService;
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    /// <summary>
    /// The pipeline context from the last conversion (for debug inspection).
    /// </summary>
    public ForwardConversionState? LastContext { get; private set; }

    /// <summary>
    /// User-facing diagnostics from the last conversion (parse-time + convert-time merged).
    /// Empty when the conversion was clean. Surface this in the Dashboard editor output panel
    /// the same way <c>BlockScriptExecutor.FormatCompileErrors</c> surfaces compile errors.
    /// </summary>
    public ConversionDiagnostics? LastDiagnostics => LastContext?.Diagnostics;

    public BlockScriptToBlueprintConverter(
        IBlockScriptParser parser,
        INodeRegistry nodeRegistry,
        ILayoutService layoutService)
    {
        _parser = parser;
        _nodeRegistry = nodeRegistry;
        _layoutService = layoutService;
    }

    public BlockScriptToBlueprintConverter(
        IBlockScriptParser parser,
        INodeRegistry nodeRegistry,
        ILayoutService layoutService,
        BuiltinFunctionRegistry functionRegistry) : this(parser, nodeRegistry, layoutService)
    {
        _functionRegistry = functionRegistry;
    }

    public KitX.Core.Contract.Workflow.Blueprint Convert(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        var result = _parser.Parse(sourceCode);
        if (!result.IsSuccess || result.Script == null)
            throw new InvalidOperationException($"Failed to parse BlockScript: {result.ErrorMessage}");

        if (helperFunctions != null)
            result.Script.HelperFunctions = helperFunctions;

        var blueprint = Convert(result.Script);

        // Surface parse-time diagnostics (e.g. unsupported-statement warnings) alongside the
        // convert-time diagnostics already collected on LastContext.
        LastContext?.Diagnostics.AddRange(result.Diagnostics);
        return blueprint;
    }

    public KitX.Core.Contract.Workflow.Blueprint Convert(BlockScript script)
    {
        var helpers = script.HelperFunctions ?? new List<HelperFunction>();

        // ── Phase 1: ConstBlock + PubVarBlock processing ──
        var context = new ForwardConversionState
        {
            Script = script,
            HelperFunctions = helpers
        };

        Phase1_ProcessConstAndPubVar(context);
        Log.Debug("[Converter] Phase 1: {ConstCount} const nodes, {PubVarCount} pub vars",
            context.ConstNodes.Count, context.PubVarNames.Count);

        // ── Phase 2: Script formatting (expand nested calls + loop condition duplication) ──
        var cfg = ConversionPaths.BS2CFG(script, helpers, _functionRegistry, context);
        context.FormattedScript = cfg;
        Log.Debug("[Converter] Phase 2: {BlockCount} blocks, {StmtCount} statements",
            cfg.Blocks.Count,
            cfg.Blocks.Sum(b => b.Statements.Count));

        // ── Phase 3: CFG → BP via pipeline ──
        ConversionPaths.CFG2BP(context.FormattedScript, context, _nodeRegistry, helpers, _functionRegistry);
        Log.Debug("[Converter] Phase 3: {NodeCount} nodes, {ExecEdgeCount} exec edges",
            context.AllNodes.Count, context.ExecEdges.Count);

        // ── Phase 3.5: Reaching-definitions analysis ──
        // Runs after CFG2BP (nodes + PubVarAssignments populated) and before DataEdgeBuilder
        // (which queries it to resolve multi-writer variables like Test K's cond).
        context.ReachingDefinitions = new Analysis.ReachingDefinitionsAnalysis(context.FormattedScript);
        context.ReachingDefinitions.Run();
        Log.Debug("[Converter] Phase 3.5: reaching-definitions analysis complete");

        // ── Phase 4+5: Data edges + deduplication ──
        var dataEdgeBuilder = new DataEdgeBuilder(_functionRegistry);
        dataEdgeBuilder.Build(context);
        Log.Debug("[Converter] Phase 4+5: {DataEdgeCount} data edges", context.DataEdges.Count);

        // ── Phase 6: Assemble Blueprint ──
        var assembler = new BlueprintAssembler();
        var blueprint = assembler.Assemble(context);

        // ── Layout ──
        _layoutService.LayoutNodes(blueprint);

        LastContext = context;

        Log.Information("[Converter] Complete: {NodeCount} nodes, {ConnCount} connections",
            blueprint.Nodes.Count, blueprint.Connections.Count);

        return blueprint;
    }

    // ──────────────────────────────────────────────
    // Phase 1: ConstBlock + PubVarBlock
    // ──────────────────────────────────────────────

    private void Phase1_ProcessConstAndPubVar(ForwardConversionState context)
    {
        // Process ConstBlock variables → ConstNodes or VariableNodes
        if (context.Script.ConstBlock != null)
        {
            foreach (var varDecl in context.Script.ConstBlock.Variables)
            {
                var hasInitialValue = varDecl.DefaultValue != null || !string.IsNullOrEmpty(varDecl.InitialValueExpression);

                if (hasInitialValue)
                {
                    // Variable with initial value → ConstNode (editable value)
                    var value = varDecl.DefaultValue?.ToString() ?? varDecl.InitialValueExpression ?? "";
                    var constNode = (ConstNode)_nodeRegistry.Create(BlueprintNodeType.Const);
                    constNode.ConstName = varDecl.Name;
                    constNode.ConstType = varDecl.Type;
                    constNode.ConstValue = value;
                    context.ConstNodes[varDecl.Name] = constNode;
                    context.AllNodes.Add(constNode);
                }
                else
                {
                    // Variable without initial value → VariableNode (type-only, floating)
                    var varNode = (VariableNode)_nodeRegistry.Create(BlueprintNodeType.Variable);
                    varNode.VarName = varDecl.Name;
                    varNode.VarType = varDecl.Type;
                    context.VariableNodes[varDecl.Name] = varNode;
                    context.AllNodes.Add(varNode);
                }
            }
        }

        // Process PubVarBlock variables → PubVarNames
        if (context.Script.PubVarBlock != null)
        {
            foreach (var varDecl in context.Script.PubVarBlock.Variables)
            {
                if (!context.PubVarNames.Contains(varDecl.Name))
                    context.PubVarNames.Add(varDecl.Name);
            }
        }
    }

    /// <summary>
    /// Dumps the formatted script (Phase 2 output) as a human-readable string.
    /// </summary>
    public static string DumpFormattedScript(ForwardConversionState context)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in context.FormattedScript.Blocks)
        {
            sb.AppendLine($"#Block {block.Name}  (FallThrough={block.FallThroughTarget ?? "null"})");
            foreach (var stmt in block.GetEffectiveStatements())
            {
                var fp = stmt.Fingerprint != null ? $" FP={stmt.Fingerprint}" : "";
                var args = stmt.Arguments != null ? string.Join(", ", stmt.Arguments) : "";
                sb.AppendLine($"  [{(stmt.FlowControlShape?.ToString() ?? "Expr")}] {stmt.OriginalExpression}" +
                    $" | PubVarTarget={stmt.PubVarTarget} Func={stmt.FunctionName}" +
                    $" Args=[{args}]" +
                    $" CondExpr={stmt.ConditionExpression} True={stmt.TrueBlockName} False={stmt.FalseBlockName}{fp}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
