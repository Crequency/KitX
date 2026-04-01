using System;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Implementation of node creation service
/// </summary>
public class NodeCreationService : INodeCreationService
{
    /// <summary>
    /// Creates a constant node
    /// </summary>
    public ConstNode CreateConstNode(string name, string type, string? defaultValue)
    {
        var constNode = new ConstNode
        {
            ConstName = name,
            ConstType = type,
            ConstValue = defaultValue ?? GetDefaultValueForType(type)
        };
        Log.Debug("Created ConstNode: Name={ConstName}, Id={NodeId}", constNode.Name, constNode.Id);
        return constNode;
    }

    /// <summary>
    /// Creates an entry node at the specified position
    /// </summary>
    public EntryNode CreateEntryNode(double x, double y)
    {
        var entryNode = new EntryNode
        {
            X = x,
            Y = y
        };
        Log.Debug("Created EntryNode: Name={NodeName}, Id={NodeId}", entryNode.Name, entryNode.Id);
        return entryNode;
    }

    /// <summary>
    /// Creates a get variable node
    /// </summary>
    public GetNode CreateGetNode(string varName)
    {
        var getNode = new GetNode
        {
            VarName = varName,
            Name = $"Get:{varName}"
        };
        Log.Debug("Created GetNode: VarName={VarName}, Id={NodeId}", varName, getNode.Id);
        return getNode;
    }

    /// <summary>
    /// Creates a set variable node
    /// </summary>
    public SetNode CreateSetNode(string varName)
    {
        var setNode = new SetNode
        {
            VarName = varName,
            Name = $"Set:{varName}"
        };
        Log.Debug("Created SetNode: VarName={VarName}, Id={NodeId}", varName, setNode.Id);
        return setNode;
    }

    /// <summary>
    /// Creates a print node
    /// </summary>
    public PrintNode CreatePrintNode()
    {
        var printNode = new PrintNode();
        Log.Debug("Created PrintNode: Id={NodeId}", printNode.Id);
        return printNode;
    }

    /// <summary>
    /// Creates a pause node
    /// </summary>
    public PauseNode CreatePauseNode()
    {
        var pauseNode = new PauseNode();
        Log.Debug("Created PauseNode: Id={NodeId}", pauseNode.Id);
        return pauseNode;
    }

    /// <summary>
    /// Creates a call function node
    /// </summary>
    public CallNode CreateCallNode(string functionName)
    {
        var callNode = new CallNode
        {
            FunctionName = functionName,
            Name = $"Call:{functionName}"
        };
        Log.Debug("Created CallNode: FunctionName={FunctionName}, Id={NodeId}", functionName, callNode.Id);
        return callNode;
    }

    /// <summary>
    /// Creates a call helper function node
    /// </summary>
    public CallHelperNode CreateCallHelperNode(string helperFunctionName)
    {
        var callHelperNode = new CallHelperNode
        {
            HelperFunctionName = helperFunctionName,
            Name = $"Helper:{helperFunctionName}"
        };
        Log.Debug("Created CallHelperNode: HelperFunctionName={HelperFunctionName}, Id={NodeId}", helperFunctionName, callHelperNode.Id);
        return callHelperNode;
    }

    /// <summary>
    /// Creates a branch node
    /// </summary>
    public BranchNode CreateBranchNode()
    {
        var branchNode = new BranchNode();
        Log.Debug("Created BranchNode: Id={NodeId}", branchNode.Id);
        return branchNode;
    }

    /// <summary>
    /// Creates a loop node
    /// </summary>
    public LoopNode CreateLoopNode()
    {
        var loopNode = new LoopNode();
        Log.Debug("Created LoopNode: Id={NodeId}", loopNode.Id);
        return loopNode;
    }

    /// <summary>
    /// Creates a break node
    /// </summary>
    public BreakNode CreateBreakNode()
    {
        var breakNode = new BreakNode();
        Log.Debug("Created BreakNode: Id={NodeId}", breakNode.Id);
        return breakNode;
    }

    /// <summary>
    /// Gets default value for a given type
    /// </summary>
    private static string GetDefaultValueForType(string type)
    {
        return type.ToLower() switch
        {
            "int" or "integer" or "long" or "short" or "byte" => "0",
            "float" or "double" or "decimal" => "0",
            "bool" or "boolean" => "false",
            "string" => "",
            "char" => "\0",
            _ => ""
        };
    }
}
