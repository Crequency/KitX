using System;
using System.Collections.Generic;
using System.Threading;
using KitX.Core.Workflow;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Script globals for block script execution - provides access to built-in functions and collects output
/// </summary>
public class BlockScriptExecutionGlobals
{
    private readonly BlockScopeManager _scopeManager;
    private readonly List<string> _output;
    private readonly Dictionary<string, object?> _variables = new();

    /// <summary>
    /// NextBlock 内置变量 - 设置后执行器会跳转到指定块
    /// 每个块执行前会被重置为 null
    /// </summary>
    public string? NextBlock { get; set; } = null;

    /// <summary>
    /// Creates script globals
    /// </summary>
    public BlockScriptExecutionGlobals(BlockScopeManager scopeManager, List<string> output)
    {
        _scopeManager = scopeManager;
        _output = output;
    }

    /// <summary>
    /// Gets a variable value - called by CSharpScript when accessing unknown properties
    /// Returns dynamic to allow implicit conversion to target variable types
    /// </summary>
    public dynamic Get(string name)
    {
        if (name == "NextBlock")
            return NextBlock!;
        if (_variables.TryGetValue(name, out var value))
            return value!;
        return _scopeManager.ResolveVariable(name)!;
    }

    /// <summary>
    /// Sets a variable value
    /// </summary>
    public void Set(string name, object? value)
    {
        if (name == "NextBlock")
        {
            NextBlock = value as string;
            return;
        }
        _variables[name] = value;
        _scopeManager.SetVariable(name, value, global: false);
    }

    /// <summary>
    /// Sets a variable value in global scope
    /// </summary>
    public void SetGlobalVariable(string name, object? value)
    {
        if (name == "NextBlock")
        {
            NextBlock = value as string;
            return;
        }
        _variables[name] = value;
        _scopeManager.SetVariable(name, value, global: true);
    }

    /// <summary>
    /// Resets NextBlock to null (called before each block execution)
    /// </summary>
    public void ResetNextBlock()
    {
        NextBlock = null;
    }

    /// <summary>
    /// Gets all variables for debugging
    /// </summary>
    public Dictionary<string, object?> GetAllVariables() => new(_variables);

    /// <summary>
    /// Condition branch - sets NextBlock and returns the target block name
    /// </summary>
    public string? Branch(bool condition, string trueBlock, string falseBlock)
    {
        NextBlock = condition ? trueBlock : falseBlock;
        return NextBlock;
    }

    /// <summary>
    /// Loop while condition is true (three-argument syntax)
    /// </summary>
    public string? Loop(bool condition, string trueBlock, string falseBlock)
    {
        NextBlock = condition ? trueBlock : falseBlock;
        return NextBlock;
    }

    /// <summary>
    /// Print a value
    /// </summary>
    public void Print(object? value)
    {
        var str = value?.ToString() ?? "null";
        _output.Add(str);
        WorkflowOutput.WriteLine(value);
    }

    /// <summary>
    /// Pause execution
    /// </summary>
    public void Pause(int milliseconds)
    {
        Thread.Sleep(milliseconds);
    }

    /// <summary>
    /// LoopBodyEnd - marks the end of a loop body and returns to the loop condition block
    /// </summary>
    public string? LoopBodyEnd(string parentBlockName)
    {
        NextBlock = $"{parentBlockName}_Loop";
        return NextBlock;
    }
}
