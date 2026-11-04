using KitX.ToolKit.Contract;
using KitX.ToolKit.Instances;

namespace KitX.ToolKit.Hosting;

/// <summary>
/// <see cref="IRunService"/> implementation: delegates spawn/cancel to the
/// <see cref="ToolkitInstanceManager"/>. (The <c>Validate</c> member was retired in the D3
/// cleanup — validation is a pure <see cref="Validation.ConfigValidator"/> concern that
/// callers invoke directly, so it no longer lives on the facade.)
/// </summary>
internal sealed class RunService : IRunService
{
    private readonly ToolkitInstanceManager _manager;

    public RunService(ToolkitInstanceManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <inheritdoc/>
    public string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null)
        => _manager.Spawn(toolkitId, triggerId, payload, initiator);

    /// <inheritdoc/>
    /// <remarks>
    /// This is the service-level entry point, so the reason is <see cref="InstanceEndReason.Requested"/>:
    /// someone asked for this instance specifically. The manager's own internal callers pass
    /// stronger reasons (shutdown / unmount / eviction) because they know more.
    /// </remarks>
    public void EndInstance(string instanceId) => _manager.EndInstance(instanceId, InstanceEndReason.Requested);

    /// <inheritdoc/>
    public void EndAll() => _manager.EndAll();
}
