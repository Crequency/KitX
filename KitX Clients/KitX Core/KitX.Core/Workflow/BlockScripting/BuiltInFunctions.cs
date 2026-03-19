using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Built-in functions for block scripts - these functions are injected into the script execution context
/// </summary>
public static class BuiltInFunctions
{
    /// <summary>
    /// Gets the workflow output for collecting Print() output
    /// </summary>
    private static List<string> _output { get; } = new();

    /// <summary>
    /// Condition branch - triggers jump to target block without returning a value
    /// </summary>
    /// <remarks>
    /// This function throws a BranchException which is caught by the execution engine
    /// to implement block jumping. The condition determines which branch to take.
    /// </remarks>
    /// <param name="condition">Boolean condition</param>
    /// <param name="trueBlock">Block name to jump to when condition is true</param>
    /// <param name="falseBlock">Block name to jump to when condition is false</param>
    public static void Branch(bool condition, string trueBlock, string falseBlock)
    {
        var targetBlock = condition ? trueBlock : falseBlock;
        throw new BranchException(targetBlock);
    }

    /// <summary>
    /// Loop - triggers jump back to a block while condition is true
    /// </summary>
    /// <param name="condition">Boolean condition</param>
    /// <param name="loopBlock">Block name to jump to while condition is true</param>
    public static void Loop(bool condition, string loopBlock)
    {
        if (condition)
            throw new BranchException(loopBlock);
        // Condition is false, continue to next statement
    }

    /// <summary>
    /// Pause execution for specified milliseconds (for debugging)
    /// </summary>
    /// <param name="milliseconds">Milliseconds to sleep</param>
    public static void Pause(int milliseconds)
    {
        Thread.Sleep(milliseconds);
    }

    /// <summary>
    /// Print a value to workflow output
    /// </summary>
    /// <param name="value">Value to print</param>
    public static void Print(object? value)
    {
        WorkflowOutput.WriteLine(value);
        _output.Add(value?.ToString() ?? "null");
    }

    /// <summary>
    /// Clears the accumulated output
    /// </summary>
    public static void ClearOutput()
    {
        _output.Clear();
    }

    /// <summary>
    /// Gets all accumulated output
    /// </summary>
    public static IReadOnlyList<string> GetOutput() => _output.AsReadOnly();
}

/// <summary>
/// Branch exception - used by execution engine to trigger block jumps
/// </summary>
public class BranchException : Exception
{
    /// <summary>
    /// Target block name to jump to
    /// </summary>
    public string TargetBlock { get; }

    /// <summary>
    /// Creates a new branch exception
    /// </summary>
    /// <param name="targetBlock">Target block name</param>
    public BranchException(string targetBlock) : base($"Branch to {targetBlock}")
    {
        TargetBlock = targetBlock;
    }
}

/// <summary>
/// Return exception - used by execution engine to return from script
/// </summary>
public class ReturnException : Exception
{
    /// <summary>
    /// Return value
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Creates a new return exception
    /// </summary>
    /// <param name="value">Return value</param>
    public ReturnException(object? value) : base("Return from script")
    {
        Value = value;
    }
}

/// <summary>
/// Break exception - used by execution engine to break from loop
/// </summary>
public class BreakException : Exception
{
    /// <summary>
    /// Creates a new break exception
    /// </summary>
    public BreakException() : base("Break from loop")
    {
    }
}

/// <summary>
/// Script globals for execution - provides access to built-in functions and global state
/// </summary>
public class ScriptGlobals
{
    /// <summary>
    /// Reference to scope manager for variable access
    /// </summary>
    public BlockScopeManager ScopeManager { get; }

    /// <summary>
    /// Input parameters passed to the script
    /// </summary>
    public Dictionary<string, object?>? Parameters { get; }

    /// <summary>
    /// Creates script globals
    /// </summary>
    public ScriptGlobals(BlockScopeManager scopeManager, Dictionary<string, object?>? parameters = null)
    {
        ScopeManager = scopeManager;
        Parameters = parameters;
    }

    /// <summary>
    /// Branch to another block
    /// </summary>
    public void Branch(bool condition, string trueBlock, string falseBlock)
    {
        BuiltInFunctions.Branch(condition, trueBlock, falseBlock);
    }

    /// <summary>
    /// Loop while condition is true
    /// </summary>
    public void Loop(bool condition, string loopBlock)
    {
        BuiltInFunctions.Loop(condition, loopBlock);
    }

    /// <summary>
    /// Print a value
    /// </summary>
    public void Print(object? value)
    {
        BuiltInFunctions.Print(value);
    }

    /// <summary>
    /// Pause execution
    /// </summary>
    public void Pause(int milliseconds)
    {
        BuiltInFunctions.Pause(milliseconds);
    }
}
