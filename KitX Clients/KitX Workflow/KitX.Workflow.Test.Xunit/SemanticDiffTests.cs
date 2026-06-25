using Xunit;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// RED-LIGHT suite C — semantic diff between two CFGs.
/// Front-end feature dependency: minimal-change sync (Blueprint-Editor-Redesign-Plan §功能A).
///
/// These tests resolve <see cref="ICFGDiffer"/> from DI. Until the differ is implemented and
/// registered, resolution throws and every test here is RED.
///
/// Identity strategy under test: block name (block-level) + statement Fingerprint
/// (statement-level) + Myers alignment (in-block ordering).
/// </summary>
public class SemanticDiffTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public SemanticDiffTests(WorkflowFixture fx) => _fx = fx;

    private const string Base = """
        #PubVarBlock
        int x;

        #MainBlock
        1 > x;
        Goto("End");
        """ + TestData.End;

    /// <summary>The differ must be registered in DI.</summary>
    [Fact]
    public void Differ_IsRegistered()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        Assert.NotNull(differ);
    }

    /// <summary>Identical CFGs produce an empty diff.</summary>
    [Fact]
    public void IdenticalCFG_EmptyDiff()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var a = _fx.BS2CFG(Base, TestData.DeclHelpers)!;
        var b = _fx.BS2CFG(Base, TestData.DeclHelpers)!;

        var diff = differ.Diff(a, b);
        Assert.True(diff.IsEmpty);
    }

    /// <summary>Adding one statement reports exactly one Insert (no spurious removes).</summary>
    [Fact]
    public void AddedStatement_DetectedAsInsert()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var modified = Base.Replace("1 > x;", "1 > x;\n2 > x;");
        var a = _fx.BS2CFG(Base, TestData.DeclHelpers)!;
        var b = _fx.BS2CFG(modified, TestData.DeclHelpers)!;

        var diff = differ.Diff(a, b);
        Assert.NotEmpty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    /// <summary>Changing a function argument changes the fingerprint → reported as Modify, not Insert.</summary>
    [Fact]
    public void ModifiedArg_DetectedAsModify()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var modified = Base.Replace("1 > x;", "2 > x;");
        var a = _fx.BS2CFG(Base, TestData.DeclHelpers)!;
        var b = _fx.BS2CFG(modified, TestData.DeclHelpers)!;

        var diff = differ.Diff(a, b);
        Assert.NotEmpty(diff.Modified);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    /// <summary>Reordering two distinct statements reports Move, not Delete+Insert
    /// (validates Myers alignment is in use).</summary>
    [Fact]
    public void ReorderedStatements_ReportedAsMove()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var base2 = """
            #PubVarBlock
            int x; int y;

            #MainBlock
            1 > x;
            2 > y;
            Goto("End");
            """ + TestData.End;
        var reordered = """
            #PubVarBlock
            int x; int y;

            #MainBlock
            2 > y;
            1 > x;
            Goto("End");
            """ + TestData.End;

        var a = _fx.BS2CFG(base2, TestData.DeclHelpers)!;
        var b = _fx.BS2CFG(reordered, TestData.DeclHelpers)!;

        var diff = differ.Diff(a, b);
        // A move should NOT inflate the Added/Removed counts (that would indicate Delete+Insert
        // instead of true alignment).
        Assert.NotEmpty(diff.Moved);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    /// <summary>Renaming a block is reported as a block-level remove+add (block identity = name).</summary>
    [Fact]
    public void RenamedBlock_DetectedAsBlockRemoveAdd()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var renamed = Base.Replace("Goto(\"End\");", "Goto(\"Finale\");")
                          .Replace("#Block End", "#Block Finale");
        var a = _fx.BS2CFG(Base, TestData.DeclHelpers)!;
        var b = _fx.BS2CFG(renamed, TestData.DeclHelpers)!;

        var diff = differ.Diff(a, b);
        Assert.Contains("End", diff.BlocksRemoved);
        Assert.Contains("Finale", diff.BlocksAdded);
    }
}
