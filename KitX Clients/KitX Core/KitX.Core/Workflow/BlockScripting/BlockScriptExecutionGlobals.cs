using System;
using System.Collections.Generic;
using System.Threading;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
using KitX.Core.Workflow;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

/// <summary>
/// Script globals for block script execution - provides access to built-in functions and collects output
/// </summary>
public class BlockScriptExecutionGlobals
{
    private readonly BlockScopeManager _scopeManager;
    private readonly List<string> _output;
    private readonly Dictionary<string, object?> _variables = new();
    private readonly IPluginManager? _pluginManager;

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
    /// Creates script globals with plugin manager support
    /// </summary>
    public BlockScriptExecutionGlobals(BlockScopeManager scopeManager, List<string> output,
        IPluginManager? pluginManager) : this(scopeManager, output)
    {
        _pluginManager = pluginManager;
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

    /// <summary>
    /// 调用插件函数。所有分发策略（类型化调用、fire-and-forget vs 同步等待）
    /// 由 RealPluginManager.CallAuto() 内部自动完成，调用方无需关心。
    /// </summary>
    public object? PluginCall(string pluginName, string methodName, params object?[] args)
    {
        if (_pluginManager == null)
        {
            Log.Warning("[BlockScriptGlobals] PluginCall: no plugin manager available, " +
                "cannot call {PluginName}.{MethodName}", pluginName, methodName);
            return null;
        }

        var callInfo = new PluginCallInfo
        {
            PluginName = pluginName,
            MethodName = methodName,
            Parameters = args ?? Array.Empty<object?>()
        };

        try
        {
            if (_pluginManager is RealPluginManager realManager)
                return realManager.CallAuto(callInfo);

            // Fallback for other IPluginManager implementations
            return _pluginManager.Call<object?>(callInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[BlockScriptGlobals] PluginCall failed: {PluginName}.{MethodName}",
                pluginName, methodName);
            return null;
        }
    }
}
