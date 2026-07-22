namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Text.Json;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// ScriptPersistenceManager — disk persistence of compiled workflow assemblies.
// Adapted from v5.1 WorkflowIR's ScriptPersistenceManager (145 lines, nearly
// identical — only the namespace and entry type change).
//
// Persists compiled assemblies to disk as {hash}.dll + {hash}.meta.json under
// Data/CompiledScripts/{workflow-id}/, with version-based invalidation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages disk persistence of compiled workflow assemblies. Each workflow gets a
/// subdirectory under <see cref="CompiledScriptsRoot"/>; stale assemblies (version
/// mismatch / corrupt meta) are deleted so the next run recompiles.
/// </summary>
internal sealed class ScriptPersistenceManager
{
    internal static readonly string CompiledScriptsRoot = Path.Combine("./Data/", "CompiledScripts");

    internal static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Action<string, CompiledScriptEntry> _registerCacheEntry;
    private readonly Func<string> _getKitXVersion;
    private readonly Func<string, CompiledScriptEntry?> _tryGetCacheEntry;

    internal ScriptPersistenceManager(
        Action<string, CompiledScriptEntry> registerCacheEntry,
        Func<string> getKitXVersion,
        Func<string, CompiledScriptEntry?> tryGetCacheEntry)
    {
        _registerCacheEntry = registerCacheEntry;
        _getKitXVersion = getKitXVersion;
        _tryGetCacheEntry = tryGetCacheEntry;
    }

    /// <summary>Attempts to load a compiled assembly from disk (null if absent/stale/corrupt).</summary>
    internal CompiledScriptEntry? TryLoadFromDisk(string workflowId, string hash)
    {
        var dir = Path.Combine(CompiledScriptsRoot, workflowId);
        var dllPath = Path.Combine(dir, $"{hash}.dll");
        var metaPath = Path.Combine(dir, $"{hash}.meta.json");

        if (!File.Exists(dllPath) || !File.Exists(metaPath)) return null;

        try
        {
            var metaJson = File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<CompiledScriptMeta>(metaJson, MetaJsonOptions);
            if (meta == null) { DeleteFromDisk(workflowId, hash); return null; }

            var currentVersion = _getKitXVersion();
            if (meta.KitXVersion != currentVersion)
            {
                Log.Debug("[ScriptPersistenceManager] Version mismatch for hash '{Hash}': disk={Disk}, current={Current}. Recompiling.",
                    hash, meta.KitXVersion, currentVersion);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            if (string.IsNullOrEmpty(meta.TypeName)) { DeleteFromDisk(workflowId, hash); return null; }

            var dllBytes = File.ReadAllBytes(dllPath);
            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(new MemoryStream(dllBytes));

            var entry = new CompiledScriptEntry(loadedAssembly, alc);
            _registerCacheEntry(hash, entry);
            return entry;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptPersistenceManager] Error loading hash '{Hash}' from disk, deleting", hash);
            DeleteFromDisk(workflowId, hash);
            return null;
        }
    }

    /// <summary>Persists a compiled assembly and its metadata to disk.</summary>
    internal void SaveToDisk(string workflowId, string hash, MemoryStream assemblyBytes, string typeName)
    {
        try
        {
            var dir = Path.Combine(CompiledScriptsRoot, workflowId);
            Directory.CreateDirectory(dir);

            var dllPath = Path.Combine(dir, $"{hash}.dll");
            var metaPath = Path.Combine(dir, $"{hash}.meta.json");

            File.WriteAllBytes(dllPath, assemblyBytes.ToArray());

            var meta = new CompiledScriptMeta
            {
                ScriptHash = hash,
                CompileTimeUtc = DateTime.UtcNow,
                KitXVersion = _getKitXVersion(),
                TypeName = typeName,
            };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, MetaJsonOptions));

            Log.Debug("[ScriptPersistenceManager] Persisted hash '{Hash}' (workflow: {WfId})", hash, workflowId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptPersistenceManager] Failed to persist hash '{Hash}'", hash);
        }
    }

    /// <summary>Deletes persisted assembly files from disk.</summary>
    internal static void DeleteFromDisk(string workflowId, string hash)
    {
        try
        {
            var dir = Path.Combine(CompiledScriptsRoot, workflowId);
            var dllPath = Path.Combine(dir, $"{hash}.dll");
            var metaPath = Path.Combine(dir, $"{hash}.meta.json");
            if (File.Exists(dllPath)) File.Delete(dllPath);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptPersistenceManager] Error deleting disk cache for hash '{Hash}'", hash);
        }
    }

    /// <summary>Preloads all persisted compiled scripts for a workflow into the cache.</summary>
    internal int PreloadFromDisk(string workflowId)
    {
        var dir = Path.Combine(CompiledScriptsRoot, workflowId);
        if (!Directory.Exists(dir)) return 0;

        var count = 0;
        foreach (var metaPath in Directory.GetFiles(dir, "*.meta.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<CompiledScriptMeta>(File.ReadAllText(metaPath), MetaJsonOptions);
                if (meta == null || string.IsNullOrEmpty(meta.ScriptHash)) continue;
                if (_tryGetCacheEntry(meta.ScriptHash) != null) continue;
                if (TryLoadFromDisk(workflowId, meta.ScriptHash) != null) count++;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[ScriptPersistenceManager] Error preloading from {Path}", metaPath);
            }
        }
        return count;
    }
}

/// <summary>Metadata for a persisted compiled assembly.</summary>
internal sealed class CompiledScriptMeta
{
    public string ScriptHash { get; set; } = string.Empty;
    public DateTime CompileTimeUtc { get; set; }
    public string KitXVersion { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}