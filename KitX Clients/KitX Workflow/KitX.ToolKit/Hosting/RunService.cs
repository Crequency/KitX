using KitX.ToolKit.Contract;
using KitX.ToolKit.Instances;

namespace KitX.ToolKit.Hosting;

/// <summary>
/// <see cref="IRunService"/> implementation: delegates spawn to the
/// <see cref="ToolkitInstanceManager"/>.
/// </summary>
/// <remarks>
/// The cancel family (<c>EndInstance</c> / <c>EndAll</c>) was removed from the contract on
/// 2026-11-08 (ADR-0015 D5.2). Stopping flows is <see cref="IToolkitService.Disable"/>, which
/// lives on the other service because it is a lifecycle operation on a ToolKit, not an operation
/// on a flow.
/// </remarks>
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
}
