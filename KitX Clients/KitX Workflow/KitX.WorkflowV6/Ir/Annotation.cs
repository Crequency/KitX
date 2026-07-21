namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// Annotation — view/render metadata attached to a Statement or a Workflow.
//
// Inherited design from KitX.WorkflowIR.IrAnnotation: view state (canvas position,
// collapsed state, debug highlight, source-position markers, ...) is deliberately
// separated from semantic fields so it never participates in structural equality.
// Two workflows that differ only in canvas layout are semantically equal.
//
// The Kind string identifies the annotation family (e.g. "Layout", "Comment",
// "SourceSpan"); the Key string names the specific datum inside that family
// (e.g. a stable-id for per-node positions); the Value is the payload, encoded
// as a small immutable record so it stays copyable and comparable.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A piece of view/render metadata attached to a <see cref="Statement"/> or a
/// <see cref="Workflow"/>. Excluded from semantic equality.
/// </summary>
public sealed record Annotation
{
    /// <summary>Annotation family (e.g. "Layout", "Comment", "SourceSpan").</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Specific datum key inside the family. For per-node layout this is the
    /// statement's stable id; for workflow-level annotations this may be a
    /// well-known string like "Viewport".
    /// </summary>
    public required string Key { get; init; }

    /// <summary>The payload value. Shape depends on <see cref="Kind"/>.</summary>
    public required AnnotationValue Value { get; init; }
}

/// <summary>The payload of an <see cref="Annotation"/>. Discriminated by <see cref="AnnotationKind"/>.</summary>
public sealed record AnnotationValue
{
    /// <summary>Which value facet is populated.</summary>
    public AnnotationKind AnnotationKind { get; init; } = AnnotationKind.None;

    public double X { get; init; }
    public double Y { get; init; }
    public string? Text { get; init; }
    public int IntValue { get; init; }
}

/// <summary>Discriminant for <see cref="AnnotationValue"/>.</summary>
public enum AnnotationKind
{
    None = 0,
    Layout,
    Text,
    Int,
}
