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
// "SourceSpan", "DebugHighlight"); the Key string names the specific datum inside
// that family (e.g. a stable-id for per-node positions, or "IsExecuting"/"IsBreakpoint"
// for debug highlights); the Value is the payload, encoded as a small immutable record
// so it stays copyable and comparable.
//
// v6 additions over v5:
//   • AnnotationKind.Bool — used by DebugHighlight annotations (IsExecuting /
//     IsBreakpoint / HasError) per discussion notes §十二-I (BP-side interactive
//     debugging is MVP, needs a per-statement "currently executing" flag).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A piece of view/render metadata attached to a <see cref="Statement"/> or a
/// <see cref="Workflow"/>. Excluded from semantic equality.
/// </summary>
public sealed record Annotation
{
    /// <summary>Annotation family (e.g. "Layout", "Comment", "SourceSpan", "DebugHighlight").</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Specific datum key inside the family. For per-node layout this is the
    /// statement's stable id; for workflow-level annotations this may be a
    /// well-known string like "Viewport"; for DebugHighlight this is one of
    /// "IsExecuting" / "IsBreakpoint" / "HasError".
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

    /// <summary>
    /// Boolean payload — used by DebugHighlight annotations ("IsExecuting",
    /// "IsBreakpoint", "HasError") added for the v6 BP-side interactive debugger
    /// (discussion notes §十二-I).
    /// </summary>
    public bool BoolValue { get; init; }

    /// <summary>Convenience factory for a Layout annotation payload.</summary>
    public static AnnotationValue Layout(double x, double y) =>
        new() { AnnotationKind = AnnotationKind.Layout, X = x, Y = y };

    /// <summary>Convenience factory for a Text annotation payload.</summary>
    public static AnnotationValue TextValue(string text) =>
        new() { AnnotationKind = AnnotationKind.Text, Text = text };

    /// <summary>Convenience factory for an Int annotation payload.</summary>
    public static AnnotationValue IntValueOf(int value) =>
        new() { AnnotationKind = AnnotationKind.Int, IntValue = value };

    /// <summary>Convenience factory for a Bool annotation payload (debug highlights).</summary>
    public static AnnotationValue BoolValueOf(bool value) =>
        new() { AnnotationKind = AnnotationKind.Bool, BoolValue = value };
}

/// <summary>Discriminant for <see cref="AnnotationValue"/>.</summary>
public enum AnnotationKind
{
    None = 0,
    Layout,
    Text,
    Int,
    Bool,
}