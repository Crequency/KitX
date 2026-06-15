using System.Text.Json;
using Serilog;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Manages disk persistence of compiled script assemblies.
/// Handles loading, saving, and preloading of compiled scripts from disk.
/// </summary>
internal class ScriptPersistenceManager
{
    /// <summary>
    /// Root directory for persisted compiled script assemblies.
    /// Each workflow gets a subdirectory: Data/CompiledScripts/{workflow-id}/
    /// </summary>
    internal static readonly string CompiledScriptsRoot = Path.Combine("./Data/", "CompiledScripts");

    /// <summary>
    /// JSON serializer options for meta.json persistence.
    /// </summary>
    internal static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Callback to register a loaded compiled script into the cache.
    /// </summary>
    private readonly Action<string, CompiledScriptEntry> _registerCacheEntry;

    /// <summary>
    /// Callback to compute the current KitX version for cache invalidation.
    /// </summary>
    private readonly Func<string> _getKitXVersion;

    /// <summary>
    /// The cache dictionary for registering entries.
    /// </summary>
    private readonly Func<string, CompiledScriptEntry?> _tryGetCacheEntry;

    /// <summary>
    /// Initializes a new persistence manager.
    /// </summary>
    /// <param name="registerCacheEntry">Called to register a loaded entry into the cache.</param>
    /// <param name="getKitXVersion">Called to get the current KitX version.</param>
    /// <param name="tryGetCacheEntry">Called to check if an entry is already cached.</param>
    internal ScriptPersistenceManager(
        Action<string, CompiledScriptEntry> registerCacheEntry,
        Func<string> getKitXVersion,
        Func<string, CompiledScriptEntry?> tryGetCacheEntry)
    {
        _registerCacheEntry = registerCacheEntry;
        _getKitXVersion = getKitXVersion;
        _tryGetCacheEntry = tryGetCacheEntry;
    }

    /// <summary>
    /// Attempts to load a compiled script assembly from disk.
    /// Validates KitX version match before loading; stale assemblies are deleted.
    /// </summary>
    /// <returns>Loaded instance, or null if not found / stale / corrupt.</returns>
    internal ICompiledBlockScript? TryLoadFromDisk(string workflowId, string hash)
    {
        var dir = Path.Combine(CompiledScriptsRoot, workflowId);
        var dllPath = Path.Combine(dir, $"{hash}.dll");
        var metaPath = Path.Combine(dir, $"{hash}.meta.json");

        if (!File.Exists(dllPath) || !File.Exists(metaPath))
            return null;

        try
        {
            var metaJson = File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<CompiledScriptMeta>(metaJson, MetaJsonOptions);
            if (meta == null)
            {
                Log.Debug("[ScriptPersistenceManager] Corrupt meta.json for hash '{Hash}', deleting", hash);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            var currentVersion = _getKitXVersion();
            if (meta.KitXVersion != currentVersion)
            {
                Log.Debug("[ScriptPersistenceManager] KitX version mismatch for hash '{Hash}': " +
                    "disk={DiskVer}, current={CurrentVer}. Deleting and recompiling.",
                    hash, meta.KitXVersion, currentVersion);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            if (string.IsNullOrEmpty(meta.TypeName))
            {
                Log.Debug("[ScriptPersistenceManager] Missing TypeName in meta for hash '{Hash}', deleting", hash);
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            var dllBytes = File.ReadAllBytes(dllPath);
            var alc = new CollectibleAssemblyLoadContext(hash);
            var loadedAssembly = alc.LoadFromStream(new MemoryStream(dllBytes));

            var scriptType = loadedAssembly.GetType(meta.TypeName);
            if (scriptType == null)
            {
                Log.Debug("[ScriptPersistenceManager] Type '{TypeName}' not found in disk assembly for hash '{Hash}', deleting",
                    meta.TypeName, hash);
                alc.Unload();
                DeleteFromDisk(workflowId, hash);
                return null;
            }

            var instance = (ICompiledBlockScript)Activator.CreateInstance(scriptType)!;
            var entry = new CompiledScriptEntry(instance, alc);
            _registerCacheEntry(hash, entry);

            return instance;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptPersistenceManager] Error loading from disk for hash '{Hash}', deleting", hash);
            DeleteFromDisk(workflowId, hash);
            return null;
        }
    }

    /// <summary>
    /// Persists a compiled assembly and its metadata to disk.
    /// </summary>
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
                TypeName = typeName
            };
            var metaJson = JsonSerializer.Serialize(meta, MetaJsonOptions);
            File.WriteAllText(metaPath, metaJson);

            Log.Debug("[ScriptPersistenceManager] Persisted compiled script hash '{Hash}' to disk (workflow: {WfId})",
                hash, workflowId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ScriptPersistenceManager] Failed to persist compiled script to disk for hash '{Hash}'", hash);
        }
    }

    /// <summary>
    /// Deletes persisted assembly files from disk.
    /// </summary>
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

    /// <summary>
    /// Preloads all persisted compiled scripts for a given workflow from disk.
    /// </summary>
    /// <param name="workflowId">Workflow ID to preload scripts for.</param>
    /// <returns>Number of scripts successfully loaded into cache.</returns>
    internal int PreloadFromDisk(string workflowId)
    {
        var dir = Path.Combine(CompiledScriptsRoot, workflowId);
        if (!Directory.Exists(dir))
            return 0;

        var count = 0;
        foreach (var metaPath in Directory.GetFiles(dir, "*.meta.json"))
        {
            try
            {
                var metaJson = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<CompiledScriptMeta>(metaJson, MetaJsonOptions);
                if (meta == null || string.IsNullOrEmpty(meta.ScriptHash))
                    continue;

                if (_tryGetCacheEntry(meta.ScriptHash) != null)
                    continue;

                if (TryLoadFromDisk(workflowId, meta.ScriptHash) != null)
                    count++;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[ScriptPersistenceManager] Error preloading from {Path}", metaPath);
            }
        }

        if (count > 0)
            Log.Debug("[ScriptPersistenceManager] Preloaded {Count} compiled scripts for workflow {WfId}",
                count, workflowId);

        return count;
    }
}
