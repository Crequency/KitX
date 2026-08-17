using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Models;

namespace KitX.ToolKit.Contracts;

/// <summary>
/// File access for a ToolKit's <b>bundled</b> workflows (Bench RFC §10 storage layout:
/// <c>Data/Toolkits/{toolkitId}/workflows/*.kcs</c>). A config workflow's <c>File</c> is a
/// path relative to the ToolKit root; this store resolves, loads and saves it as a
/// <see cref="KcsFileFormat"/>.
///
/// <para>This is the single contract for ToolKit workflow file access, shared by the
/// Dashboard (editor load/save + "new workflow" template) and the ToolKit runtime
/// (Bench scheduler). The root layout is encapsulated in the implementation — callers pass
/// a <c>toolkitId</c> and a relative file, never a hand-built absolute path.</para>
///
/// <para><b>Threat model:</b> a <c>.kcs</c> file is executable code (its IrData compiles
/// and runs). Only load files from trusted ToolKit packages — never auto-scan arbitrary
/// directories. Resolution rejects paths that escape the ToolKit root.</para>
/// </summary>
public interface IToolkitWorkflowFileStore
{
    /// <summary>
    /// Resolves a config workflow's relative <c>File</c> to an absolute path under the
    /// ToolKit root. Appends a <c>.kcs</c> extension when absent and throws
    /// <see cref="InvalidOperationException"/> if the resolved path escapes the root.
    /// </summary>
    string ResolveWorkflowPath(string toolkitId, string relativeFile);

    /// <summary>
    /// Loads a KCS envelope from an explicit bundle path (editor entry point). Returns
    /// null when the file is missing, corrupt, or exceeds the size cap.
    /// </summary>
    Task<KcsFileFormat?> LoadAsync(string path);

    /// <summary>Saves a KCS envelope to an explicit bundle path (editor save path).</summary>
    Task SaveAsync(string path, KcsFileFormat kcs);

    /// <summary>
    /// Writes the built-in minimal runnable v6 workflow template (UX v2 §4.5/V17) for a
    /// ToolKit workflow. The v6 IR has no Entry/Return statements — an empty body IS the
    /// minimal runnable program, and the editor projects it as the default empty program
    /// on open.
    /// </summary>
    Task WriteMinimalWorkflowAsync(string toolkitId, ToolkitWorkflow workflow, string author);
}
