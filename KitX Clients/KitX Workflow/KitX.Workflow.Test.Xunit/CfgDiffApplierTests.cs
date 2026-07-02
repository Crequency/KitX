using Xunit;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test.Xunit;

public class CfgDiffApplierTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public CfgDiffApplierTests(WorkflowFixture fx) => _fx = fx;

    private const string Simple = """
        #MainBlock
        Print("hello");
        Goto("End");
        #Block End
        Exit();
        """;

    [Fact]
    public void EmptyDiff_LeavesCFGUnchanged()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var blockCount = cfg.Blocks.Count;
        var stmtCount = cfg.Blocks[0].Statements.Count;

        var diff = new CfgDiff();
        var applier = _fx.GetService<ICfgDiffApplier>();
        applier.Apply(diff, cfg);

        Assert.Equal(blockCount, cfg.Blocks.Count);
        Assert.Equal(stmtCount, cfg.Blocks[0].Statements.Count);
    }

    [Fact]
    public void AddStatement_IncreasesCount()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var before = mainBlock.Statements.Count;

        var newStmt = new CFGStatement
        {
            StatementId = "test-add-1",
            BlockName = "MainBlock",
            OriginalExpression = "Print(\"added\")",
            Fingerprint = "Print(\"added\")",
            FunctionName = "Print",
            Arguments = ["\"added\""],
        };
        var diff = new CfgDiff
        {
            Added = [new StatementChange("MainBlock", "Print(\"added\")",
                StatementId: "test-add-1", NewStatement: newStmt, Index: 1)],
        };
        var applier = _fx.GetService<ICfgDiffApplier>();
        applier.Apply(diff, cfg);

        Assert.Equal(before + 1, mainBlock.Statements.Count);
        Assert.Contains(mainBlock.Statements, s => s.Fingerprint == "Print(\"added\")");
    }

    [Fact]
    public void RemoveStatement_DecreasesCount()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var before = mainBlock.Statements.Count;
        var firstStmt = mainBlock.Statements[0];

        var diff = new CfgDiff
        {
            Removed = [new StatementChange("MainBlock",
                firstStmt.Fingerprint ?? firstStmt.OriginalExpression,
                StatementId: firstStmt.StatementId)],
        };
        var applier = _fx.GetService<ICfgDiffApplier>();
        applier.Apply(diff, cfg);

        Assert.Equal(before - 1, mainBlock.Statements.Count);
    }

    [Fact]
    public void ModifyStatement_ReplacesObject()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var oldStmt = mainBlock.Statements[0];
        var oldId = oldStmt.StatementId;

        var newStmt = new CFGStatement
        {
            StatementId = "will-be-overwritten",
            BlockName = "MainBlock",
            OriginalExpression = "Print(\"modified\")",
            Fingerprint = "Print(\"modified\")",
            FunctionName = "Print",
            Arguments = ["\"modified\""],
        };
        var diff = new CfgDiff
        {
            Modified = [new StatementChange("MainBlock", "Print(\"modified\")",
                StatementId: oldId, NewStatement: newStmt)],
        };
        var applier = _fx.GetService<ICfgDiffApplier>();
        applier.Apply(diff, cfg);

        var replaced = mainBlock.Statements[0];
        Assert.Equal(oldId, replaced.StatementId);
        Assert.Equal("Print(\"modified\")", replaced.Fingerprint);
    }

    [Fact]
    public void CrossBlockStatement_MovesCorrectly()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var src = """
            #MainBlock
            Print("a");
            Goto("BlockA");
            #Block BlockA
            Print("move-me");
            Goto("BlockB");
            #Block BlockB
            Print("hello");
            Exit();
            """;
        var dst = """
            #MainBlock
            Print("a");
            Goto("BlockA");
            #Block BlockA
            Goto("BlockB");
            #Block BlockB
            Print("move-me");
            Print("hello");
            Exit();
            """;
        var oldCfg = _fx.BS2CFG(src)!;
        var newCfg = _fx.BS2CFG(dst)!;

        var diff = differ.Diff(oldCfg, newCfg);
        Assert.NotEmpty(diff.Removed);
        Assert.NotEmpty(diff.Added);

        var applier = _fx.GetService<ICfgDiffApplier>();
        applier.Apply(diff, oldCfg);

        var blockA = oldCfg.Blocks.First(b => b.Name == "BlockA");
        Assert.Single(blockA.Statements);
    }

    [Fact]
    public void AddBlock_InsertsIntoCFG()
    {
        var differ = _fx.GetService<ICFGDiffer>();
        var src = """
            #MainBlock
            Print("first");
            Goto("End");
            #Block End
            Exit();
            """;
        var dst = """
            #MainBlock
            Print("first");
            Goto("End");
            #Block End
            Exit();
            #Block Extra
            Print("extra");
            Goto("End");
            """;
        var oldCfg = _fx.BS2CFG(src)!;
        var newCfg = _fx.BS2CFG(dst)!;

        var countBefore = oldCfg.Blocks.Count;
        var diff = differ.Diff(oldCfg, newCfg);
        Assert.NotEmpty(diff.BlocksAdded);

        var applier = _fx.GetService<ICfgDiffApplier>();
        applier.Apply(diff, oldCfg);

        Assert.Equal(countBefore + 1, oldCfg.Blocks.Count);
        Assert.Contains(oldCfg.Blocks, b => b.Name == "Extra");
    }
}
