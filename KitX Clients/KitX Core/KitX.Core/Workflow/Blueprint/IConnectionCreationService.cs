using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

public interface IConnectionCreationService
{
    void CreateExecConnection(BlueprintNode source, BlueprintNode target, Contract.Workflow.Blueprint blueprint);

    void CreateDataConnection(BlueprintNode source, string sourcePin, BlueprintNode target, string targetPin, string? pubVarName, Contract.Workflow.Blueprint blueprint);

    void LinkNodesWithExec(List<BlueprintNode> nodes, Contract.Workflow.Blueprint blueprint);

    void CreateDataConnectionsForExpression(string expression, BlueprintNode targetNode, ConversionContext context);

    void CreateDataConnectionsForArguments(SeparatedSyntaxList<ArgumentSyntax>? arguments, BlueprintNode callNode, ConversionContext context);

    void AddParameterPins(BlueprintNode node, SeparatedSyntaxList<ArgumentSyntax>? arguments);

    void CreateDataConnectionForVariable(string varName, BlueprintPin targetPin, BlueprintNode callNode, ConversionContext context);

    void CreateDataConnectionForVariable(string varName, BlueprintNode targetNode, ConversionContext context);

    void ProcessArgumentExpression(ExpressionSyntax expr, BlueprintPin targetPin, BlueprintNode callNode, ConversionContext context);

    bool IsVariableReference(string token);

    void CreateConstNodeForLiteral(LiteralExpressionSyntax literal, BlueprintNode targetNode, ConversionContext context);

    void CreateConstNodeForLiteralWithPin(LiteralExpressionSyntax literal, BlueprintPin targetPin, BlueprintNode targetNode, ConversionContext context);

    string GetMethodNameFromExpression(InvocationExpressionSyntax invoke);
}
