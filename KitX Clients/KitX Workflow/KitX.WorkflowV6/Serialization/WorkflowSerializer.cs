namespace KitX.WorkflowV6.Serialization;

using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowSerializer — bidirectional Workflow ↔ JSON (placeholder).
//
// Inherited concept from KitX.WorkflowIR.Serialization.IrSerializer: serialise the
// structured IR to JSON via a DTO layer (so the wire format is explicit and decoupled
// from the immutable model) and deserialise back. Used by the on-disk workflow file
// format (KitX.FileFormats) and by the Dashboard's storage service.
//
// JSON conventions will match v5: PascalCase, WriteIndented for human-readable files.
// Fingerprint is unwrapped to its Value string; Annotation payloads are flattened.
//
// Method bodies are NotImplemented pending the implementation plan.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serialises and deserialises <see cref="Workflow"/> to/from JSON.</summary>
public static class WorkflowSerializer
{
    /// <summary>Serialises a <see cref="Workflow"/> to an indented JSON string.</summary>
    public static string Serialize(Workflow ir) =>
        throw new NotImplementedException("WorkflowSerializer.Serialize: v6 DTO layer not implemented.");

    /// <summary>Deserialises a JSON string into a <see cref="Workflow"/>.</summary>
    public static Workflow Deserialize(string json) =>
        throw new NotImplementedException("WorkflowSerializer.Deserialize: v6 DTO layer not implemented.");
}
