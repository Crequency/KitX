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

    /// <summary>Snapshot of every instance across all mounted ToolKits.</summary>
    IReadOnlyList<InstanceSnapshot> Instances { get; }

    /// <summary>Raised when the ToolKit list changes (CRUD).</summary>
    event EventHandler? ToolkitListChanged;

    /// <summary>Raised for every event on the flow channel (spawn/complete/cancel/run/data/ui).</summary>
    event EventHandler<BenchEvent>? BenchEvent;
}
