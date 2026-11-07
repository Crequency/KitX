using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Storage;

namespace KitX.ToolKit.Hosting;

/// <summary>
/// <see cref="IToolkitService"/> implementation: composes the <see cref="ToolkitStore"/>
/// (persistence) with the <see cref="ToolkitInstanceManager"/> (mount/instances). The
/// frontend depends only on the contract.
/// </summary>
internal sealed class ToolkitService : IToolkitService
{
    private readonly ToolkitStore _store;
    private readonly ToolkitInstanceManager _manager;

    public ToolkitService(ToolkitStore store, ToolkitInstanceManager manager)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _manager.ToolkitEvent += (_, e) => ToolkitEvent?.Invoke(this, e);
    }

    /// <inheritdoc/>
    public event EventHandler? ToolkitListChanged;

    /// <inheritdoc/>
    public event EventHandler<ToolkitEvent>? ToolkitEvent;

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
            throw new ToolkitServiceException(
                ToolkitServiceException.MountedCode,
                $"ToolKit '{toolkit.GetId()}' is mounted; unmount before updating.");
        var saved = _store.Save(toolkit);
        ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <inheritdoc/>
    public bool DeleteToolkit(string toolkitId)
    {
        if (_manager.IsMounted(toolkitId))
            throw new ToolkitServiceException(
                ToolkitServiceException.MountedCode,
                $"ToolKit '{toolkitId}' is mounted; unmount before deleting.");
        var deleted = _store.Delete(toolkitId);
        if (deleted)
            ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return deleted;
    }

    /// <inheritdoc/>
    public void Mount(string toolkitId)
    {
        var toolkit = _store.Load(toolkitId)
            ?? throw new ToolkitServiceException(
                ToolkitServiceException.NotFoundCode,
                $"ToolKit '{toolkitId}' not found.");
        _manager.Mount(toolkit);
    }

    /// <inheritdoc/>
    public void Unmount(string toolkitId) => _manager.Unmount(toolkitId);

    /// <inheritdoc/>
    public void Disable(string toolkitId) => _manager.Disable(toolkitId);

    /// <inheritdoc/>
    public void Enable(string toolkitId) => _manager.Enable(toolkitId);

    /// <inheritdoc/>
    public int Uninstall(string toolkitId)
    {
        var removed = _manager.Uninstall(toolkitId);

        // The on-disk config goes too: "uninstall" that leaves the directory behind would be
        // re-discovered by ListToolkits on the next scan, which reads as the opposite of removed.
        _store.Delete(toolkitId);
        ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <inheritdoc/>
    public LifecycleState? GetLifecycleState(string toolkitId) => _manager.GetLifecycleState(toolkitId);

    /// <inheritdoc/>
    public IReadOnlyList<string> MountedToolkitIds => _manager.MountedToolkitIds;

    /// <inheritdoc/>
    public bool IsMounted(string toolkitId) => _manager.IsMounted(toolkitId);

    /// <inheritdoc/>
    public IReadOnlyList<Diagnostic> Validate(string toolkitId)
    {
        var toolkit = _store.Load(toolkitId);
        if (toolkit is null)
        {
            return
            [
                Diagnostic.Error(
                    ToolkitServiceException.NotFoundCode,
                    $"ToolKit '{toolkitId}' not found.",
                    new DiagnosticPath(ToolkitId: toolkitId)),
            ];
        }

        // Same call Mount makes, minus the scheduler. Sharing it is the point: a config that
        // compiles here and fails there (or the reverse) would make the check worthless.
        return _manager.Validate(toolkit);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, int> ActiveFlowCounts() => _manager.ActiveFlowCounts();
}
