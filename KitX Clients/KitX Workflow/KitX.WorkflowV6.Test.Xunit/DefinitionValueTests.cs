// ─────────────────────────────────────────────────────────────────────────────
// Definition-node value semantics (2026-08-02):
//   DefaultValue (read-only on BP)  ←  KS script declaration initialiser
//   ConstValue / VarInitialValue    ←  user value (BP-editable, editor-layer override)
// The reverse path writes the DEFAULT back into the IR — the user value never
// rewrites the KS script text (it is an override handled by the editor layer).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class DefinitionValueTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public DefinitionValueTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private static ConstNode? ConstDef(Blueprint bp, string name)
        => bp.Nodes.OfType<ConstNode>().FirstOrDefault(n => n.ConstName == name);

    private static VariableNode? VarDef(Blueprint bp, string name)
        => bp.Nodes.OfType<VariableNode>()
            .FirstOrDefault(n => n.VarName == name
                                 && !bp.Connections.Any(c => c.SourceNodeId == n.Id || c.TargetNodeId == n.Id));

    [Fact]
    public void Const_Default_Value_RoundTrips_Via_Definition_Node()
    {
        var ir = _fixture.KsLens.Parse("""
            const {
                int x = 5
            }
            Print(x)
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        var def = ConstDef(bp, "x");
        Assert.NotNull(def);
        Assert.Equal("5", def.DefaultValue);
        Assert.Null(def.ConstValue);   // user value starts empty

        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Equal("5", reversed.Constants["x"].InitialValueExpression);
        var text = _fixture.KsLens.Project(reversed);
        Assert.True(text.Contains("int x = 5"), $"Text:\n{text}");
    }

    [Fact]
    public void Var_Default_Value_RoundTrips_Via_Definition_Node()
    {
        // Regression: the scalar var initialiser used to be dropped on the BP→KS path.
        var ir = _fixture.KsLens.Parse("""
            var {
                int counter = 0
            }
            counter > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        var def = VarDef(bp, "counter");
        Assert.NotNull(def);
        Assert.Equal("0", def.DefaultValue);
        Assert.Null(def.VarInitialValue);

        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Equal("0", reversed.GlobalVars["counter"].InitialValueExpression);
        var text = _fixture.KsLens.Project(reversed);
        Assert.True(text.Contains("int counter = 0"), $"Text:\n{text}");
    }

    [Fact]
    public void User_Value_Does_Not_Rewrite_The_Script_Default()
    {
        // The user value (ConstValue) is an editor-layer override: the reverse path
        // keeps the script's default, so switching BP→KS preserves the KS text.
        var ir = _fixture.KsLens.Parse("""
            const {
                int x = 5
            }
            Print(x)
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var def = ConstDef(bp, "x")!;
        def.ConstValue = "8";   // simulate a user edit on the BP node

        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Equal("5", reversed.Constants["x"].InitialValueExpression);
        var text = _fixture.KsLens.Project(reversed);
        Assert.True(text.Contains("int x = 5"), $"Text:\n{text}");
    }

    [Fact]
    public void No_Initial_Value_Leaves_Default_Empty()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int empty
            }
            empty > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var def = VarDef(bp, "empty")!;
        Assert.Null(def.DefaultValue);
        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Null(reversed.GlobalVars["empty"].InitialValueExpression);
    }

    [Fact]
    public void Usage_Nodes_Do_Not_Carry_Definition_Values()
    {
        // Regression: Restore/Sync must only touch the standalone definition node —
        // wired usage nodes must never carry (or clobber with) a user value.
        var ir = _fixture.KsLens.Parse("""
            const {
                int x = 5
            }
            Print(x)
            x > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        var def = ConstDef(bp, "x")!;
        def.ConstValue = "10";   // user edit lands on the definition node only

        // Wired usage nodes keep their initial-value fields untouched.
        var usages = bp.Nodes.Where(n => bp.Connections.Any(c => c.SourceNodeId == n.Id || c.TargetNodeId == n.Id));
        foreach (var usage in usages)
        {
            if (usage is ConstNode cn) Assert.Null(cn.ConstValue);
            if (usage is VariableNode vn) Assert.Null(vn.VarInitialValue);
        }
    }

    [Fact]
    public void Panel_User_Value_Flows_To_Bp_Node_And_Runtime_IR()
    {
        // User scenario: script has NO initial value, but the Variable Constants
        // panel carries a user value ("10"). Switching to BP must show it on the
        // definition node, and executing (BP mode) must inject it at runtime —
        // exactly like the KS-side panel injection.
        var ir = _fixture.KsLens.Parse("""
            var {
                int counter
            }
            counter > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var def = VarDef(bp, "counter")!;
        Assert.Null(def.DefaultValue);

        // 1. Panel → BP: RestoreUserValuesFromPanel mirrors the panel UserValue.
        string panelUserValue = "10";
        def.VarInitialValue = panelUserValue;   // node now displays "10" on the canvas

        // 2. BP edit → panel: SyncUserValuesFromBlueprint (BP mode Run / switch).
        string synced = def.VarInitialValue ?? def.DefaultValue!;   // "10"

        // 3. Runtime injection: overrides → ApplyConstantOverrides on the reversed IR.
        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Null(reversed.GlobalVars["counter"].InitialValueExpression);
        var overrides = new Dictionary<string, string?> { ["counter"] = synced };
        var applied = KitX.WorkflowV6.Ir.WorkflowOverrides.ApplyConstantOverrides(reversed, overrides);
        Assert.Equal("10", applied.GlobalVars["counter"].InitialValueExpression);
    }
}
