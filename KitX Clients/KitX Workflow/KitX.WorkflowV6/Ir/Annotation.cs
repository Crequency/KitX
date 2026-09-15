namespace KitX.WorkflowV6.Ir;

using System.Text.Json.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// Annotation — view/render metadata attached to a Statement or a Workflow.
//
// Ported design from archived v5.1 KitX.WorkflowIR.IrAnnotation: view state (canvas position,
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

/// <summary>The payload of an <see cref="Annotation"/>. Abstract base for the discriminated union.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(LayoutValue), "Layout")]
[JsonDerivedType(typeof(TextValue), "Text")]
[JsonDerivedType(typeof(IntValue), "Int")]
[JsonDerivedType(typeof(BoolValue), "Bool")]
public abstract record AnnotationValue
{
    /// <summary>Which concrete variant this value is. Derived from the runtime type.</summary>
    [JsonIgnore]
    public abstract AnnotationKind Kind { get; }

    /// <summary>Convenience factory for a Layout annotation payload.
    /// 零生产者（生产代码无 Layout annotation 产出），T5 位置持久化立项预留——勿删勿改。
    /// Hidden from IntelliSense until T5 lands.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static LayoutValue Layout(double x, double y) => new(x, y);

    /// <summary>Convenience factory for a Text annotation payload.
    /// 零生产者（生产代码无 Text annotation 产出），T5 位置持久化立项预留——勿删勿改。
    /// Hidden from IntelliSense until T5 lands.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static TextValue TextValue(string text) => new(text);

    /// <summary>Convenience factory for an Int annotation payload.
    /// 零生产者（生产代码无 Int annotation 产出），T5 位置持久化立项预留——勿删勿改。
    /// Hidden from IntelliSense until T5 lands.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static IntValue IntValueOf(int value) => new(value);

    /// <summary>Convenience factory for a Bool annotation payload (debug highlights).
    /// 零生产者（生产代码无 Bool annotation 产出），T5 位置持久化立项预留——勿删勿改。
    /// Hidden from IntelliSense until T5 lands.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BoolValue BoolValueOf(bool value) => new(value);
}

/// <summary>Layout (canvas position) payload — <see cref="AnnotationKind.Layout"/>.</summary>
public sealed record LayoutValue(double X, double Y) : AnnotationValue
{
    [JsonIgnore]
    public override AnnotationKind Kind => AnnotationKind.Layout;
}

/// <summary>Text payload — <see cref="AnnotationKind.Text"/>.</summary>
public sealed record TextValue(string Text) : AnnotationValue
{
    [JsonIgnore]
    public override AnnotationKind Kind => AnnotationKind.Text;
}

/// <summary>Integer payload — <see cref="AnnotationKind.Int"/>.</summary>
public sealed record IntValue(int Value) : AnnotationValue
{
    [JsonIgnore]
    public override AnnotationKind Kind => AnnotationKind.Int;
}

/// <summary>Boolean payload — <see cref="AnnotationKind.Bool"/>.</summary>
public sealed record BoolValue(bool Value) : AnnotationValue
{
    [JsonIgnore]
    public override AnnotationKind Kind => AnnotationKind.Bool;
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