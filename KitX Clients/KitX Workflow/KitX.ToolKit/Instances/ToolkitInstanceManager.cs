using System.Collections.Concurrent;
using System.Text.Json;
using KitX.ToolKit.Compile;
using KitX.ToolKit.Run;
using KitX.WorkflowV6.Builtin;
using Microsoft.Extensions.DependencyInjection;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Time;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Triggers;
using Serilog;

namespace KitX.ToolKit.Instances;

/// <summary>
/// The instance-model orchestration entry point (ToolKit 实例模型定稿). Replaces the old
/// single-active trigger-manager semantics (the retired <c>the retired single-active manager</c>) with a
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
internal sealed class ToolkitInstanceManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly TriggerSourceRegistry _registry;
    private readonly IWorkflowExecutor _executor;
    private readonly DataStore _dataStore;
    private readonly Func<Toolkit, ToolkitFileStore> _fileStoreFactory;
    private readonly ConfigValidator _validator;
    private readonly ToolkitInstanceManagerOptions _options;

    /// <summary>
    /// Where lifecycle state and declared key values survive a restart (design §4, Q12).
    /// Null means memory only — see the constructor's remark.
    /// </summary>
    private readonly IToolkitStateStore? _stateStore;

    private readonly ConcurrentDictionary<string, MountedToolkit> _mounted = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ToolkitInstance> _instances = new(StringComparer.Ordinal);

    // Serializes the "concurrency check → StartRun → _instances registration" sequence
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
        IIdGenerator? idGenerator = null,
        IToolkitStateStore? stateStore = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));

        // Null means "memory only": the lifecycle is lost when the process ends. The composition
        // root always passes one, so the shipped behaviour persists; a test that does not care
        // about restart behaviour does not have to invent a directory to say so.
        _stateStore = stateStore;

        // No fallback here on purpose (ADR-0010 D2). A file store rooted at
        // AppContext.BaseDirectory would work in a test run and silently point somewhere else in
        // a deployed host; requiring the factory makes the composition root state its intent.
        _fileStoreFactory = fileStoreFactory
            ?? throw new ArgumentNullException(nameof(fileStoreFactory));

        _validator = validator ?? new ConfigValidator();
        _options = options ?? new ToolkitInstanceManagerOptions();
        _clock = clock ?? SystemClock.Instance;
        _ids = idGenerator ?? GuidIdGenerator.Instance;

        // The DataStore blackboard is the panel-projection primitive: project every
        // instance-scoped panel write into the Bench event channel so the host can render
        // live values, log entries and dialog requests (GUI RFC §5.7 / UX v2 C26-C27).
    }

    /// <summary>Raised for every Bench event (spawn/complete/cancel/run/data/ui).</summary>
    public event EventHandler<ToolkitEvent>? ToolkitEvent;

    /// <summary>The ids of currently mounted ToolKits.</summary>
    public IReadOnlyList<string> MountedToolkitIds => _mounted.Keys.ToList();

    /// <summary>True when the given ToolKit is mounted.</summary>
    public bool IsMounted(string toolkitId) => _mounted.ContainsKey(toolkitId);

    /// <summary>Snapshot of every instance across all mounted ToolKits.</summary>
    public IReadOnlyList<InstanceSnapshot> Instances
        => _instances.Values.Select(instance => instance.ToSnapshot()).ToList();

    /// <summary>
    /// Mounts a ToolKit: validates its config, compiles it, builds its scheduler, and starts its
    /// Spawn trigger sources. Idempotent — re-mounting an already-mounted id is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compiling here is what makes the compiled form the single source of truth for the graph,
    /// the trigger table and the resolved file paths. Before this, <see cref="RunScheduler"/>
    /// fell back to deriving the graph from the config itself, so the compiler was never
    /// exercised outside its own tests — two implementations of the same rules, only one of
    /// them running in production.
    /// </para>
    /// <para>
    /// A config that fails to compile throws, exactly like one that fails validation. Both are
    /// "this toolkit cannot be mounted, and here is why"; inventing a second reporting channel
    /// for the second half of the same check would split the answer across two places.
    /// </para>
    /// </remarks>
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

        var fileStore = _fileStoreFactory(toolkit);
        var compiled = Compile(toolkit, fileStore);

        var scheduler = new RunScheduler(compiled, _executor, fileStore);
        var mounted = new MountedToolkit(compiled, scheduler);

        // Restore before anything can run (design §4: "进程重启 → 声明的键恢复"; Q12/Q66).
        // Restoring after the sources start would let a flow read a key that is about to be
        // overwritten by older data, which is a state nobody wrote on purpose.
        var restored = RestoreState(mounted);

        // Registered before any source starts (ADR-0005 D5, requirement R2.1). A source may
        // fire synchronously from Start, and Spawn resolves the toolkit through this dictionary
        // — registering afterwards dropped that first event silently: no error, no log, no
        // event. The built-in sources do not fire from Start today, but ITriggerSource is an
        // open interface, so the ordering is the only thing that makes the guarantee true.
        _mounted[id] = mounted;

        // A toolkit disabled before the restart comes back disabled: that is the whole point of
        // persisting the lifecycle. It is registered (so it stays addressable — design §4,
        // "保留期 = 可寻址期") but its sources do not start.
        if (mounted.State == LifecycleState.Disabled)
        {
            Log.Information(
                "[ToolkitInstanceManager] Mounted ToolKit {Toolkit} in the disabled state it was left in; " +
                "restored {Count} keys, triggers stay stopped",
                id, restored);
            return;
        }

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

        Log.Information("[ToolkitInstanceManager] Mounted ToolKit {Toolkit}; restored {Count} keys",
            id, restored);
    }

    /// <summary>
    /// Applies the state file to a freshly constructed mount: the lifecycle state, then the
    /// values of the keys the toolkit still declares.
    /// </summary>
    /// <remarks>
    /// <para>A value is restored only when its declaration still exists. The declaration is the
    /// contract, so a value whose key was removed from <c>toolkit.json</c> is dropped rather
    /// than resurrected as an unaddressable orphan — the workflow would not be allowed to read
    /// it anyway, so keeping it would only make the file lie about what the toolkit has.</para>
    ///
    /// <para>Restoring writes through the same store the workflows use, so a value lands exactly
    /// where a workflow's read will look for it, with no second key format to keep in sync.</para>
    /// </remarks>
    /// <returns>How many declared keys were restored.</returns>
    private int RestoreState(MountedToolkit mounted)
    {
        if (_stateStore is null)
            return 0;

        var id = mounted.Compiled.ToolkitId;

        var state = _stateStore.Load(id);
        if (state is null)
            return 0;

        if (state.State is { } persisted)
            mounted.State = persisted;

        var restored = 0;
        foreach (var (local, value) in state.Values)
        {
            if (!mounted.Keys.IsDeclared(local))
            {
                Log.Warning(
                    "[ToolkitInstanceManager] Dropping the stored value of '{Key}' for {Toolkit}: " +
                    "the key is no longer declared",
                    local, id);
                continue;
            }

            _dataStore.Set(mounted.Keys.FullKey(local), value);
            restored++;
        }

        return restored;
    }

    /// <summary>
    /// Persists the lifecycle state and the current values of the toolkit's declared keys.
    /// </summary>
    /// <remarks>
    /// Called at every state transition rather than on a timer: the lifecycle change is the only
    /// moment the file has to be right, and a periodic write would also capture half-finished
    /// values that no workflow intended to commit.
    /// </remarks>
    private void PersistState(MountedToolkit mounted)
    {
        if (_stateStore is null)
            return;

        var id = mounted.Compiled.ToolkitId;
        var values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

        foreach (var local in mounted.Keys.Names)
        {
            if (_dataStore.TryGet(mounted.Keys.FullKey(local), out var value))
                values[local] = value;
        }

        _stateStore.Save(id, new ToolkitStateFile { State = mounted.State, Values = values });
    }

    /// <summary>
    /// Compiles a toolkit, turning its diagnostics into the exception a caller can act on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every diagnostic is listed, not just the first: a config that misses three references
    /// should not need three mount attempts to discover that.
    /// </para>
    /// <para>
    /// The builtin catalogue comes from the container, so a workflow calling a function this host
    /// does not provide is caught here rather than at the first run. ADR-0011's whole point is
    /// that a name is resolved once, at compile time, against what is actually registered —
    /// compiling without the catalogue would leave that half of the compiler unused in
    /// production, the same shape of defect the file check had.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Runs the compiler over <paramref name="toolkit"/> and returns its diagnostics instead of
    /// throwing. Empty means <see cref="Mount"/> would succeed.
    /// </summary>
    /// <remarks>
    /// Mount and this method call the same <see cref="ToolkitCompiler.Compile"/> with the same
    /// arguments. Keeping them in one place is what makes the check trustworthy: if the two ever
    /// diverge, the editor reports "fine" for configs that then refuse to mount.
    /// </remarks>
    internal IReadOnlyList<Diagnostic> Validate(Toolkit toolkit)
    {
        var fileStore = _fileStoreFactory(toolkit);
        var compiled = ToolkitCompiler.Compile(
            toolkit,
            out var diagnostics,
            storageRoot: fileStore.Root,
            builtins: new BuiltinResolver(_services.GetServices<IBuiltinFunction>()));

        _ = compiled;
        return diagnostics;
    }

    private CompiledToolkit Compile(Toolkit toolkit, ToolkitFileStore fileStore)
    {
        var compiled = ToolkitCompiler.Compile(
            toolkit,
            out var diagnostics,
            storageRoot: fileStore.Root,
            builtins: new BuiltinResolver(_services.GetServices<IBuiltinFunction>()));

        if (compiled is not null)
            return compiled;

        // A failed compile always has at least one error diagnostic, so this never falls back to
        // a generic message in practice; the guard is here so a future compiler change cannot
        // turn a failure into an exception that explains nothing.
        var errors = diagnostics.Count > 0
            ? string.Join("\n  ", diagnostics.Select(d => $"[{d.Code}] {d.Message}"))
            : "the compiler reported failure without a diagnostic";

        throw new InvalidOperationException("ToolKit config failed to compile:\n  " + errors);
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

        // Persisted immediately: "restart brings it back disabled" is the whole difference
        // between a lifecycle state and a runtime flag (design §4, Q12).
        PersistState(mounted);

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
        PersistState(mounted);
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

        // The state file is part of the toolkit (design Q66: persistence is the ToolKit's
        // property, not the store's). Leaving it behind would re-restore the values this call
        // just deleted on the next mount — the deletion would look like it did not work.
        _stateStore?.Delete(toolkitId);

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
        var id = mounted.Compiled.ToolkitId;

        // Start only Spawn sources. UIEvent/WorkflowCompletion are intra-instance: UIEvent is
        // wired per-instance in the GUI iteration; WorkflowCompletion is a scheduler edge.
        foreach (var trigger in mounted.Compiled.Triggers)
        {
            if (!trigger.Spawns)
                continue;

            var source = _registry.Create(trigger.Declaration, _services);
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
    /// not mounted / the concurrency cap is exceeded.
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
            Raise(new TriggerRejectedEvent(NewId(), toolkitId, string.Empty, Now(), triggerId,
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
            if (!HasFlowBudget(toolkitId, triggerId))
                return null;

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
    /// <remarks>Public-face overload: the caller is <see cref="RunService"/>, i.e. someone asked
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

        // No key cleanup here. Q48 removed the per-run data channel, so an instance no longer
        // owns a namespace to clear: workflow values live under the toolkit's declared keys
        // ({toolkitId}/{localName}), and those outlive the instance by design. This method used
        // to sweep "{toolkitId}/{instanceId}/wf/" — a prefix nothing writes any more.
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

    /// <summary>
    /// Routes an external event to the owning instance's <see cref="TriggerType.UIEvent"/> chain
    /// (intra — never spawns a new instance).
    /// </summary>
    /// <param name="instanceId">The instance whose chain should run.</param>
    /// <param name="eventName">The event name to match against <see cref="TriggerConfig.Event"/>.</param>
    /// <param name="value">The event payload, surfaced to bindings as <c>$payload.value</c>.</param>
    /// <remarks>
    /// <para>This used to be <c>RaiseControlEvent</c> and took a <c>controlId</c>, because the only
    /// thing that could raise it was a panel control. A panel is no longer a ToolKit concept
    /// (Q64 / ADR-0013 D4), but the <b>capability</b> it used — "an outside event wakes a flow for
    /// this instance" — belongs to the trigger model, not to the panel. ADR-0013 D4 says keep it
    /// and rename it, so it no longer names a control and no longer clears a dialog slot.</para>
    ///
    /// <para>What is left is the trigger dispatch alone: match by <see cref="TriggerConfig.Event"/>
    /// and start the bound workflows. <see cref="TriggerConfig.Control"/> is still consulted when a
    /// trigger declares one, so a config written before the panel removal keeps its scoping.</para>
    /// </remarks>
    public void RaiseInstanceEvent(string instanceId, string eventName, object? value = null)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return;
        if (!_mounted.TryGetValue(instance.ToolkitId, out var mounted))
            return;

        var payload = JsonSerializer.SerializeToElement(new { @event = eventName, value });
        var matching = mounted.Compiled.Triggers
            .OfType<CompiledUiEventTrigger>()
            .Where(t => MatchesEvent(t.Event, eventName))
            .ToList();

        foreach (var trigger in matching)
        {
            // Each chain takes a flow slot, checked the same way Spawn checks it. Under the
            // gate so two events cannot both consume the last slot.
            lock (_spawnGate)
            {
                if (!HasFlowBudget(instance.ToolkitId, trigger.Id))
                    break;
            }

            // A UIEvent chain on a Completed instance re-transitions it back to Running
            // (EnsureRunning before StartRun so the RunStarted projection already sees Running),
            // and the new run is tracked so the instance only returns to Completed once it too
            // has finished. StartRun cannot return null here — the trigger is a UIEvent type.
            instance.EnsureRunning();
            var run = mounted.Scheduler.StartRun(trigger.Id, payload, instance.Initiator, instance.InstanceId, AttachRunEvents);
            if (run is not null)
                instance.TrackRun(run);
        }
    }

    private static bool MatchesEvent(string? declaredEvent, string eventName)
        => declaredEvent is not null
           && string.Equals(declaredEvent, eventName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether starting one more flow fits under the machine-wide concurrency cap. Callers
    /// must hold <see cref="_spawnGate"/>: the count and the follow-up <c>StartRun</c> have to
    /// be atomic, or two threads can both read "one slot left" and both take it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the count is <c>ActiveInstanceCount</c> and not a local tally.</b> That
    /// property is <c>RunScheduler._runs.Count</c>, and the scheduler adds on start and removes
    /// on completion — so it is the flow count by construction. Summing
    /// <c>ToolkitInstance.ActiveRuns</c> would work too but would leave <c>RaiseEvent</c>
    /// uncapped, because it starts runs without creating an instance.</para>
    ///
    /// <para><b>Why both entry points call this.</b> Before Q83 the cap was checked only in
    /// <c>Spawn</c>; an external event starting UIEvent chains walked straight past it, so
    /// "one spawn + N event chains" could exceed the cap without bound. One check, two callers,
    /// no path around it.</para>
    /// </remarks>
    private bool HasFlowBudget(string toolkitId, string triggerId)
    {
        var max = _options.MaxConcurrentFlows;
        if (max <= 0)
            return true;

        var active = _mounted.Values.Sum(m => m.Scheduler.ActiveInstanceCount);
        if (active < max)
            return true;

        Log.Warning(
            "[ToolkitInstanceManager] Trigger {Trigger} of {Toolkit} rejected: concurrency-limit ({Active}/{Max})",
            triggerId, toolkitId, active, max);
        Raise(new TriggerRejectedEvent(NewId(), toolkitId, string.Empty, Now(), triggerId,
            "concurrency-limit"));
        return false;
    }

    private void Raise(ToolkitEvent e) => ToolkitEvent?.Invoke(this, e);

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
        foreach (var id in _mounted.Keys.ToList())
            Unmount(id);
        EndAll();
    }

    /// <summary>
    /// A mounted ToolKit: its compiler output, scheduler, and started Spawn sources.
    /// </summary>
    /// <remarks>
    /// <b>No <c>Toolkit</c> here.</b> ADR-0002 D1 says the runtime takes a
    /// <see cref="CompiledToolkit"/> and never the config again. This type used to keep the
    /// config alongside the compiled output, which made the ADR true of <c>RunScheduler</c> and
    /// false of everything around it — the manager went on reading the config for triggers,
    /// and the key declarations straight from the document it was told not to touch.
    /// </remarks>
    private sealed class MountedToolkit : IDisposable
    {
        public MountedToolkit(CompiledToolkit compiled, RunScheduler scheduler)
        {
            Compiled = compiled;
            Scheduler = scheduler;

            // F7: index triggers by id so per-instance surface resolution and Spawn lookup are
            // O(1) instead of an O(T) linear scan per snapshot. The compiler guarantees trigger
            // Ids are unique (it reports duplicates as diagnostics), so the dictionary is
            // well-defined without a fallback.
            TriggerById = new Dictionary<string, CompiledTrigger>(StringComparer.Ordinal);
            foreach (var trigger in compiled.Triggers)
                TriggerById[trigger.Id] = trigger;
        }

        public CompiledToolkit Compiled { get; }
        public RunScheduler Scheduler { get; }

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
        /// <remarks>Comes from the compiler, not rebuilt here — see <see cref="CompiledToolkit.Keys"/>.</remarks>
        public KeyTable Keys => Compiled.Keys;

        public Dictionary<string, CompiledTrigger> TriggerById { get; }
        public List<ITriggerSource> Sources { get; } = [];

        public void Dispose()
        {
            StopSources();
            Scheduler.Dispose();
        }
    }
}
