using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Serialization;
using Xunit;

namespace KitX.WorkflowIR.Test.Xunit;

/// <summary>
/// Verifies the IR JSON serialization layer (IrDto + IrSerializer) round-trips a
/// complex workflow losslessly. These tests are the direct regression guard for the
/// two defects fixed relative to the legacy CfgDto:
///
///   1. Pipeline AST completeness — Sources + Segments + per-segment Arguments
///      (Placeholder/Literal) survive Serialize→Deserialize. (Legacy CfgDto stored
///      only PipelineSource text.)
///   2. Per-node positions — every node's (X, Y) survives via Annotations keyed by
///      the node's fingerprint/stableId. (Legacy CfgDto serialized only the block's
///      LayoutX/LayoutY.)
/// </summary>
public class IrModelRoundTripTests
{
    // ── Structural round-trip: a complex workflow survives intact. ──

    [Fact]
    public void Serialize_Deserialize_PreservesComplexWorkflow()
    {
        var original = MakeComplexWorkflow();
        var json = IrSerializer.Serialize(original);
        var roundTripped = IrSerializer.Deserialize(json);

        // IrWorkflow/IrBlock override Equals to SequenceEqual their collections, so
        // Assert.Equal works at those levels. But the statement sub-records
        // (IrPipelineStatement, IrSegment, ...) do NOT override Equals, and the
        // synthesised record equality for an ImmutableArray field is *reference*
        // equality — so two structurally-identical statements compare unequal. We
        // therefore compare the whole workflow with StructuralEquals, which
        // SequenceCompares every ImmutableArray and JSON-normalises object? values.
        Assert.True(StructuralEquals(original, roundTripped),
            "Round-tripped workflow is not structurally equal to the original.");
    }

    // ── Defect #1 fix: Pipeline AST completeness. ──

    [Fact]
    public void PipelineAst_SurvivesRoundTrip_SourcesSegmentsArguments()
    {
        var original = MakeComplexWorkflow();
        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(original));

        // Find the pipeline statement in the entry block (it's the only non-terminator).
        var origPipe = (IrPipelineStatement)original.EntryBlock.Statements[0];
        var rtPipe = (IrPipelineStatement)roundTripped.EntryBlock.Statements[0];

        // Sources preserved verbatim (SequenceEqual: ImmutableArray default Equals
        // is reference equality, so use SequenceEqual for content comparison).
        Assert.True(origPipe.Sources.SequenceEqual(rtPipe.Sources));
        Assert.Equal(2, rtPipe.Sources.Length);
        Assert.Equal("Get(x)", rtPipe.Sources[0]);
        Assert.Equal("42", rtPipe.Sources[1]);

        // Two segments preserved.
        Assert.Equal(2, rtPipe.Segments.Length);

        // Segment 0: FunctionCall "Filter" with a Placeholder + a Literal argument.
        var seg0 = rtPipe.Segments[0];
        Assert.Equal(IrSegmentKind.FunctionCall, seg0.Kind);
        Assert.Equal("Filter", seg0.FunctionName);
        Assert.Equal(2, seg0.Arguments.Length);
        Assert.Equal(IrPipelineArgumentKind.Placeholder, seg0.Arguments[0].Kind);
        Assert.Equal(0, seg0.Arguments[0].PlaceholderIndex);   // first pipeline input slot
        Assert.Equal(IrPipelineArgumentKind.Literal, seg0.Arguments[1].Kind);
        Assert.Equal("\"fast\"", seg0.Arguments[1].Literal);

        // Segment 1: Variable tap (terminal `> result`).
        var seg1 = rtPipe.Segments[1];
        Assert.Equal(IrSegmentKind.Variable, seg1.Kind);
        Assert.Equal("result", seg1.VariableName);
    }

    // ── Defect #2 fix: per-node positions. ──

    [Fact]
    public void PerNodePositions_SurviveRoundTrip()
    {
        var original = MakeComplexWorkflow();
        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(original));

        // The pipeline statement's node position is stored as a Layout annotation
        // keyed by the statement's fingerprint value (the per-node identity key).
        var pipe = (IrPipelineStatement)original.EntryBlock.Statements[0];
        var nodeKey = pipe.Fingerprint.Value;

        var rtBlock = roundTripped.EntryBlock;
        var layout = rtBlock.Annotations
            .Single(a => a.Kind == AnnotationKind.Layout && a.Key == nodeKey);

        Assert.NotNull(layout.Value);
        var coords = (IrLayout)layout.Value!;
        Assert.Equal(120.5, coords.X);
        Assert.Equal(240.0, coords.Y);
    }

    [Fact]
    public void BlockAnchorPosition_SurvivesRoundTrip()
    {
        var original = MakeComplexWorkflow();
        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(original));

        var anchor = roundTripped.EntryBlock.Annotations
            .Single(a => a.Kind == AnnotationKind.Layout && a.Key == "BlockPos");
        var coords = (IrLayout)anchor.Value!;
        Assert.Equal(10.0, coords.X);
        Assert.Equal(20.0, coords.Y);
    }

    // ── Control-flow Targets completeness. ──

    [Fact]
    public void ControlFlow_Targets_SurviveRoundTrip()
    {
        var original = MakeComplexWorkflow();
        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(original));

        // The BranchHeader block's terminator is the Branch control-flow statement.
        var branchBlock = roundTripped.GetBlock("TrueBlock")!;
        var branch = branchBlock.Statements
            .OfType<IrControlFlowStatement>()
            .Single(s => s.Op == ControlFlowOp.Branch);

        Assert.Equal("Branch", branch.FunctionName);
        Assert.True(branch.Arguments.SequenceEqual(new[] { "cond" }));
        Assert.Equal(2, branch.Targets.Length);
        Assert.Equal("True", branch.Targets[0].PinName);
        Assert.Equal("DoneTrue", branch.Targets[0].TargetBlockName);
        Assert.Equal("False", branch.Targets[1].PinName);
        Assert.Equal("DoneFalse", branch.Targets[1].TargetBlockName);
    }

    // ── Annotations of every Kind round-trip. ──

    [Fact]
    public void AllAnnotationKinds_SurviveRoundTrip()
    {
        var original = MakeComplexWorkflow();
        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(original));

        var anns = roundTripped.EntryBlock.Annotations;

        // Layout (block anchor) — already asserted above; here check the set.
        Assert.Contains(anns, a => a.Kind == AnnotationKind.Layout && a.Key == "BlockPos");
        // Comment annotation.
        var comment = anns.Single(a => a.Kind == AnnotationKind.Comment);
        Assert.Equal("block-note", comment.Key);
        Assert.Equal("this block does X", comment.Value);
        // SourcePosition annotation.
        var pos = anns.Single(a => a.Kind == AnnotationKind.SourcePosition);
        Assert.Equal("block-note", pos.Key);
        var sp = (IrSourcePosition)pos.Value!;
        Assert.Equal(3, sp.Line);
        Assert.Equal(5, sp.Column);
    }

    // ── Constants / GlobalVars round-trip. ──

    [Fact]
    public void ConstantsAndGlobals_SurviveRoundTrip()
    {
        var original = MakeComplexWorkflow();
        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(original));

        Assert.True(roundTripped.Constants.ContainsKey("Pi"));
        // DefaultValue is `object?`; System.Text.Json deserialises it to a JsonElement,
        // so compare by JSON-normalised value rather than boxed-type equality.
        AssertJsonEqual(3.14, roundTripped.Constants["Pi"].DefaultValue);
        Assert.Equal("double", roundTripped.Constants["Pi"].Type);

        Assert.True(roundTripped.GlobalVars.ContainsKey("Counter"));
        Assert.Equal("int", roundTripped.GlobalVars["Counter"].Type);
        AssertJsonEqual(0, roundTripped.GlobalVars["Counter"].DefaultValue);
    }

    [Fact]
    public void Serialize_ProducesVersion_6_0()
    {
        var json = IrSerializer.Serialize(MakeComplexWorkflow());
        Assert.Contains("\"Version\": \"6.0\"", json);
    }

    // ── A simpler sanity check: empty-ish workflow round-trips. ──

    [Fact]
    public void MinimalWorkflow_RoundTrips()
    {
        var minimal = new IrWorkflow
        {
            MainBlockName = "#MainBlock",
            Blocks =
            [
                new IrBlock
                {
                    Name = "#MainBlock",
                    Kind = IrBlockKind.Entry,
                },
            ],
        };

        var roundTripped = IrSerializer.Deserialize(IrSerializer.Serialize(minimal));
        Assert.Equal(minimal, roundTripped);
    }

    // ── Structural comparison helpers ──
    //
    // IrWorkflow/IrBlock override Equals to SequenceEqual their ImmutableArray fields,
    // but the IR *statement/declaration sub-records* (IrPipelineStatement, IrSegment,
    // IrControlFlowStatement, IrPipelineArgument, IrEdge, IrBlockVar) do NOT override
    // Equals — and the synthesised record equality for an ImmutableArray field falls
    // back to reference equality (verified: two structurally-identical ImmutableArrays
    // compare unequal). So a raw Assert.Equal on a workflow with statements fails even
    // when the content is identical. StructuralEquals walks the tree and SequenceCompares
    // every ImmutableArray, giving true content equality without touching the Ir/ models.

    private static bool StructuralEquals(IrWorkflow? a, IrWorkflow? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.MainBlockName != b.MainBlockName) return false;
        if (a.Blocks.Length != b.Blocks.Length) return false;
        // Compare each block: IrBlock.Equals compares BlockVars/Successors/edges by
        // content, BUT its Statements.SequenceEqual delegates to the synthesised
        // IrStatement.Equals (reference equality on ImmutableArrays), so we compare
        // statements ourselves via StmtEqual.
        for (int i = 0; i < a.Blocks.Length; i++)
        {
            if (!BlockEqual(a.Blocks[i], b.Blocks[i])) return false;
        }
        if (!ConstantsEqual(a.Constants, b.Constants)) return false;
        if (!GlobalVarsEqual(a.GlobalVars, b.GlobalVars)) return false;
        return true;
    }

    private static bool BlockEqual(IrBlock a, IrBlock b)
    {
        // Semantic fields (everything IrBlock.Equals checks), plus statements compared
        // via StmtEqual (content-based) rather than the synthesised record Equals.
        if (a.Name != b.Name) return false;
        if (a.Kind != b.Kind) return false;
        if (a.HasExplicitBlockBody != b.HasExplicitBlockBody) return false;
        if (a.BlockVars.Length != b.BlockVars.Length
            || !a.BlockVars.SequenceEqual(b.BlockVars)) return false;   // IrBlockVar is scalar-only → OK
        if (a.Successors.Length != b.Successors.Length
            || !a.Successors.SequenceEqual(b.Successors)) return false; // IrEdge is positional record → OK
        if (a.Statements.Length != b.Statements.Length) return false;
        for (int i = 0; i < a.Statements.Length; i++)
            if (!StmtEqual(a.Statements[i], b.Statements[i])) return false;
        return true;
    }

    // IrConstant/IrGlobalVar carry an `object?` DefaultValue whose boxed type drifts
    // to JsonElement on deserialise; compare scalars directly and DefaultValue by JSON.
    private static bool ConstantsEqual(
        ImmutableDictionary<string, IrConstant> a,
        ImmutableDictionary<string, IrConstant> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var v2)) return false;
            if (v.Name != v2.Name || v.Type != v2.Type
                || v.InitialValueExpression != v2.InitialValueExpression) return false;
            if (!JsonEqual(v.DefaultValue, v2.DefaultValue)) return false;
        }
        return true;
    }

    private static bool GlobalVarsEqual(
        ImmutableDictionary<string, IrGlobalVar> a,
        ImmutableDictionary<string, IrGlobalVar> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var v2)) return false;
            if (v.Name != v2.Name || v.Type != v2.Type
                || v.InitialValueExpression != v2.InitialValueExpression) return false;
            if (!JsonEqual(v.DefaultValue, v2.DefaultValue)) return false;
        }
        return true;
    }

    private static bool JsonEqual(object? a, object? b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    // Recursively compares statement sub-records by content (SequenceEqual for arrays).
    private static bool StmtEqual(IrStatement? a, IrStatement? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Fingerprint != b.Fingerprint) return false;
        if (a.Comment != b.Comment) return false;
        if (a.SourceLine != b.SourceLine) return false;

        switch (a, b)
        {
            case (IrPipelineStatement pa, IrPipelineStatement pb):
                return pa.Sources.SequenceEqual(pb.Sources)
                    && SegmentsEqual(pa.Segments, pb.Segments);
            case (IrControlFlowStatement ca, IrControlFlowStatement cb):
                return ca.Op == cb.Op
                    && ca.FunctionName == cb.FunctionName
                    && ca.FullFunctionName == cb.FullFunctionName
                    && ca.Arguments.SequenceEqual(cb.Arguments)
                    && ca.Targets.SequenceEqual(cb.Targets);   // IrControlFlowTarget is positional (scalars) → OK
            default:
                return false;
        }
    }

    // IrSegment carries an ImmutableArray<IrPipelineArgument> Arguments field; since
    // IrSegment doesn't override Equals, compare it field-by-field (IrPipelineArgument
    // is scalar-only, so its record Equals is fine).
    private static bool SegmentsEqual(ImmutableArray<IrSegment> a, ImmutableArray<IrSegment> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            var sa = a[i];
            var sb = b[i];
            if (sa.Kind != sb.Kind) return false;
            if (sa.FunctionName != sb.FunctionName) return false;
            if (sa.FullFunctionName != sb.FullFunctionName) return false;
            if (sa.VariableName != sb.VariableName) return false;
            if (sa.Arguments.Length != sb.Arguments.Length) return false;
            for (int j = 0; j < sa.Arguments.Length; j++)
                if (!sa.Arguments[j].Equals(sb.Arguments[j])) return false;
        }
        return true;
    }

    /// <summary>
    /// Asserts two <c>object?</c> values are equal after JSON normalisation. Needed
    /// because <c>IrConstant.DefaultValue</c> is <c>object?</c> and System.Text.Json
    /// deserialises it to a <c>JsonElement</c> (e.g. a boxed <c>double</c> becomes a
    /// <c>JsonElement</c>), so direct equality fails. Comparing the JSON spellings is
    /// the faithful content check.
    /// </summary>
    private static void AssertJsonEqual(object? expected, object? actual)
    {
        var expectedJson = JsonSerializer.Serialize(expected);
        var actualJson = JsonSerializer.Serialize(actual);
        Assert.Equal(expectedJson, actualJson);
    }

    // ── Test fixture: a workflow exercising every serialised feature. ──

    private static IrWorkflow MakeComplexWorkflow()
    {
        // Pipeline: `Get(x), 42 > Filter(_, "fast") > result`
        //   - 2 sources
        //   - segment 0: FunctionCall "Filter" with [Placeholder(0), Literal("\"fast\"")]
        //   - segment 1: Variable tap into "result"
        var pipeline = new IrPipelineStatement
        {
            Fingerprint = IrFingerprint.Compute("Filter", ["Get(x)", "42"]),
            Comment = "the main pipeline",
            SourceLine = 1,
            Sources = ["Get(x)", "42"],
            Segments =
            [
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = "Filter",
                    Arguments =
                    [
                        IrPipelineArgument.Placeholder(0),
                        IrPipelineArgument.Lit("\"fast\""),
                    ],
                },
                new IrSegment
                {
                    Kind = IrSegmentKind.Variable,
                    VariableName = "result",
                },
            ],
        };

        var entryBlock = new IrBlock
        {
            Name = "#MainBlock",
            Kind = IrBlockKind.Entry,
            Statements = [pipeline],
            Successors =
            [
                new IrEdge("#MainBlock", "TrueBlock", IrEdgeType.Sequential, null),
            ],
            // Annotations carry BOTH the block anchor AND per-node positions (defect #2 fix),
            // plus a comment and a source position.
            Annotations =
            [
                new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(10, 20)),
                new IrAnnotation(AnnotationKind.Layout, pipeline.Fingerprint.Value,
                    new IrLayout(120.5, 240)),
                new IrAnnotation(AnnotationKind.Comment, "block-note", "this block does X"),
                new IrAnnotation(AnnotationKind.SourcePosition, "block-note",
                    new IrSourcePosition(3, 5)),
            ],
        };

        // BranchHeader block with a Branch terminator having two targets.
        var branch = new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("Branch", ["cond"]),
            Op = ControlFlowOp.Branch,
            FunctionName = "Branch",
            Arguments = ["cond"],
            Targets =
            [
                new IrControlFlowTarget("True", "DoneTrue"),
                new IrControlFlowTarget("False", "DoneFalse"),
            ],
        };

        var branchBlock = new IrBlock
        {
            Name = "TrueBlock",
            Kind = IrBlockKind.BranchHeader,
            Statements = [branch],
            Successors =
            [
                new IrEdge("TrueBlock", "DoneTrue", IrEdgeType.BranchTrue, "True"),
                new IrEdge("TrueBlock", "DoneFalse", IrEdgeType.BranchFalse, "False"),
            ],
            BlockVars =
            [
                new IrBlockVar("tmp", "int", "0"),
            ],
            HasExplicitBlockBody = true,
        };

        return new IrWorkflow
        {
            MainBlockName = "#MainBlock",
            Blocks = [entryBlock, branchBlock],
            Constants = ImmutableDictionary.CreateRange(
            [
                new KeyValuePair<string, IrConstant>("Pi",
                    new IrConstant("Pi", "double", "3.14", 3.14)),
            ]),
            GlobalVars = ImmutableDictionary.CreateRange(
            [
                new KeyValuePair<string, IrGlobalVar>("Counter",
                    new IrGlobalVar("Counter", "int", "0", 0)),
            ]),
        };
    }
}
