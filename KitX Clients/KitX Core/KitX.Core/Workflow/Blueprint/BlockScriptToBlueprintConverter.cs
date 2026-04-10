using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

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
    public PipelineContext? LastContext { get; private set; }

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

    public Contract.Workflow.Blueprint Convert(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        var result = _parser.Parse(sourceCode);
        if (!result.IsSuccess || result.Script == null)
            throw new InvalidOperationException($"Failed to parse BlockScript: {result.ErrorMessage}");

        if (helperFunctions != null)
            result.Script.HelperFunctions = helperFunctions;

        return Convert(result.Script);
    }

    public Contract.Workflow.Blueprint Convert(BlockScript script)
    {
        var helpers = script.HelperFunctions ?? new List<HelperFunction>();

        // ── Phase 1: ConstBlock + PubVarBlock processing ──
        var context = new PipelineContext
        {
            Script = script,
            HelperFunctions = helpers
        };

        Phase1_ProcessConstAndPubVar(context);
        Log.Debug("[Converter] Phase 1: {ConstCount} const nodes, {PubVarCount} pub vars",
            context.ConstNodes.Count, context.PubVarNames.Count);

        // ── Phase 2: Script formatting (expand nested calls + loop condition duplication) ──
        var formatter = new ScriptFormatter(helpers, _functionRegistry);
        context.FormattedScript = formatter.Format(script, context);
        Log.Debug("[Converter] Phase 2: {BlockCount} blocks, {StmtCount} statements",
            context.FormattedScript.Blocks.Count,
            context.FormattedScript.Blocks.Sum(b => b.Statements.Count));

        // ── Phase 3: Node creation + exec edges + PubVar reuse ──
        var nodeBuilder = new NodeBuilder(_nodeRegistry, helpers, _functionRegistry);
        nodeBuilder.Build(context.FormattedScript, context);
        Log.Debug("[Converter] Phase 3: {NodeCount} nodes, {ExecEdgeCount} exec edges",
            context.AllNodes.Count, context.ExecEdges.Count);

        // ── Phase 4+5: Data edges + deduplication ──
        var dataEdgeBuilder = new DataEdgeBuilder();
        dataEdgeBuilder.Build(context);
        Log.Debug("[Converter] Phase 4+5: {DataEdgeCount} data edges", context.DataEdges.Count);

        // ── Phase 6: Assemble Blueprint ──
        var assembler = new PipelineAssembler();
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

    private void Phase1_ProcessConstAndPubVar(PipelineContext context)
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
    public static string DumpFormattedScript(PipelineContext context)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in context.FormattedScript.Blocks)
        {
            sb.AppendLine($"#Block {block.Name}  (NextBlock={block.NextBlockName ?? "null"})");
            foreach (var stmt in block.Statements)
            {
                var dup = stmt.IsLoopConditionDuplication ? " [LoopCondDup]" : "";
                var fp = stmt.Fingerprint != null ? $" FP={stmt.Fingerprint}" : "";
                var args = stmt.Arguments != null ? string.Join(", ", stmt.Arguments) : "";
                sb.AppendLine($"  [{stmt.Kind}] {stmt.OriginalExpression}" +
                    $" | PubVarTarget={stmt.PubVarTarget} Func={stmt.FunctionName}" +
                    $" Args=[{args}] SetVar={stmt.SetVarName} GetVar={stmt.GetVarName}" +
                    $" CondPubVar={stmt.ConditionPubVar} True={stmt.TrueBlockName} False={stmt.FalseBlockName}" +
                    $" LoopBodyEndReturnTo={stmt.LoopBodyEndReturnTo}{dup}{fp}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
