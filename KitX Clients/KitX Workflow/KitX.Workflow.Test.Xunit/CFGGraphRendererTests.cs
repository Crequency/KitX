using Xunit;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// RED-LIGHT suite B — CFG → Blueprint graph renderer (the v5.1 G-3 path).
/// Front-end feature dependency: BP as a rendered view of CFG (功能A/B).
///
/// These tests resolve <see cref="ICFGGraphRenderer"/> from DI. Until the renderer is
/// implemented and registered (currently a phantom per CFG-Architecture-v5.1.md G-3),
/// resolution throws and every test here is RED.
/// </summary>
public class CFGGraphRendererTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public CFGGraphRendererTests(WorkflowFixture fx) => _fx = fx;

    private const string Linear = """
        #PubVarBlock
        int currentLoop;

        #MainBlock
        0 > currentLoop;
        Goto("End");
        """ + TestData.End;

    private const string WithBlock = """
        #PubVarBlock
        int x;

        #MainBlock
        Goto("Worker");

        #Block Worker
        x > Print;
        Goto("End");
        """ + TestData.End;

    /// <summary>The renderer must be registered in DI (resolving it must not throw).</summary>
    [Fact]
    public void Renderer_IsRegistered()
    {
        var renderer = _fx.GetService<ICFGGraphRenderer>();
        Assert.NotNull(renderer);
    }

    /// <summary>MainBlock renders to an EntryNode plus its successor nodes.</summary>
    [Fact]
    public void Render_MainBlock_ProducesEntryNode()
    {
        var renderer = _fx.GetService<ICFGGraphRenderer>();
        var cfg = _fx.BS2CFG(Linear, TestData.DeclHelpers)!;
        var bp = renderer.Render(cfg);

        Assert.NotEmpty(bp.Nodes);
        Assert.Contains(bp.Nodes, n => n.NodeType == BlueprintNodeType.Entry);
    }

    /// <summary>A pipeline <c>0 &gt; currentLoop</c> materialises a data edge (a connection whose
    /// PubVarName or target is the variable node), not just an Exec edge.</summary>
    [Fact]
    public void Render_Pipeline_ProducesDataEdge()
    {
        var renderer = _fx.GetService<ICFGGraphRenderer>();
        var cfg = _fx.BS2CFG(Linear, TestData.DeclHelpers)!;
        var bp = renderer.Render(cfg);

        // The assignment "0 > currentLoop" must produce at least one connection that represents
        // data flow into currentLoop. The contract marks data connections with PubVarName when the
        // target is a PubVar assignment; otherwise the target node is a Variable node.
        var varNodeIds = bp.Nodes
            .Where(n => n.NodeType == BlueprintNodeType.Variable)
            .Select(n => n.Id)
            .ToHashSet();
        bool hasDataConnection = bp.Connections.Any(c =>
            !string.IsNullOrEmpty(c.PubVarName) || varNodeIds.Contains(c.TargetNodeId));
        Assert.True(hasDataConnection,
            "Expected at least one data connection into currentLoop (PubVarName set or target is a Variable node).");
    }

    /// <summary>A named #Block renders to a foldable BlockNode (compound), per §11.</summary>
    [Fact]
    public void Render_Block_ProducesCompoundNode()
    {
        var renderer = _fx.GetService<ICFGGraphRenderer>();
        var cfg = _fx.BS2CFG(WithBlock, TestData.DeclHelpers)!;
        var bp = renderer.Render(cfg);

        Assert.Contains(bp.Nodes, n => n.NodeType == BlueprintNodeType.Block);
    }
}
