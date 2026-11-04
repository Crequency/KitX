using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;

namespace KitX.ToolKit.Compile;

/// <summary>
/// A config that has been read once, validated, and translated into everything the runtime
/// needs (ADR-0002 D1).
///
/// <para><b>Immutable, and "existing means valid".</b> There are no setters — every field is
/// computed by <see cref="ToolkitCompiler"/> and fixed at construction. If you hold one of
/// these, the config was legal and the translation succeeded. That is the whole contract, and
/// it is why the runtime must never take a <see cref="Toolkit"/> again.</para>
///
/// <para><b>Why this is not just a cached view of config.</b> Four consumers used to derive the
/// completion edges from config independently and had already diverged; building the graph once
/// removes the possibility of them disagreeing. Similarly, the three path rules become one, and
/// builtin names resolve here instead of failing at run time when reflection returns null.</para>
/// </summary>
internal sealed class CompiledToolkit
{
    /// <summary>The toolkit id this was compiled from.</summary>
    public required string ToolkitId { get; init; }

    /// <summary>The declared metadata (name / version / author / icon / description /
    /// MinKitXVersion / tags). Carried verbatim — the compiler reads <c>MinKitXVersion</c>
    /// but does not rewrite it.</summary>
    public required ToolkitMeta Meta { get; init; }

    /// <summary>
    /// Every workflow, keyed by id, with its file path resolved against the toolkit root.
    /// </summary>
    public required IReadOnlyDictionary<string, CompiledWorkflow> Workflows { get; init; }

    /// <summary>
    /// The workflow-completion graph. <b>The only one</b> — validation, scheduling, export and
    /// the canvas all read this object (ADR-0002 D3).
    /// </summary>
    public required WorkflowGraph Graph { get; init; }

    /// <summary>
    /// Trigger declarations in their closed form. Spawn triggers are separated from intra-instance
    /// ones at compile time so the manager never has to re-inspect nullable fields.
    /// </summary>
    public required IReadOnlyList<CompiledTrigger> Triggers { get; init; }

    /// <summary>
    /// Declared plugin requirements. The compiler has verified that every <c>PluginCall</c> in
    /// every workflow targets a name on this list (ADR-0002 D6) — it has <b>not</b> verified the
    /// plugin is installed; that is an assembly-time question.
    /// </summary>
    public required IReadOnlyList<PluginRequirement> Plugins { get; init; }

    /// <summary>Canvas comments, carried through for export.</summary>
    public required IReadOnlyList<ToolkitComment> Comments { get; init; }

    /// <summary>Maximum concurrent instances, or null for unlimited.</summary>
    public int? MaxInstances { get; init; }

    /// <summary>
    /// Warnings that did not prevent compilation (e.g. a deprecated field still being read).
    /// Errors never reach this object — a config with any error produces no
    /// <see cref="CompiledToolkit"/> at all.
    /// </summary>
    public IReadOnlyList<Diagnostic> Warnings { get; init; } = [];

    /// <summary>Triggers that spawn a new instance.</summary>
    public IEnumerable<CompiledTrigger> SpawnTriggers => Triggers.Where(t => t.Spawns);

    /// <summary>Looks up a workflow by id. Null when the id is not declared.</summary>
    public CompiledWorkflow? FindWorkflow(string workflowId)
        => Workflows.TryGetValue(workflowId, out var wf) ? wf : null;
}

/// <summary>
/// A single workflow, with its relative <c>File</c> already resolved to an absolute path and
/// its parameter declarations extracted.
/// </summary>
/// <param name="Id">The logical workflow id referenced by bindings and edges.</param>
/// <param name="File">The <c>File</c> as written in config (relative to the toolkit root).</param>
/// <param name="AbsolutePath">The resolved path, or empty when the compiler had no storage
/// root to resolve against.</param>
/// <param name="Parameters">Declared parameters (empty when the workflow declares none).</param>
/// <param name="Description">Optional description.</param>
/// <param name="Builtins">
/// The builtin names this workflow calls, as resolved at compile time. Empty when no resolver
/// was supplied — which means "not checked", not "uses none" (see <see cref="BuiltinResolver.Empty"/>).
/// </param>
internal sealed record CompiledWorkflow(
    string Id,
    string File,
    string AbsolutePath,
    IReadOnlyList<WorkflowParameter> Parameters,
    string? Description,
    IReadOnlyList<string>? Builtins = null);

/// <summary>A declared workflow parameter (name / default), as written in config.</summary>
internal sealed record WorkflowParameter(string Name, string? Default);
