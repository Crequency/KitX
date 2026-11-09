using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Surface;
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

        // File layer: the two numbers that say how to read everything else. Both are probed with
        // TryReadInt rather than ReadInt, because "the field is absent" and "the field says the
        // current value" have to be different answers. A file with no layoutVersion has not said
        // which layout model it was written in; filling in the current one is exactly the guess
        // this field exists to prevent.
        if (!TryReadInt(root, "schemaVersion", out var schemaVersion))
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, SurfaceDiagnosticCodes.FileMissingVersion,
                $"'{FileName}' does not declare 'schemaVersion'. A file that does not say which " +
                "schema it is written in cannot be read as if it said the current one."));
        }

        if (schemaVersion != SurfaceDocument.CurrentSchemaVersion)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.unsupported-schema",
                $"'{FileName}' declares schemaVersion {schemaVersion}; this build understands " +
                $"{SurfaceDocument.CurrentSchemaVersion}. Refusing rather than guessing."));
        }

        if (!TryReadInt(root, "layoutVersion", out var layoutVersion))
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, SurfaceDiagnosticCodes.FileMissingVersion,
                $"'{FileName}' does not declare 'layoutVersion'. This file was written before the " +
                "layout model had a version, or it predates the model entirely; either way laying " +
                "it out under the current rules would be a guess."));
        }

        if (layoutVersion != SurfaceDocument.CurrentLayoutVersion)
        {
            // A probe, not a preference: a future build that writes layout model 2 must be refused
            // here rather than laid out as if it were model 1.
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.unsupported-layout-version",
                $"'{FileName}' declares layoutVersion {layoutVersion}; this build lays out " +
                $"{SurfaceDocument.CurrentLayoutVersion}. Refusing rather than reflowing."));
        }

        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return SurfaceLoadResult.Refused(FileError(
                toolkitId, "surface.file.elements-missing",
                $"'{FileName}' has no 'elements' array."));
        }

        var kept = new List<SurfaceElement>(elements.GetArrayLength());
        List<Diagnostic>? warnings = null;

        // The document layer gets the same unknown-field check the element layer has had since the
        // `col`/`column` incident. Without it, `columns: 4` on an old file was read by nothing and
        // reported by nothing — the field was simply gone. A warning rather than a refusal: the
        // document may still lay out perfectly, and the author is the one who can act on it.
        foreach (var unknown in UnknownDocumentMembers(root))
        {
            (warnings ??= []).Add(ElementError(
                toolkitId, SurfaceDiagnosticCodes.FileUnknownField,
                $"'{FileName}' has an unrecognised top-level field '{unknown}'; it was ignored. " +
                "Layout files written for an older layout model often carry one."));
        }

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

            // A misspelled field is not an error to the serialiser: it lands in no property and
            // the element keeps its defaults. That is how a file written with `col` rendered every
            // element in column 1 while reporting no problems at all. The element is kept — it may
            // still be perfectly drawable — but the author is told the field went nowhere.
            foreach (var unknown in UnknownMembers(element, parsed))
            {
                (warnings ??= []).Add(ElementError(
                    toolkitId, SurfaceDiagnosticCodes.ElementUnknownField,
                    $"Element of type '{parsed.GetType().Name}' has an unrecognised field " +
                    $"'{unknown}'; it was ignored and the default applied."));
            }

            kept.Add(parsed);
        }

        return new SurfaceLoadResult(
            new SurfaceDocument { SchemaVersion = schemaVersion, LayoutVersion = layoutVersion, Elements = kept },
            [],
            warnings ?? []);
    }

    /// <summary>Reads an integer, or reports that it was not there or was not one.</summary>
    /// <remarks>
    /// The difference from <see cref="ReadInt"/> is the whole reason this overload exists: a missing
    /// field must not be readable as a value. Callers that genuinely want a default (nothing in this
    /// file does, since both version probes are mandatory) should say so by name.
    /// </remarks>
    private static bool TryReadInt(JsonElement root, string name, out int value)
    {
        if (root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
        => TryReadInt(root, name, out var value) ? value : fallback;

    /// <summary>The field names on the document itself that no property of <see cref="SurfaceDocument"/> claims.</summary>
    /// <remarks>
    /// The same comparison <see cref="UnknownMembers"/> makes for one element, made one level up.
    /// It exists because the element layer had it and the document layer did not: a file could carry
    /// <c>columns: 4</c> — a field this build no longer reads — and be silently laid out with no one
    /// told the value went nowhere. See the `col`/`column` incident for what that looks like when it
    /// happens to a field that <i>was</i> being read.
    /// </remarks>
    private static IEnumerable<string> UnknownDocumentMembers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        var known = KnownMembers(typeof(SurfaceDocument), includeDiscriminator: false);
        foreach (var property in root.EnumerateObject())
        {
            if (!known.Contains(property.Name))
                yield return property.Name;
        }
    }

    /// <summary>
    /// The field names on one element that no property of its type claims.
    /// </summary>
    /// <remarks>
    /// <para>Read straight off the JSON rather than configured, because the point is to compare the
    /// file against the type as it is compiled today. A hand-maintained list would drift from the
    /// type, and the drift would be silent in exactly the way this check exists to catch.</para>
    ///
    /// <para><c>System.Text.Json</c> can be told to reject unknown members
    /// (<c>JsonUnmappedMemberHandling.Disallow</c>), but it throws — one misspelled field would
    /// take the whole card down instead of the one element. The element here is kept and the
    /// warning names the field, which is the granularity §3.3.4 asks for.</para>
    /// </remarks>
    private static IEnumerable<string> UnknownMembers(JsonElement element, SurfaceElement parsed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        var known = KnownMembers(parsed.GetType());
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
                yield return property.Name;
        }
    }

    /// <summary>
    /// The JSON names a type accepts: every <see cref="JsonPropertyNameAttribute"/> on it and its
    /// base, plus the polymorphic discriminator when <paramref name="includeDiscriminator"/> is set.
    /// </summary>
    /// <remarks>
    /// The discriminator is an element-only field: it says which concrete record an array entry is.
    /// A document has no such thing, so asking for it there would quietly excuse a stray
    /// <c>type</c> on the root — a field nothing reads, reported by nothing, which is the exact shape
    /// this whole check exists to catch.
    /// </remarks>
    private static HashSet<string> KnownMembers(Type type, bool includeDiscriminator = true)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (includeDiscriminator)
            names.Add(DiscriminatorName);

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(
                         System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.DeclaredOnly))
            {
                var explicitName = property
                    .GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: true)
                    .Cast<JsonPropertyNameAttribute>()
                    .FirstOrDefault();

                names.Add(explicitName?.Name ?? SerializerOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name);
            }
        }

        return names;
    }

    /// <summary>The discriminator property every element carries. It names a type, not a field.</summary>
    private const string DiscriminatorName = "type";

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
