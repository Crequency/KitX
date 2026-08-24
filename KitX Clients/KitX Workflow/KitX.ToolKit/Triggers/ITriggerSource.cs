using KitX.ToolKit.Models;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// The unified trigger-source abstraction (Bench RFC §4). Every trigger type —
/// Manual, PluginEvent, UIEvent, WorkflowCompletion, Timer — collapses onto this
/// one interface. A source <c>Start</c>s listening and raises <see cref="Fired"/>
/// with a JSON payload that becomes the target workflow's parameter channel.
/// </summary>
public interface ITriggerSource
{
    /// <summary>The trigger id (from <see cref="Trigger.Id"/>).</summary>
    string Id { get; }

    /// <summary>The unified trigger type discriminant.</summary>
    TriggerType Type { get; }

    /// <summary>Begins listening for the underlying event.</summary>
    void Start(IServiceProvider services);

    /// <summary>Stops listening and releases any timers/subscriptions.</summary>
    void Stop();

    /// <summary>Raised when the source fires, carrying the JSON payload.</summary>
    event EventHandler<TriggerFiredEventArgs>? Fired;

    /// <summary>
    /// Programmatically raises <see cref="Fired"/> with the given payload. Used by
    /// <see cref="ManualTrigger"/>, by the Bench scheduler (WorkflowCompletion edges)
    /// and by tests. Sources that fire from external events call it internally too.
    /// </summary>
    void Fire(object? payload = null);
}
