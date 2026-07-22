namespace KitX.WorkflowV6.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowSerializer — bidirectional Workflow ↔ JSON.
//
// Inherited concept from KitX.WorkflowIR.Serialization.IrSerializer: serialise the
// structured IR to JSON via a DTO layer (so the wire format is explicit and decoupled
// from the immutable model) and deserialise back. Used by the on-disk workflow file
// format (KitX.FileFormats) and by the Dashboard's storage service.
//
// JSON conventions match v5: PascalCase, WriteIndented for human-readable files.
// Fingerprint is unwrapped to its Value string; Annotation payloads are flattened.
//
// Phase 7 uses System.Text.Json polymorphic serialization via [JsonPolymorphic] +
// [JsonDerivedType] attributes on KsNode and Statement — the discriminant is the
// "$kind" property emitted by System.Text.Json's polymorphic mode. Each statement
// carries its StatementKind as well, for wire-stable dispatch.
//
// A version field ("v6.0") is written at the top of every serialised document so
// future migration logic can detect the IR version.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serialises and deserialises <see cref="Workflow"/> to/from JSON.</summary>
public static class WorkflowSerializer
{
    private const string Version = "v6.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // No PropertyNamingPolicy: PascalCase is the C# default (properties keep their names).
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new FingerprintJsonConverter(), new AnnotationValueJsonConverter(), new KsLiteralValueConverter() },
    };

    /// <summary>Serialises a <see cref="Workflow"/> to an indented JSON string.</summary>
    public static string Serialize(Workflow ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var doc = new WorkflowDocument
        {
            Version = Version,
            Body = [.. ir.Body],
            Constants = ir.Constants.Values,
            GlobalVars = ir.GlobalVars.Values,
            HelperFunctions = [.. ir.HelperFunctions],
            Annotations = [.. ir.Annotations],
        };
        return JsonSerializer.Serialize(doc, Options);
    }

    /// <summary>Deserialises a JSON string into a <see cref="Workflow"/>.</summary>
    public static Workflow Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var doc = JsonSerializer.Deserialize<WorkflowDocument>(json, Options)
            ?? throw new JsonException("Failed to deserialize Workflow document.");
        var constants = (doc.Constants ?? []).ToImmutableDictionary(c => c.Name);
        var globalVars = (doc.GlobalVars ?? []).ToImmutableDictionary(g => g.Name);
        return new Workflow
        {
            Body = [.. (doc.Body ?? [])],
            Constants = constants,
            GlobalVars = globalVars,
            HelperFunctions = [.. (doc.HelperFunctions ?? [])],
            Annotations = [.. (doc.Annotations ?? [])],
        };
    }
}

/// <summary>The top-level serialisation envelope. Carries the version + the IR content.</summary>
internal sealed class WorkflowDocument
{
    public string Version { get; set; } = "v6.0";
    public List<Statement> Body { get; set; } = [];
    public IEnumerable<Constant>? Constants { get; set; }
    public IEnumerable<GlobalVar>? GlobalVars { get; set; }
    public List<HelperFunction> HelperFunctions { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
}

// ── JSON converters for value types that don't serialize natively ──

/// <summary>Serialises Fingerprint as a bare string (its Value), not a nested object.</summary>
internal sealed class FingerprintJsonConverter : JsonConverter<Fingerprint>
{
    public override Fingerprint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, Fingerprint value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// <summary>
/// Serialises the object-typed Value of a KsLiteral. System.Text.Json would otherwise
/// round-trip it as a JsonElement (breaking equality with the original boxed value).
/// This converter handles the common literal types: string, int, double, bool, char.
/// Public so the [JsonConverter] attribute on KsLiteral.Value can reference it.
/// </summary>
public sealed class KsLiteralValueConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt32(out var i) ? (object)i
                                 : reader.TryGetInt64(out var l) ? (object)l
                                 : reader.GetDouble(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone(),
        };
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case double d: writer.WriteNumberValue(d); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case char c: writer.WriteStringValue(c.ToString()); break;
            default: JsonSerializer.Serialize(writer, value, value.GetType(), options); break;
        }
    }
}

/// <summary>
/// Serialises AnnotationValue with a discriminator tag so the kind survives round-trip.
/// Emits as { "kind": "Layout", "x": 1, "y": 2 } etc.
/// </summary>
internal sealed class AnnotationValueJsonConverter : JsonConverter<AnnotationValue>
{
    public override AnnotationValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Kind", out var kindEl))
            return new AnnotationValue();
        var kindStr = kindEl.GetString();
        return kindStr switch
        {
            "Layout" => new AnnotationValue { AnnotationKind = AnnotationKind.Layout, X = root.GetProperty("X").GetDouble(), Y = root.GetProperty("Y").GetDouble() },
            "Text" => new AnnotationValue { AnnotationKind = AnnotationKind.Text, Text = root.GetProperty("Text").GetString() },
            "Int" => new AnnotationValue { AnnotationKind = AnnotationKind.Int, IntValue = root.GetProperty("IntValue").GetInt32() },
            "Bool" => new AnnotationValue { AnnotationKind = AnnotationKind.Bool, BoolValue = root.GetProperty("BoolValue").GetBoolean() },
            _ => new AnnotationValue(),
        };
    }

    public override void Write(Utf8JsonWriter writer, AnnotationValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Kind", value.AnnotationKind.ToString());
        switch (value.AnnotationKind)
        {
            case AnnotationKind.Layout:
                writer.WriteNumber("X", value.X);
                writer.WriteNumber("Y", value.Y);
                break;
            case AnnotationKind.Text:
                writer.WriteString("Text", value.Text ?? string.Empty);
                break;
            case AnnotationKind.Int:
                writer.WriteNumber("IntValue", value.IntValue);
                break;
            case AnnotationKind.Bool:
                writer.WriteBoolean("BoolValue", value.BoolValue);
                break;
        }
        writer.WriteEndObject();
    }
}