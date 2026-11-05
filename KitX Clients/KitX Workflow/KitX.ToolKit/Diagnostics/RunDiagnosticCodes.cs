namespace KitX.ToolKit.Diagnostics;

/// <summary>
/// Stable error codes for flow-run failures (ADR-0007 D1: a <c>Code</c> MUST be a stable value,
/// MUST NOT be a message prefix or a hash).
/// </summary>
/// <remarks>
/// <para><b>Why these live in the implementation assembly and not the contract.</b> The contract
/// carries the code as a plain string on <see cref="Contract.Diagnostic"/>. Putting the constants
/// beside it would cost a contract type, and S4's job is to shrink the contract — adding one back
/// to name the others would be a strange way to achieve that.</para>
/// <para>The consequence is deliberate: consumers branch on the documented literals. That is the
/// same contract a wire protocol gives you, and it is what ADR-0007 asked for — the code is the
/// interface, the constant is a convenience.</para>
/// <para><b>These strings are frozen once shipped.</b> Renaming one breaks every consumer that
/// branched on it, silently. Add a new code instead.</para>
/// </remarks>
internal static class RunDiagnosticCodes
{
    /// <summary>The workflow's <c>.kcs</c> file could not be found for the mounted ToolKit.</summary>
    public const string WorkflowNotFound = "flow.workflow.not-found";

    /// <summary>The workflow file could not be read or compiled.</summary>
    public const string WorkflowUnreadable = "flow.workflow.unreadable";

    /// <summary>The workflow executed and reported failure without throwing.</summary>
    public const string WorkflowFailed = "flow.workflow.failed";

    /// <summary>The workflow threw an exception while executing.</summary>
    public const string WorkflowThrew = "flow.workflow.threw";

    /// <summary>
    /// Two parallel branches feeding one AND-join published the same property name
    /// (design Q69 §5.6.7). Choosing one would make the node's inputs depend on arrival order.
    /// </summary>
    public const string JoinConflict = "flow.join.conflict";

    /// <summary>No failure code applies but the run did not succeed. A defect, not a normal path.</summary>
    public const string Unknown = "flow.failed.unknown";
}
