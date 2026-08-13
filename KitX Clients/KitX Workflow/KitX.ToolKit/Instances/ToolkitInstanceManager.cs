using System.Collections.Concurrent;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.Validation;
using Serilog;

namespace KitX.ToolKit.Instances;

/// <summary>
/// The instance-model orchestration entry point (ToolKit 实例模型定稿). Replaces the old
/// single-active <see cref="BenchTriggerManager"/> semantics:
/// <list type="bullet">
///   <item><b>Mount</b> — subscribes a ToolKit's Spawn triggers (Manual/PluginEvent/Timer),
///   making it instantiable. Multiple ToolKits can be mounted at once (D1).</item>
///   <item><b>Spawn</b> — a fired Spawn trigger creates a new <see cref="ToolkitInstance"/>
///   (D1). Each instance is isolated (DataStore namespace, panel, cancellation) and carries
///   its <see cref="Initiator"/> (D5).</item>
///   <item><b>End</b> — cancels + destroys an instance; <b>Unmount</b> unsubscribes Spawn
///   triggers and ends all the ToolKit's instances (D8).</item>
/// </list>
/// </summary>
public sealed class ToolkitInstanceManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly TriggerSourceRegistry _registry;
    private readonly IWorkflowExecutor _executor;
    private readonly DataStore _dataStore;
    private readonly Func<Toolkit, ToolkitFileStore> _fileStoreFactory;
    private readonly ConfigValidator _validator;

    private readonly ConcurrentDictionary<string, MountedToolkit> _mounted = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ToolkitInstance> _instances = new(StringComparer.Ordinal);
    private bool _disposed;

    public ToolkitInstanceManager(
        IServiceProvider services,
        TriggerSourceRegistry registry,
        IWorkflowExecutor executor,
        DataStore dataStore,
        Func<Toolkit, ToolkitFileStore>? fileStoreFactory = null,
        ConfigValidator? validator = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _fileStoreFactory = fileStoreFactory ?? (toolkit => new ToolkitFileStore(
            Path.Combine(AppContext.BaseDirectory, "Data", "Toolkits", Sanitize(toolkit.GetId()))));
        _validator = validator ?? new ConfigValidator();
    }

    /// <summary>Raised for every Bench event (spawn/complete/cancel/run/data/ui).</summary>
    public event EventHandler<BenchEvent>? BenchEvent;

    /// <summary>The ids of currently mounted ToolKits.</summary>
    public IReadOnlyList<string> MountedToolkitIds => _mounted.Keys.ToList();

    /// <summary>True when the given ToolKit is mounted.</summary>
    public bool IsMounted(string toolkitId) => _mounted.ContainsKey(toolkitId);

    /// <summary>Snapshot of every instance across all mounted ToolKits.</summary>
    public IReadOnlyList<InstanceSnapshot> Instances => _instances.Values.Select(i => i.ToSnapshot()).ToList();

    /// <summary>
    /// Mounts a ToolKit: validates its config, builds its scheduler, and starts its Spawn
    /// trigger sources. Idempotent — re-mounting an already-mounted id is a no-op.
    /// </summary>
    public void Mount(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);
        ThrowIfDisposed();

        var id = toolkit.GetId();
        if (_mounted.ContainsKey(id))
            return;

        var validation = _validator.Validate(toolkit);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                "Invalid ToolKit config:\n  " + string.Join("\n  ", validation.Errors));

        var scheduler = new BenchScheduler(toolkit, _executor, _dataStore, _fileStoreFactory(toolkit));
        var mounted = new MountedToolkit(toolkit, scheduler);

        // Start only Spawn sources. UIEvent/WorkflowCompletion are intra-instance: UIEvent is
        // wired per-instance in the GUI iteration; WorkflowCompletion is a scheduler edge.
        foreach (var trigger in toolkit.Triggers)
        {
            if (!trigger.Type.IsSpawn())
                continue;

            var source = _registry.Create(trigger, _services);
            source.Fired += (_, e) => Spawn(id, e.TriggerId, e.Payload, null);
            mounted.Sources.Add(source);
            source.Start(_services);
            Log.Information("[ToolkitInstanceManager] Mounted Spawn source {Id} ({Type}) for {Toolkit}",
                trigger.Id, trigger.Type, id);
        }

        _mounted[id] = mounted;
        Log.Information("[ToolkitInstanceManager] Mounted ToolKit {Toolkit}", id);
    }

    /// <summary>
    /// Unmounts a ToolKit: stops its Spawn sources, disposes its scheduler, and ends every
    /// running instance of it (D8). Idempotent.
    /// </summary>
    public void Unmount(string toolkitId)
    {
        if (!_mounted.TryRemove(toolkitId, out var mounted))
            return;

        foreach (var kv in _instances.Where(kv => kv.Value.ToolkitId == toolkitId).ToList())
            EndInstance(kv.Key);

        mounted.Dispose();
        Log.Information("[ToolkitInstanceManager] Unmounted ToolKit {Toolkit}", toolkitId);
    }

    /// <summary>
    /// Spawns a new instance of a mounted ToolKit from a Spawn trigger. Returns the new
    /// instance id, or null when the trigger is unknown / not a Spawn type / the ToolKit is
    /// not mounted / the MaxInstances cap is exceeded.
    /// </summary>
    public string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null)
    {
        ThrowIfDisposed();
        if (!_mounted.TryGetValue(toolkitId, out var mounted))
            return null;

        var trigger = mounted.Toolkit.Triggers.FirstOrDefault(t => t.Id == triggerId);
        if (trigger is null || !trigger.Type.IsSpawn())
            return null;

        // MaxInstances cap (D7): count only Running instances of this ToolKit.
        var max = mounted.Toolkit.MaxInstances;
        if (max is > 0 &&
            _instances.Values.Count(i => i.ToolkitId == toolkitId && i.Status == InstanceStatus.Running) >= max)
        {
            Log.Warning("[ToolkitInstanceManager] Spawn of {Toolkit} rejected: MaxInstances={Max} reached",
                toolkitId, max);
            Raise(new InstanceSpawnedEvent(NewId(), toolkitId, string.Empty, Now(), triggerId,
                initiator ?? Initiator.Unknown, System.Text.Json.JsonSerializer.SerializeToElement<object?>(null)));
            return null;
        }

        var run = mounted.Scheduler.StartRun(triggerId, payload, initiator ?? Initiator.Unknown);
        if (run is null)
            return null;

        var instance = new ToolkitInstance(toolkitId, triggerId, run, initiator ?? Initiator.Unknown);
        instance.Completed += (_, _) => Raise(new InstanceCompletedEvent(
            NewId(), toolkitId, instance.InstanceId, Now(), run.FailedRuns == 0));
        instance.Cancelled += (_, _) => Raise(new InstanceCancelledEvent(
            NewId(), toolkitId, instance.InstanceId, Now()));

        _instances[instance.InstanceId] = instance;
        Raise(new InstanceSpawnedEvent(NewId(), toolkitId, instance.InstanceId, Now(), triggerId,
            instance.Initiator, System.Text.Json.JsonSerializer.SerializeToElement<object?>(null)));
        return instance.InstanceId;
    }

    /// <summary>Ends an instance (cancels all its runs + destroys it). Idempotent.</summary>
    public void EndInstance(string instanceId)
    {
        if (!_instances.TryRemove(instanceId, out var instance))
            return;
        instance.Cancel();
        instance.NotifyCancelled();
        instance.Dispose();
        Log.Information("[ToolkitInstanceManager] Ended instance {Instance}", instanceId);
    }

    /// <summary>Ends every instance of every mounted ToolKit.</summary>
    public void EndAll()
    {
        foreach (var id in _instances.Keys.ToList())
            EndInstance(id);
    }

    private void Raise(BenchEvent e) => BenchEvent?.Invoke(this, e);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static DateTimeOffset Now() => DateTimeOffset.UtcNow;

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c)).ToArray();
        var clean = new string(chars);
        return string.IsNullOrWhiteSpace(clean) ? "untitled" : clean;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ToolkitInstanceManager));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var id in _mounted.Keys.ToList())
            Unmount(id);
        EndAll();
    }

    /// <summary>A mounted ToolKit: its config + scheduler + started Spawn sources.</summary>
    private sealed class MountedToolkit : IDisposable
    {
        public MountedToolkit(Toolkit toolkit, BenchScheduler scheduler)
        {
            Toolkit = toolkit;
            Scheduler = scheduler;
        }

        public Toolkit Toolkit { get; }
        public BenchScheduler Scheduler { get; }
        public List<ITriggerSource> Sources { get; } = [];

        public void Dispose()
        {
            foreach (var source in Sources)
                source.Stop();
            Sources.Clear();
            Scheduler.Dispose();
        }
    }
}
