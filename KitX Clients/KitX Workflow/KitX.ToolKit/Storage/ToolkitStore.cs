using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using KitX.WorkflowV6.Serialization;

namespace KitX.ToolKit.Storage;

/// <summary>
/// Persistent storage for ToolKit configs (ToolKit 前后端分离 GUI 稿 §9.1). Layout:
/// <c>{root}/{id}/toolkit.json</c> (the config truth) + <c>{root}/{id}/workflows/*.kcs</c>
/// (bundled workflows, resolved by <c>Bench.ToolkitFileStore</c>).
///
/// <para>An id is a GUID assigned on create; <see cref="ToolkitMeta.Name"/> is display-only.
/// <c>Update</c>/<c>Delete</c> are rejected for mounted ToolKits (the caller enforces this
/// via the service layer). <c>Save</c> hard-validates the config before persisting.</para>
///
/// <para><b>Threat model:</b> a <c>.kcs</c> is executable code — only load ToolKits from
/// trusted directories (mirrors <c>ToolkitFileStore</c>).</para>
/// </summary>
public sealed class ToolkitStore
{
    /// <summary>The per-ToolKit config file name, stored at <c>{root}/{id}/toolkit.json</c>.</summary>
    public const string ConfigFileName = "toolkit.json";

    private readonly string _root;
    private readonly ConfigValidator _validator;

    public ToolkitStore(string root, ConfigValidator? validator = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _validator = validator ?? new ConfigValidator();
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

        // Ensure the id is consistent with the directory name.
        toolkit.Id = toolkitId;
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
            toolkit.Id = Guid.NewGuid().ToString("N");

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
