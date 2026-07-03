using System.Collections.Immutable;
using KitX.Workflow.Ir;
using KitX.Workflow.Util;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Verifies the invariants of the immutable IR model: record equality, immutability,
/// fingerprint content-derivation + re-parse stability, layout-coordinate separation
/// from semantic equality, and stable-id derivation.
/// </summary>
public class IrModelTests
{
    // ── Record equality: same content → equal, regardless of construction site. ──

    [Fact]
    public void IrBlock_WithSameContent_IsStructurallyEqual()
    {
        var a = MakeBasicBlock("B1");
        var b = MakeBasicBlock("B1");
        Assert.Equal(a, b);
    }

    [Fact]
    public void IrBlock_WithDifferentName_IsNotEqual()
    {
        var a = MakeBasicBlock("B1");
        var b = MakeBasicBlock("B2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IrWorkflow_WithSameBlocksConstantsGlobals_IsEqual()
    {
        var a = MakeSimpleWorkflow();
        var b = MakeSimpleWorkflow();
        Assert.Equal(a, b);
    }

    // ── Immutability: ImmutableArray cannot be mutated through the model. ──

    [Fact]
    public void IrBlock_Statements_IsImmutable()
    {
        var block = MakeBasicBlock("B1");
        // ImmutableArray<T> exposes no mutation surface; Assert it is indeed the
        // immutable collection type so future edits can't accidentally reintroduce List<T>.
        Assert.IsType<ImmutableArray<IrStatement>>(block.Statements);
    }

    // ── Layout-coordinate separation: canvas position must NOT affect equality. ──
    // This is the central fix for the legacy CFGBlock mixing LayoutX/Y into the model.

    [Fact]
    public void IrBlock_EqualityIgnores_LayoutAnnotation()
    {
        // Two blocks that are semantically identical but rendered at different canvas
        // positions MUST be equal — coordinates live in Annotations, out of equality.
        var atOrigin = MakeBasicBlock("B1") with
        {
            Annotations = [new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(0, 0))]
        };
        var elsewhere = MakeBasicBlock("B1") with
        {
            Annotations = [new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(500, 250))]
        };

        Assert.Equal(atOrigin, elsewhere);
    }

    [Fact]
    public void IrBlock_LayoutAnnotation_DoesNotAffect_WorkflowEquality()
    {
        var w1 = MakeSimpleWorkflow() with
        {
            Blocks = [MakeBasicBlock("B1") with
            {
                Annotations = [new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(10, 20))]
            }]
        };
        var w2 = MakeSimpleWorkflow() with
        {
            Blocks = [MakeBasicBlock("B1") with
            {
                Annotations = [new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(999, 999))]
            }]
        };

        Assert.Equal(w1, w2);
    }

    // ── IrFingerprint: content-derived, deterministic, re-parse-stable. ──

    [Theory]
    [InlineData("Print", new[] { "\"hello\"" }, null, "Print(\"hello\")")]
    [InlineData("Print", new[] { "  \"hello\"  " }, null, "Print(\"hello\")")]      // whitespace ignored
    [InlineData("Get", new[] { "x" }, null, "Get(x)")]
    [InlineData("Branch", new[] { "cond" }, null, "Branch(cond)")]
    [InlineData("Func", new[] { "a", "b" }, "vaaa0001", "Func(a,b)=>vaaa0001")]     // target folds in
    public void IrFingerprint_Compute_IsDeterministic(
        string func, string[] args, string? target, string expected)
    {
        var fp = IrFingerprint.Compute(func, args, target);
        Assert.Equal(expected, fp.Value);
    }

    [Fact]
    public void IrFingerprint_SameContent_AcrossTwoComputes_IsEqual()
    {
        // The re-parse-stability guarantee: the same call expression yields the same
        // fingerprint no matter when or where it's computed.
        var first = IrFingerprint.Compute("Print", ["Get(x)"]);
        var second = IrFingerprint.Compute("Print", ["Get(x)"]);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IrFingerprint_DifferentArgs_AreDistinct()
    {
        var a = IrFingerprint.Compute("Print", ["1"]);
        var b = IrFingerprint.Compute("Print", ["2"]);
        Assert.NotEqual(a, b);
    }

    // ── IrFingerprint.DeriveStableId: deterministic, content-keyed. ──

    [Fact]
    public void DeriveStableId_SameInputs_ProduceSameId()
    {
        var fp = IrFingerprint.Compute("Print", ["\"hi\""]);
        var id1 = IrFingerprint.DeriveStableId("MainBlock", fp, 0);
        var id2 = IrFingerprint.DeriveStableId("MainBlock", fp, 0);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void DeriveStableId_DifferentOrdinal_ProduceDifferentId()
    {
        var fp = IrFingerprint.Compute("Print", ["\"hi\""]);
        var id0 = IrFingerprint.DeriveStableId("MainBlock", fp, 0);
        var id1 = IrFingerprint.DeriveStableId("MainBlock", fp, 1);
        Assert.NotEqual(id0, id1);
    }

    [Fact]
    public void DeriveStableId_IsTwelveHexChars()
    {
        var fp = IrFingerprint.Compute("Print", ["\"hi\""]);
        var id = IrFingerprint.DeriveStableId("MainBlock", fp, 0);
        Assert.Equal(12, id.Length);
        Assert.All(id, c => Assert.True(c is >= '0' and <= '9' or >= 'A' and <= 'F'));
    }

    // ── Control-flow: unified Targets replaces the legacy arm quartet. ──

    [Fact]
    public void IrControlFlowStatement_Branch_HasTwoTargets()
    {
        var branch = new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("Branch", ["cond"]),
            Op = ControlFlowOp.Branch,
            FunctionName = "Branch",
            Arguments = ["cond"],
            Targets =
            [
                new IrControlFlowTarget("True", "TrueBlock"),
                new IrControlFlowTarget("False", "FalseBlock"),
            ],
        };

        Assert.Equal(2, branch.Targets.Length);
        Assert.Equal("TrueBlock", branch.Targets[0].TargetBlockName);
        Assert.Equal("FalseBlock", branch.Targets[1].TargetBlockName);
    }

    [Fact]
    public void IrControlFlowStatement_Goto_HasOneTarget()
    {
        var gotoStmt = new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("Goto", ["TargetBlock"]),
            Op = ControlFlowOp.Goto,
            FunctionName = "Goto",
            Arguments = ["TargetBlock"],
            Targets = [new IrControlFlowTarget("Exec", "TargetBlock")],
        };

        Assert.Single(gotoStmt.Targets);
        Assert.Equal("TargetBlock", gotoStmt.Targets[0].TargetBlockName);
    }

    // ── Pipeline statement: structured AST, no flattener coupling. ──

    [Fact]
    public void IrPipelineStatement_CarriesStructuredAst()
    {
        // `Get(x) > Print` — one source, one function-call segment with one placeholder.
        var pipe = new IrPipelineStatement
        {
            Fingerprint = IrFingerprint.Compute("Print", ["Get(x)"]),
            Sources = ["Get(x)"],
            Segments =
            [
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = "Print",
                    Arguments = [IrPipelineArgument.Placeholder(0)],
                },
            ],
        };

        Assert.Single(pipe.Sources);
        Assert.Single(pipe.Segments);
        Assert.Equal(IrSegmentKind.FunctionCall, pipe.Segments[0].Kind);
        Assert.Single(pipe.Segments[0].Arguments);
        Assert.Equal(IrPipelineArgumentKind.Placeholder, pipe.Segments[0].Arguments[0].Kind);
    }

    // ── IrWorkflow: EntryBlock is Blocks[0]; GetBlock looks up by name. ──

    [Fact]
    public void IrWorkflow_EntryBlock_IsFirstBlock()
    {
        var wf = MakeSimpleWorkflow();
        Assert.Equal("#MainBlock", wf.EntryBlock.Name);
        Assert.True(wf.EntryBlock.IsEntry);
    }

    [Fact]
    public void IrWorkflow_GetBlock_FindsByName()
    {
        var wf = MakeSimpleWorkflow();
        var found = wf.GetBlock("B1");
        Assert.NotNull(found);
        Assert.Equal("B1", found!.Name);
    }

    [Fact]
    public void IrWorkflow_GetBlock_ReturnsNullForUnknown()
    {
        var wf = MakeSimpleWorkflow();
        Assert.Null(wf.GetBlock("Nonexistent"));
    }

    // ── PubVarNaming: round-trip counter extraction. ──

    [Theory]
    [InlineData(0, "vaaa0000")]
    [InlineData(1, "vaaa0001")]
    [InlineData(10000, "vaab0000")]
    [InlineData(175760000 - 1, "vzzz9999")]
    public void PubVarNaming_GenerateAndExtract_RoundTrips(int counter, string expectedName)
    {
        var name = PubVarNaming.GeneratePubVarName(counter);
        Assert.Equal(expectedName, name);
        Assert.Equal(counter, PubVarNaming.TryExtractPubVarCounter(name));
    }

    [Fact]
    public void PubVarNaming_Extract_ReturnsNullForInvalidFormat()
    {
        Assert.Null(PubVarNaming.TryExtractPubVarCounter("notAPubVar"));
        Assert.Null(PubVarNaming.TryExtractPubVarCounter("vAAA0001"));   // uppercase letters invalid
        Assert.Null(PubVarNaming.TryExtractPubVarCounter("vaaa"));        // too short
    }

    // ── Test helpers ──

    private static IrBlock MakeBasicBlock(string name) => new()
    {
        Name = name,
        Kind = IrBlockKind.Basic,
        Statements = [],
        BlockVars = [],
        Successors = [],
        Annotations = [],
    };

    private static IrWorkflow MakeSimpleWorkflow() => new()
    {
        MainBlockName = "#MainBlock",
        Blocks =
        [
            new IrBlock
            {
                Name = "#MainBlock",
                Kind = IrBlockKind.Entry,
                Statements = [],
                Successors = [],
            },
            MakeBasicBlock("B1"),
        ],
    };
}
