using Xunit;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test.Xunit;

public class BpEditApplierTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public BpEditApplierTests(WorkflowFixture fx) => _fx = fx;

    private const string Simple = """
        #MainBlock
        Print("hello");
        Exit();
        """;

    [Fact]
    public void AddNodeInBlock_AppendsStatement()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var session = new WorkflowSession(cfg);
        var applier = _fx.GetService<IBpEditApplier>();
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var before = mainBlock.Statements.Count;

        applier.ApplyBpEdit(session, new AddNodeInBlock("MainBlock", "Print"));

        Assert.Equal(before + 1, mainBlock.Statements.Count);
    }

    [Fact]
    public void DeleteNode_RemovesStatement()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var session = new WorkflowSession(cfg);
        var applier = _fx.GetService<IBpEditApplier>();
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var nodeId = mainBlock.Statements[0].StatementId;
        var before = mainBlock.Statements.Count;

        applier.ApplyBpEdit(session, new DeleteNode(nodeId));

        Assert.Equal(before - 1, mainBlock.Statements.Count);
    }

    [Fact]
    public void SetNodeArgument_UpdatesArgument()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var session = new WorkflowSession(cfg);
        var applier = _fx.GetService<IBpEditApplier>();
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var nodeId = mainBlock.Statements[0].StatementId;

        applier.ApplyBpEdit(session, new SetNodeArgument(nodeId, 0, "\"world\""));

        Assert.Equal("\"world\"", mainBlock.Statements[0].Arguments[0]);
    }

    [Fact]
    public void AddBlock_InsertsNewBlock()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var session = new WorkflowSession(cfg);
        var applier = _fx.GetService<IBpEditApplier>();
        var before = cfg.Blocks.Count;

        applier.ApplyBpEdit(session, new AddBlock("NewBlock"));

        Assert.Equal(before + 1, cfg.Blocks.Count);
        Assert.Contains(cfg.Blocks, b => b.Name == "NewBlock");
    }

    [Fact]
    public void RenameBlock_UpdatesAllReferences()
    {
        var cfg = _fx.BS2CFG(Simple)!;
        var session = new WorkflowSession(cfg);
        var applier = _fx.GetService<IBpEditApplier>();

        applier.ApplyBpEdit(session, new RenameBlock("MainBlock", "Start"));

        Assert.DoesNotContain(cfg.Blocks, b => b.Name == "MainBlock");
        Assert.Contains(cfg.Blocks, b => b.Name == "Start");
    }

    [Fact]
    public void DeleteBlock_RemovesBlock()
    {
        var src = """
            #MainBlock
            Goto("Extra");
            #Block Extra
            Print("extra");
            Exit();
            """;
        var cfg = _fx.BS2CFG(src)!;
        var session = new WorkflowSession(cfg);
        var applier = _fx.GetService<IBpEditApplier>();
        var before = cfg.Blocks.Count;

        applier.ApplyBpEdit(session, new DeleteBlock("Extra"));

        Assert.Equal(before - 1, cfg.Blocks.Count);
        Assert.DoesNotContain(cfg.Blocks, b => b.Name == "Extra");
    }

    [Fact]
    public void BpEditApplier_IsRegistered()
    {
        var applier = _fx.GetService<IBpEditApplier>();
        Assert.NotNull(applier);
    }
}
