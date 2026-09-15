using KitX.ToolKit.Contracts;
using KitX.ToolKit.Instances;

namespace KitX.ToolKit.Services;

/// <summary>
/// <see cref="IBenchService"/> implementation: delegates spawn/cancel to the
/// <see cref="ToolkitInstanceManager"/>. (The <c>Validate</c> member was retired in the D3
/// cleanup — validation is a pure <see cref="Validation.ConfigValidator"/> concern that
/// callers invoke directly, so it no longer lives on the facade.)
/// </summary>
public sealed class BenchService : IBenchService
{
    private readonly ToolkitInstanceManager _manager;

    public BenchService(ToolkitInstanceManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <inheritdoc/>
    public string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null)
        => _manager.Spawn(toolkitId, triggerId, payload, initiator);

    /// <inheritdoc/>
    public void EndInstance(string instanceId) => _manager.EndInstance(instanceId);

    /// <inheritdoc/>
    public void EndAll() => _manager.EndAll();
}
