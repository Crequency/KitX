using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Serialization;
using KitX.ToolKit.Time;
using KitX.WorkflowV6.Serialization;
using Serilog;

namespace KitX.ToolKit.Storage;

/// <summary>
/// Persistent storage for ToolKit configs (ToolKit 前后端分离 GUI 稿 §9.1). Layout:
/// <c>{root}/{id}/toolkit.json</c> (the config truth) + <c>{root}/{id}/workflows/*.kcs</c>
/// (bundled workflows, resolved by <c>ToolkitFileStore</c>).
///
/// <para>An id is a GUID assigned on create; <see cref="ToolkitMeta.Name"/> is display-only.
/// <c>Update</c>/<c>Delete</c> are rejected for mounted ToolKits (the caller enforces this
/// via the service layer). <c>Save</c> hard-validates the config before persisting.</para>
///
/// <para><b>Threat model:</b> a <c>.kcs</c> is executable code — only load ToolKits from
/// trusted directories (mirrors <c>ToolkitFileStore</c>).</para>
/// </summary>
internal sealed class ToolkitStore
{
    /// <summary>The per-ToolKit config file name, stored at <c>{root}/{id}/toolkit.json</c>.</summary>
    public const string ConfigFileName = "toolkit.json";

    private readonly string _root;
    private readonly ConfigValidator _validator;
    private readonly IIdGenerator _ids;

    /// <summary>Creates a store rooted at <paramref name="root"/> using the default id source.</summary>
    public ToolkitStore(string root, ConfigValidator? validator = null)
        : this(root, validator, null)
    {
    }

    /// <summary>
    /// Creates a store with an explicit id source. <paramref name="idGenerator"/> is internal, so
    /// this overload is too — a test asserts the generated id instead of matching a random GUID
    /// (ADR-0006 D1); production code goes through the public constructor.
    /// </summary>
    internal ToolkitStore(string root, ConfigValidator? validator, IIdGenerator? idGenerator)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _validator = validator ?? new ConfigValidator();
        _ids = idGenerator ?? GuidIdGenerator.Instance;
    }

    /// <summary>The storage root directory.</summary>
    public string Root => _root;

    /// <summary>Scans the storage root and returns every ToolKit's config (metadata).</summary>
    public IReadOnlyList<Toolkit> List()
    {
        if (!Directory.Exists(_root))
            return [];

        var result = new List<Toolkit>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var toolkit = Load(Path.GetFileName(dir));
            if (toolkit is not null)
                result.Add(toolkit);
        }
        return result;
    }

    /// <summary>Loads a ToolKit's config truth by id; null when absent or corrupt.</summary>
    public Toolkit? Load(string toolkitId)
    {
        var path = ConfigPath(toolkitId);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        var toolkit = ToolkitConfig.Deserialize(json);
        if (toolkit is null)
            return null;

        // The config owns the identity; the directory is only where it is stored (ADR-0006 D2,
        // requirement R1.1). This used to overwrite `toolkit.Id` with the directory name, so
        // copying a directory produced two copies of the same declaration answering to two
        // different ids — one key space each.
        //
        // An empty id is the one case the directory still decides, because such a config
        // predates ids being written at all. It is reported rather than silently patched: a
        // toolkit whose id comes from its folder breaks the moment the folder is renamed.
        if (string.IsNullOrWhiteSpace(toolkit.Id))
        {
            Log.Warning(
                "[ToolkitStore] {Path} declares no Id; falling back to the directory name '{Id}'. " +
                "Rename the directory and this toolkit becomes a different toolkit.",
                path, toolkitId);
            toolkit.Id = toolkitId;
        }

        return toolkit;
    }

    /// <summary>
    /// Saves a ToolKit config. Assigns a GUID id when the config has none. Hard-validates
    /// before persisting; throws <see cref="InvalidOperationException"/> on invalid config.
    /// </summary>
    public Toolkit Save(Toolkit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);

        var validation = _validator.Validate(toolkit);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                "Invalid ToolKit config:\n  " + string.Join("\n  ", validation.Errors));

        if (string.IsNullOrWhiteSpace(toolkit.Id))
            toolkit.Id = _ids.NewToolkitId();

        var dir = ToolkitDir(toolkit.Id);
        Directory.CreateDirectory(dir);
        KcsFileIo.AtomicWrite(ConfigPath(toolkit.Id), ToolkitConfig.Serialize(toolkit));
        return toolkit;
    }

    /// <summary>Deletes a ToolKit's directory recursively. Returns false when absent.</summary>
    public bool Delete(string toolkitId)
    {
        var dir = ToolkitDir(toolkitId);
        if (!Directory.Exists(dir))
            return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    /// <summary>True when a ToolKit with the given id exists on disk.</summary>
    public bool Exists(string toolkitId) => Directory.Exists(ToolkitDir(toolkitId));

    /// <summary>
    /// Resolves the per-ToolKit directory under the storage root. Guards against path
    /// traversal/escape: an id must be a non-empty, non-absolute, single path segment
    /// (no separators, no "." / "..", no invalid file-name characters). Throws
    /// <see cref="ArgumentException"/> otherwise, so <see cref="Delete"/> can never
    /// reach beyond the Toolkit's own directory (e.g. <c>Delete("")</c> must not delete
    /// the storage root).
    /// </summary>
    private string ToolkitDir(string toolkitId)
    {
        if (string.IsNullOrEmpty(toolkitId))
            throw new ArgumentException("Toolkit id must not be null or empty.", nameof(toolkitId));
        if (toolkitId == "." || toolkitId == "..")
            throw new ArgumentException($"Toolkit id '{toolkitId}' is a reserved path segment.", nameof(toolkitId));
        if (toolkitId.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new ArgumentException($"Toolkit id '{toolkitId}' must not contain path separators.", nameof(toolkitId));
        if (Path.IsPathRooted(toolkitId))
            throw new ArgumentException($"Toolkit id '{toolkitId}' must be a relative name, not an absolute path.", nameof(toolkitId));
        if (toolkitId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Toolkit id '{toolkitId}' contains invalid file-name characters.", nameof(toolkitId));
        return Path.Combine(_root, toolkitId);
    }

    private string ConfigPath(string toolkitId) => Path.Combine(ToolkitDir(toolkitId), ConfigFileName);
}
