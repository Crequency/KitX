using Xunit;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test.Xunit;

public class BsSyncServiceTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public BsSyncServiceTests(WorkflowFixture fx) => _fx = fx;

    private const string Hello = """
        #MainBlock
        Print("hello");
        Exit();
        """;

    private const string HelloWorld = """
        #MainBlock
        Print("hello");
        Print("world");
        Exit();
        """;

    [Fact]
    public void ApplyBsEdit_SameText_ProducesEmptyDiff()
    {
        var sync = _fx.GetService<IBsSyncService>();
        var cfg = _fx.BS2CFG(Hello)!;
        var session = new WorkflowSession(cfg);

        var cs = sync.ApplyBsEdit(session, Hello);
        Assert.True(cs.StatementDiff?.IsEmpty ?? true);
    }

    [Fact]
    public void ApplyBsEdit_AddedStatement_AppliesToCfg()
    {
        var sync = _fx.GetService<IBsSyncService>();
        var cfg = _fx.BS2CFG(Hello)!;
        var session = new WorkflowSession(cfg);
        var mainBlock = cfg.Blocks.First(b => b.Name == "MainBlock");
        var before = mainBlock.Statements.Count;

        var cs = sync.ApplyBsEdit(session, HelloWorld);
        Assert.NotNull(cs.StatementDiff);
        Assert.False(cs.StatementDiff!.IsEmpty);
        Assert.NotEmpty(cs.StatementDiff.Added);
        Assert.Equal(before + 1, mainBlock.Statements.Count);
    }

    [Fact]
    public void ApplyBsEdit_FiresCfgChanged()
    {
        var sync = _fx.GetService<IBsSyncService>();
        var cfg = _fx.BS2CFG(Hello)!;
        var session = new WorkflowSession(cfg);

        CfgChangeSet? received = null;
        session.CfgChanged += cs => received = cs;

        sync.ApplyBsEdit(session, HelloWorld);

        Assert.NotNull(received);
        Assert.False(received!.StatementDiff?.IsEmpty);
    }

    [Fact]
    public void WorkflowSession_HoldsCfg()
    {
        var cfg = _fx.BS2CFG(Hello)!;
        var session = new WorkflowSession(cfg);
        Assert.Same(cfg, session.Cfg);
    }
}
