using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

public interface IFlowProcessingService
{
    void ProcessConstBlock(BlockDefinition block, ConversionContext context);

    void ProcessPubVarBlock(BlockDefinition block, ConversionContext context);

    void ProcessMainBlock(BlockDefinition block, ConversionContext context, INodeCreationService nodeCreationService);

    BlueprintNode? ProcessStatement(BlockStatement statement, ConversionContext context, INodeCreationService nodeCreationService, double x, double y);

    BlueprintNode? ProcessFlowControlStatement(FlowControlStatement flowCtrl, ConversionContext context, INodeCreationService nodeCreationService, double x, double y);

    BlueprintNode? ProcessBlockRecursive(BlockDefinition block, ConversionContext context, INodeCreationService nodeCreationService, double x, double y);

    void ProcessBranchNodeConnections(BranchNode branch, BlockDefinition block, ConversionContext context, double x, double y);

    void ProcessBranchNodeConnectionsInMain(BranchNode branch, FlowControlStatement flowCtrl, ConversionContext context);

    void ProcessLoopNodeConnections(LoopNode loop, BlockDefinition block, ConversionContext context, double x, double y);

    void ProcessLoopNodeConnectionsInMain(LoopNode loop, FlowControlStatement flowCtrl, ConversionContext context);

    void ProcessLoopBlock(BlockDefinition block, ConversionContext context, INodeCreationService nodeCreationService);

    void HandleLoopBodyEnd(FlowControlStatement flowCtrl, ConversionContext context);

    BlueprintNode? GetFirstConnectableNode(BlueprintNode node, ConversionContext context);
}
