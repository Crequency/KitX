using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;

namespace KitX.ToolKit.Contracts;

/// <summary>
/// Bench orchestration facade (ToolKit 前后端分离 GUI 稿 §4.1): spawn / cancel / validate.
/// The frontend and (later) the remote API call this instead of the concrete instance manager.
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

    /// <summary>Validates a ToolKit config (identity, references, strict DAG, UI rules).</summary>
    ConfigValidationResult Validate(Toolkit toolkit);
}
