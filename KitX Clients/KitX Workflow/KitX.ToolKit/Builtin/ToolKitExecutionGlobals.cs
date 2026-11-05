using System.Text.Json;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Backend.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKitExecutionGlobals — the host-side ExecutionGlobals subclass that carries
// the ToolKit first-class builtins (the Toolkit* data plane + FlowIn/FlowOut).
//
// This is the payload of the external ExecutionGlobals extension seam
// (IExecutionGlobalsFactory, registered by AddKitXWorkflowV6 with TryAdd and
// overridden here by AddKitXToolKit). The generated structured C# declares
// `public sealed class G : ToolKitExecutionGlobals`, so workflow calls to
// ToolkitSet / ToolkitAppend / FlowIn &c. resolve directly to these typed
// methods — the reserved-name plugin bridge (BuiltinDataStorePlugin routed by
// PluginHostAdapter) is retired.
//
// Each method inlines what the retired plugin's Invoke dispatch did, but with
// strong-typed signatures and direct service access (no PluginHost.Call).
//
// FlowIn/FlowOut are instance-scoped: <see cref="InstanceId"/> (from the run
// context) gates FlowOut to a no-op when the workflow runs outside a ToolKit
// instance, matching the historical safe-default convention. The Toolkit data
// plane is not scoped that way — it resolves the owning instance's key table and
// throws when there is none, because a workflow that writes keys without a
// declared table is a configuration error, not a no-op.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The ToolKit ExecutionGlobals subclass that carries the first-class
/// Toolkit* / Bench* builtins, backed directly by the ToolKit services
/// (DataStore, ToolkitInstanceManager). Generated workflow code derives its <c>G</c> from
/// this type, so the methods are reachable without the reserved-name plugin bridge.
/// </summary>
public class ToolKitExecutionGlobals : ExecutionGlobals
{
    private DataStore _dataStore;

    /// <summary>
    /// The flow-scheduling half of the data plane (ADR-0004 D1). Owned here rather than by the
    /// store, because "block this flow until a key appears" is a statement about the flow — the
    /// store has no flows, and keeping the two apart is what let its surface shrink to the five
    /// members the contract allows.
    /// </summary>
    private KeyWaitRegistry _waits;
    private ToolkitInstanceManager _manager;
    private DataStoreOptions _options;

    /// <summary>
    /// Creates a globals instance wired to the ToolKit services. The factory (and the
    /// DI graph) guarantees the services are non-null.
    /// </summary>
    internal ToolKitExecutionGlobals(
        DataStore dataStore,
        ToolkitInstanceManager manager,
        DataStoreOptions? options = null)
    {
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _waits = new KeyWaitRegistry(_dataStore);
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? new DataStoreOptions();
    }

    /// <summary>
    /// Parameterless constructor for the generated G subclass: the backend's factory
    /// <c>Activator.CreateInstance</c>s the generated G (which derives from this type),
    /// so this type must be Activator-constructible. Services are null until
    /// <see cref="Inject"/> is called by the factory.
    /// <para>
    /// <b>Public, not internal.</b> Generated workflow code derives from this type and is
    /// constructed by reflection, so the constructor cannot be narrowed just because its
    /// dependency-taking sibling now is. This is a V6 codegen constraint, not a design statement
    /// about the surface area (ADR-0012's "implementation assembly exposes zero public types" is
    /// reached at S5, together with the rest of the surface, not here).
    /// </para>
    /// </summary>
    public ToolKitExecutionGlobals()
    {
        _dataStore = null!;
        _waits = null!;
        _manager = null!;
        _options = new DataStoreOptions();
    }

    /// <summary>
    /// Wires the ToolKit services into an Activator-created instance. Called by
    /// <see cref="ToolKitExecutionGlobalsFactory.Create"/> after it instantiates the
    /// generated G, so the G's base (this type) has its services before RunAsync runs.
    /// </summary>
    internal void Inject(
        DataStore dataStore,
        ToolkitInstanceManager manager,
        DataStoreOptions? options = null)
    {
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _waits = new KeyWaitRegistry(_dataStore);
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? new DataStoreOptions();
    }

    // ── Instance-scoped run context (from HostRunContext) ──

    /// <summary>
    /// The owning instance's id, read per-run from the backend-injected
    /// <see cref="ExecutionGlobals.RunContext"/> (a <see cref="HostRunContext"/>). Null
    /// when the workflow runs outside a ToolKit instance; only the data-plane
    /// builtins need it, and they throw rather than no-op (see RequireKeyTable).
    public string? InstanceId => (RunContext as HostRunContext)?.InstanceId;

    /// <summary>
    /// The workflow's instance-scoped DataStore output namespace (Bench scheduler
    /// injected), read from <see cref="ExecutionGlobals.RunContext"/>. Null outside a
    /// ToolKit instance; <see cref="FlowOut"/> no-ops then.
    /// </summary>
    public string? OutputNamespace => (RunContext as HostRunContext)?.OutputNamespace;

    /// <summary>
    /// The raw run-time constant overrides (resolved trigger binding params included),
    /// read from <see cref="ExecutionGlobals.RunContext"/>. Null outside a ToolKit
    /// instance; <see cref="FlowIn"/> returns its default then.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? RawOverrides => (RunContext as HostRunContext)?.RawOverrides;

    // ── Toolkit data plane — the workflow's keys, scoped to this toolkit. ──
    //
    // Every method here takes a LOCAL key name ("workdir"), never a full key. The toolkit
    // prefix is applied by KeyTable, which is the only place that knows it — so "read another
    // toolkit's data" has nowhere to put the prefix (ADR-0004 D2). A write to a key declared
    // with role In / InOnSubmit throws rather than silently landing (ADR-0004 D3): the user owns
    // that key, and a workflow writing it would be overwritten by the user's next edit anyway.

    /// <summary>ToolkitSet(key, value) → writes a declared key the workflow owns.</summary>
    public object? ToolkitSet(string key, object? value)
    {
        var table = RequireKeyTable();
        _dataStore.Set(table.ResolveForWorkflowWrite(key), value);
        return true;
    }

    /// <summary>ToolkitGet(key) → reads a declared key (any role: a read cannot corrupt).</summary>
    public object? ToolkitGet(string key)
    {
        var table = RequireKeyTable();
        return _dataStore.Get(table.ResolveForWorkflowRead(key));
    }

    /// <summary>
    /// ToolkitWait(keys...) → waits until all named keys are present (AND), honouring the run's
    /// cancellation token.
    ///
    /// <para><b>Why this is still a workflow-facing function.</b> ADR-0004 D1 removed waiting from
    /// the <i>storage</i> API, not from the language: a flow that awaits a value another flow
    /// produces needs a way to say so. It lives on <see cref="KeyWaitRegistry"/> now, which owns
    /// the waiter bookkeeping the store used to.</para>
    ///
    /// <para><b>Why it returns a Task.</b> A parked flow must not hold a thread (ADR-0008 D1).
    /// The generator sees the task-returning signature by reflection and emits <c>await</c> at the
    /// call site, so the workflow's own body is async and the thread goes back to the pool while
    /// this wait is outstanding.</para>
    /// </summary>
    public Task<JsonElement> ToolkitWait(params string[] keys)
    {
        if (keys.Length == 0)
            return Task.FromResult(EmptyObject());
        var table = RequireKeyTable();
        var full = keys.Select(table.ResolveForWorkflowRead).ToArray();
        return _waits.WaitAsync(full, _options.DefaultWaitTimeout, RunToken);
    }

    /// <summary>ToolkitWaitAny(keys...) → waits until any named key is present (OR).</summary>
    public Task<JsonElement> ToolkitWaitAny(params string[] keys)
    {
        if (keys.Length == 0)
            return Task.FromResult(EmptyObject());
        var table = RequireKeyTable();
        var full = keys.Select(table.ResolveForWorkflowRead).ToArray();
        return _waits.WaitAnyAsync(full, _options.DefaultWaitTimeout, RunToken);
    }

    /// <summary>ToolkitRemove(key) → removes a declared key the workflow owns.</summary>
    public object? ToolkitRemove(string key)
    {
        var table = RequireKeyTable();
        return _dataStore.Remove(table.ResolveForWorkflowWrite(key));
    }

    /// <summary>ToolkitKeys() → the local names of every declared key (a JSON array).</summary>
    public object? ToolkitKeys()
    {
        var table = RequireKeyTable();
        return JsonSerializer.SerializeToElement(table.Names.ToArray());
    }

    /// <summary>ToolkitContains(key) → whether the declared key currently holds a value.</summary>
    public object? ToolkitContains(string key)
    {
        var table = RequireKeyTable();
        return table.IsDeclared(key) && _dataStore.Contains(table.FullKey(key));
    }

    /// <summary>
    /// ToolkitAppend(key, entry) → appends to a declared key whose role is
    /// <see cref="Contract.Declaration.KeyRole.Stream"/>.
    ///
    /// <para>Appending is an operation on a key, so ADR-0004 D1's "a stream is a key role, not a
    /// separate storage method" is enforced here: a non-stream key refuses the append rather than
    /// quietly turning into an array.</para>
    /// </summary>
    public object? ToolkitAppend(string key, object? entry)
    {
        var table = RequireKeyTable();
        if (!table.TryResolve(key, out var declaration))
            throw new KeyAccessException(key, "key.undeclared");
        if (declaration.Role != Contract.Declaration.KeyRole.Stream)
            throw new KeyAccessException(key, "key.role-not-stream");
        _dataStore.Append(table.FullKey(key), entry);
        return true;
    }

    /// <summary>
    /// The toolkit's key table, or a throw when this run is not inside a ToolKit instance.
    ///
    /// <para>Failing loudly is deliberate: the previous generation of these builtins wrote into
    /// whatever string arrived, so a workflow that ran unscoped silently wrote process-global
    /// keys. There is no correct unscoped behaviour to fall back to.</para>
    /// </summary>
    private KeyTable RequireKeyTable()
    {
        if (InstanceId is null)
            throw new InvalidOperationException(
                "Toolkit* builtins require a ToolKit instance; this workflow is running unscoped.");
        return _manager.GetKeyTable(InstanceId)
            ?? throw new InvalidOperationException(
                $"No key table for instance '{InstanceId}'; the toolkit is not mounted.");
    }

    // ── Bench I/O pair ──

    /// <summary>
    /// FlowIn(name, default) → reads a trigger binding param resolved for this run
    /// (from <see cref="RawOverrides"/>). Returns <paramref name="defaultValue"/> when
    /// the name is absent or the workflow runs outside a ToolKit instance.
    /// </summary>
    public string FlowIn(string name, string? defaultValue = null)
    {
        if (RawOverrides is not null && RawOverrides.TryGetValue(name, out var value) && value is not null)
            return value;
        return defaultValue ?? "";
    }

    /// <summary>
    /// FlowOut(key, value) → publishes a value on this workflow's completion-edge output
    /// packet (writes <c>{outputNamespace}/{key}</c> into the DataStore). No-op when the
    /// workflow runs outside a ToolKit instance (no output namespace).
    /// </summary>
    public void FlowOut(string key, object? value)
    {
        if (string.IsNullOrEmpty(OutputNamespace))
            return;
        _dataStore.Set($"{OutputNamespace}/{key}", value);
    }

    private static JsonElement EmptyObject()
        => JsonSerializer.SerializeToElement(new System.Text.Json.Nodes.JsonObject());
}

/// <summary>
/// The ToolKit <see cref="IExecutionGlobalsFactory"/>: generated G classes derive from
/// <see cref="ToolKitExecutionGlobals"/>, and each run gets a fresh instance wired to the
/// ToolKit services. Registered by <c>AddKitXToolKit</c> (AddSingleton, overriding the V6
/// default factory's TryAdd registration).
///
/// <para><b>Deliberately lazy:</b> the ToolKit services are resolved from the service
/// provider inside <see cref="Create"/>, never in this constructor. The eager alternative
/// is a DI cycle — ToolKitExecutionGlobalsFactory → ToolkitInstanceManager →
/// IWorkflowExecutor → WorkflowRunner → StructuredRoslynBackend → IExecutionGlobalsFactory
/// → (this factory) — which throws at first resolution (e.g. opening the Bench page).
/// By Create() time the whole container is built, so the lazy path is always safe.</para>
/// </summary>
internal sealed class ToolKitExecutionGlobalsFactory : IExecutionGlobalsFactory
{
    private readonly IServiceProvider _services;

    /// <summary>Captures the service provider; ToolKit services are resolved lazily per run.</summary>
    public ToolKitExecutionGlobalsFactory(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc/>
    public Type BaseType => typeof(ToolKitExecutionGlobals);

    /// <inheritdoc/>
    public ExecutionGlobals Create(Type gType)
    {
        // The generated G derives from ToolKitExecutionGlobals; Activator.CreateInstance
        // calls the parameterless base constructor, then we wire the ToolKit services so
        // the G's Toolkit*/Bench* builtins have their backing services at run time.
        var g = (ToolKitExecutionGlobals)Activator.CreateInstance(gType)!;
        g.Inject(
            _services.GetRequiredService<DataStore>(),
            _services.GetRequiredService<ToolkitInstanceManager>(),
            _services.GetRequiredService<DataStoreOptions>());
        return g;
    }
}
