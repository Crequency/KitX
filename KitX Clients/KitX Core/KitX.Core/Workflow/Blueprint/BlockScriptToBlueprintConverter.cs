using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts BlockScript to Blueprint
/// </summary>
public class BlockScriptToBlueprintConverter : IBlockScriptToBlueprintConverter
{
    private readonly IBlockScriptParser _parser;
    private readonly IFlowProcessingService _flowProcessingService;
    private readonly IConnectionCreationService _connectionCreationService;
    private readonly ILayoutService _layoutService;

    public BlockScriptToBlueprintConverter(
        IBlockScriptParser parser,
        IFlowProcessingService flowProcessingService,
        IConnectionCreationService connectionCreationService,
        ILayoutService layoutService)
    {
        _parser = parser;
        _flowProcessingService = flowProcessingService;
        _connectionCreationService = connectionCreationService;
        _layoutService = layoutService;
    }

    public Contract.Workflow.Blueprint Convert(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        var result = _parser.Parse(sourceCode);
        if (!result.IsSuccess || result.Script == null)
        {
            throw new InvalidOperationException($"Failed to parse BlockScript: {result.ErrorMessage}");
        }
        return Convert(result.Script);
    }

    public Contract.Workflow.Blueprint Convert(BlockScript script)
    {
        var blueprint = new Contract.Workflow.Blueprint
        {
            Name = "Imported from BlockScript",
            HelperFunctions = script.HelperFunctions,
            PubVarNames = new List<string>()
        };

        var context = new ConversionContext
        {
            Blueprint = blueprint,
            Script = script,
            NodeMap = new Dictionary<string, BlueprintNode>(),
            NextPubVarIndex = 0,
            NamedBlockMap = new Dictionary<string, BlockDefinition>(),
            BlockFirstNodes = new Dictionary<string, BlueprintNode>(),
            VisitedBlocks = new HashSet<string>(),
            VariableSources = new Dictionary<string, VariableSource>(),
            LoopNodesByParentBlock = new Dictionary<string, LoopNode>(),
            PendingNodesForExecChain = new List<BlueprintNode>()
        };

        // Process ConstBlock
        if (script.ConstBlock != null)
        {
            _flowProcessingService.ProcessConstBlock(script.ConstBlock, context);
        }

        // Process PubVarBlock
        if (script.PubVarBlock != null)
        {
            _flowProcessingService.ProcessPubVarBlock(script.PubVarBlock, context);
        }

        // Process MainBlock
        if (script.MainBlock != null)
        {
            _flowProcessingService.ProcessMainBlock(script.MainBlock, context, null);
        }

        // Process NamedBlocks
        foreach (var kvp in script.NamedBlocks)
        {
            context.NamedBlockMap[kvp.Key] = kvp.Value;
        }

        // Process LoopBlocks
        foreach (var kvp in script.LoopBlocks)
        {
            _flowProcessingService.ProcessLoopBlock(kvp.Value, context, null);
        }

        // Auto-layout nodes
        _layoutService.LayoutNodes(blueprint);

        Log.Information("[BlueprintConversion] Complete: {NodeCount} nodes, {ConnectionCount} connections",
            blueprint.Nodes.Count, blueprint.Connections.Count);

        return blueprint;
    }
}
