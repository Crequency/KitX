using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using KitX.WorkflowV6.Serialization;
using Serilog;

namespace KitX.ToolKit.Storage;

/// <summary>
/// Reads and writes <c>surface.json</c> under a ToolKit's directory.
/// </summary>
/// <remarks>
/// <para><b>Same base root as <see cref="ToolkitStore"/>.</b> One storage root, one place that
/// knows how a ToolKit's directory is laid out — a second root would eventually disagree about
/// where a ToolKit lives, and then a layout would be read from the wrong one.</para>
///
/// <para><b>Three failures, three outcomes.</b> A file-level defect (unparsable JSON, an unknown
/// <c>schemaVersion</c>, a <c>columns</c> other than 4) refuses the whole document. An element-level
/// defect drops that element and keeps the rest. The line between them is "can a renderer still
/// draw something", not severity: half a card is better than a white screen, but a document whose
/// own rules cannot be established must not be half-interpreted.</para>
///
/// <para><b>Elements are parsed one at a time on purpose.</b> The model uses
/// <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>, and that attribute turns
/// an unrecognised <c>type</c> into an exception for the <i>whole</i> deserialization call — which
/// would promote a single bad element into a file-level refusal, exactly the outcome the paragraph
/// above forbids. So the array is walked as raw JSON and each element is deserialized alone; one
/// throwing does not take the others with it.</para>
///
/// <para><b>Nothing throws.</b> A missing file is the normal state of a ToolKit nobody has laid out
/// yet; a corrupt one is the state after a bad hand-edit. Both are reported through
/// <see cref="SurfaceLoadResult"/>, and both are recoverable by saving from the editor.</para>
/// </remarks>
internal sealed class SurfaceStore : ISurfaceStore
{
    /// <summary>The layout file's name inside a ToolKit directory.</summary>
    public const string FileName = "surface.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _root;

    public SurfaceStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <inheritdoc/>
    public event EventHandler<SurfaceChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public SurfaceLoadResult Load(string toolkitId)
    {
        if (string.IsNullOrWhiteSpace(toolkitId))
            return SurfaceLoadResult.Empty;

        var path = PathFor(toolkitId);
        if (!File.Exists(path))
        {
            // Not a defect. ADR-0016 D4: a ToolKit with no card is a ToolKit with no card.
            return SurfaceLoadResult.Empty;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.unreadable",
                $"Could not read '{FileName}': {ex.Message}"));
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.invalid-json",
                $"'{FileName}' is not valid JSON: {ex.Message}"));
        }

        using (parsed)
        {
            return ReadDocument(parsed.RootElement, toolkitId);
        }
    }

    /// <inheritdoc/>
    public void Save(string toolkitId, SurfaceDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);
        ArgumentNullException.ThrowIfNull(document);

        var path = PathFor(toolkitId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        KcsFileIo.AtomicWrite(path, JsonSerializer.Serialize(document, SerializerOptions));

        Changed?.Invoke(this, new SurfaceChangedEventArgs(toolkitId));
    }

    /// <inheritdoc/>
    public void Delete(string toolkitId)
    {
        if (string.IsNullOrWhiteSpace(toolkitId))
            return;

        var path = PathFor(toolkitId);
        if (!File.Exists(path))
            return;

        File.Delete(path);
        Changed?.Invoke(this, new SurfaceChangedEventArgs(toolkitId));
    }

    /// <summary>
    /// Walks a parsed document, applying the file-level checks first and then the element-level
    /// ones. Caller owns <paramref name="root"/>.
    /// </summary>
    private static SurfaceLoadResult ReadDocument(JsonElement root, string toolkitId)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.root-not-object",
                $"'{FileName}' must contain a JSON object."));
        }

        // File layer: the two numbers that say how to read everything else.
        var schemaVersion = ReadInt(root, "schemaVersion", SurfaceDocument.CurrentSchemaVersion);
        if (schemaVersion != SurfaceDocument.CurrentSchemaVersion)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.unsupported-schema",
                $"'{FileName}' declares schemaVersion {schemaVersion}; this build understands " +
                $"{SurfaceDocument.CurrentSchemaVersion}. Refusing rather than guessing."));
        }

        var columns = ReadInt(root, "columns", SurfaceDocument.DefaultColumns);
        if (columns != SurfaceDocument.DefaultColumns)
        {
            // A probe, not a preference: a future build that writes six columns must be refused
            // here rather than rendered as if it were four.
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.unsupported-columns",
                $"'{FileName}' declares columns {columns}; this build lays out " +
                $"{SurfaceDocument.DefaultColumns}. Refusing rather than reflowing."));
        }

        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.elements-missing",
                $"'{FileName}' has no 'elements' array."));
        }

        var kept = new List<SurfaceElement>(elements.GetArrayLength());
        List<Diagnostic>? warnings = null;

        foreach (var element in elements.EnumerateArray())
        {
            SurfaceElement? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SurfaceElement>(element.GetRawText(), SerializerOptions);
            }
            catch (JsonException ex)
            {
                // One unknown type must not take the card down with it.
                (warnings ??= []).Add(ElementError(
                    toolkitId, "surface.element.unsupported-type",
                    $"Dropped one element: {ex.Message}"));
                continue;
            }

            if (parsed is null)
            {
                (warnings ??= []).Add(ElementError(
                    toolkitId, "surface.element.empty",
                    "Dropped one element: it deserialised to null."));
                continue;
            }

            kept.Add(parsed);
        }

        return new SurfaceLoadResult(
            new SurfaceDocument { SchemaVersion = schemaVersion, Columns = columns, Elements = kept },
            [],
            warnings ?? []);
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : fallback;

    private static Diagnostic FileError(string toolkitId, string code, string message)
        => Diagnostic.Error(code, message, new DiagnosticPath(ToolkitId: toolkitId));

    private static Diagnostic ElementError(string toolkitId, string code, string message)
        => Diagnostic.Warning(code, message, new DiagnosticPath(ToolkitId: toolkitId));

    /// <summary>
    /// The layout file's path for a ToolKit. The id is validated by <c>ToolkitIdGuard</c> at
    /// compile time, so the segment cannot escape the root here.
    /// </summary>
    private string PathFor(string toolkitId) => Path.Combine(_root, toolkitId, FileName);
}
