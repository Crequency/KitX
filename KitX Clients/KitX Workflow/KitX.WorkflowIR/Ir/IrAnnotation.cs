namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrAnnotation — view/render metadata, deliberately separated from semantic fields.
//
// The legacy CFG mixed layout coordinates (LayoutX/LayoutY/NodePositions) and
// comments directly into CFGBlock/CFGStatement. That broke structural equality:
// two IRs that are semantically identical but rendered at different canvas
// positions were unequal, and round-tripping through BS (which has no notion of
// canvas position) silently dropped coordinates.
//
// Annotations are an out-of-band channel: they never participate in record
// equality of the semantic model, but they survive IR updates that preserve
// identity (via IrFingerprint) and they serialize to .kcs so positions persist.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Kind of an <see cref="IrAnnotation"/>.</summary>
public enum AnnotationKind
{
    /// <summary>
    /// A layout coordinate (block anchor or per-statement node position). <see cref="Key"/>
    /// identifies what the coordinate belongs to; <see cref="Value"/> is an
    /// <see cref="IrLayout"/>.
    /// </summary>
    Layout,

    /// <summary>A free-form comment attached to a block or statement.</summary>
    Comment,

    /// <summary>Original source position (line/column) in the BS text.</summary>
    SourcePosition,
}

/// <summary>
/// An immutable piece of view/render metadata attached to an IR element. Annotations
/// are not part of the semantic identity of the element they decorate — they are
/// matched by <see cref="Key"/> and merged/overwritten independently of the semantic
/// record equality.
/// </summary>
public sealed record IrAnnotation(AnnotationKind Kind, string Key, object? Value)
{
    /// <summary>
    /// True when this annotation carries a layout coordinate (Kind == Layout and
    /// Value is an <see cref="IrLayout"/>).
    /// </summary>
    public bool IsLayout => Kind == AnnotationKind.Layout && Value is IrLayout;
}

/// <summary>
/// An (X, Y) layout coordinate. The unit is BP-canvas points; the origin is the
/// top-left of the editing surface. Stored inside <see cref="IrAnnotation.Value"/>.
/// </summary>
public sealed record IrLayout(double X, double Y);

/// <summary>
/// A 1-based source position (line, column) in the original BS text. Stored inside
/// <see cref="IrAnnotation.Value"/> for AnnotationKind.SourcePosition.
/// </summary>
public sealed record IrSourcePosition(int Line, int Column);
