using Xunit;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// RED-LIGHT suite A — stable identity round-trip.
/// Front-end feature dependency: minimal-change sync (Blueprint-Editor-Redesign-Plan §功能A/B/C).
///
/// These tests lock the contract that a CFG derived from BS is structurally stable across
/// re-parses (same fingerprints, same block membership), which is the precondition for any
/// diff-based sync. They are expected to PASS once the fingerprint/block-identity invariants
/// hold end-to-end; the round-trip-equivalence ones may already pass, the explicit-id ones
/// document intent for the forthcoming diff engine.
/// </summary>
public class StableIdRoundTripTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public StableIdRoundTripTests(WorkflowFixture fx) => _fx = fx;

    private const string WhileDo = """
        #ConstBlock
        int guessNum = 5;
        int targetNum = 7;

        #PubVarBlock
        int currentLoop;
        bool cond;

        #MainBlock
        0 > currentLoop;
        Goto("LoopCond");

        #Block LoopCond
        currentLoop > HelperFuncCompare("BLE", _, 100) > cond;
        Branch(cond, "LoopBody", "EndLogic");

        #Block LoopBody
        currentLoop > Print;
        currentLoop > HelperFuncAdd(_, 1) > currentLoop;
        Goto("LoopCond");

        #Block EndLogic
        Print("条件循环结束");
        Exit();
        """;

    /// <summary>Same BS parsed twice must yield CFGs with identical per-block fingerprint sequences
    /// (the precondition for diff — if fingerprints were non-deterministic, diff is meaningless).</summary>
    [Fact]
    public void BS2CFG_AssignsStableFingerprintsAcrossReparse()
    {
        var cfg1 = _fx.BS2CFG(WhileDo, TestData.DeclHelpers);
        var cfg2 = _fx.BS2CFG(WhileDo, TestData.DeclHelpers);

        Assert.NotNull(cfg1);
        Assert.NotNull(cfg2);

        foreach (var (b1, b2) in cfg1.Blocks.Zip(cfg2.Blocks))
        {
            Assert.Equal(b1.Name, b2.Name);
            var fp1 = b1.GetEffectiveStatements().Select(s => s.Fingerprint ?? s.OriginalExpression).ToList();
            var fp2 = b2.GetEffectiveStatements().Select(s => s.Fingerprint ?? s.OriginalExpression).ToList();
            Assert.Equal(fp1, fp2);
        }
    }

    /// <summary>BS → CFG → BS → CFG must be semantically stable: the second CFG's structure
    /// (blocks + fingerprints) equals the first's.</summary>
    [Fact]
    public void RoundTrip_BS_CFG_BS_CFG_PreservesStructure()
    {
        var cfg1 = _fx.BS2CFG(WhileDo, TestData.DeclHelpers);
        Assert.NotNull(cfg1);
        var rendered = new KitX.Workflow.Conversion.CFGRenderer().Render(cfg1);
        var cfg2 = _fx.BS2CFG(rendered, TestData.DeclHelpers);

        Assert.NotNull(cfg2);
        Assert.Equal(
            cfg1.Blocks.Select(b => b.Name),
            cfg2.Blocks.Select(b => b.Name));

        foreach (var (b1, b2) in cfg1.Blocks.Zip(cfg2.Blocks))
        {
            var fp1 = b1.GetEffectiveStatements().Select(s => s.Fingerprint ?? s.OriginalExpression).ToList();
            var fp2 = b2.GetEffectiveStatements().Select(s => s.Fingerprint ?? s.OriginalExpression).ToList();
            Assert.Equal(fp1, fp2);
        }
    }

    /// <summary>Block names must round-trip exactly (block identity is the diff anchor).</summary>
    [Fact]
    public void RoundTrip_PreservesBlockNames()
    {
        var cfg = _fx.BS2CFG(WhileDo, TestData.DeclHelpers);
        Assert.NotNull(cfg);
        var names = cfg.Blocks.Select(b => b.Name).ToList();
        Assert.Contains("MainBlock", names);
        Assert.Contains("LoopCond", names);
        Assert.Contains("LoopBody", names);
        Assert.Contains("EndLogic", names);
    }
}
