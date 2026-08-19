using KitX.ToolKit.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// Maps a <see cref="TriggerType"/> to the <see cref="ITriggerSource"/> implementation
/// that handles it (Bench RFC §10: "TriggerManager 泛化为 ITriggerSource 集合（每
/// Trigger 类型一实现）"). The built-in types are registered by
/// <see cref="BuildDefault"/>/DI; a host can register its own factory to override a type.
/// </summary>
public sealed class TriggerSourceRegistry
{
    private readonly Dictionary<TriggerType, Func<Trigger, IServiceProvider, ITriggerSource>> _factories = new();

    /// <summary>Registers a factory for a trigger type. Overwrites any prior factory for that type.</summary>
    public void Register(TriggerType type, Func<Trigger, IServiceProvider, ITriggerSource> factory)
        => _factories[type] = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>Creates the <see cref="ITriggerSource"/> for a trigger via its registered factory.</summary>
    public ITriggerSource Create(Trigger trigger, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!_factories.TryGetValue(trigger.Type, out var factory))
            throw new InvalidOperationException($"No trigger source registered for type '{trigger.Type}'.");
        return factory(trigger, services);
    }

    /// <summary>True when a factory is registered for the given type.</summary>
    public bool IsRegistered(TriggerType type) => _factories.ContainsKey(type);

    /// <summary>
    /// Builds the registry with the default built-in source set: Manual / PluginEvent /
    /// Timer. The UIEvent and WorkflowCompletion "shadow" sources were retired in the D4
    /// cleanup — UIEvent is wired per-instance by the manager's RaiseControlEvent (which
    /// reads the trigger config directly), and WorkflowCompletion is a scheduler edge, so
    /// neither needs an ITriggerSource. The <see cref="TriggerType"/> enum keeps all five
    /// types (the config model is unchanged).
    /// </summary>
    public static TriggerSourceRegistry BuildDefault()
    {
        var registry = new TriggerSourceRegistry();

        registry.Register(TriggerType.Manual,
            (t, _) => new ManualTrigger(t.Id));

        registry.Register(TriggerType.PluginEvent,
            (t, sp) => new PluginEventTrigger(t.Id,
                sp.GetRequiredService<KitX.Core.Contract.Plugin.IPluginServer>(),
                t.Config));

        registry.Register(TriggerType.Timer,
            (t, _) => new TimerTrigger(t.Id, t.Config));

        return registry;
    }
}
