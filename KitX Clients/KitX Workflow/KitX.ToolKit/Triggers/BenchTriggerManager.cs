using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using Serilog;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// The unified trigger dispatcher (Bench RFC §4). Activates a <see cref="Toolkit"/>: builds
/// one source per declared (non-completion) trigger via the <see cref="TriggerSourceRegistry"/>,
/// starts them, and on <see cref="ITriggerSource.Fired"/> forwards to the
/// <see cref="BenchScheduler"/> as a parameter-injected run. WorkflowCompletion triggers are
/// edges, not sources — the scheduler drives them.
///
/// Only one ToolKit is active at a time; <see cref="Activate"/> replaces the previous one.
/// </summary>
public sealed class BenchTriggerManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly TriggerSourceRegistry _registry;
    private readonly IWorkflowExecutor _executor;
    private readonly Func<Toolkit, ToolkitFileStore> _fileStoreFactory;

    private readonly List<ITriggerSource> _sources = [];
    private BenchScheduler? _scheduler;
    private DataStore? _dataStore;
    private Toolkit? _activeToolkit;

    /// <param name="services">Service provider for resolving source dependencies (e.g. IPluginServer).</param>
    /// <param name="registry">The trigger source registry (type → implementation factory).</param>
    /// <param name="executor">The workflow executor the scheduler runs nodes with.</param>
    /// <param name="fileStoreFactory">Maps a ToolKit to its file store. Defaults to
    /// <c>Data/Toolkits/{name}</c> under the app base directory.</param>
    public BenchTriggerManager(
        IServiceProvider services,
        TriggerSourceRegistry registry,
        IWorkflowExecutor executor,
        Func<Toolkit, ToolkitFileStore>? fileStoreFactory = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _fileStoreFactory = fileStoreFactory ?? (toolkit => new ToolkitFileStore(
            Path.Combine(AppContext.BaseDirectory, "Data", "Toolkits", Sanitize(toolkit.Meta.Name))));
    }

    /// <summary>Forwarded from the active scheduler; raised when a triggered run completes.</summary>
    public event EventHandler<BenchRunCompletedEventArgs>? RunCompleted;

    /// <summary>The ToolKit currently active, or null.</summary>
    public Toolkit? ActiveToolkit => _activeToolkit;

    /// <summary>The active scheduler, or null when no ToolKit is active.</summary>
    public BenchScheduler? Scheduler => _scheduler;

    /// <summary>
    /// Activates a ToolKit, replacing any currently active one. Throws
    /// <see cref="InvalidOperationException"/> when the config fails validation.
    /// </summary>
    public void Activate(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);

        var validation = new ConfigValidator().Validate(toolkit);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                "Invalid ToolKit config:\n  " + string.Join("\n  ", validation.Errors));

        Deactivate();

        _activeToolkit = toolkit;
        _dataStore = new DataStore();
        _scheduler = new BenchScheduler(toolkit, _executor, _dataStore, _fileStoreFactory(toolkit));
        _scheduler.RunCompleted += (_, e) => RunCompleted?.Invoke(this, e);

        foreach (var trigger in toolkit.Triggers)
        {
            if (trigger.Type == TriggerType.WorkflowCompletion)
                continue; // scheduler-driven edge, not an external source

            var source = _registry.Create(trigger, _services);
            source.Fired += OnSourceFired;
            _sources.Add(source);
            source.Start(_services);
            Log.Information("[BenchTriggerManager] Started trigger source {Id} ({Type})", trigger.Id, trigger.Type);
        }
    }

    /// <summary>Stops all sources and disposes the active scheduler. Idempotent.</summary>
    public void Deactivate()
    {
        foreach (var source in _sources)
        {
            source.Fired -= OnSourceFired;
            source.Stop();
        }
        _sources.Clear();

        _scheduler?.Dispose();
        _scheduler = null;
        _dataStore = null;
        _activeToolkit = null;
    }

    /// <summary>Programmatically fires a trigger (e.g. a Manual trigger from a run button or CLI).</summary>
    public void Fire(string triggerId, object? payload = null)
        => _scheduler?.StartRun(triggerId, payload);

    private void OnSourceFired(object? sender, TriggerFiredEventArgs e)
        => _scheduler?.StartRun(e.TriggerId, e.Payload);

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c)).ToArray();
        var clean = new string(chars);
        return string.IsNullOrWhiteSpace(clean) ? "untitled" : clean;
    }

    /// <inheritdoc/>
    public void Dispose() => Deactivate();
}
