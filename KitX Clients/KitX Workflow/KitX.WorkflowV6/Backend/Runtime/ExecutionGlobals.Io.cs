namespace KitX.WorkflowV6.Backend.Runtime;

using System.Collections;
using System.IO;
using System.Text.Json;
using System.Threading;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Io — file I/O and timing primitives
// (Pause / ReadTextFile / WriteTextFile / Len).
// Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>Pause: sleep for N milliseconds.</summary>
    public void Pause(int milliseconds) => Thread.Sleep(milliseconds);

    /// <summary>ReadTextFile: read a text file into a string.</summary>
    public string ReadTextFile(string path) => File.ReadAllText(path);

    /// <summary>WriteTextFile: write content to a text file (overwrites).</summary>
    public void WriteTextFile(string path, string content) => File.WriteAllText(path, content);

    /// <summary>
    /// Len: polymorphic length/count dispatcher. Returns the length of strings,
    /// JSON arrays/objects, .NET arrays, and collections. Returns 0 for null or
    /// scalar types (int, bool, etc.).
    /// </summary>
    public int Len(object? value) => value switch
    {
        null => 0,
        string s => s.Length,
        JsonElement je => je.ValueKind switch
        {
            JsonValueKind.Array => je.GetArrayLength(),
            JsonValueKind.Object => je.EnumerateObject().Count(),
            JsonValueKind.String => je.GetString()?.Length ?? 0,
            _ => 0,
        },
        Array a => a.Length,
        ICollection c => c.Count,
        _ => 0,
    };
}
