using KitX.ToolKit.Contract.Declaration;

namespace KitX.ToolKit.Contract;

/// <summary>
/// Flows that are started by something other than a trigger firing on its own.
/// The frontend and (later) the remote API call this instead of a concrete instance manager.
///
/// <para>The <c>Validate</c> member was retired in the D3 cleanup — validation is a pure
/// <c>ConfigValidator</c> concern; the pre-mount gate lives on
/// <see cref="IToolkitService.Validate"/>, and the two answer different questions.</para>
///
/// <para><b>No cancel family.</b> Until 2026-11-08 this also exposed
/// <c>EndInstance(instanceId)</c> and <c>EndAll()</c>. Q83 removed the instance, so
/// <c>EndInstance</c> has nothing left to address — a flow id is an identity, not an address
/// (ADR-0015 §3.1). <c>EndAll</c> was superseded by <see cref="IToolkitService.Disable"/>,
/// which stops the same flows <em>and</em> persists the disabled state, so keeping a second
/// way to do nearly the same thing would only offer callers a chance to pick the weaker one.
/// Machine-wide stop is a loop over <see cref="IToolkitService.MountedToolkitIds"/> — one line
/// at the call site, not a contract member.</para>
/// </summary>
public interface IRunService
{
    /// <summary>
    /// Starts a flow of a mounted ToolKit from a Spawn trigger. Returns the new flow id, or
    /// <see langword="null"/> when the trigger is unknown / not a Spawn type / the ToolKit is not
    /// mounted or is disabled / the machine-wide concurrency budget is exhausted.
    /// </summary>
    /// <remarks>
    /// The returned id is for log correlation only. A flow cannot be addressed by it, which is
    /// why there is no <c>Cancel(flowId)</c>: stopping flows is done by stopping the ToolKit that
    /// owns them via <see cref="IToolkitService.Disable"/>.
    /// </remarks>
    string? Spawn(string toolkitId, string triggerId, object? payload = null, Initiator? initiator = null);
}
