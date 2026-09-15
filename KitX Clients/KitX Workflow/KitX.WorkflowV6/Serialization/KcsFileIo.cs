namespace KitX.WorkflowV6.Serialization;

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// KcsFileIo — shared .kcs load/write plumbing used by every workflow file store.
//
// Consolidates the "10 MB size cap + tolerant KcsFileFormat deserialize" logic
// used by the ToolKit BenchWorkflowRunner and the ToolKit ToolkitFileStore. It also
// owns the atomic write path (temp file + replace) so a crashed write never leaves
// a half-written .kcs in place of a good one. (The legacy WorkflowStorageService
// that once shared this plumbing was retired in the D2 cleanup.)
//
// Threat model (W-3): a .kcs file is executable code (its IrData compiles and runs).
// ReadAllWithLimitAsync enforces the 10 MB cap (a DoS guard against oversized files)
// and DeserializeTolerant swallows corrupt JSON. Only callers that already trust the
// source path should use these helpers.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared I/O for <c>.kcs</c> workflow files: the single 10 MB size cap, tolerant
/// <see cref="KcsFileFormat"/> deserialization, and atomic file replacement.
/// </summary>
public static class KcsFileIo
{
    /// <summary>Hard cap on a single .kcs file's size (10 MB). Files beyond this are
    /// rejected on read and on write instead of being processed.</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Reads the whole file as text, rejecting files over <see cref="MaxFileBytes"/>.
    /// Returns null when the file is missing or oversized (logging a warning with the
    /// path so an oversized load attempt is auditable).
    /// </summary>
    public static async Task<string?> ReadAllWithLimitAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
        {
            Log.Warning(
                "[KcsFileIo] Refusing to read oversized .kcs ({Bytes} bytes > {Max}): {Path}",
                info.Length, MaxFileBytes, path);
            return null;
        }

        return await File.ReadAllTextAsync(path);
    }

    /// <summary>
    /// Deserializes a JSON string into a <see cref="KcsFileFormat"/>, returning null for
    /// corrupt/malformed JSON instead of throwing.
    /// </summary>
    public static KcsFileFormat? DeserializeTolerant(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<KcsFileFormat>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes a <see cref="KcsFileFormat"/> and atomically writes it to
    /// <paramref name="path"/>. Throws <see cref="InvalidOperationException"/> when the
    /// serialized payload exceeds <see cref="MaxFileBytes"/> (the write-side twin of the
    /// read-side cap in <see cref="ReadAllWithLimitAsync"/>).
    /// </summary>
    public static async Task WriteKcsAsync(string path, KcsFileFormat kcs)
    {
        var json = JsonSerializer.Serialize(kcs, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw new InvalidOperationException(
                $"Refusing to write oversized .kcs (> {MaxFileBytes} byte cap): {path}");
        await AtomicWriteAsync(path, json);
    }

    /// <summary>
    /// Atomically writes <paramref name="content"/> to <paramref name="path"/>: the text
    /// is written to a sibling <c>.tmp</c> file which is then swapped into place
    /// (<c>File.Replace</c> when the target exists, else <c>File.Move</c>). The parent
    /// directory is created when absent. Any failure cleans up the temp file.
    /// </summary>
    public static void AtomicWrite(string path, string content)
    {
        var tmp = EnsureParentAndGetTempPath(path);
        try
        {
            File.WriteAllText(tmp, content);
            SwapTempIntoPlace(tmp, path);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>
    /// Asynchronous equivalent of <see cref="AtomicWrite(string,string)"/>.
    /// </summary>
    public static async Task AtomicWriteAsync(string path, string content)
    {
        var tmp = EnsureParentAndGetTempPath(path);
        try
        {
            await File.WriteAllTextAsync(tmp, content);
            SwapTempIntoPlace(tmp, path);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static string EnsureParentAndGetTempPath(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        return path + ".tmp";
    }

    private static void SwapTempIntoPlace(string tmp, string destination)
    {
        if (File.Exists(destination))
            File.Replace(tmp, destination, null);
        else
            File.Move(tmp, destination);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the original exception is the one that matters.
        }
    }
}
