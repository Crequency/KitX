using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;

namespace KitX.ToolKit.Contracts;

/// <summary>
/// ToolKit lifecycle + storage facade (ToolKit 前后端分离 GUI 稿 §4.1). Shared by the
/// desktop management page and (later) the remote API. The frontend depends only on this
/// contract — never on the concrete <c>Instances.ToolkitInstanceManager</c>.
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
    void Unmount(string toolkitId);

    /// <summary>The ids of currently mounted ToolKits.</summary>
    IReadOnlyList<string> MountedToolkitIds { get; }

    /// <summary>True when the given ToolKit is mounted.</summary>
    bool IsMounted(string toolkitId);

    /// <summary>Snapshot of every instance across all mounted ToolKits.</summary>
    IReadOnlyList<InstanceSnapshot> Instances { get; }

    /// <summary>Raised when the ToolKit list changes (CRUD).</summary>
    event EventHandler? ToolkitListChanged;

    /// <summary>Raised for every Bench event (spawn/complete/cancel/run/data/ui).</summary>
    event EventHandler<Events.BenchEvent>? BenchEvent;
}
