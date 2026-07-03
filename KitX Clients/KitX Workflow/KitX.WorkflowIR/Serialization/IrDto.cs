using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Ir;

namespace KitX.WorkflowIR.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// IrDto — JSON serialization DTO for the immutable WorkflowIR.
//
// The legacy CfgDto (v5.1, in KitX.Core.Contract.Workflow) had two structural
// defects that this DTO layer fixes:
//
//   1. Pipeline AST was flattened to text. CfgStmtDto exposed PipelineSource as a
//      single string — the structured `a, b > F > G > x` AST was lost, so .kcs
//      round-trip could not reproduce sources/segments/placeholders. Here,
//      PipelineStmtDto carries the full Sources + Segments (+ per-segment Arguments
//      distinguishing Placeholder vs Literal), so the AST survives verbatim.
//
//   2. Per-node positions were not serialized. CfgBlockDto stored only the block's
//      own LayoutX/LayoutY; every node coordinate was dropped on save. Here,
//      BlockDto.Annotations (Layout kind with a Key = fingerprint/stableId) carries
//      per-node (X, Y), keyed by node identity, alongside the block anchor.
//
// Statement polymorphism is avoided: StmtDto carries a Kind discriminator
// ("Pipeline" / "ControlFlow") plus flat sub-fields for both shapes, instead of a
// polymorphic JSON converter. AnnotationDto likewise carries a Kind discriminator
// with explicit fields (LayoutX/LayoutY/Line/Column/Text) rather than an
// `object?` Value, to dodge System.Text.Json polymorphic-serialization pitfalls.
//
// HelperFunction / HelperFunctionParameter come from KitX.Core.Contract.Workflow
// unchanged (they already serialise cleanly as mutable classes).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Top-level serialization DTO. <see cref="Version"/> is the DTO-schema version
/// (independent of the IR model version); bumped to "6.0" to mark the
/// AST-complete + per-node-position format that supersedes CfgDto v5.1.
/// </summary>
public sealed record IrDto
{
    public string Version { get; init; } = "6.0";
    public string MainBlockName { get; init; } = "#MainBlock";
    public List<BlockDto> Blocks { get; init; } = [];
    public List<ConstDto> Constants { get; init; } = [];
    public List<GlobalVarDto> GlobalVars { get; init; } = [];
    public List<HelperFunction> HelperFunctions { get; init; } = [];
}

/// <summary>Block DTO. Annotations is the single channel for block + per-node layout.</summary>
public sealed record BlockDto
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "Basic"; // Entry | Basic | BranchHeader
    public List<StmtDto> Statements { get; init; } = [];
    public List<BlockVarDto> BlockVars { get; init; } = [];
    public List<EdgeDto> Successors { get; init; } = [];
    public bool HasExplicitBlockBody { get; init; }
    public List<AnnotationDto> Annotations { get; init; } = [];
}

/// <summary>
/// Statement DTO with a Kind discriminator ("Pipeline" | "ControlFlow") so the JSON
/// is flat and self-describing without polymorphic converters. Pipeline-specific
/// fields (Sources/Segments) and control-flow fields (Op/Arguments/Targets) are both
/// present; only the ones implied by Kind are populated.
/// </summary>
public sealed record StmtDto
{
    /// <summary>Fingerprint value (content-derived identity; see IrFingerprint).</summary>
    public string Fingerprint { get; init; } = "";

    public string? Comment { get; init; }
    public int SourceLine { get; init; }

    /// <summary>Discriminator: "Pipeline" or "ControlFlow".</summary>
    public string Kind { get; init; } = "Pipeline";

    // ── Pipeline shape (Kind == "Pipeline") ──

    /// <summary>Verbatim source expressions (left of the first `>`). Fixes defect #1.</summary>
    public List<string> Sources { get; init; } = [];

    /// <summary>Ordered pipeline segments. Fixes defect #1 (was a single text string in CfgDto).</summary>
    public List<SegmentDto> Segments { get; init; } = [];

    // ── Control-flow shape (Kind == "ControlFlow") ──

    /// <summary>Control-flow builtin: Branch | ForLoop | Switch | Goto | Break | Exit.</summary>
    public string? Op { get; init; }
    public string? FunctionName { get; init; }
    public string? FullFunctionName { get; init; }
    public List<string> Arguments { get; init; } = [];
    public List<ControlFlowTargetDto> Targets { get; init; } = [];
}

/// <summary>Pipeline statement shape, isolated for clarity/readability of the DTO map.</summary>
public sealed record PipelineStmtDto
{
    public string Fingerprint { get; init; } = "";
    public string? Comment { get; init; }
    public int SourceLine { get; init; }
    public List<string> Sources { get; init; } = [];
    public List<SegmentDto> Segments { get; init; } = [];
}

/// <summary>Control-flow statement shape, isolated for clarity/readability of the DTO map.</summary>
public sealed record ControlFlowStmtDto
{
    public string Fingerprint { get; init; } = "";
    public string? Comment { get; init; }
    public int SourceLine { get; init; }
    public string? Op { get; init; }
    public string? FunctionName { get; init; }
    public string? FullFunctionName { get; init; } = null;
    public List<string> Arguments { get; init; } = [];
    public List<ControlFlowTargetDto> Targets { get; init; } = [];
}

/// <summary>
/// One atomic pipeline segment: a function call (FunctionName set, Arguments may hold
/// Placeholders) or a variable tap (VariableName set, terminal `> var` form).
/// </summary>
public sealed record SegmentDto
{
    public string Kind { get; init; } = "FunctionCall"; // FunctionCall | Variable
    public string? FunctionName { get; init; }
    public string? FullFunctionName { get; init; }
    public string? VariableName { get; init; }
    public List<PipelineArgDto> Arguments { get; init; } = [];
}

/// <summary>
/// One argument slot of a segment: a Placeholder (`_`, matched by PlaceholderIndex
/// to a pipeline value) or a Literal (verbatim argument text).
/// </summary>
public sealed record PipelineArgDto
{
    public string Kind { get; init; } = "Literal"; // Placeholder | Literal
    public int PlaceholderIndex { get; init; }
    public string? Literal { get; init; }
}

/// <summary>An outgoing control-flow target: (pin name, target block).</summary>
public sealed record ControlFlowTargetDto
{
    public string PinName { get; init; } = "";
    public string TargetBlockName { get; init; } = "";
}

/// <summary>A typed control-flow edge between two blocks.</summary>
public sealed record EdgeDto
{
    public string FromBlockName { get; init; } = "";
    public string ToBlockName { get; init; } = "";
    public string Type { get; init; } = "Sequential";
    public string? PinName { get; init; }
}

public sealed record ConstDto
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
    public object? DefaultValue { get; init; }
}

public sealed record GlobalVarDto
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
    public object? DefaultValue { get; init; }
}

public sealed record BlockVarDto
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
}

/// <summary>
/// View/render metadata attached to an IR element, serialised flat. Kind discriminates
/// the payload fields:
///   • Layout          → Key (= "BlockPos" or a node stableId/fingerprint) + LayoutX/LayoutY
///   • Comment         → Key + Text
///   • SourcePosition  → Key + Line/Column
/// This replaces IrAnnotation's `object? Value` so System.Text.Json needs no polymorphic
/// converter.
/// </summary>
public sealed record AnnotationDto
{
    public string Kind { get; init; } = "Layout"; // Layout | Comment | SourcePosition
    public string Key { get; init; } = "";

    // Layout payload
    public double? LayoutX { get; init; }
    public double? LayoutY { get; init; }

    // SourcePosition payload
    public int? Line { get; init; }
    public int? Column { get; init; }

    // Comment payload
    public string? Text { get; init; }
}

/// <summary>An (X, Y) coordinate, used where a pure positional value is wanted.</summary>
public sealed record PositionDto
{
    public double X { get; init; }
    public double Y { get; init; }
}
