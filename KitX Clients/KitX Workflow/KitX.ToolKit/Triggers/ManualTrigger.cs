using KitX.ToolKit.Models;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// A manually-fired trigger ("press Run"). It listens to nothing — firing is
/// programmatic via <see cref="ITriggerSource.Fire"/> (canvas run button, CLI,
/// tests). Start/Stop are no-ops.
/// </summary>
public sealed class ManualTrigger : TriggerSourceBase
{
    public ManualTrigger(string id)
        : base(id, TriggerType.Manual)
    {
    }

    /// <inheritdoc/>
    public override void Start(IServiceProvider services)
    {
    }

    /// <inheritdoc/>
    public override void Stop()
    {
    }
}
