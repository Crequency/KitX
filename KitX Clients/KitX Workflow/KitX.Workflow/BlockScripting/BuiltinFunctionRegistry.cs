using System.Reflection;
using Serilog;
using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// 内置函数定义的中央注册中心。通过反射自动发现所有 <see cref="IBuiltinFunctionDefinition"/>
/// 实现类并注册。替代 <c>ExprUtils</c> 中的散弹式 HashSet 和各管线类中的 switch/if-chain。
/// </summary>
public class BuiltinFunctionRegistry
{
    private readonly Dictionary<string, IBuiltinFunctionDefinition> _functions = new();

    /// <summary>
    /// 进程级共享单例，懒初始化一次 <see cref="Discover"/>（反射扫描本程序集）。
    /// 编译期静态消费者（CFG2CSConverter/CSCompiler）使用此单例，避免各自重复 Discover
    /// 造成的多次反射开销与实例漂移。DI 路径仍可独立注册（服务消费者通过注入获取）。
    /// </summary>
    public static BuiltinFunctionRegistry Instance { get; } =
        new Lazy<BuiltinFunctionRegistry>(() => Discover(typeof(BuiltinFunctionRegistry).Assembly)).Value;

    /// <summary>注册一个函数定义</summary>
    public void Register(IBuiltinFunctionDefinition definition)
    {
        // v5.0 §7: control-flow functions must have no data output pins (only Execution pins).
        // A control-flow statement terminates its block, so no later statement can consume a
        // return value. Enforced at registration so future flow-control builtins can't violate it.
        if (definition.ArgLayout != null)
        {
            foreach (var pin in definition.OutputPins)
            {
                if (pin.Type != PinType.Execution)
                {
                    throw new InvalidOperationException(
                        $"BS_INVALID_FLOWCTRL_PINS: builtin '{definition.FunctionName}' is flow-control " +
                        $"but declares a non-Execution output pin '{pin.Name}' ({pin.Type}). " +
                        "Control-flow functions must have only Execution output pins (§7).");
                }
            }
        }

        _functions[definition.FunctionName] = definition;
        Log.Debug("[BuiltinFunctionRegistry] Registered: {Name} (FlowControl={FC}, NonExtractable={NE})",
            definition.FunctionName, definition.ArgLayout != null, definition.IsNonExtractable);
    }

    /// <summary>按函数名查找定义。未找到返回 null。</summary>
    public IBuiltinFunctionDefinition? Get(string functionName)
        => _functions.TryGetValue(functionName, out var def) ? def : null;

    /// <summary>所有已注册的函数名集合</summary>
    public HashSet<string> AllFunctionNames => _functions.Keys.ToHashSet();

    /// <summary>分类为 NonExtractable 的函数名集合</summary>
    public HashSet<string> NonExtractableNames =>
        _functions.Values.Where(f => f.IsNonExtractable).Select(f => f.FunctionName).ToHashSet();

    /// <summary>分类为 FlowControl 的函数名集合</summary>
    public HashSet<string> FlowControlNames =>
        _functions.Values.Where(f => f.ArgLayout != null).Select(f => f.FunctionName).ToHashSet();

    /// <summary>所有已注册的定义</summary>
    public IReadOnlyCollection<IBuiltinFunctionDefinition> AllDefinitions => _functions.Values;

    /// <summary>
    /// 从指定程序集中反射扫描所有 <see cref="IBuiltinFunctionDefinition"/> 实现类，
    /// 实例化并注册。应在 DI 启动时调用一次。
    /// </summary>
    public static BuiltinFunctionRegistry Discover(params Assembly[] assemblies)
    {
        var registry = new BuiltinFunctionRegistry();
        var ifaceType = typeof(IBuiltinFunctionDefinition);

        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (ifaceType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                {
                    try
                    {
                        var instance = (IBuiltinFunctionDefinition)Activator.CreateInstance(type)!;
                        registry.Register(instance);
                    }
                    catch (Exception ex)
                    {
                        // Engine-internal: a builtin function definition failed to construct. This is a
                        // backend bug, not a user/workflow-author error — log to Serilog, do not surface.
                        Log.Error(ex, "[BuiltinFunctionRegistry] Failed to instantiate {Type}", type.Name);
                    }
                }
            }
        }

        Log.Information("[BuiltinFunctionRegistry] Discovered {Count} builtin functions: {Names}",
            registry._functions.Count, string.Join(", ", registry._functions.Keys));
        return registry;
    }
}
