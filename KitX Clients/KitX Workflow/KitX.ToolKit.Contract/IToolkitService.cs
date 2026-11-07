using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Events;

namespace KitX.ToolKit.Contract;

/// <summary>
/// ToolKit lifecycle + storage facade (ToolKit 前后端分离 GUI 稿 §4.1). Shared by the
/// desktop management page and (later) the remote API. The frontend depends only on this
/// contract — never on a concrete instance manager.
/// </summary>
public interface IToolkitService
{
    /// <summary>Scans the storage directory and returns every ToolKit's metadata.</summary>
    IReadOnlyList<Toolkit> ListToolkits();

    /// <summary>Reads a ToolKit's config truth by id; null when absent.</summary>
    Toolkit? GetToolkit(string toolkitId);

    /// <summary>Creates a ToolKit: validates + assigns an id + persists. Returns the stored config.</summary>
    Toolkit CreateToolkit(Toolkit draft);

    /// <summary>Updates a ToolKit: validates + persists. Rejected while mounted.</summary>
    Toolkit UpdateToolkit(Toolkit toolkit);

    /// <summary>Deletes a ToolKit (recursively). Rejected while mounted.</summary>
    bool DeleteToolkit(string toolkitId);

    /// <summary>Mounts a ToolKit (subscribes its Spawn triggers). Idempotent.</summary>
    void Mount(string toolkitId);

    /// <summary>Unmounts a ToolKit (unsubscribes Spawn triggers + ends all its instances). Idempotent.</summary>
    /// <remarks>Unmounting forgets the in-memory scheduler but <b>keeps the toolkit's keys</b>; the
    /// caller may mount it again. Use <see cref="Uninstall"/> to erase data.</remarks>
    void Unmount(string toolkitId);

    /// <summary>Stops a ToolKit's triggers and cancels its flows, <b>keeping its data</b>. Idempotent.</summary>
    /// <remarks>A disabled toolkit stays mounted and stays readable; only triggering is refused.
    /// This is the difference between "disabled" and "uninstalled" (ADR-0005 D2).</remarks>
    void Disable(string toolkitId);

    /// <summary>Resumes a disabled ToolKit's triggers. Idempotent.</summary>
    void Enable(string toolkitId);

    /// <summary>Removes a ToolKit: stops it, ends its instances, deletes its declared keys and its
    /// stored config. Returns the number of data keys removed. Idempotent.</summary>
    int Uninstall(string toolkitId);

    /// <summary>The lifecycle state of a toolkit, or null when it is not mounted.</summary>
    LifecycleState? GetLifecycleState(string toolkitId);

    /// <summary>The ids of currently mounted ToolKits.</summary>
    IReadOnlyList<string> MountedToolkitIds { get; }

    /// <summary>True when the given ToolKit is mounted.</summary>
    bool IsMounted(string toolkitId);

    /// <summary>
    /// Validates a stored ToolKit's config without mounting it, returning the diagnostics that
    /// would stop <see cref="Mount"/>. Empty means it would mount.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the pre-mount gate, not the editor's live check.</b> It reads the copy on
    /// disk and runs the full compiler — including loading every declared <c>.kcs</c> — because
    /// that is what <see cref="Mount"/> does. <see cref="ConfigValidator.Validate"/> answers a
    /// different question: "is this draft, which is in memory and may never be saved, well
    /// formed?" It does no I/O and runs on every keystroke. The two can legitimately disagree, and
    /// that asymmetry is load-bearing: a draft with no saved id has nothing to validate here, and
    /// a config that passes the draft check can still fail to mount because a file is missing.
    /// <b>Do not merge them.</b></para>
    ///
    /// <para><b>Why both this and <see cref="Mount"/> exist.</b> <see cref="Mount"/> has to fail
    /// loudly for the case the caller skipped the check. The two must never disagree: if this
    /// returns diagnostics, Mount throws <c>toolkit.compile.failed</c>; if this returns empty,
    /// Mount succeeds. They share one <c>ToolkitCompiler.Compile</c> call precisely so that
    /// cannot drift.</para>
    ///
    /// <para>Unknown ids return a single <c>toolkit.not-found</c> diagnostic rather than throwing,
    /// so the same call serves "does this config work" and "does this id exist".</para>
    /// </remarks>
    IReadOnlyList<Diagnostic> Validate(string toolkitId);

    /// <summary>How many flows are running right now, per mounted ToolKit.</summary>
    /// <remarks>
    /// <para><b>A snapshot query, not a state to maintain.</b> The authoritative count already
    /// exists inside the runtime — it is the same number the concurrency budget compares against
    /// — so asking for it is one dictionary walk. Rebuilding it on the client from a stream of
    /// flow events would create a second source of truth that can drift, and would make the count
    /// only as correct as the client's event handling (ADR-0015 D5.1).</para>
    ///
    /// <para>Replaces <c>Instances</c>, which returned every instance as a snapshot record. Q83
    /// collapsed "instance" into "the toolkit's execution context", and what callers actually read
    /// off that snapshot was the running count.</para>
    ///
    /// <para>ToolKits with no running flows are absent from the dictionary rather than present
    /// with zero, so a caller iterating it sees work, not the installed catalogue.</para>
    /// </remarks>
    IReadOnlyDictionary<string, int> ActiveFlowCounts();

    /// <summary>Raised when the ToolKit list changes (CRUD).</summary>
    event EventHandler? ToolkitListChanged;

    /// <summary>Raised for every event on the flow channel (spawn/complete/cancel/run/data/ui).</summary>
    event EventHandler<ToolkitEvent>? ToolkitEvent;
}
