// ─────────────────────────────────────────────────────────────────────────────
// Phase 6 acceptance tests for SyncService + WorkflowSession.
//
// Covers the KS edit round-trip:
//   • KS edit round-trip: session initial IR → KS edit → session.Ir updated
//   • Layout preservation: editing one Print doesn't disturb other node coordinates
//   • IrChanged fires on edit with correct AffectedPaths
//   • Empty edit (same text) does NOT fire IrChanged
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Session;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class SessionTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public SessionTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private (SyncService svc, WorkflowSession session) MakeSession(string initialBs)
    {
        var ir = _fixture.KsLens.Parse(initialBs, []);
        var session = new WorkflowSession(ir);
        var svc = new SyncService(_fixture.Registry);
        return (svc, session);
    }

    [Fact]
    public void KS_Edit_Round_Trip_Adds_Print()
    {
        var (svc, session) = MakeSession("Print(\"a\")\n");
        var changeSet = svc.ApplyKsEdit(session, "Print(\"a\")\nPrint(\"b\")\n");
        Assert.NotNull(changeSet.StatementDiff);
        Assert.False(changeSet.StatementDiff!.IsEmpty);
        // The session's IR should now contain both Print statements.
        Assert.Equal(2, session.Ir.Body.Length);
    }

    [Fact]
    public void IrChanged_Fires_On_Edit()
    {
        var (svc, session) = MakeSession("Print(\"a\")\n");
        int fireCount = 0;
        WorkflowChangeSet? receivedChangeSet = null;
        session.IrChanged += cs => { fireCount++; receivedChangeSet = cs; };

        svc.ApplyKsEdit(session, "Print(\"a\")\nPrint(\"b\")\n");

        Assert.Equal(1, fireCount);
        Assert.NotNull(receivedChangeSet);
        Assert.NotEmpty(receivedChangeSet!.AffectedPaths);
    }

    [Fact]
    public void Empty_Edit_Does_Not_Fire()
    {
        var (svc, session) = MakeSession("Print(\"a\")\n");
        int fireCount = 0;
        session.IrChanged += _ => fireCount++;

        // Same KS text → no change → no event.
        svc.ApplyKsEdit(session, "Print(\"a\")\n");

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void KS_Edit_Preserves_Other_Node_Coordinates()
    {
        // Two Print statements; the first has a Layout annotation. Edit the second;
        // the first's Layout must survive the edit round-trip.
        var ir = _fixture.KsLens.Parse("Print(\"a\")\nPrint(\"b\")\n", []);

        // Attach a Layout annotation to the first statement.
        var layoutAnn = new Annotation
        {
            Kind = "Layout",
            Key = "node0",
            Value = AnnotationValue.Layout(50, 75),
        };
        var firstStmt = ir.Body[0];
        ir = ir with
        {
            Body = [firstStmt with { Annotations = [layoutAnn] }, ..ir.Body[1..]],
        };

        var session = new WorkflowSession(ir);
        var svc = new SyncService(_fixture.Registry);

        // Edit: change the second Print's argument.
        svc.ApplyKsEdit(session, "Print(\"a\")\nPrint(\"c\")\n");

        // The first statement's Layout annotation must be preserved.
        var resultFirst = session.Ir.Body[0];
        Assert.Contains(resultFirst.Annotations, a => a.Kind == "Layout" && a.Key == "node0");
        var layout = Assert.Single(resultFirst.Annotations, a => a.Kind == "Layout");
        var layoutVal = Assert.IsType<LayoutValue>(layout.Value);
        Assert.Equal(50, layoutVal.X);
        Assert.Equal(75, layoutVal.Y);
    }

    [Fact]
    public void KS_Edit_Remove_Statement_Updates_Ir()
    {
        var (svc, session) = MakeSession("Print(\"a\")\nPrint(\"b\")\n");
        svc.ApplyKsEdit(session, "Print(\"a\")\n");
        Assert.Single(session.Ir.Body);
    }

    // ── 5.5: 声明区编辑（const/var 块）不再静默失效 ─────────────────────────

    [Fact]
    public void KS_Edit_Const_Value_Updates_Session_Ir()
    {
        var (svc, session) = MakeSession("const {\n    int x = 5\n}\nPrint(x)\n");
        int fireCount = 0;
        session.IrChanged += _ => fireCount++;

        // Only the const value changes — the body is identical.
        var changeSet = svc.ApplyKsEdit(session, "const {\n    int x = 6\n}\nPrint(x)\n");

        Assert.NotNull(changeSet.StatementDiff);
        Assert.False(changeSet.StatementDiff!.IsEmpty);
        Assert.Single(changeSet.StatementDiff.DeclarationChanges);
        Assert.Equal("6", session.Ir.Constants["x"].InitialValueExpression);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void KS_Edit_GlobalVar_Edit_Updates_Session_Ir()
    {
        var (svc, session) = MakeSession("var {\n    int counter\n}\nPrint(counter)\n");
        svc.ApplyKsEdit(session, "var {\n    string counter\n}\nPrint(counter)\n");
        Assert.Equal("string", session.Ir.GlobalVars["counter"].Type);
    }

    [Fact]
    public void KS_Edit_Adds_Const_To_Session_Ir()
    {
        var (svc, session) = MakeSession("const {\n    int a = 1\n}\nPrint(a)\n");
        svc.ApplyKsEdit(session, "const {\n    int a = 1\n    int b = 2\n}\nPrint(a)\nPrint(b)\n");
        Assert.True(session.Ir.Constants.ContainsKey("b"));
        Assert.Equal(2, session.Ir.Body.Length);
    }

    [Fact]
    public void KS_Edit_Unchanged_Const_Does_Not_Fire()
    {
        var (svc, session) = MakeSession("const {\n    int x = 5\n}\nPrint(x)\n");
        int fireCount = 0;
        session.IrChanged += _ => fireCount++;
        // Same text (same const value) → no diff → no event.
        svc.ApplyKsEdit(session, "const {\n    int x = 5\n}\nPrint(x)\n");
        Assert.Equal(0, fireCount);
    }
}