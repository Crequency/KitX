using System.Text.Json;
using KitX.ToolKit.Models;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// Shared plumbing for <see cref="ITriggerSource"/> implementations: id/type storage,
/// the <see cref="Fired"/> event and a <see cref="Raise"/> helper that normalizes any
/// payload to a <see cref="JsonElement"/>.
/// </summary>
public abstract class TriggerSourceBase : ITriggerSource
{
    protected TriggerSourceBase(string id, TriggerType type)
    {
        Id = id;
        Type = type;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TriggerType Type { get; }

    /// <inheritdoc/>
    public event EventHandler<TriggerFiredEventArgs>? Fired;

    /// <inheritdoc/>
    public abstract void Start(IServiceProvider services);

    /// <inheritdoc/>
    public abstract void Stop();

    /// <inheritdoc/>
    public void Fire(object? payload = null) => Raise(NormalizePayload(payload));

    /// <summary>Raises <see cref="Fired"/> with the given JSON payload.</summary>
    protected void Raise(JsonElement payload)
        => Fired?.Invoke(this, new TriggerFiredEventArgs(Id, payload));

    private static JsonElement NormalizePayload(object? payload) => payload switch
    {
        null => JsonSerializer.SerializeToElement<object?>(null),
        JsonElement je => je.Clone(),
        JsonDocument jd => jd.RootElement.Clone(),
        _ => JsonSerializer.SerializeToElement(payload),
    };
}
