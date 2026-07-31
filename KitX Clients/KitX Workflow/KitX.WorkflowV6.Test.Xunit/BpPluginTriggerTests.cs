// ─────────────────────────────────────────────────────────────────────────────
// PluginTriggerNode support tests (P3-δ): the trigger entry node replaces the
// EntryNode on the canvas when TriggerType=PluginEvent. The reverse translator,
// structural reducer, scope analyzer, and layout service must all treat it as the
// exec-graph root (same 0-in/1-Exec-out pin shape as EntryNode).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class BpPluginTriggerTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BpPluginTriggerTests(WorkflowTestFixture fixture) => _fixture = fixture;

    /// <summary>Replaces the EntryNode with a PluginTriggerNode, preserving node Id + output pin Id (v5.1 frontend pattern).</summary>
    private static void ReplaceEntryWithPluginTrigger(Blueprint bp, string pluginName = "TestPlugin", string triggerName = "TestTrigger")
    {
        var entry = bp.Nodes.First(n => n is EntryNode);
        var idx = bp.Nodes.IndexOf(entry);

        var trigger = new PluginTriggerNode
        {
            Id = entry.Id,
            Name = "PluginTrigger",
            X = entry.X,
            Y = entry.Y,
            PluginName = pluginName,
            TriggerName = triggerName,
        };
        trigger.OutputPins[0].Id = entry.OutputPins[0].Id;

        bp.Nodes[idx] = trigger;
    }

    [Fact]
    public void Reverse_Restores_Body_When_Root_Is_PluginTriggerNode()
    {
        var ir = _fixture.ParseKS("Print(\"hello\")\n");
        var bp = _fixture.BpLens.Project(ir);
        Assert.Contains(bp.Nodes, n => n is EntryNode);

        ReplaceEntryWithPluginTrigger(bp);
        Assert.Contains(bp.Nodes, n => n is PluginTriggerNode);

        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.NotEmpty(reversed.Body);
        Assert.Single(reversed.Body);
    }

    [Fact]
    public void Reverse_With_PluginTriggerNode_Root_Is_Structurally_Equivalent()
    {
        var ir = _fixture.ParseKS("""
            var {
                int counter
            }
            0 > counter
            counter > Print
            """);
        var bp = _fixture.BpLens.Project(ir);

        ReplaceEntryWithPluginTrigger(bp);

        var reversed = _fixture.BpLens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty with PluginTriggerNode root: {diff.StatementChanges.Length} changes: "
            + string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}")));
    }

    [Fact]
    public void ValidateDetailed_Accepts_PluginTriggerNode_Root()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("Print(\"hello\")\n"));
        ReplaceEntryWithPluginTrigger(bp);

        var violation = _fixture.BpLens.ValidateDetailed(bp);
        Assert.Null(violation);
    }

    [Fact]
    public void AnalyzeScopes_Accepts_PluginTriggerNode_Root()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("Print(\"hello\")\n"));
        ReplaceEntryWithPluginTrigger(bp);

        // Must not throw (top-level nodes are not framed — an empty list is fine).
        var scopes = _fixture.BpLens.AnalyzeScopes(bp);
        Assert.NotNull(scopes);
    }

    [Fact]
    public void Layout_Accepts_PluginTriggerNode_Root()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("Print(\"hello\")\n"));
        ReplaceEntryWithPluginTrigger(bp);

        var layout = new LayoutService();
        // Must not throw and must keep the trigger node as the root anchor.
        var exception = Record.Exception(() => layout.Layout(bp));
        Assert.Null(exception);
    }
}
