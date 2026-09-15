using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Storage;

namespace KitX.ToolKit.Services;

/// <summary>
/// <see cref="IToolkitService"/> implementation: composes the <see cref="ToolkitStore"/>
/// (persistence) with the <see cref="ToolkitInstanceManager"/> (mount/instances). The
/// frontend depends only on the contract.
/// </summary>
public sealed class ToolkitService : IToolkitService
{
    private readonly ToolkitStore _store;
    private readonly ToolkitInstanceManager _manager;

    public ToolkitService(ToolkitStore store, ToolkitInstanceManager manager)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _manager.BenchEvent += (_, e) => BenchEvent?.Invoke(this, e);
    }

    /// <inheritdoc/>
    public event EventHandler? ToolkitListChanged;

    /// <inheritdoc/>
    public event EventHandler<BenchEvent>? BenchEvent;

    /// <inheritdoc/>
    public IReadOnlyList<Toolkit> ListToolkits() => _store.List();

    /// <inheritdoc/>
    public Toolkit? GetToolkit(string toolkitId) => _store.Load(toolkitId);

    /// <inheritdoc/>
    public Toolkit CreateToolkit(Toolkit draft)
    {
        var saved = _store.Save(draft);
        ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <inheritdoc/>
    public Toolkit UpdateToolkit(Toolkit toolkit)
    {
        if (_manager.IsMounted(toolkit.GetId()))
            throw new InvalidOperationException($"ToolKit '{toolkit.GetId()}' is mounted; unmount before updating.");
        var saved = _store.Save(toolkit);
        ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <inheritdoc/>
    public bool DeleteToolkit(string toolkitId)
    {
        if (_manager.IsMounted(toolkitId))
            throw new InvalidOperationException($"ToolKit '{toolkitId}' is mounted; unmount before deleting.");
        var deleted = _store.Delete(toolkitId);
        if (deleted)
            ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return deleted;
    }

    /// <inheritdoc/>
    public void Mount(string toolkitId)
    {
        var toolkit = _store.Load(toolkitId)
            ?? throw new InvalidOperationException($"ToolKit '{toolkitId}' not found.");
        _manager.Mount(toolkit);
    }

    /// <inheritdoc/>
    public void Unmount(string toolkitId) => _manager.Unmount(toolkitId);

    /// <inheritdoc/>
    public IReadOnlyList<string> MountedToolkitIds => _manager.MountedToolkitIds;

    /// <inheritdoc/>
    public bool IsMounted(string toolkitId) => _manager.IsMounted(toolkitId);

    /// <inheritdoc/>
    public IReadOnlyList<InstanceSnapshot> Instances => _manager.Instances;
}
