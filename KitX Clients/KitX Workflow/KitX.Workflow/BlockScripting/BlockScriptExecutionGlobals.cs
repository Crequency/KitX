using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Script globals for block script execution — 仅保留核心手脚架。
/// 各 builtin 函数的运行时方法已迁移到各自的 partial class 文件中。
/// </summary>
public partial class BlockScriptExecutionGlobals
{
    // ─── 核心字段 ───────────────────────────────────────────────

    private readonly BlockScopeManager _scopeManager;
    private readonly List<string> _output;
    private readonly Dictionary<string, object?> _variables = new();
    private readonly IPluginManager? _pluginManager;

    public IBlueprintDebugController? Debugger { get; set; }

    // ─── 内置属性 ───────────────────────────────────────────────

    /// <summary>
    /// NextBlock 内置变量 - 设置后执行器会跳转到指定块。
    /// 这是控制流的唯一载体。普通 <c>Set/Get</c> 不再识别 "NextBlock" 这个名字,
    /// 因此用户脚本无法通过变量赋值劫持控制流、绕过 Loop/Break 语义或 CFG 校验。
    /// 仅两类调用方可写此属性:(1) 生成的 <c>RunAsync</c> 主干(G.NextBlock = ...)——
    /// 它是可信基础设施,完全由 CFG2CSConverter 按已校验的 CFG 产出;(2) 受信任的 flow
    /// 函数(Branch/Loop/Switch/Flip/ToLoopCond),应优先通过 <see cref="AdvanceTo"/> 写入。
    /// </summary>
    public string? NextBlock { get; set; }

    /// <summary>
    /// 受信任的控制流改写入口。内置 flow 函数应通过此方法设置下一个块,
    /// 而非直接写 <see cref="NextBlock"/>,以保持单一改写路径。
    /// </summary>
    internal string? AdvanceTo(string? blockName)
    {
        NextBlock = blockName;
        return NextBlock;
    }

    /// <summary>
    /// Number of blocks executed so far in the current run.
    /// </summary>
    public int ExecutedBlockCount { get; set; }

    // ─── 构造 ───────────────────────────────────────────────

    public BlockScriptExecutionGlobals(BlockScopeManager scopeManager, List<string> output)
    {
        _scopeManager = scopeManager;
        _output = output;
    }

    public BlockScriptExecutionGlobals(BlockScopeManager scopeManager, List<string> output,
        IPluginManager? pluginManager) : this(scopeManager, output)
    {
        _pluginManager = pluginManager;
    }

    // ─── 变量访问 ───────────────────────────────────────────────

    /// <summary>
    /// Gets a variable value dynamically (CSharpScript path).
    /// "NextBlock" is no longer a recognised variable name — control flow is only mutated
    /// via <see cref="AdvanceTo"/> by trusted flow functions.
    /// </summary>
    public dynamic Get(string name)
    {
        if (_variables.TryGetValue(name, out var value))
            return value!;
        return _scopeManager.ResolveVariable(name)!;
    }

    /// <summary>
    /// Gets a variable value typed as T (compiled assembly path).
    /// </summary>
    public T? Get<T>(string name)
    {
        if (_variables.TryGetValue(name, out var value))
            return (T?)value;
        return (T?)_scopeManager.ResolveVariable(name);
    }

    /// <summary>
    /// Sets a variable value in local scope.
    /// </summary>
    public void Set(string name, object? value)
    {
        _variables[name] = value;
        _scopeManager.SetVariable(name, value, global: false);
        Debugger?.UpdateVariableSnapshot(GetAllVariables());
    }

    /// <summary>
    /// Sets a variable value in global scope.
    /// </summary>
    public void SetGlobalVariable(string name, object? value)
    {
        _variables[name] = value;
        _scopeManager.SetVariable(name, value, global: true);
        Debugger?.UpdateVariableSnapshot(GetAllVariables());
    }

    /// <summary>
    /// Resets NextBlock to null (called before each block execution).
    /// </summary>
    public void ResetNextBlock()
    {
        NextBlock = null;
    }

    /// <summary>
    /// Gets all variables for debugging.
    /// </summary>
    public Dictionary<string, object?> GetAllVariables() => new(_variables);

    // ─── 运行状态重置 ───────────────────────────────────────────────

    /// <summary>
    /// Resets run-level state (called when execution starts from Entry node).
    /// </summary>
    public void ResetRunState()
    {
        ResetFlipCounter();
        ExecutedBlockCount = 0;
    }
}
