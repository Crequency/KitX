using System.Collections.Concurrent;
using System.Text.Json;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Run;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Panels;
using KitX.ToolKit.Time;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.Validation;
using Serilog;

namespace KitX.ToolKit.Instances;

/// <summary>
/// The instance-model orchestration entry point (ToolKit 实例模型定稿). Replaces the old
/// single-active trigger-manager semantics (the retired <c>BenchTriggerManager</c>) with a
/// multi-instance model — each fired Spawn trigger creates an independent instance:
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
    private readonly ToolkitInstanceManagerOptions _options;

    private readonly ConcurrentDictionary<string, MountedToolkit> _mounted = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ToolkitInstance> _instances = new(StringComparer.Ordinal);

    // Serializes the "MaxInstances check → StartRun → _instances registration" sequence
    // so concurrent Spawn calls cannot all observe zero Running instances and jointly
    // exceed the cap (a TOCTOU window in the pre-existing code).
    private readonly object _spawnGate = new();

    /// <summary>The injected clock (ADR-0006 D1) — no wall-clock reads anywhere in this file.</summary>
    private readonly IClock _clock;

    /// <summary>The injected id source (ADR-0006 D1) — no <c>Guid.NewGuid</c> at call sites.</summary>
    private readonly IIdGenerator _ids;

    private bool _disposed;

    internal ToolkitInstanceManager(
        IServiceProvider services,
        TriggerSourceRegistry registry,
        IWorkflowExecutor executor,
        DataStore dataStore,
        Func<Toolkit, ToolkitFileStore>? fileStoreFactory = null,
        ConfigValidator? validator = null,
        ToolkitInstanceManagerOptions? options = null,
        IClock? clock = null,
        IIdGenerator? idGenerator = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _fileStoreFactory = fileStoreFactory ?? (_ => new ToolkitFileStore(
            Path.Combine(AppContext.BaseDirectory, "Data", "Toolkits")));
        _validator = validator ?? new ConfigValidator();
        _options = options ?? new ToolkitInstanceManagerOptions();
        _clock = clock ?? SystemClock.Instance;
        _ids = idGenerator ?? GuidIdGenerator.Instance;

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
            mounted.TriggerById.TryGetValue(instance.TriggerId, out var trigger))
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
    /// <remarks>
    /// Unmount is <b>not</b> uninstall: it forgets the toolkit's in-memory scheduler but leaves the
    /// data alone, because the caller may mount it again. Clearing data is
    /// <see cref="Uninstall"/>'s job and only <see cref="Uninstall"/>'s job (ADR-0005 D2).
    /// </remarks>
    public void Unmount(string toolkitId)
    {
        if (!_mounted.TryRemove(toolkitId, out var mounted))
            return;

        EndInstancesOf(toolkitId, InstanceEndReason.Unmount);
        mounted.Dispose();
        Log.Information("[ToolkitInstanceManager] Unmounted ToolKit {Toolkit}", toolkitId);
    }

    /// <summary>
    /// Stops a mounted ToolKit without forgetting it: spawn sources stop, running flows are
    /// cancelled, <b>declared keys survive</b> (ADR-0005 D2).
    /// </summary>
    /// <remarks>
    /// <para>The toolkit stays in <c>_mounted</c> so its key table still resolves, its panel keys
    /// stay readable and its instances remain enumerable — a disabled toolkit is still
    /// <i>addressable</i>, it just cannot be triggered. Anything else would make "disable" and
    /// "uninstall" the same operation with different log lines.</para>
    /// <para>Re-enabling is <see cref="Enable"/>. Calling this twice is a no-op.</para>
    /// </remarks>
    public void Disable(string toolkitId)
    {
        ThrowIfDisposed();
        if (!_mounted.TryGetValue(toolkitId, out var mounted))
            return;
        if (mounted.State == LifecycleState.Disabled)
            return;

        mounted.StopSources();
        EndInstancesOf(toolkitId, InstanceEndReason.Disabled);
        mounted.State = LifecycleState.Disabled;
        Log.Information("[ToolkitInstanceManager] Disabled ToolKit {Toolkit}; data retained", toolkitId);
    }

    /// <summary>Re-activates a disabled ToolKit: spawn sources start again and triggering resumes.</summary>
    /// <remarks>Idempotent. Re-enabling a toolkit that was never disabled is a no-op. This does not
    /// restart the flows that were cancelled by <see cref="Disable"/> — they are gone, and pretending
    /// otherwise would mean the toolkit holds state it cannot describe.</remarks>
    public void Enable(string toolkitId)
    {
        ThrowIfDisposed();
        if (!_mounted.TryGetValue(toolkitId, out var mounted))
            return;
        if (mounted.State != LifecycleState.Disabled)
            return;

        mounted.State = LifecycleState.Enabled;
        StartSpawnSources(mounted);
        Log.Information("[ToolkitInstanceManager] Enabled ToolKit {Toolkit}", toolkitId);
    }

    /// <summary>
    /// Removes a ToolKit entirely: stops it, ends its instances and <b>deletes every key it
    /// declared</b> (ADR-0005 D2). Idempotent.
    /// </summary>
    /// <remarks>
    /// <para>This is the operation that actually erases data, which is why it is not just
    /// "unmount with a flag". The key set is the toolkit's own declaration — the manager is the
    /// only object that both knows which toolkit is going away and owns the store (Q66).</para>
    /// <para>Deletion is scoped to full keys, not to a raw prefix: a toolkit whose id is a prefix of
    /// another's would otherwise take its neighbour's data with it.</para>
    /// </remarks>
    public int Uninstall(string toolkitId)
    {
        ThrowIfDisposed();

        var hadMounted = _mounted.TryRemove(toolkitId, out var mounted);

        EndInstancesOf(toolkitId, InstanceEndReason.Uninstall);
        mounted?.Dispose();

        // Every declared key plus the whole panel namespace. Declared keys cover the `Out`/`In`/
        // `Stream` roles; the panel prefix covers what the old panel host wrote there, which is not
        // in the declaration but is still this toolkit's data.
        var removed = 0;
        var keys = mounted?.Keys.Names.ToList() ?? [];
        foreach (var local in keys)
            removed += _dataStore.Remove($"{toolkitId}/{local}") ? 1 : 0;

        removed += _dataStore.RemoveByPrefix($"{toolkitId}/");

        Log.Information(
            "[ToolkitInstanceManager] Uninstalled ToolKit {Toolkit} (was mounted: {Mounted}); removed {Count} keys",
            toolkitId, hadMounted, removed);
        return removed;
    }

    /// <summary>The lifecycle state of a toolkit, or null when it is not mounted at all.</summary>
    public LifecycleState? GetLifecycleState(string toolkitId)
        => _mounted.TryGetValue(toolkitId, out var mounted) ? mounted.State : null;

    /// <summary>Ends every instance belonging to one toolkit, for the given reason.</summary>
    private void EndInstancesOf(string toolkitId, InstanceEndReason reason)
    {
        foreach (var kv in _instances.Where(kv => kv.Value.ToolkitId == toolkitId).ToList())
            EndInstance(kv.Key, reason);
    }

    /// <summary>Creates and starts one Spawn source per Spawn trigger on the mounted toolkit.</summary>
    private void StartSpawnSources(MountedToolkit mounted)
    {
        var id = mounted.Toolkit.GetId();

        // Start only Spawn sources. UIEvent/WorkflowCompletion are intra-instance: UIEvent is
        // wired per-instance in the GUI iteration; WorkflowCompletion is a scheduler edge.
        foreach (var trigger in mounted.Toolkit.Triggers)
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

        // A disabled toolkit is still mounted and still readable — only triggering is refused
        // (ADR-0005 D2, error code `toolkit.not-enabled`). Refusing here rather than at each
        // trigger source means one check covers every path into a flow, including a stale source
        // that somehow survived a Disable.
        if (mounted.State == LifecycleState.Disabled)
        {
            Log.Debug("[ToolkitInstanceManager] Spawn of {Toolkit} refused: toolkit.not-enabled", toolkitId);
            Raise(new InstanceSpawnRejectedEvent(NewId(), toolkitId, string.Empty, Now(), triggerId,
                "toolkit.not-enabled"));
            return null;
        }

        var trigger = mounted.TriggerById.TryGetValue(triggerId, out var t) ? t : null;
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
                _instances.Values.Count(i => i.ToolkitId == toolkitId && i.IsRunning) >= max)
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
                // C6: Succeeded aggregates across ALL of the instance's runs (Spawn + UIEvent).
                Raise(new InstanceCompletedEvent(
                    NewId(), toolkitId, instance.InstanceId, Now(), instance.Succeeded));
                EvictIfOverCompletedCap();
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
    private void AttachRunEvents(WorkflowRun run)
    {
        run.NodeStarted += (_, e) => Raise(new RunStartedEvent(
            NewId(), run.ToolkitId, run.NamespaceId, Now(), e.InstanceId, e.WorkflowId));
        run.NodeCompleted += (_, e) => Raise(new RunCompletedEvent(
            NewId(), run.ToolkitId, run.NamespaceId, Now(), e.InstanceId, e.WorkflowId, e.Succeeded, e.Error));
    }

    /// <summary>Ends an instance (cancels all its runs + destroys it). Idempotent.</summary>
    /// <remarks>Public-face overload: the caller is <see cref="BenchService"/>, i.e. someone asked
    /// for this specific instance, so the reason is <see cref="InstanceEndReason.Requested"/>.</remarks>
    public void EndInstance(string instanceId) => EndInstance(instanceId, InstanceEndReason.Requested);

    /// <summary>Ends an instance (cancels all its runs + destroys it). Idempotent.</summary>
    /// <param name="instanceId">The instance to end. Unknown ids are a no-op.</param>
    /// <param name="reason">
    /// Why it is being ended. Never inferred: each caller knows its own motivation, and a
    /// shutdown that looks like a failure (or the reverse) is worse than no log line.
    /// </param>
    internal void EndInstance(string instanceId, InstanceEndReason reason)
    {
        if (!_instances.TryRemove(instanceId, out var instance))
            return;
        instance.Cancel();
        instance.NotifyCancelled();
        instance.Dispose();

        // The instance's data namespace is {toolkitId}/{instanceId}/wf/*; every run (Spawn +
        // UIEvent chains) writes produced values there and, without this, ended/completed
        // instances would accumulate DataStore keys forever. Panel keys ({toolkitId}/{instanceId}/
        // panel/*) are deliberately retained so the panel data stays viewable after the end.
        var wfPrefix = $"{instance.ToolkitId}/{instance.InstanceId}/wf/";
        var cleared = _dataStore.RemoveByPrefix(wfPrefix);
        if (cleared > 0)
            Log.Debug("[ToolkitInstanceManager] Cleared {Count} wf keys for ended instance {Instance}", cleared, instanceId);

        Log.Information("[ToolkitInstanceManager] Ended instance {Instance} ({Reason})", instanceId, reason);
    }

    /// <summary>
    /// Enforces <see cref="ToolkitInstanceManagerOptions.CompletedInstanceCap"/>: when the
    /// number of Completed instances exceeds the cap, the oldest (by <c>CompletedAt</c>) are
    /// ended until the count is back at the cap. Runs on the completion thread of the
    /// instance that just transitioned; a cap &lt;= 0 means unlimited (no eviction).
    /// </summary>
    private void EvictIfOverCompletedCap()
    {
        var cap = _options.CompletedInstanceCap;
        if (cap <= 0)
            return;

        // Snapshot the completed instances oldest-first. O(n) per completion event, which is
        // acceptable given completion frequency is low; an incremental counter was considered
        // but rejected to keep the logic simple and the snapshot self-correcting.
        var completed = _instances.Values
            .Where(i => !i.IsRunning)
            .OrderBy(i => i.CompletedAt)
            .ToList();

        if (completed.Count <= cap)
            return;

        foreach (var instance in completed.Take(completed.Count - cap))
        {
            // A UIEvent chain may have re-activated a Completed instance since the snapshot;
            // skip it so a Running instance is never evicted. Under-eviction self-heals the
            // next time it completes (firing this method again).
            if (instance.IsRunning)
                continue;

            Log.Information("[ToolkitInstanceManager] Instance {Instance} evicted by CompletedInstanceCap (cap={Cap})",
                instance.InstanceId, cap);
            EndInstance(instance.InstanceId, InstanceEndReason.Evicted);
        }
    }

    /// <summary>Ends every instance of every mounted ToolKit.</summary>
    /// <remarks>
    /// <para>Termination order is <b>newest first</b> (ADR-0005 D3): a flow started later is more
    /// likely to be reading keys left behind by an earlier one, so tearing down the dependants
    /// before their providers keeps the last observable states meaningful. The order is decided
    /// here, on a materialised snapshot, rather than by enumerating the dictionary — a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> makes no ordering promise at all.</para>
    /// <para>Every termination carries a reason, so a reader of the log can tell an orderly
    /// shutdown from a failure or an operator's decision.</para>
    /// </remarks>
    public void EndAll()
    {
        var ordered = _instances
            .OrderByDescending(kv => kv.Value.StartedAt)
            .ThenBy(kv => kv.Value.InstanceId, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in ordered)
            EndInstance(id, InstanceEndReason.Shutdown);
    }

    /// <summary>The toolkit id an instance belongs to, or null when unknown.</summary>
    public string? GetToolkitId(string instanceId)
        => _instances.TryGetValue(instanceId, out var i) ? i.ToolkitId : null;

    /// <summary>
    /// The declared key table of the toolkit an instance belongs to, or null when the instance is
    /// unknown. This is what the <c>Toolkit*</c> builtins resolve local key names against.
    /// </summary>
    internal KeyTable? GetKeyTable(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return null;
        return _mounted.TryGetValue(instance.ToolkitId, out var mounted) ? mounted.Keys : null;
    }

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
        if (string.Equals(eventName, PanelScope.EventConfirm, StringComparison.OrdinalIgnoreCase))
            _dataStore.Remove(PanelScope.Key(instance.ToolkitId, instanceId, controlId, PanelScope.PropRequest));

        var payload = JsonSerializer.SerializeToElement(new { controlId, @event = eventName, value });
        foreach (var trigger in mounted.Toolkit.Triggers.Where(t =>
                     t.Type == TriggerType.UIEvent && Matches(t.Config, controlId, eventName)))
        {
            // C6: a UIEvent chain on a Completed instance re-transitions it back to Running
            // (EnsureRunning before StartRun so the RunStarted projection already sees Running),
            // and the new run is tracked so the instance only returns to Completed once it too
            // has finished. StartRun cannot return null here — the trigger is a UIEvent type.
            instance.EnsureRunning();
            var run = mounted.Scheduler.StartRun(trigger.Id, payload, instance.Initiator, instance.InstanceId, AttachRunEvents);
            if (run is not null)
                instance.TrackRun(run);
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

        if (string.Equals(prop, PanelScope.PropRequest, StringComparison.Ordinal))
        {
            if (!e.Removed && e.NewValue is { } request)
                RaiseDialogRequested(toolkitId, instanceId, controlId, request);
            return;
        }

        var value = e.NewValue;
        if (string.Equals(prop, PanelScope.PropLog, StringComparison.Ordinal) && !e.Appended && value is { ValueKind: JsonValueKind.Array } array)
        {
            // UiLog appends to a ring-buffer key. An incremental Append already carries the
            // single new entry (e.Appended), so only a full-array Set needs the newest-entry
            // extraction here — surface the last line so the panel appends exactly one.
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

        var marker = key.IndexOf(PanelScope.PanelKeySegment, StringComparison.Ordinal);
        if (marker < 0)
            return false;

        var prefix = key[..marker].Split('/');
        if (prefix.Length < 2 || string.IsNullOrWhiteSpace(prefix[0]) || string.IsNullOrWhiteSpace(prefix[1]))
            return false;
        toolkitId = prefix[0];
        instanceId = prefix[1];

        var suffix = key[(marker + PanelScope.PanelKeySegment.Length)..].Split('/');
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

    private string NewId() => _ids.NewFlowId();

    private DateTimeOffset Now() => _clock.UtcNow;

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
            Keys = KeyTable.From(toolkit.GetId(), toolkit.Keys);

            // F7: index triggers by id so per-instance surface resolution and Spawn lookup are
            // O(1) instead of an O(T) linear scan per snapshot. Config validation guarantees
            // trigger Ids are unique; a defensive first-wins fallback (with a warning) keeps
            // the dictionary well-defined if a duplicate ever slips through validation.
            TriggerById = new Dictionary<string, Trigger>(StringComparer.Ordinal);
            foreach (var trigger in toolkit.Triggers)
            {
                if (!TriggerById.TryAdd(trigger.Id, trigger))
                    Log.Warning("[ToolkitInstanceManager] Duplicate trigger Id '{Id}' in {Toolkit}; using the first",
                        trigger.Id, toolkit.GetId());
            }
        }

        public Toolkit Toolkit { get; }
        public BenchScheduler Scheduler { get; }

        /// <summary>
        /// Whether this toolkit's triggers are live. Starts <see cref="LifecycleState.Enabled"/>
        /// because mounting is what enables it today; <see cref="ToolkitInstanceManager.Disable"/>
        /// flips it without forgetting the toolkit, which is what keeps data retention possible.
        /// </summary>
        public LifecycleState State { get; set; } = LifecycleState.Enabled;

        /// <summary>Stops every Spawn source and forgets them, so they cannot fire again.</summary>
        /// <remarks>
        /// Disposing the sources rather than merely stopping them matters: a source that is stopped
        /// but still subscribed to an external event can be resurrected by the next
        /// <see cref="ToolkitInstanceManager.Enable"/>, and then the same trigger fires twice.
        /// </remarks>
        public void StopSources()
        {
            foreach (var source in Sources)
                source.Stop();
            Sources.Clear();
        }

        /// <summary>
        /// The toolkit's declared key table, resolved against its id (ADR-0004 D2/D3). Workflows
        /// write local names; this is what turns them into full keys, and what rejects a write to a
        /// key the user owns.
        /// </summary>
        public KeyTable Keys { get; }

        public Dictionary<string, Trigger> TriggerById { get; }
        public List<ITriggerSource> Sources { get; } = [];

        public void Dispose()
        {
            StopSources();
            Scheduler.Dispose();
        }
    }
}
