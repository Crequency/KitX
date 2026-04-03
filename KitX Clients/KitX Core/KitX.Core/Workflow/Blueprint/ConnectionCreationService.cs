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

public class ConnectionCreationService : IConnectionCreationService
{
    public void CreateExecConnection(BlueprintNode source, BlueprintNode target, Contract.Workflow.Blueprint blueprint)
    {
        var sourceExecOut = source.OutputPins.FirstOrDefault(p => p.Name == "Exec");
        var targetExecIn = target.InputPins.FirstOrDefault(p => p.Name == "Exec");
        if (sourceExecOut == null || targetExecIn == null) { Log.Warning("Cannot create Exec connection: missing Exec pin"); return; }
        if (sourceExecOut.Direction != PinDirection.Output) { Log.Warning("Cannot create Exec connection: source.Exec is not an output pin"); return; }
        if (source.Id == target.Id) { Log.Warning("Skipping self-loop Exec connection"); return; }
        blueprint.AddConnection(new BlueprintConnection { SourceNodeId = source.Id, SourcePinId = sourceExecOut.Id, TargetNodeId = target.Id, TargetPinId = targetExecIn.Id });
    }

    public void CreateDataConnection(BlueprintNode source, string sourcePin, BlueprintNode target, string targetPin, string? pubVarName, Contract.Workflow.Blueprint blueprint)
    {
        var srcPin = source.OutputPins.FirstOrDefault(p => p.Name == sourcePin);
        var tgtPin = target.InputPins.FirstOrDefault(p => p.Name == targetPin);
        if (srcPin == null || tgtPin == null) { Log.Warning("Cannot create data connection: missing pin"); return; }
        blueprint.AddConnection(new BlueprintConnection { SourceNodeId = source.Id, SourcePinId = srcPin.Id, TargetNodeId = target.Id, TargetPinId = tgtPin.Id, PubVarName = pubVarName });
    }

    public void LinkNodesWithExec(List<BlueprintNode> nodes, Contract.Workflow.Blueprint blueprint)
    {
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            var currentNode = nodes[i];
            var nextNode = nodes[i + 1];
            if (currentNode.Id == nextNode.Id) continue;
            var currentExecOut = currentNode.OutputPins.FirstOrDefault(p => p.Name == "Exec");
            if (currentExecOut == null) { Log.Debug("Skipping Exec link"); continue; }
            if (currentExecOut.Direction != PinDirection.Output) { Log.Warning("Cannot create Exec connection"); continue; }
            var nextExecIn = nextNode.InputPins.FirstOrDefault(p => p.Name == "Exec");
            if (nextExecIn == null) { Log.Warning("Cannot create Exec connection"); continue; }
            blueprint.AddConnection(new BlueprintConnection { SourceNodeId = currentNode.Id, SourcePinId = currentExecOut.Id, TargetNodeId = nextNode.Id, TargetPinId = nextExecIn.Id });
        }
    }


    public void CreateDataConnectionsForExpression(string expression, BlueprintNode targetNode, ConversionContext context)
    {
        var wrappedCode = "_ = " + expression + ";";
        var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, cancellationToken: CancellationToken.None);
        var root = syntaxTree.GetCompilationUnitRoot();
        var globalStmt = root.Members.FirstOrDefault() as GlobalStatementSyntax;
        var stmt = globalStmt?.Statement as ExpressionStatementSyntax;

        if (stmt?.Expression == null) return;

        ProcessExpressionForDataConnections(stmt.Expression, targetNode, context);
    }

    private void ProcessExpressionForDataConnections(ExpressionSyntax expr, BlueprintNode targetNode, ConversionContext context)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax literal:
                CreateConstNodeForLiteral(literal, targetNode, context);
                break;

            case IdentifierNameSyntax identifier:
                CreateDataConnectionForVariable(identifier.Identifier.Text, targetNode, context);
                break;

            case AssignmentExpressionSyntax assignment:
                ProcessExpressionForDataConnections(assignment.Right, targetNode, context);
                break;

            case InvocationExpressionSyntax invoke:
                var funcName = GetMethodNameFromExpression(invoke);

                if (funcName != "Get" && funcName != "Set")
                {
                    var isHelper = context.Script.HelperFunctions.Any(h => h.Name == funcName);
                    BlueprintNode callNode;
                    if (isHelper)
                        callNode = new CallHelperNode { HelperFunctionName = funcName, Name = "Helper:" + funcName };
                    else
                        callNode = new CallNode { FunctionName = funcName, Name = "Call:" + funcName };

                    AddParameterPins(callNode, invoke.ArgumentList.Arguments);
                    context.Blueprint.AddNode(callNode);
                    context.PendingNodesForExecChain.Add(callNode);

                    CreateDataConnectionsForArguments(invoke.ArgumentList.Arguments, callNode, context);

                    var returnPin = callNode.OutputPins.FirstOrDefault(p => p.Name == "Return");
                    var targetPin = targetNode.InputPins.FirstOrDefault(p => p.Name != "Exec");
                    if (returnPin != null && targetPin != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = callNode.Id,
                            SourcePinId = returnPin.Id,
                            TargetNodeId = targetNode.Id,
                            TargetPinId = targetPin.Id,
                            PubVarName = null
                        });
                    }
                }
                break;

            default:
                var exprStr = expr.ToString();
                if (IsVariableReference(exprStr))
                {
                    CreateDataConnectionForVariable(exprStr, targetNode, context);
                }
                break;
        }
    }


    public void CreateDataConnectionsForArguments(SeparatedSyntaxList<ArgumentSyntax>? arguments, BlueprintNode callNode, ConversionContext context)
    {
        if (!arguments.HasValue || arguments.Value.Count == 0) return;

        var paramIndex = 1;
        foreach (var arg in arguments.Value)
        {
            var paramPin = callNode.InputPins.FirstOrDefault(p => p.Name == "param" + paramIndex);
            if (paramPin == null) break;
            paramIndex++;
            ProcessArgumentExpression(arg.Expression, paramPin, callNode, context);
        }
    }

    public void AddParameterPins(BlueprintNode node, SeparatedSyntaxList<ArgumentSyntax>? arguments)
    {
        if (!arguments.HasValue || arguments.Value.Count == 0) return;

        foreach (var arg in arguments.Value)
        {
            node.InputPins.Add(new BlueprintPin
            {
                Name = "param" + node.InputPins.Count,
                Direction = PinDirection.Input,
                Type = PinType.Any
            });
        }
    }

    public void CreateDataConnectionForVariable(string varName, BlueprintPin targetPin, BlueprintNode callNode, ConversionContext context)
    {
        if (context.Script.ConstBlock?.Variables.Any(v => v.Name == varName) == true)
        {
            var constNode = context.Blueprint.Nodes.OfType<ConstNode>().FirstOrDefault(c => c.ConstName == varName);
            if (constNode != null)
            {
                var constPin = constNode.OutputPins.First(p => p.Name == "Value");
                context.Blueprint.AddConnection(new BlueprintConnection
                {
                    SourceNodeId = constNode.Id,
                    SourcePinId = constPin.Id,
                    TargetNodeId = callNode.Id,
                    TargetPinId = targetPin.Id,
                    PubVarName = null
                });
            }
        }
        else
        {
            var source = context.VariableSources.Values.FirstOrDefault(s => s.PubVarName == varName);
            if (source != null)
            {
                context.Blueprint.AddConnection(new BlueprintConnection
                {
                    SourceNodeId = source.Node.Id,
                    SourcePinId = source.Pin.Id,
                    TargetNodeId = callNode.Id,
                    TargetPinId = targetPin.Id,
                    PubVarName = source.PubVarName
                });
            }
        }
    }

    public void CreateDataConnectionForVariable(string varName, BlueprintNode targetNode, ConversionContext context)
    {
        var targetPin = targetNode.InputPins.FirstOrDefault(p => p.Name != "Exec");
        if (targetPin == null) return;

        if (context.Script.ConstBlock?.Variables.Any(v => v.Name == varName) == true)
        {
            var constNode = context.Blueprint.Nodes.OfType<ConstNode>().FirstOrDefault(c => c.ConstName == varName);
            if (constNode != null)
            {
                var constPin = constNode.OutputPins.First(p => p.Name == "Value");
                context.Blueprint.AddConnection(new BlueprintConnection
                {
                    SourceNodeId = constNode.Id,
                    SourcePinId = constPin.Id,
                    TargetNodeId = targetNode.Id,
                    TargetPinId = targetPin.Id,
                    PubVarName = null
                });
            }
        }
        else
        {
            var source = context.VariableSources.Values.FirstOrDefault(s => s.PubVarName == varName);
            if (source != null)
            {
                context.Blueprint.AddConnection(new BlueprintConnection
                {
                    SourceNodeId = source.Node.Id,
                    SourcePinId = source.Pin.Id,
                    TargetNodeId = targetNode.Id,
                    TargetPinId = targetPin.Id,
                    PubVarName = source.PubVarName
                });
            }
        }
    }

    public void ProcessArgumentExpression(ExpressionSyntax expr, BlueprintPin targetPin, BlueprintNode callNode, ConversionContext context)
    {
        switch (expr)
        {
            case InvocationExpressionSyntax invoke:
                var funcName = GetMethodNameFromExpression(invoke);
                Log.Debug("[ProcessArgumentExpression] InvocationExpressionSyntax: funcName={FuncName}, expr={Expr}",
                    funcName, expr.ToString());

                if (funcName == "Get")
                {
                    var varName = invoke.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty;

                    var isPubVar = context.Script.PubVarBlock?.Variables
                        .Any(v => v.Name == varName) == true;

                    if (isPubVar)
                    {
                        var source = context.VariableSources.Values
                            .FirstOrDefault(s => s.PubVarName == varName);
                        if (source != null)
                        {
                            context.Blueprint.AddConnection(new BlueprintConnection
                            {
                                SourceNodeId = source.Node.Id,
                                SourcePinId = source.Pin.Id,
                                TargetNodeId = callNode.Id,
                                TargetPinId = targetPin.Id,
                                PubVarName = varName
                            });
                        }
                        else
                        {
                            Log.Warning("[ProcessArgumentExpression] PubVar {VarName} not found in VariableSources", varName);
                        }
                    }
                    else
                    {
                        var getNode = new GetNode { VarName = varName, Name = "Get:" + varName };
                        context.Blueprint.AddNode(getNode);
                        context.PendingNodesForExecChain.Add(getNode);
                        var getValuePin = getNode.OutputPins.First(p => p.Name == "Value");

                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = getNode.Id,
                            SourcePinId = getValuePin.Id,
                            TargetNodeId = callNode.Id,
                            TargetPinId = targetPin.Id,
                            PubVarName = null
                        });
                    }
                }
                else if (funcName == "Set")
                {
                    var args = invoke.ArgumentList.Arguments;
                    if (args.Count >= 2)
                    {
                        var varName = args[0].Expression.ToString();
                        var valueExpr = args[1].Expression;
                        var setNode = new SetNode { VarName = varName, Name = "Set:" + varName };
                        context.Blueprint.AddNode(setNode);
                        var setValuePin = setNode.InputPins.FirstOrDefault(p => p.Name == "Value");
                        if (setValuePin != null)
                        {
                            ProcessArgumentExpression(valueExpr, setValuePin, setNode, context);
                        }
                    }
                }
                else
                {
                    var isHelper = context.Script.HelperFunctions.Any(h => h.Name == funcName);
                    BlueprintNode innerCallNode = isHelper
                        ? new CallHelperNode { HelperFunctionName = funcName, Name = "Helper:" + funcName }
                        : new CallNode { FunctionName = funcName, Name = "Call:" + funcName };

                    AddParameterPins(innerCallNode, invoke.ArgumentList.Arguments);
                    context.Blueprint.AddNode(innerCallNode);
                    context.PendingNodesForExecChain.Add(innerCallNode);
                    CreateDataConnectionsForArguments(invoke.ArgumentList.Arguments, innerCallNode, context);

                    var returnPin = innerCallNode.OutputPins.FirstOrDefault(p => p.Name == "Return");
                    if (returnPin != null)
                    {
                        context.Blueprint.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = innerCallNode.Id,
                            SourcePinId = returnPin.Id,
                            TargetNodeId = callNode.Id,
                            TargetPinId = targetPin.Id,
                            PubVarName = null
                        });
                        context.VariableSources["__call_" + funcName + "_" + Guid.NewGuid().ToString("N") + "__"] = new VariableSource
                        {
                            Node = innerCallNode,
                            Pin = returnPin,
                            PubVarName = null
                        };
                    }
                }
                break;

            case LiteralExpressionSyntax literal:
                CreateConstNodeForLiteralWithPin(literal, targetPin, callNode, context);
                break;

            case IdentifierNameSyntax identifier:
                CreateDataConnectionForVariable(identifier.Identifier.Text, targetPin, callNode, context);
                break;

            default:
                var exprStr = expr.ToString();
                if (IsVariableReference(exprStr))
                {
                    CreateDataConnectionForVariable(exprStr, targetPin, callNode, context);
                }
                break;
        }
    }

    public void CreateConstNodeForLiteral(LiteralExpressionSyntax literal, BlueprintNode targetNode, ConversionContext context)
    {
        var targetPin = targetNode.InputPins.FirstOrDefault(p => p.Name != "Exec");
        if (targetPin == null) return;

        if (literal.Token.Value is string strVal)
        {
            targetPin.DefaultValue = strVal;
            Log.Debug("Set DefaultValue for {TargetNode}.{TargetPin}: {Value}", targetNode.Name, targetPin.Name, strVal);
        }
        else if (literal.Token.Value is int intVal)
        {
            targetPin.DefaultValue = intVal.ToString();
            Log.Debug("Set DefaultValue for {TargetNode}.{TargetPin}: {Value}", targetNode.Name, targetPin.Name, intVal);
        }
    }

    public void CreateConstNodeForLiteralWithPin(LiteralExpressionSyntax literal, BlueprintPin targetPin, BlueprintNode targetNode, ConversionContext context)
    {
        if (literal.Token.Value is string strVal)
        {
            targetPin.DefaultValue = strVal;
            Log.Debug("Set DefaultValue for {TargetNode}.{TargetPin}: {Value}", targetNode.Name, targetPin.Name, strVal);
        }
        else if (literal.Token.Value is int intVal)
        {
            targetPin.DefaultValue = intVal.ToString();
            Log.Debug("Set DefaultValue for {TargetNode}.{TargetPin}: {Value}", targetNode.Name, targetPin.Name, intVal);
        }
    }

    public bool IsVariableReference(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (bool.TryParse(token, out _)) return false;
        if (int.TryParse(token, out _) || double.TryParse(token, out _)) return false;
        if (token.StartsWith("'") && token.EndsWith("'")) return false;
        if (token.StartsWith("'") && token.EndsWith("'")) return false;
        if (token.Contains("(")) return false;
        var regex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z_]\w*$");
        return regex.IsMatch(token);
    }

    public string GetMethodNameFromExpression(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is IdentifierNameSyntax id)
            return id.Identifier.Text;
        if (invoke.Expression is GenericNameSyntax generic)
            return generic.Identifier.Text;
        if (invoke.Expression is MemberAccessExpressionSyntax member)
            return member.Name.Identifier.Text;
        return string.Empty;
    }
}
