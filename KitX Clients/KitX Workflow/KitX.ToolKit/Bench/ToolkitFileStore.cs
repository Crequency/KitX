using System.Text.Json;
using KitX.Core.Contract.Workflow;

namespace KitX.ToolKit.Bench;

/// <summary>
/// File access for a ToolKit's bundled workflows (Bench RFC §10: storage at
/// <c>Data/Toolkits/{id}/workflows/*.kcs</c>). A config workflow's <c>File</c> is a
/// path relative to the ToolKit root; this store resolves and loads it into a
/// <see cref="KcsFileFormat"/>.
///
/// <para><b>Threat model:</b> a <c>.kcs</c> file is executable code (its IrData compiles
/// and runs). Only load files from trusted ToolKit packages — never auto-scan arbitrary
/// directories.</para>
/// </summary>
public sealed class ToolkitFileStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private const long MaxKcsFileBytes = 10 * 1024 * 1024;

    private readonly string _root;

    public ToolkitFileStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>The ToolKit storage root directory.</summary>
    public string Root => _root;

    /// <summary>Resolves a relative <c>File</c> path to an absolute path under the root.</summary>
    public string Resolve(string relativePath) => Path.Combine(_root, relativePath);

    /// <summary>Loads a workflow file (relative path) as a <see cref="KcsFileFormat"/>. Null on missing/corrupt/oversized.</summary>
    public async Task<KcsFileFormat?> LoadAsync(string relativePath)
    {
        var path = Resolve(relativePath);
        if (!File.Exists(path))
            return null;

        var info = new FileInfo(path);
        if (info.Length > MaxKcsFileBytes)
            return null;

        var json = await File.ReadAllTextAsync(path);
        try
        {
            return JsonSerializer.Deserialize<KcsFileFormat>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
