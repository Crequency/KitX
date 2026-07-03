using System.Collections.Immutable;
using System.Linq;
using KitX.Workflow.Diff;
using KitX.Workflow.Ir;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Verifies the immutable IR semantic diff engine: <see cref="IrDiffer"/> (the
/// LCS-based alignment, A-level port of the legacy CfgDiffer) and
/// <see cref="IrDiffApply"/> (the pure-function rewrite of the legacy mutating
/// CfgDiffApplier). These are the regression guard for the greenfield successor
/// of the legacy <c>SemanticDiffTests</c> / <c>CfgDiffApplierTests</c>.
///
/// Identity strategy under test (unchanged from the legacy algorithm):
///   • Block identity = Name (rename = delete+insert)
///   • Statement identity = Fingerprint (content-derived, re-parse-stable)
///   • In-block alignment = LCS over the fingerprint sequence
///
/// Plus the greenfield-only guarantees:
///   • Pure function — Apply never mutates its input (reference equality preserved)
///   • Layout inheritance — unchanged statements keep their BP canvas position
/// </summary>
public class IrDiffTests
{
    // ── Differ: identical IRs produce an empty diff ──

    [Fact]
    public void IdenticalIRs_ProduceEmptyDiff()
    {
        var a = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));
        var b = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        Assert.True(diff.IsEmpty);
        Assert.Empty(diff.BlockChanges);
        Assert.Empty(diff.StatementChanges);
    }

    // ── Differ: adding a statement reports exactly one Added ──

    [Fact]
    public void AddedStatement_ReportedAsAdded()
    {
        var a = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));
        var b = MakeWorkflow(MakeEntry(
            MakeCall("Print", "hello"),
            MakeCall("Print", "world"),   // newly added
            MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        var added = diff.StatementChanges.Where(c => c.Kind == DiffKind.Added).ToList();
        Assert.Single(added);
        Assert.Equal("Print(\"world\")", added[0].Fingerprint.Value);
        Assert.Empty(diff.StatementChanges.Where(c => c.Kind == DiffKind.Removed));
        Assert.Empty(diff.BlockChanges);
    }

    // ── Differ: removing a statement reports exactly one Removed ──

    [Fact]
    public void RemovedStatement_ReportedAsRemoved()
    {
        var a = MakeWorkflow(MakeEntry(
            MakeCall("Print", "hello"),
            MakeCall("Print", "world"),
            MakeGoto("End")));
        var b = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        var removed = diff.StatementChanges.Where(c => c.Kind == DiffKind.Removed).ToList();
        Assert.Single(removed);
        Assert.Equal("Print(\"world\")", removed[0].Fingerprint.Value);
        Assert.Empty(diff.StatementChanges.Where(c => c.Kind == DiffKind.Added));
    }

    // ── Differ: changing an argument changes the fingerprint → Modified ──

    [Fact]
    public void ModifiedArgument_ReportedAsModified()
    {
        // `Print("hello")` → `Print("hi")`: same call site, different argument →
        // the fingerprint changes, so this is a Modify, not an Insert+Delete.
        var a = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));
        var b = MakeWorkflow(MakeEntry(MakeCall("Print", "hi"), MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        var modified = diff.StatementChanges.Where(c => c.Kind == DiffKind.Modified).ToList();
        Assert.Single(modified);
        Assert.Equal("Print(\"hi\")", modified[0].Fingerprint.Value);
        Assert.NotNull(modified[0].NewValue);
        Assert.Empty(diff.StatementChanges.Where(c => c.Kind == DiffKind.Added));
        Assert.Empty(diff.StatementChanges.Where(c => c.Kind == DiffKind.Removed));
    }

    // ── Differ: reordering two distinct statements → Moved (validates LCS) ──

    [Fact]
    public void ReorderedStatements_ReportedAsMoved()
    {
        // `Print("a"); Print("b")` → `Print("b"); Print("a")`. LCS over fingerprints
        // matches nothing in order, but the two off-LCS slots pair up as Moves.
        var a = MakeWorkflow(MakeEntry(MakeCall("Print", "a"), MakeCall("Print", "b"), MakeGoto("End")));
        var b = MakeWorkflow(MakeEntry(MakeCall("Print", "b"), MakeCall("Print", "a"), MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        var moved = diff.StatementChanges.Where(c => c.Kind == DiffKind.Moved).ToList();
        Assert.NotEmpty(moved);
        // A move must NOT inflate Added/Removed (that would indicate Delete+Insert
        // instead of true alignment).
        Assert.Empty(diff.StatementChanges.Where(c => c.Kind == DiffKind.Added));
        Assert.Empty(diff.StatementChanges.Where(c => c.Kind == DiffKind.Removed));
        Assert.True(diff.PositionsChanged);
    }

    // ── Differ: PositionsChanged is false for pure add/remove/modify ──

    [Fact]
    public void PositionsChanged_False_WhenNoMove()
    {
        var a = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));
        var b = MakeWorkflow(MakeEntry(MakeCall("Print", "hi"), MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        Assert.False(diff.PositionsChanged);
    }

    // ── Differ: cross-block move → single Moved with ToBlock set ──

    [Fact]
    public void CrossBlockMove_ReportedAsMovedWithToBlock()
    {
        // Statement `Print("move-me")` moves from BlockA to BlockB.
        var a = MakeWorkflow(
            MakeEntry(MakeCall("Print", "a"), MakeGoto("BlockA")),
            MakeBasic("BlockA", MakeCall("Print", "move-me"), MakeGoto("BlockB")),
            MakeBasic("BlockB", MakeCall("Print", "hello"), MakeExit()));

        var b = MakeWorkflow(
            MakeEntry(MakeCall("Print", "a"), MakeGoto("BlockA")),
            MakeBasic("BlockA", MakeGoto("BlockB")),
            MakeBasic("BlockB", MakeCall("Print", "move-me"), MakeCall("Print", "hello"), MakeExit()));

        var diff = IrDiffer.Compute(a, b);

        var moved = diff.StatementChanges
            .Where(c => c.Kind == DiffKind.Moved && c.ToBlock == "BlockB")
            .ToList();
        Assert.NotEmpty(moved);
        Assert.Equal("BlockA", moved[0].BlockName);
        Assert.Equal("Print(\"move-me\")", moved[0].Fingerprint.Value);
        // No stray Added/Removed for the moved statement.
        Assert.DoesNotContain(diff.StatementChanges,
            c => c.Kind is DiffKind.Added or DiffKind.Removed && c.Fingerprint.Value == "Print(\"move-me\")");
    }

    // ── Differ: renamed block → block-level remove + add ──

    [Fact]
    public void RenamedBlock_ReportedAsBlockRemoveAdd()
    {
        var a = MakeWorkflow(
            MakeEntry(MakeCall("Print", "first"), MakeGoto("End")),
            MakeBasic("End", MakeExit()));

        var b = MakeWorkflow(
            MakeEntry(MakeCall("Print", "first"), MakeGoto("Finale")),
            MakeBasic("Finale", MakeExit()));

        var diff = IrDiffer.Compute(a, b);

        Assert.Contains(diff.BlockChanges, bc => bc.Name == "End" && bc.Kind == BlockChangeKind.Removed);
        Assert.Contains(diff.BlockChanges, bc => bc.Name == "Finale" && bc.Kind == BlockChangeKind.Added);
        var addedBlock = diff.BlockChanges.Single(bc => bc.Name == "Finale");
        Assert.NotNull(addedBlock.NewBlock);
    }

    // ── Differ: added block → BlockChangeKind.Added with NewBlock ──

    [Fact]
    public void AddedBlock_ReportedAsAddedWithNewBlock()
    {
        var a = MakeWorkflow(
            MakeEntry(MakeCall("Print", "first"), MakeGoto("End")),
            MakeBasic("End", MakeExit()));

        var b = MakeWorkflow(
            MakeEntry(MakeCall("Print", "first"), MakeGoto("End")),
            MakeBasic("End", MakeExit()),
            MakeBasic("Extra", MakeCall("Print", "extra"), MakeGoto("End")));

        var diff = IrDiffer.Compute(a, b);

        var added = diff.BlockChanges.Where(bc => bc.Kind == BlockChangeKind.Added).ToList();
        Assert.Single(added);
        Assert.Equal("Extra", added[0].Name);
        Assert.NotNull(added[0].NewBlock);
        Assert.Empty(diff.BlockChanges.Where(bc => bc.Kind == BlockChangeKind.Removed));
    }

    // ── Apply: empty diff returns a reference-equal IR (no allocation) ──

    [Fact]
    public void Apply_EmptyDiff_ReturnsReferenceIdenticalIR()
    {
        var ir = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));
        var diff = new IrDiff();   // empty

        var result = IrDiffApply.Apply(ir, diff);

        Assert.Same(ir, result);
    }

    // ── Apply: pure function — input IR is NEVER mutated ──

    [Fact]
    public void Apply_DoesNotMutate_InputIR()
    {
        var ir = MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End")));
        // Capture the entry block OBJECT reference (IrBlock is a class) and its
        // statement count before the apply. IrDiffApply must rebuild a NEW block
        // rather than mutate the existing one, so the entry block reference in the
        // input IR is unchanged afterwards.
        var originalEntryBlock = ir.EntryBlock;
        var originalStatementCount = ir.EntryBlock.Statements.Length;

        // A non-empty diff that touches the entry block.
        var diff = new IrDiff
        {
            StatementChanges =
            [
                new StatementChange
                {
                    BlockName = "#MainBlock",
                    Fingerprint = IrFingerprint.Compute("Print", ["\"added\""]),
                    Kind = DiffKind.Added,
                    NewValue = MakeCall("Print", "added"),
                    NewIndex = 1,
                },
            ],
        };

        _ = IrDiffApply.Apply(ir, diff);

        // The input IR is unchanged: the entry block is the same reference, its
        // statement count is unchanged, and the whole IR still equals a fresh copy.
        Assert.Same(originalEntryBlock, ir.EntryBlock);
        Assert.Equal(originalStatementCount, ir.EntryBlock.Statements.Length);
        Assert.Equal(
            MakeWorkflow(MakeEntry(MakeCall("Print", "hello"), MakeGoto("End"))),
            ir);
    }

    // ── Apply: round-trip — Diff(old,new) then Apply(old, diff) ≡ new ──
    // This is the key correctness invariant: applying the diff between two IRs to
    // the old one must reconstruct the new one's STATEMENTS (semantic content).
    // (Layout annotations are reconciled separately; see the next test.)

    [Fact]
    public void Apply_DiffRoundTrip_ReconstructsNewStatements()
    {
        var oldIr = MakeWorkflow(MakeEntry(
            MakeCall("Print", "hello"),
            MakeGoto("End")));

        var newIr = MakeWorkflow(MakeEntry(
            MakeCall("Print", "hello"),
            MakeCall("Print", "added"),   // new
            MakeGoto("End")));

        var diff = IrDiffer.Compute(oldIr, newIr);
        var reconstructed = IrDiffApply.Apply(oldIr, diff);

        // Statements of the entry block must match newIr's (content equality).
        Assert.True(reconstructed.EntryBlock.Statements.SequenceEqual(newIr.EntryBlock.Statements));
    }

    // ── Apply: removing a block severs Successors referencing it ──

    [Fact]
    public void Apply_RemovedBlock_SeverSuccessorsReferencingIt()
    {
        // Entry points at "End" via a Sequential successor; "End" is then removed.
        var ir = MakeWorkflow(
            MakeEntry([MakeCall("Print", "first")],
                [new IrEdge("#MainBlock", "End", IrEdgeType.Sequential, "Exec")]),
            MakeBasic("End", MakeExit()));

        var diff = new IrDiff
        {
            BlockChanges = [new BlockChange { Name = "End", Kind = BlockChangeKind.Removed }],
        };

        var result = IrDiffApply.Apply(ir, diff);

        // The entry block no longer has a Successor to the dropped "End".
        Assert.DoesNotContain(result.EntryBlock.Successors, e => e.ToBlockName == "End");
        Assert.Null(result.GetBlock("End"));
    }

    // ── Apply: removing a block blanks control-flow targets pointing at it ──

    [Fact]
    public void Apply_RemovedBlock_BlanksControlFlowTargetsPointingAtIt()
    {
        // A Goto("End") where "End" is removed should have its target blanked.
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("End")),
            MakeBasic("End", MakeExit()));

        var diff = new IrDiff
        {
            BlockChanges = [new BlockChange { Name = "End", Kind = BlockChangeKind.Removed }],
        };

        var result = IrDiffApply.Apply(ir, diff);

        var gotoStmt = (IrControlFlowStatement)result.EntryBlock.Statements[0];
        Assert.Equal(string.Empty, gotoStmt.Targets[0].TargetBlockName);
    }

    // ── Layout inheritance: unchanged statement keeps its BP canvas position ──
    // This is the §7 requirement: a BS edit that leaves a node unchanged must not
    // drop its canvas coordinates. IrDiffApply copies the Layout annotation from
    // the old IR for every unchanged statement.

    [Fact]
    public void Apply_PreservesLayoutAnnotation_ForUnchangedStatement()
    {
        // oldIr: a single pipeline statement with a known layout (120.5, 240).
        var pipeline = MakeCall("Filter", "Get(x)");
        var oldIr = MakeWorkflow(MakeEntry(
            [pipeline, MakeGoto("End")],
            successors: [],
            annotations:
            [
                new IrAnnotation(AnnotationKind.Layout, pipeline.Fingerprint.Value,
                    new IrLayout(120.5, 240)),
            ]));

        // newIr: add a second statement AFTER the pipeline. The pipeline is
        // unchanged (same fingerprint), so its layout must survive the apply.
        var newPipeline = MakeCall("Filter", "Get(x)");   // same content → same fingerprint
        var newIr = MakeWorkflow(MakeEntry(
            [newPipeline, MakeCall("Print", "added"), MakeGoto("End")]));

        var diff = IrDiffer.Compute(oldIr, newIr);
        var result = IrDiffApply.Apply(oldIr, diff);

        // The pipeline's layout annotation is present in the result, keyed by its
        // fingerprint, with the original (120.5, 240) coordinates.
        var layout = result.EntryBlock.Annotations
            .Single(a => a.Kind == AnnotationKind.Layout && a.Key == newPipeline.Fingerprint.Value);
        var coords = (IrLayout)layout.Value!;
        Assert.Equal(120.5, coords.X);
        Assert.Equal(240.0, coords.Y);
    }

    // ── Layout inheritance: block anchor survives too ──

    [Fact]
    public void Apply_PreservesLayoutAnnotation_BlockAnchor()
    {
        var oldIr = MakeWorkflow(MakeEntry(
            [MakeCall("Print", "hello"), MakeGoto("End")],
            successors: [],
            annotations:
            [
                new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(10, 20)),
            ]));

        var newIr = MakeWorkflow(MakeEntry(
            [MakeCall("Print", "hi"), MakeGoto("End")]));

        var diff = IrDiffer.Compute(oldIr, newIr);
        var result = IrDiffApply.Apply(oldIr, diff);

        var anchor = result.EntryBlock.Annotations
            .Single(a => a.Kind == AnnotationKind.Layout && a.Key == "BlockPos");
        var coords = (IrLayout)anchor.Value!;
        Assert.Equal(10.0, coords.X);
        Assert.Equal(20.0, coords.Y);
    }

    // ── Layout inheritance: Modified statement does NOT inherit old layout ──
    // (Its fingerprint changed, so the old layout no longer applies to it.)

    [Fact]
    public void Apply_DoesNotInheritLayout_ForModifiedStatement()
    {
        var oldPipeline = MakeCall("Print", "hello");
        var oldIr = MakeWorkflow(MakeEntry(
            [oldPipeline, MakeGoto("End")],
            successors: [],
            annotations:
            [
                new IrAnnotation(AnnotationKind.Layout, oldPipeline.Fingerprint.Value,
                    new IrLayout(99, 99)),
            ]));

        // Modify the statement: `Print("hello")` → `Print("hi")` (new fingerprint).
        var newIr = MakeWorkflow(MakeEntry(
            [MakeCall("Print", "hi"), MakeGoto("End")]));

        var diff = IrDiffer.Compute(oldIr, newIr);
        var result = IrDiffApply.Apply(oldIr, diff);

        // The OLD fingerprint's layout key must NOT appear (the statement changed).
        Assert.DoesNotContain(result.EntryBlock.Annotations,
            a => a.Kind == AnnotationKind.Layout && a.Key == oldPipeline.Fingerprint.Value);
    }

    // ── Apply: full Diff→Apply round-trip preserves layout end-to-end ──

    [Fact]
    public void DiffApply_RoundTrip_PreservesLayoutEndToEnd()
    {
        // Start: one statement with a layout. Edit: add a second statement.
        var pipe = MakeCall("Print", "keep-me");
        var oldIr = MakeWorkflow(MakeEntry(
            [pipe, MakeGoto("End")],
            successors: [],
            annotations:
            [
                new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(5, 5)),
                new IrAnnotation(AnnotationKind.Layout, pipe.Fingerprint.Value,
                    new IrLayout(100, 200)),
            ]));

        // Build the edited IR by lowerering would require the parser; instead, mimic
        // what the editor does: take the projected text, edit it, re-parse. Here we
        // construct the "new IR" directly with the unchanged statement + an added one.
        var newIr = MakeWorkflow(MakeEntry(
            [
                MakeCall("Print", "keep-me"),   // unchanged (same fingerprint)
                MakeCall("Print", "new"),
                MakeGoto("End"),
            ]));

        var diff = IrDiffer.Compute(oldIr, newIr);
        var result = IrDiffApply.Apply(oldIr, diff);

        // Both the block anchor and the unchanged statement's layout survived.
        Assert.Contains(result.EntryBlock.Annotations,
            a => a.Kind == AnnotationKind.Layout && a.Key == "BlockPos"
              && ((IrLayout)a.Value!).X == 5);
        Assert.Contains(result.EntryBlock.Annotations,
            a => a.Kind == AnnotationKind.Layout && a.Key == pipe.Fingerprint.Value
              && ((IrLayout)a.Value!).X == 100
              && ((IrLayout)a.Value!).Y == 200);
    }

    // ── Test helpers: minimal IR construction (no parser dependency) ──

    /// <summary>Makes a pipeline statement for a bare call like <c>Print("hello")</c>.</summary>
    private static IrPipelineStatement MakeCall(string func, string arg)
    {
        var fp = IrFingerprint.Compute(func, [$"\"{arg}\""]);
        return new IrPipelineStatement
        {
            Fingerprint = fp,
            Sources = [$"{func}(\"{arg}\")"],
            Segments =
            [
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = func,
                    Arguments = [IrPipelineArgument.Lit($"\"{arg}\"")],
                },
            ],
        };
    }

    /// <summary>Makes a Goto(target) control-flow terminator.</summary>
    private static IrControlFlowStatement MakeGoto(string target) => new()
    {
        Fingerprint = IrFingerprint.Compute("Goto", [$"\"{target}\""]),
        Op = ControlFlowOp.Goto,
        FunctionName = "Goto",
        Arguments = [$"\"{target}\""],
        Targets = [new IrControlFlowTarget("Exec", target)],
    };

    /// <summary>Makes an Exit() control-flow terminator.</summary>
    private static IrControlFlowStatement MakeExit() => new()
    {
        Fingerprint = IrFingerprint.Compute("Exit", []),
        Op = ControlFlowOp.Exit,
        FunctionName = "Exit",
        Arguments = [],
        Targets = [],
    };

    /// <summary>Makes the entry block (#MainBlock) with the given statements.</summary>
    private static IrBlock MakeEntry(
        ImmutableArray<IrStatement> statements,
        ImmutableArray<IrEdge>? successors = null,
        ImmutableArray<IrAnnotation>? annotations = null) => new()
    {
        Name = "#MainBlock",
        Kind = IrBlockKind.Entry,
        Statements = statements,
        Successors = successors ?? [],
        Annotations = annotations ?? [],
    };

    /// <summary>Convenience: entry block from a params array of statements.</summary>
    private static IrBlock MakeEntry(params IrStatement[] statements) =>
        MakeEntry(statements.ToImmutableArray());

    /// <summary>Makes a basic (non-entry) block.</summary>
    private static IrBlock MakeBasic(string name, params IrStatement[] statements) => new()
    {
        Name = name,
        Kind = IrBlockKind.Basic,
        Statements = statements.ToImmutableArray(),
        Successors = [],
        Annotations = [],
    };

    /// <summary>Makes a workflow from a params array of blocks (entry first).</summary>
    private static IrWorkflow MakeWorkflow(params IrBlock[] blocks) => new()
    {
        MainBlockName = "#MainBlock",
        Blocks = blocks.ToImmutableArray(),
    };
}
