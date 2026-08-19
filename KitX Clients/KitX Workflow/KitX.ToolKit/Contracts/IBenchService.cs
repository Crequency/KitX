namespace KitX.ToolKit.Contracts;

/// <summary>
/// Bench orchestration facade (ToolKit 前后端分离 GUI 稿 §4.1): spawn / cancel.
/// The frontend and (later) the remote API call this instead of the concrete instance manager.
///
/// <para>The <c>Validate</c> member was retired in the D3 cleanup — validation is a pure
/// <see cref="Validation.ConfigValidator"/> concern that callers (e.g. the Dashboard editor)
/// invoke directly, so it no longer belongs on the orchestration facade.</para>
/// </summary>
public interface IBenchService
{
    /// <summary>
    /// Spawns a new instance of a mounted ToolKit from a Spawn trigger. Returns the new
    /// instance id, or null when the trigger is unknown / not a Spawn type / the ToolKit is
    /// not mounted / the MaxInstances cap is exceeded.
    /// </summary>
    string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null);

    /// <summary>Ends an instance (cancels all its runs + destroys it). Idempotent.</summary>
    void EndInstance(string instanceId);

    /// <summary>Ends every instance of every mounted ToolKit.</summary>
    void EndAll();
}
