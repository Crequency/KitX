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
    public Toolkit DuplicateToolkit(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var source = _store.Load(sourceId)
            ?? throw new ToolkitServiceException(
                ToolkitServiceException.NotFoundCode,
                $"ToolKit '{sourceId}' not found.");

        // Blanking the id is what the three-step manual equivalent gets wrong: leaving it in place
        // does not fail, it overwrites the original with a copy of itself. The store assigns a fresh
        // one because the id is empty.
        //
        // Every list is rebuilt rather than shared: `Toolkit` is a mutable class, so handing the
        // source's lists to the copy would make editing one edit both.
        var copy = new Toolkit
        {
            Id = string.Empty,
            Meta = new ToolkitMeta
            {
                Name = CopyName(source.Meta?.Name),
                Version = source.Meta?.Version ?? "1.0.0",
                Author = source.Meta?.Author ?? string.Empty,
                Icon = source.Meta?.Icon ?? string.Empty,
                Description = source.Meta?.Description ?? string.Empty,
                MinKitXVersion = source.Meta?.MinKitXVersion ?? "0.0.0.0",
                Tags = source.Meta?.Tags is { } tags ? [.. tags] : [],
            },
            Workflows = [.. source.Workflows],
            Plugins = [.. source.Plugins],
            Triggers = [.. source.Triggers],
            Comments = [.. source.Comments],
            Keys = [.. source.Keys],
        };

        // `_store.Save` writes toolkit.json only, so the copy starts with no values under its keys.
        // That is the intended meaning of "a fresh ToolKit configured the same way", not an
        // oversight.
        var saved = _store.Save(copy);
        ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>
    /// A display name for a copy: the source's name with a suffix, so two identically named cards
    /// do not both appear in the list.
    /// </summary>
    private static string CopyName(string? sourceName)
    {
        var name = string.IsNullOrWhiteSpace(sourceName) ? "ToolKit" : sourceName;
        return $"{name} (copy)";
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
