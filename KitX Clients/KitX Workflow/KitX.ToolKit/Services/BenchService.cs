using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;

namespace KitX.ToolKit.Services;

/// <summary>
/// <see cref="IBenchService"/> implementation: delegates spawn/cancel/validate to the
/// <see cref="ToolkitInstanceManager"/> and the <see cref="ConfigValidator"/>.
/// </summary>
public sealed class BenchService : IBenchService
{
    private readonly ToolkitInstanceManager _manager;
    private readonly ConfigValidator _validator;

    public BenchService(ToolkitInstanceManager manager, ConfigValidator validator)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _manager.BenchEvent += (_, e) => Event?.Invoke(this, e);
    }

    /// <inheritdoc/>
    public event EventHandler<BenchEvent>? Event;

    /// <inheritdoc/>
    public string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null)
        => _manager.Spawn(toolkitId, triggerId, payload, initiator);

    /// <inheritdoc/>
    public void EndInstance(string instanceId) => _manager.EndInstance(instanceId);

    /// <inheritdoc/>
    public void EndAll() => _manager.EndAll();

    /// <inheritdoc/>
    public ConfigValidationResult Validate(Toolkit toolkit) => _validator.Validate(toolkit);
}
