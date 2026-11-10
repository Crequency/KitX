using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Serialization;

namespace KitX.ToolKit.Run;

/// <summary>
/// File access for a ToolKit's bundled workflows (Bench RFC §10: storage at
/// <c>Data/Toolkits/{toolkitId}/workflows/*.kcs</c>). A config workflow's <c>File</c> is a
/// path relative to the ToolKit root; this store resolves and loads it into a
/// <see cref="KcsFileFormat"/>.
///
/// <para>Implements <see cref="IToolkitWorkflowFileStore"/>. The ctor takes the <b>base</b>
/// storage root (e.g. <c>Data/Toolkits</c>, matching <c>Storage.ToolkitStore</c>); each
/// operation is scoped to a <c>toolkitId</c> sub-directory. Resolution rejects paths that
/// escape the ToolKit root.</para>
///
/// <para><b>Threat model:</b> a <c>.kcs</c> file is executable code (its IrData compiles
/// and runs). Only load files from trusted ToolKit packages — never auto-scan arbitrary
/// directories.</para>
/// </summary>
internal sealed class ToolkitFileStore : IToolkitWorkflowFileStore
{
    /// <summary>
    /// Built-in minimal runnable v6 workflow template (UX v2 §4.5/V17). The v6 IR has no
    /// Entry/Return statements — an empty body IS the minimal runnable program, and the
    /// editor projects it as the default empty program on open.
    /// </summary>
    private static readonly Workflow MinimalWorkflowTemplate = new();

    private readonly string _root;

    public ToolkitFileStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>The ToolKit storage base root directory.</summary>
    public string Root => _root;

    /// <inheritdoc/>
    public string ResolveWorkflowPath(string toolkitId, string relativeFile)
        => Resolve(_root, toolkitId, relativeFile);

    /// <summary>
    /// Resolves a relative workflow file under <c>{toolkitRoot}/{toolkitId}</c>, refusing paths that
    /// escape that directory.
    /// </summary>
    /// <remarks>
    /// <para><b>Static so a caller holding only a root can resolve without inventing a store.</b>
    /// <see cref="RunScheduler"/> needs exactly this and nothing else from the type — it resolves one
    /// path per node and never loads or saves. Handing it a whole <see cref="ToolkitFileStore"/> made
    /// "any directory will do" compile, and eight test sites promptly passed
    /// <c>Path.GetTempPath()</c>: legal, and meaning "some directory" rather than "this toolkit's".</para>
    ///
    /// <para>The escape check is the reason this is one implementation rather than two. A second
    /// copy in the scheduler would drift from this one, and the drift would be a path-traversal hole
    /// in whichever copy was wrong.</para>
    /// </remarks>
    public static string Resolve(string toolkitRoot, string toolkitId, string relativeFile)
    {
        if (string.IsNullOrWhiteSpace(relativeFile))
            relativeFile = "workflow.kcs";
        if (!relativeFile.EndsWith(".kcs", StringComparison.OrdinalIgnoreCase))
            relativeFile += ".kcs";

        var root = Path.Combine(toolkitRoot, toolkitId);
        var path = Path.GetFullPath(Path.Combine(root, relativeFile));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workflow path escapes the ToolKit root.");

        return path;
    }

    /// <inheritdoc/>
    public async Task<KcsFileFormat?> LoadAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        var json = await KcsFileIo.ReadAllWithLimitAsync(path);
        if (json is null)
            return null;
        return KcsFileIo.DeserializeTolerant(json);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Serializes and atomically writes (temp file + replace). Throws
    /// <see cref="InvalidOperationException"/> when the serialized payload exceeds the
    /// shared 10 MB cap (<see cref="KcsFileIo.MaxFileBytes"/>).
    /// </remarks>
    public Task SaveAsync(string path, KcsFileFormat kcs)
        => KcsFileIo.WriteKcsAsync(path, kcs);

    /// <inheritdoc/>
    /// <remarks>
    /// Writes the generated minimal workflow atomically (temp file + replace). Throws
    /// <see cref="InvalidOperationException"/> when the serialized payload exceeds the
    /// shared 10 MB cap (<see cref="KcsFileIo.MaxFileBytes"/>).
    /// </remarks>
    public async Task WriteMinimalWorkflowAsync(string toolkitId, ToolkitWorkflow workflow, string author)
    {
        var path = ResolveWorkflowPath(toolkitId, workflow.File);

        var now = DateTime.UtcNow;
        var kcs = new KcsFileFormat
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Description = string.Empty,
            Author = author,
            CreatedTime = now,
            LastModifiedTime = now,
            IrVersion = "v6",
            VariableConstants = [],
            IrData = WorkflowSerializer.Serialize(MinimalWorkflowTemplate),
        };

        await KcsFileIo.WriteKcsAsync(path, kcs);
    }
}
