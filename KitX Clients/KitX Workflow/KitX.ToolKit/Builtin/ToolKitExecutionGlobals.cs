using System.Text.Json;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Panels;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Backend.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Builtin;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKitExecutionGlobals — the host-side ExecutionGlobals subclass that carries
// the ToolKit first-class builtins (Ui* panel runtime + DataStore* blackboard +
// BenchIn/BenchOut).
//
// This is the payload of the external ExecutionGlobals extension seam
// (IExecutionGlobalsFactory, registered by AddKitXWorkflowV6 with TryAdd and
// overridden here by AddKitXToolKit). The generated structured C# declares
// `public sealed class G : ToolKitExecutionGlobals`, so workflow calls to UiSet /
// DataStoreSet / BenchIn &c. resolve directly to these typed methods — the
// reserved-name plugin bridge (BuiltinUiPlugin / BuiltinDataStorePlugin routed by
// PluginHostAdapter) is retired.
//
// Each method inlines what the retired plugin's Invoke dispatch did, but with
// strong-typed signatures and direct service access (no PluginHost.Call). The
// Ui* family is instance-scoped: <see cref="InstanceId"/> (the owning instance)
// gates them to no-ops when the workflow runs outside a ToolKit instance, matching
// the historical safe-default convention. The DataStore family is not instance
// scoped. BenchIn reads the raw trigger-binding overrides; BenchOut writes to the
// instance's DataStore output namespace.
//
// The generated G derives from this sealed class; per-run instances are created by
// <see cref="ToolKitExecutionGlobalsFactory.Create"/> (not Activator), so the
// injected services are wired exactly once at factory construction.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The ToolKit ExecutionGlobals subclass that carries the first-class Ui* /
/// DataStore* / Bench* builtins, backed directly by the ToolKit services
/// (DataStore, PanelRuntime). Generated workflow code derives its <c>G</c> from
/// this type, so the methods are reachable without the reserved-name plugin bridge.
/// </summary>
public class ToolKitExecutionGlobals : ExecutionGlobals
{
    private DataStore _dataStore;
    private PanelRuntime _panelRuntime;
    private ToolkitInstanceManager _manager;
    private DataStoreOptions _options;

    /// <summary>
    /// Creates a globals instance wired to the ToolKit services. The factory (and the
    /// DI graph) guarantees the services are non-null.
    /// </summary>
    public ToolKitExecutionGlobals(
        DataStore dataStore,
        PanelRuntime panelRuntime,
        ToolkitInstanceManager manager,
        DataStoreOptions? options = null)
    {
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _panelRuntime = panelRuntime ?? throw new ArgumentNullException(nameof(panelRuntime));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? new DataStoreOptions();
    }

    /// <summary>
    /// Parameterless constructor for the generated G subclass: the backend's factory
    /// <c>Activator.CreateInstance</c>s the generated G (which derives from this type),
    /// so this type must be Activator-constructible. Services are null until
    /// <see cref="Inject"/> is called by the factory.
    /// </summary>
    public ToolKitExecutionGlobals()
    {
        _dataStore = null!;
        _panelRuntime = null!;
        _manager = null!;
        _options = new DataStoreOptions();
    }

    /// <summary>
    /// Wires the ToolKit services into an Activator-created instance. Called by
    /// <see cref="ToolKitExecutionGlobalsFactory.Create"/> after it instantiates the
    /// generated G, so the G's base (this type) has its services before RunAsync runs.
    /// </summary>
    public void Inject(
        DataStore dataStore,
        PanelRuntime panelRuntime,
        ToolkitInstanceManager manager,
        DataStoreOptions? options = null)
    {
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _panelRuntime = panelRuntime ?? throw new ArgumentNullException(nameof(panelRuntime));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? new DataStoreOptions();
    }

    // ── Instance-scoped run context (from HostRunContext) ──

    /// <summary>
    /// The owning instance's id, read per-run from the backend-injected
    /// <see cref="ExecutionGlobals.RunContext"/> (a <see cref="HostRunContext"/>). Null
    /// when the workflow runs outside a ToolKit instance; the Ui* family no-ops then.
    /// </summary>
    public string? InstanceId => (RunContext as HostRunContext)?.InstanceId;

    /// <summary>
    /// The workflow's instance-scoped DataStore output namespace (Bench scheduler
    /// injected), read from <see cref="ExecutionGlobals.RunContext"/>. Null outside a
    /// ToolKit instance; <see cref="BenchOut"/> no-ops then.
    /// </summary>
    public string? OutputNamespace => (RunContext as HostRunContext)?.OutputNamespace;

    /// <summary>
    /// The raw run-time constant overrides (resolved trigger binding params included),
    /// read from <see cref="ExecutionGlobals.RunContext"/>. Null outside a ToolKit
    /// instance; <see cref="BenchIn"/> returns its default then.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? RawOverrides => (RunContext as HostRunContext)?.RawOverrides;

    // ── Kit.X.UI family (panel runtime) — instance-scoped. ──

    /// <summary>UiSet(controlId, value) → writes the control's main property key.</summary>
    public object? UiSet(string controlId, object? value)
    {
        if (InstanceId is null) return null;
        _panelRuntime.SetControlValue(InstanceId, controlId, value);
        return true;
    }

    /// <summary>UiSet(controlId, prop, value) → writes panel/{controlId}/{prop}.</summary>
    public object? UiSet(string controlId, string prop, object? value)
    {
        if (InstanceId is null)
            return null;
        var toolkitId = _manager.GetToolkitId(InstanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Set(PanelScope.Key(toolkitId, InstanceId, controlId, prop), value);
        return true;
    }

    /// <summary>UiGet(controlId) → reads the control's main property key.</summary>
    public object? UiGet(string controlId)
    {
        if (InstanceId is null)
            return null;
        return _panelRuntime.GetControlValue(InstanceId, controlId);
    }

    /// <summary>UiLog(controlId, entry) → appends to the named log control.</summary>
    public object? UiLog(string controlId, object? entry)
    {
        if (InstanceId is null)
            return null;
        var toolkitId = _manager.GetToolkitId(InstanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Append(PanelScope.Key(toolkitId, InstanceId, controlId, PanelScope.PropLog), entry);
        return true;
    }

    /// <summary>UiLog(entry) → appends to the default "log" control.</summary>
    public object? UiLog(object? entry)
    {
        if (InstanceId is null)
            return null;
        var toolkitId = _manager.GetToolkitId(InstanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Append(PanelScope.Key(toolkitId, InstanceId, PanelScope.PropLog, PanelScope.PropLog), entry);
        return true;
    }

    /// <summary>UiProgress(controlId, value) → sets the progress control's value.</summary>
    public object? UiProgress(string controlId, object? value)
    {
        if (InstanceId is null)
            return null;
        var toolkitId = _manager.GetToolkitId(InstanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Set(PanelScope.Key(toolkitId, InstanceId, controlId, PanelScope.PropValue), value);
        return true;
    }

    /// <summary>UiDialog(controlId, message, buttons...) → writes the dialog request slot.</summary>
    public object? UiDialog(string controlId, string message, params string[] buttons)
    {
        if (InstanceId is null)
            return null;
        var toolkitId = _manager.GetToolkitId(InstanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Set(PanelScope.Key(toolkitId, InstanceId, controlId, PanelScope.PropRequest),
            new { message, buttons });
        return true;
    }

    /// <summary>UiOpenPanel() → requests the host to present the instance's panel.</summary>
    public object? UiOpenPanel()
    {
        if (InstanceId is null)
            return null;
        _panelRuntime.RequestPanelOpen(InstanceId);
        return true;
    }

    // ── KitX.DataStore family (data blackboard) — not instance-scoped. ──

    /// <summary>DataStoreSet(key, value) → writes a blackboard key.</summary>
    public object? DataStoreSet(string key, object? value)
    {
        _dataStore.Set(key, value);
        return true;
    }

    /// <summary>DataStoreGet(key) → reads a blackboard key.</summary>
    public object? DataStoreGet(string key)
        => _dataStore.Get(key);

    /// <summary>DataStoreWait(keys...) → blocks until all keys are set (AND), honouring the run's cancellation token.</summary>
    public object? DataStoreWait(params string[] keys)
    {
        if (keys.Length == 0)
            return EmptyObject();
        return _dataStore.Wait(keys, _options.DefaultWaitTimeout, RunToken);
    }

    /// <summary>DataStoreWaitAny(keys...) → blocks until any key is set (OR), honouring the run's cancellation token.</summary>
    public object? DataStoreWaitAny(params string[] keys)
    {
        if (keys.Length == 0)
            return EmptyObject();
        return _dataStore.WaitAny(keys, _options.DefaultWaitTimeout, RunToken);
    }

    /// <summary>DataStoreRemove(key) → removes a blackboard key.</summary>
    public object? DataStoreRemove(string key)
        => _dataStore.Remove(key);

    /// <summary>DataStoreKeys() → lists all blackboard keys (as a JSON array element).</summary>
    public object? DataStoreKeys()
        => JsonSerializer.SerializeToElement(_dataStore.Keys().ToArray());

    /// <summary>DataStoreContains(key) → whether a blackboard key exists.</summary>
    public object? DataStoreContains(string key)
        => _dataStore.Contains(key);

    // ── Bench I/O pair ──

    /// <summary>
    /// BenchIn(name, default) → reads a trigger binding param resolved for this run
    /// (from <see cref="RawOverrides"/>). Returns <paramref name="defaultValue"/> when
    /// the name is absent or the workflow runs outside a ToolKit instance.
    /// </summary>
    public string BenchIn(string name, string? defaultValue = null)
    {
        if (RawOverrides is not null && RawOverrides.TryGetValue(name, out var value) && value is not null)
            return value;
        return defaultValue ?? "";
    }

    /// <summary>
    /// BenchOut(key, value) → publishes a value on this workflow's completion-edge output
    /// packet (writes <c>{outputNamespace}/{key}</c> into the DataStore). No-op when the
    /// workflow runs outside a ToolKit instance (no output namespace).
    /// </summary>
    public void BenchOut(string key, object? value)
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
public sealed class ToolKitExecutionGlobalsFactory : IExecutionGlobalsFactory
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
        // the G's Ui*/DataStore*/Bench* builtins have their backing services at run time.
        var g = (ToolKitExecutionGlobals)Activator.CreateInstance(gType)!;
        g.Inject(
            _services.GetRequiredService<DataStore>(),
            _services.GetRequiredService<PanelRuntime>(),
            _services.GetRequiredService<ToolkitInstanceManager>(),
            _services.GetRequiredService<DataStoreOptions>());
        return g;
    }
}
