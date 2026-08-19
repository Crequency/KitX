using System.Collections.Concurrent;
using System.Text.Json;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.ToolKit.Panels;
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

    // Serializes the "MaxInstances check → StartRun → _instances registration" sequence
    // so concurrent Spawn calls cannot all observe zero Running instances and jointly
    // exceed the cap (a TOCTOU window in the pre-existing code).
    private readonly object _spawnGate = new();

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
        _fileStoreFactory = fileStoreFactory ?? (_ => new ToolkitFileStore(
            Path.Combine(AppContext.BaseDirectory, "Data", "Toolkits")));
        _validator = validator ?? new ConfigValidator();

        // The DataStore blackboard is the panel-projection primitive: project every
        // instance-scoped panel write into the Bench event channel so the host can render
        // live values, log entries and dialog requests (GUI RFC §5.7 / UX v2 C26-C27).
        _dataStore.Changed += OnDataStoreChanged;
    }

    /// <summary>Raised for every Bench event (spawn/complete/cancel/run/data/ui).</summary>
    public event EventHandler<BenchEvent>? BenchEvent;

    /// <summary>The ids of currently mounted ToolKits.</summary>
    public IReadOnlyList<string> MountedToolkitIds => _mounted.Keys.ToList();

    /// <summary>True when the given ToolKit is mounted.</summary>
    public bool IsMounted(string toolkitId) => _mounted.ContainsKey(toolkitId);

    /// <summary>Snapshot of every instance across all mounted ToolKits.</summary>
    public IReadOnlyList<InstanceSnapshot> Instances => _instances.Values.Select(instance =>
    {
        var snapshot = instance.ToSnapshot();
        if (_mounted.TryGetValue(instance.ToolkitId, out var mounted) &&
            mounted.Toolkit.Triggers.FirstOrDefault(t => t.Id == instance.TriggerId) is { } trigger)
        {
            snapshot = snapshot with { Surface = trigger.Config?.Surface };
        }

        return snapshot;
    }).ToList();

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

        // The cap check and the instance registration must be atomic. StartRun schedules
        // node bodies on background threads and returns before any completes, so no
        // synchronous callback re-enters this lock.
        lock (_spawnGate)
        {
            // MaxInstances cap (D7): count only Running instances of this ToolKit.
            var max = mounted.Toolkit.MaxInstances;
            if (max is > 0 &&
                _instances.Values.Count(i => i.ToolkitId == toolkitId && i.Status == InstanceStatus.Running) >= max)
            {
                Log.Warning("[ToolkitInstanceManager] Spawn of {Toolkit} rejected: MaxInstances={Max} reached",
                    toolkitId, max);
                Raise(new InstanceSpawnRejectedEvent(NewId(), toolkitId, string.Empty, Now(), triggerId,
                    $"MaxInstances={max}"));
                return null;
            }

            var run = mounted.Scheduler.StartRun(triggerId, payload, initiator ?? Initiator.Unknown, null, AttachRunEvents);
            if (run is null)
                return null;

            var instance = new ToolkitInstance(toolkitId, triggerId, run, initiator ?? Initiator.Unknown);
            instance.Completed += (_, _) =>
            {
                Log.Information("[ToolkitInstanceManager] Instance {Instance} completed (toolkit {Toolkit})",
                    instance.InstanceId, toolkitId);
                Raise(new InstanceCompletedEvent(
                    NewId(), toolkitId, instance.InstanceId, Now(), run.FailedRuns == 0));
            };
            instance.Cancelled += (_, _) => Raise(new InstanceCancelledEvent(
                NewId(), toolkitId, instance.InstanceId, Now()));

            _instances[instance.InstanceId] = instance;
            Raise(new InstanceSpawnedEvent(NewId(), toolkitId, instance.InstanceId, Now(), triggerId,
                instance.Initiator, JsonSerializer.SerializeToElement(payload)));
            return instance.InstanceId;
        }
    }

    /// <summary>
    /// Subscribes to a run's per-node events before its root workflows are scheduled
    /// (StartRun invokes this callback pre-scheduling) and projects them onto the Bench
    /// contract. The owning instance id is the run's namespace — for intra-instance
    /// UIEvent chains that is the existing instance, not the transient run id.
    /// </summary>
    private void AttachRunEvents(BenchRunInstance run)
    {
        run.NodeStarted += (_, e) => Raise(new RunStartedEvent(
            NewId(), run.ToolkitId, run.NamespaceId, Now(), e.InstanceId, e.WorkflowId));
        run.NodeCompleted += (_, e) => Raise(new RunCompletedEvent(
            NewId(), run.ToolkitId, run.NamespaceId, Now(), e.InstanceId, e.WorkflowId, e.Succeeded, e.Error));
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

    /// <summary>The toolkit id an instance belongs to, or null when unknown.</summary>
    public string? GetToolkitId(string instanceId)
        => _instances.TryGetValue(instanceId, out var i) ? i.ToolkitId : null;

    /// <summary>Resolves a control definition from the instance's toolkit UiPanel, or null.</summary>
    public UiControl? GetControl(string instanceId, string controlId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return null;
        if (!_mounted.TryGetValue(instance.ToolkitId, out var mounted))
            return null;
        return mounted.Toolkit.UiPanel?.Controls.FirstOrDefault(c => c.Id == controlId);
    }

    /// <summary>
    /// Routes a panel control event to the owning instance's UIEvent trigger chain (intra —
    /// never spawns a new instance). Each matching UIEvent trigger starts a run scoped to the
    /// instance's namespace, so the panel stays a single interaction surface.
    /// </summary>
    public void RaiseControlEvent(string instanceId, string controlId, string eventName, object? value)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return;
        if (!_mounted.TryGetValue(instance.ToolkitId, out var mounted))
            return;

        // Dialog contract (GUI RFC §5.7): confirming clears the backend request slot.
        if (string.Equals(eventName, "Confirm", StringComparison.OrdinalIgnoreCase))
            _dataStore.Remove(PanelScope.Key(instance.ToolkitId, instanceId, controlId, "request"));

        var payload = JsonSerializer.SerializeToElement(new { controlId, @event = eventName, value });
        foreach (var trigger in mounted.Toolkit.Triggers.Where(t =>
                     t.Type == TriggerType.UIEvent && Matches(t.Config, controlId, eventName)))
        {
            mounted.Scheduler.StartRun(trigger.Id, payload, instance.Initiator, instance.InstanceId, AttachRunEvents);
        }
    }

    /// <summary>Requests the host to present (open/focus) an instance's panel.</summary>
    public void RequestPanelOpen(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return;
        Raise(new PanelOpenRequestedEvent(NewId(), instance.ToolkitId, instanceId, Now()));
    }

    /// <summary>
    /// Projects instance-scoped panel DataStore writes into Bench events:
    /// <c>{toolkitId}/{instanceId}/panel/{controlId}/{prop}</c> → control-state changes,
    /// dialog request slots → <see cref="DialogRequestedEvent"/>. Non-panel keys are
    /// deliberately not projected (the DataStore viewer is deferred to the Debug system).
    /// </summary>
    private void OnDataStoreChanged(object? sender, DataStoreChangedEventArgs e)
    {
        if (!TryParsePanelKey(e.Key, out var toolkitId, out var instanceId, out var controlId, out var prop))
            return;

        if (string.Equals(prop, "request", StringComparison.Ordinal))
        {
            if (!e.Removed && e.NewValue is { } request)
                RaiseDialogRequested(toolkitId, instanceId, controlId, request);
            return;
        }

        var value = e.NewValue;
        if (string.Equals(prop, "log", StringComparison.Ordinal) && value is { ValueKind: JsonValueKind.Array } array)
        {
            // UiLog appends to a ring-buffer key; the Changed event carries the whole
            // array. Surface the newest entry so the panel appends exactly one line.
            var entries = array.EnumerateArray().ToList();
            value = entries.Count > 0 ? entries[^1] : (JsonElement?)null;
        }

        Raise(new UiControlStateChangedEvent(NewId(), toolkitId, instanceId, Now(), controlId, prop, value));
    }

    private void RaiseDialogRequested(string toolkitId, string instanceId, string controlId, JsonElement request)
    {
        var message = string.Empty;
        var buttons = new List<string> { "确定" };

        try
        {
            if (request.ValueKind == JsonValueKind.Object)
            {
                if (request.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? string.Empty;
                if (request.TryGetProperty("buttons", out var b) && b.ValueKind == JsonValueKind.Array)
                {
                    var parsed = b.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    if (parsed.Count > 0)
                        buttons = parsed;
                }
            }
            else
            {
                message = request.GetRawText();
            }
        }
        catch (JsonException)
        {
            message = request.GetRawText();
        }

        Raise(new DialogRequestedEvent(NewId(), toolkitId, instanceId, Now(), controlId, message, buttons));
    }

    /// <summary>
    /// Parses <c>{toolkitId}/{instanceId}/panel/{controlId}/{prop}</c>. Returns false for
    /// any other key shape (global DataStore keys are not instance panel projections).
    /// </summary>
    private static bool TryParsePanelKey(
        string key, out string toolkitId, out string instanceId, out string controlId, out string prop)
    {
        toolkitId = string.Empty;
        instanceId = string.Empty;
        controlId = string.Empty;
        prop = string.Empty;

        var marker = key.IndexOf("/panel/", StringComparison.Ordinal);
        if (marker < 0)
            return false;

        var prefix = key[..marker].Split('/');
        if (prefix.Length < 2 || string.IsNullOrWhiteSpace(prefix[0]) || string.IsNullOrWhiteSpace(prefix[1]))
            return false;
        toolkitId = prefix[0];
        instanceId = prefix[1];

        var suffix = key[(marker + "/panel/".Length)..].Split('/');
        if (suffix.Length < 2 || string.IsNullOrWhiteSpace(suffix[0]) || string.IsNullOrWhiteSpace(suffix[1]))
            return false;
        controlId = suffix[0];
        prop = string.Join('/', suffix.Skip(1));
        return true;
    }

    private static bool Matches(TriggerConfig? config, string controlId, string eventName)
        => config is not null
           && string.Equals(config.Control, controlId, StringComparison.Ordinal)
           && string.Equals(config.Event, eventName, StringComparison.OrdinalIgnoreCase);

    private void Raise(BenchEvent e) => BenchEvent?.Invoke(this, e);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static DateTimeOffset Now() => DateTimeOffset.UtcNow;

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
        _dataStore.Changed -= OnDataStoreChanged;
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
