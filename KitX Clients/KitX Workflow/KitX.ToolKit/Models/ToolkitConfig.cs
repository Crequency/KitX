using KitX.ToolKit.Validation;

namespace KitX.ToolKit.Models;

/// <summary>
/// Convenience facade over config serialization + validation. The config document
/// (<see cref="Toolkit"/>) is the single source of truth; this is the one entry
/// point a host / Agent tooling uses to read, write and vet a ToolKit config.
/// </summary>
public static class ToolkitConfig
{
    /// <summary>Serializes a <see cref="Toolkit"/> to a JSON document (see <see cref="ToolkitConfigSerializer"/>).</summary>
    public static string Serialize(Toolkit toolkit) => ToolkitConfigSerializer.Serialize(toolkit);

    /// <summary>Deserializes a <see cref="Toolkit"/> from a JSON document. Returns null on malformed JSON.</summary>
    public static Toolkit? Deserialize(string json) => ToolkitConfigSerializer.Deserialize(json);

    /// <summary>Validates a <see cref="Toolkit"/> (identity, references, strict DAG).</summary>
    public static ConfigValidationResult Validate(Toolkit toolkit) => new ConfigValidator().Validate(toolkit);
}
