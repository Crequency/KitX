using KitX.ToolKit.Models;
using Serilog;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// A UI-panel control event trigger (Bench RFC §4.2 <c>UIEvent</c>). Declared and
/// registerable now, but its runtime source is a skeleton until the GUI framework
/// lands (the panel does not exist yet). Firing is possible programmatically via
/// <see cref="ITriggerSource.Fire"/> so the rest of the pipeline is testable; the
/// actual control-event wiring is the GUI iteration's responsibility.
/// </summary>
public sealed class UiEventTrigger : TriggerSourceBase
{
    private readonly TriggerConfig _config;

    public UiEventTrigger(string id, TriggerConfig config)
        : base(id, TriggerType.UIEvent)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public override void Start(IServiceProvider services)
    {
        Log.Warning("[UiEventTrigger] Trigger {Id} is a UIEvent source — the runtime UI " +
            "framework is not yet available; it only fires programmatically.", Id);
    }

    /// <inheritdoc/>
    public override void Stop()
    {
    }
}
