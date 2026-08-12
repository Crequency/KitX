using KitX.ToolKit.Models;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// The "canvas edge" trigger (Bench RFC §4.2 <c>WorkflowCompletion</c>): fired when a
/// predecessor workflow completes. Unlike timer/plugin sources it has no external
/// event pump — the Bench scheduler drives it, calling <see cref="ITriggerSource.Fire"/>
/// with the predecessor's output packet once the predecessor completes (and, for
/// AND-joins, once <i>all</i> predecessors have delivered their packets). Start/Stop
/// are no-ops because the scheduler owns the firing lifecycle.
/// </summary>
public sealed class WorkflowCompletionTrigger : TriggerSourceBase
{
    public WorkflowCompletionTrigger(string id, TriggerConfig config)
        : base(id, TriggerType.WorkflowCompletion)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>The predecessor workflow id this edge depends on (<see cref="TriggerConfig.From"/>).</summary>
    public string From => Config.From ?? string.Empty;

    internal TriggerConfig Config { get; }

    /// <inheritdoc/>
    public override void Start(IServiceProvider services)
    {
    }

    /// <inheritdoc/>
    public override void Stop()
    {
    }
}
