// ─────────────────────────────────────────────────────────────────────────────
// W-6 tests: the exec-graph walker's switch-arm scope paths must use the INDEX
// convention (/arm/{i}) — the same segment NodePath.Arm produces — never the
// label (pin-name) convention. The walk order (OutputPins order) defines the arm
// index, so exec order == walk order.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class ArmScopePathTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public ArmScopePathTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private sealed class PathRecorder : ExecGraphWalker
    {
        public readonly List<string> SubScopePaths = new();
        public readonly List<string> PinNames = new();

        protected override VisitDecision OnNode(BlueprintNode node, string scopePath)
            => VisitDecision.Visit;

        protected override void OnEnterSubScope(BuiltinFunctionNode fn, string pinName, string childScopePath)
        {
            PinNames.Add(pinName);
            SubScopePaths.Add(childScopePath);
        }
    }

    private (Blueprint Bp, PathRecorder Recorder) WalkWorkflow(string src)
    {
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var graph = new GraphIndex(bp);
        var entry = bp.Nodes.First(n => n is EntryNode);

        var recorder = new PathRecorder();
        recorder.Walk(graph, entry.Id, BpPinNames.Exec, NodePath.Top);
        return (bp, recorder);
    }

    [Fact]
    public void Switch_Arm_SubScopes_Use_Index_Segments_Not_Labels()
    {
        // Non-sequential labels: 43/45/62/60 + default. The scope paths must use the
        // arm ORDINAL — the labels must never leak into the path segment.
        var (_, recorder) = WalkWorkflow("""
            var {
                int sel
            }
            switch sel:
                43:
                    Print("plus")
                45:
                    Print("minus")
                62:
                    Print("right")
                60:
                    Print("left")
                default:
                    Print("other")
            """);

        Assert.Equal(
            new[] { "/top/arm/0", "/top/arm/1", "/top/arm/2", "/top/arm/3", "/top/default" },
            recorder.SubScopePaths);
        // Pin names stay the labels; the walk order (OutputPins order) IS the arm order.
        Assert.Equal(new[] { "43", "45", "62", "60", "Default" }, recorder.PinNames);
    }

    [Fact]
    public void Arm_Paths_Match_NodePath_Arm_Segments_Exec_Order_Equals_Walk_Order()
    {
        // 对拍 (W-6): the walker's scope paths for the arms must equal NodePath.Arm
        // composed on the walker's current scope — the same segment convention
        // DebugCodegen/BpRenderer/BpReverseTranslator use. The walk visits arms in
        // exec order, so walk order == arm index order.
        var (_, recorder) = WalkWorkflow("""
            switch 1:
                0:
                    Print("zero")
                1:
                    Print("one")
                default:
                    Print("other")
            """);

        Assert.Equal(new[]
        {
            NodePath.Arm(NodePath.Top, 0),
            NodePath.Arm(NodePath.Top, 1),
            NodePath.Default(NodePath.Top),
        }, recorder.SubScopePaths);
        Assert.Equal(new[] { "0", "1", "Default" }, recorder.PinNames);
    }

    [Fact]
    public void Nested_Switch_Uses_Index_Segments_At_Each_Level()
    {
        var (_, recorder) = WalkWorkflow("""
            var {
                int sel
            }
            switch sel:
                0:
                    switch sel:
                        5:
                            Print("five")
                        9:
                            Print("nine")
                1:
                    Print("one")
            """);

        var outerArm0 = NodePath.Arm(NodePath.Top, 0);
        Assert.Equal(new[]
        {
            outerArm0,                                  // outer arm 0 (the nested switch)
            NodePath.Arm(outerArm0, 0),                 // inner arm 5
            NodePath.Arm(outerArm0, 1),                 // inner arm 9
            NodePath.Arm(NodePath.Top, 1),              // outer arm 1
        }, recorder.SubScopePaths);
    }
}
