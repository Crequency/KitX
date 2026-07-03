using System.Text.Json;
using KitX.Workflow.Ir;

namespace KitX.Workflow.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// IrSerializer — bidirectional IR ↔ JSON via the IrDto layer.
//
// Two public entry points:
//   • Serialize(IrWorkflow)  → JSON string (DTO indirection makes the wire format
//                              explicit and decoupled from the immutable model).
//   • Deserialize(string)    → IrWorkflow
//
// The conversions are total: every IR field maps to a DTO field and back. The two
// legacy defects are closed by construction — PipelineStmtDto.Segments/Arguments
// carry the full AST, and AnnotationDto (Layout kind, per-node Key) carries every
// node coordinate.
//
// JSON options: PascalCase (matching the legacy CfgDto style), WriteIndented for
// human-readable .kcs files. IrFingerprint is serialised as its Value string (via
// a custom converter) to keep the wire format clean.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serializes and deserializes <see cref="IrWorkflow"/> to/from JSON.</summary>
public static class IrSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        // Default PascalCase property naming — matches the legacy CfgDto style.
        // No custom converters are needed: IR ↔ JSON always goes through the DTO
        // layer, where IrFingerprint is already unwrapped to a plain string and
        // IrAnnotation.Value is flattened to explicit AnnotationDto fields.
    };

    /// <summary>Serializes an <see cref="IrWorkflow"/> to an indented JSON string.</summary>
    public static string Serialize(IrWorkflow ir) =>
        JsonSerializer.Serialize(ToDto(ir), s_options);

    /// <summary>Deserializes a JSON string into an <see cref="IrWorkflow"/>.</summary>
    public static IrWorkflow Deserialize(string json) =>
        FromDto(JsonSerializer.Deserialize<IrDto>(json, s_options)
                ?? throw new JsonException("Failed to deserialize IrDto: root was null."));

    // ───────────────────────────────────────────────────────────────────────────
    // IR → DTO
    // ───────────────────────────────────────────────────────────────────────────

    internal static IrDto ToDto(IrWorkflow ir) => new()
    {
        Version = "6.0",
        MainBlockName = ir.MainBlockName,
        Blocks = ir.Blocks.Select(ToDto).ToList(),
        Constants = ir.Constants.Values.Select(ToDto).ToList(),
        GlobalVars = ir.GlobalVars.Values.Select(ToDto).ToList(),
        HelperFunctions = ir.HelperFunctions.ToList(),
    };

    private static BlockDto ToDto(IrBlock block) => new()
    {
        Name = block.Name,
        Kind = block.Kind.ToString(),
        Statements = block.Statements.Select(ToStmtDto).ToList(),
        BlockVars = block.BlockVars.Select(ToDto).ToList(),
        Successors = block.Successors.Select(ToDto).ToList(),
        HasExplicitBlockBody = block.HasExplicitBlockBody,
        Annotations = block.Annotations.Select(ToDto).ToList(),
    };

    private static StmtDto ToStmtDto(IrStatement stmt) => stmt switch
    {
        IrPipelineStatement pipe => new StmtDto
        {
            Fingerprint = pipe.Fingerprint.Value,
            Comment = pipe.Comment,
            SourceLine = pipe.SourceLine,
            Kind = "Pipeline",
            Sources = pipe.Sources.ToList(),
            Segments = pipe.Segments.Select(ToDto).ToList(),
        },
        IrControlFlowStatement cf => new StmtDto
        {
            Fingerprint = cf.Fingerprint.Value,
            Comment = cf.Comment,
            SourceLine = cf.SourceLine,
            Kind = "ControlFlow",
            Op = cf.Op.ToString(),
            FunctionName = cf.FunctionName,
            FullFunctionName = cf.FullFunctionName,
            Arguments = cf.Arguments.ToList(),
            Targets = cf.Targets.Select(ToDto).ToList(),
        },
        _ => throw new InvalidOperationException(
            $"Unknown IrStatement subtype: {stmt.GetType().Name}"),
    };

    private static SegmentDto ToDto(IrSegment seg) => new()
    {
        Kind = seg.Kind.ToString(),
        FunctionName = seg.FunctionName,
        FullFunctionName = seg.FullFunctionName,
        VariableName = seg.VariableName,
        Arguments = seg.Arguments.Select(ToDto).ToList(),
    };

    private static PipelineArgDto ToDto(IrPipelineArgument arg) => new()
    {
        Kind = arg.Kind.ToString(),
        PlaceholderIndex = arg.PlaceholderIndex,
        Literal = arg.Literal,
    };

    private static ControlFlowTargetDto ToDto(IrControlFlowTarget t) => new()
    {
        PinName = t.PinName,
        TargetBlockName = t.TargetBlockName,
    };

    private static EdgeDto ToDto(IrEdge edge) => new()
    {
        FromBlockName = edge.FromBlockName,
        ToBlockName = edge.ToBlockName,
        Type = edge.Type.ToString(),
        PinName = edge.PinName,
    };

    private static ConstDto ToDto(IrConstant c) => new()
    {
        Name = c.Name,
        Type = c.Type,
        InitialValueExpression = c.InitialValueExpression,
        DefaultValue = c.DefaultValue,
    };

    private static GlobalVarDto ToDto(IrGlobalVar g) => new()
    {
        Name = g.Name,
        Type = g.Type,
        InitialValueExpression = g.InitialValueExpression,
        DefaultValue = g.DefaultValue,
    };

    private static BlockVarDto ToDto(IrBlockVar v) => new()
    {
        Name = v.Name,
        Type = v.Type,
        InitialValueExpression = v.InitialValueExpression,
    };

    /// <summary>
    /// Flattens an <see cref="IrAnnotation"/>'s polymorphic <c>Value</c> into the
    /// flat <see cref="AnnotationDto"/> fields, keyed by Kind. The reverse mapping is
    /// <see cref="FromDto(AnnotationDto)"/>.
    /// </summary>
    private static AnnotationDto ToDto(IrAnnotation ann)
    {
        var dto = new AnnotationDto
        {
            Kind = ann.Kind.ToString(),
            Key = ann.Key,
        };
        return ann.Kind switch
        {
            AnnotationKind.Layout => ann.Value switch
            {
                IrLayout layout => dto with { LayoutX = layout.X, LayoutY = layout.Y },
                // Tolerant: a Layout annotation without an IrLayout value still round-trips
                // its Key/Kind; coordinates simply stay null.
                _ => dto,
            },
            AnnotationKind.Comment => dto with
            {
                Text = ann.Value as string ?? ann.Value?.ToString(),
            },
            AnnotationKind.SourcePosition => ann.Value switch
            {
                IrSourcePosition pos => dto with { Line = pos.Line, Column = pos.Column },
                _ => dto,
            },
            _ => dto,
        };
    }

    // ───────────────────────────────────────────────────────────────────────────
    // DTO → IR
    // ───────────────────────────────────────────────────────────────────────────

    internal static IrWorkflow FromDto(IrDto dto) => new()
    {
        MainBlockName = dto.MainBlockName,
        Blocks = dto.Blocks.Select(FromDto).ToImmutableArray(),
        Constants = dto.Constants.Select(FromDto).ToImmutableDictionary(c => c.Name),
        GlobalVars = dto.GlobalVars.Select(FromDto).ToImmutableDictionary(g => g.Name),
        HelperFunctions = dto.HelperFunctions.ToImmutableArray(),
    };

    private static IrBlock FromDto(BlockDto block) => new()
    {
        Name = block.Name,
        Kind = ParseEnum<IrBlockKind>(block.Kind, IrBlockKind.Basic),
        Statements = block.Statements.Select(FromStmtDto).ToImmutableArray(),
        BlockVars = block.BlockVars.Select(FromDto).ToImmutableArray(),
        Successors = block.Successors.Select(FromDto).ToImmutableArray(),
        HasExplicitBlockBody = block.HasExplicitBlockBody,
        Annotations = block.Annotations.Select(FromDto).ToImmutableArray(),
    };

    private static IrStatement FromStmtDto(StmtDto stmt)
    {
        var fingerprint = new IrFingerprint(stmt.Fingerprint);
        return stmt.Kind switch
        {
            "Pipeline" => (IrStatement)new IrPipelineStatement
            {
                Fingerprint = fingerprint,
                Comment = stmt.Comment,
                SourceLine = stmt.SourceLine,
                Sources = stmt.Sources.ToImmutableArray(),
                Segments = stmt.Segments.Select(FromDto).ToImmutableArray(),
            },
            "ControlFlow" => new IrControlFlowStatement
            {
                Fingerprint = fingerprint,
                Comment = stmt.Comment,
                SourceLine = stmt.SourceLine,
                Op = ParseEnum<ControlFlowOp>(stmt.Op, ControlFlowOp.Branch),
                FunctionName = stmt.FunctionName ?? string.Empty,
                FullFunctionName = stmt.FullFunctionName,
                Arguments = stmt.Arguments.ToImmutableArray(),
                Targets = stmt.Targets.Select(FromDto).ToImmutableArray(),
            },
            _ => throw new InvalidOperationException(
                $"Unknown StmtDto Kind: {stmt.Kind}"),
        };
    }

    private static IrSegment FromDto(SegmentDto seg) => new()
    {
        Kind = ParseEnum<IrSegmentKind>(seg.Kind, IrSegmentKind.FunctionCall),
        FunctionName = seg.FunctionName,
        FullFunctionName = seg.FullFunctionName,
        VariableName = seg.VariableName,
        Arguments = seg.Arguments.Select(FromDto).ToImmutableArray(),
    };

    private static IrPipelineArgument FromDto(PipelineArgDto arg) => new()
    {
        Kind = ParseEnum<IrPipelineArgumentKind>(arg.Kind, IrPipelineArgumentKind.Literal),
        PlaceholderIndex = arg.PlaceholderIndex,
        Literal = arg.Literal,
    };

    private static IrControlFlowTarget FromDto(ControlFlowTargetDto t) =>
        new(t.PinName, t.TargetBlockName);

    private static IrEdge FromDto(EdgeDto edge) => new(
        edge.FromBlockName,
        edge.ToBlockName,
        ParseEnum<IrEdgeType>(edge.Type, IrEdgeType.Sequential),
        edge.PinName);

    private static IrConstant FromDto(ConstDto c) => new(
        c.Name,
        c.Type,
        c.InitialValueExpression,
        c.DefaultValue);

    private static IrGlobalVar FromDto(GlobalVarDto g) => new(
        g.Name,
        g.Type,
        g.InitialValueExpression,
        g.DefaultValue);

    private static IrBlockVar FromDto(BlockVarDto v) => new(
        v.Name,
        v.Type,
        v.InitialValueExpression);

    /// <summary>
    /// Rebuilds an <see cref="IrAnnotation"/> from the flat DTO fields, reconstructing
    /// the polymorphic Value (IrLayout / string / IrSourcePosition) per Kind.
    /// </summary>
    private static IrAnnotation FromDto(AnnotationDto dto)
    {
        var kind = ParseEnum<AnnotationKind>(dto.Kind, AnnotationKind.Layout);
        object? value = kind switch
        {
            AnnotationKind.Layout
                when dto.LayoutX.HasValue && dto.LayoutY.HasValue
                => new IrLayout(dto.LayoutX.Value, dto.LayoutY.Value),
            AnnotationKind.Comment => dto.Text,
            AnnotationKind.SourcePosition
                when dto.Line.HasValue && dto.Column.HasValue
                => new IrSourcePosition(dto.Line.Value, dto.Column.Value),
            _ => null,
        };
        return new IrAnnotation(kind, dto.Key, value);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Small parsing/conversion helpers
    // ───────────────────────────────────────────────────────────────────────────

    private static T ParseEnum<T>(string? text, T fallback) where T : struct =>
        Enum.TryParse(text, ignoreCase: true, out T v) ? v : fallback;
}
