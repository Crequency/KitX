using System.Text.Json;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// Raised by an <see cref="ITriggerSource"/> when it fires. Carries the JSON
/// payload that becomes the target workflow's unified parameter channel
/// (Bench RFC §4.1 — Trigger.Fired(payload) → Workflow.Run(injected params)).
/// </summary>
public sealed class TriggerFiredEventArgs : EventArgs
{
    public TriggerFiredEventArgs(string triggerId, JsonElement payload)
    {
        TriggerId = triggerId;
        Payload = payload;
    }

    /// <summary>The id of the firing trigger source.</summary>
    public string TriggerId { get; }

    /// <summary>The JSON payload. For a source-triggered root it is the source payload;
    /// for a completion-driven edge it is the predecessor's output packet.</summary>
    public JsonElement Payload { get; }
}
