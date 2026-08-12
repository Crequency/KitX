namespace KitX.ToolKit.Data;

/// <summary>
/// Tuning knobs for a <see cref="DataStore"/>. Defaults suit the workflow runtime's
/// synchronous-blocking model (a <c>Wait</c> must eventually unblock even if a writer
/// never arrives, so the calling workflow does not hang forever).
/// </summary>
public sealed class DataStoreOptions
{
    /// <summary>Default timeout for blocking <c>Wait</c>/<c>WaitAny</c> calls. On expiry the call returns an empty JSON object.</summary>
    public TimeSpan DefaultWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
