namespace KitX.ToolKit.Contract;

/// <summary>
/// How a single flow ended (ADR-0005 D1). One-shot: each flow produces exactly one, and
/// it is <b>not</b> accumulated across runs.
///
/// <para><b>Cancelled is not Failed.</b> Keeping them as distinct members is the whole
/// point of this enum — the original implementation stringified cancellation
/// (<c>Bench/RunScheduler.cs:242-246</c>, the <c>OperationCanceledException</c> branch),
/// which made "the user stopped it" indistinguishable from "it broke".</para>
///
/// <para><see cref="Failed"/> MUST carry a reason and a stable error code
/// (<see cref="Diagnostic"/>, ADR-0005 D4).</para>
/// </summary>
public enum RunOutcome
{
    /// <summary>Every node ran to completion.</summary>
    Succeeded,

    /// <summary>A node failed and its downstream was truncated. Always accompanied by a
    /// <see cref="Diagnostic"/> carrying a stable <see cref="Diagnostic.Code"/>.</summary>
    Failed,

    /// <summary>Stopped on request (unmount, disable, end-instance, shutdown) — not an error.</summary>
    Cancelled,
}
