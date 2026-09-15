using KitX.ToolKit.Models;
using Serilog;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// A timer trigger (Bench RFC §4.2 <c>Timer</c>): one-shot or periodic, driven by a
/// <see cref="System.Threading.Timer"/>. "Run at KitX launch" is the <see cref="OneShot"/>
/// case (fire once after a due time).
///
/// <para>Timing is configured via <see cref="TriggerConfig.DueTimeMs"/> +
/// <see cref="TriggerConfig.IntervalMs"/> + <see cref="TriggerConfig.OneShot"/>.
/// Cron expressions (<see cref="TriggerConfig.Cron"/>) are recognized but not yet
/// scheduled — a non-empty Cron logs a warning and schedules nothing (deferred to a
/// later iteration with a dedicated Cron parser).</para>
/// </summary>
public sealed class TimerTrigger : TriggerSourceBase
{
    private readonly TriggerConfig _config;
    private System.Threading.Timer? _timer;
    private readonly object _gate = new();
    private bool _started;

    public TimerTrigger(string id, TriggerConfig config)
        : base(id, TriggerType.Timer)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public override void Start(IServiceProvider services)
    {
        lock (_gate)
        {
            if (_started)
                return;
            _started = true;
        }

        // Cron is a documented deferral: schedule nothing, but say so clearly.
        if (!string.IsNullOrWhiteSpace(_config.Cron))
        {
            Log.Warning("[TimerTrigger] Trigger {Id} uses Cron '{Cron}' which is not yet supported; " +
                "nothing will be scheduled.", Id, _config.Cron);
            return;
        }

        var dueMs = (long)(_config.DueTimeMs ?? 0);
        var periodMs = (long)(_config.IntervalMs ?? 0);
        var oneShot = _config.OneShot == true;

        // One-shot with no explicit period → fire once, then don't reschedule.
        var period = oneShot ? Timeout.Infinite : periodMs;
        _timer = new System.Threading.Timer(
            _ => Fire(new { TriggerId = Id }),
            null,
            dueMs,
            period);
    }

    /// <inheritdoc/>
    public override void Stop()
    {
        lock (_gate)
        {
            _started = false;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
