// ─────────────────────────────────────────────────────────────────────────────
// Phase 5 acceptance tests for WorkflowDiffer + WorkflowDiffApply.
//
// Covers the diff engine and applier:
//   • Empty vs empty → empty diff
//   • Add one Print → 1 Added change
//   • Modify one Print → 1 Modified change
//   • Remove one Print → 1 Removed change
//   • Layout preserved for unchanged statements
//   • Apply(baseline, Compute(baseline, new)) ≡ new (idempotence)
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class DiffTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public DiffTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private Workflow Parse(params string[] lines)
    {
        var src = string.Join('\n', lines) + '\n';
        return _fixture.KsLens.Parse(src, []);
    }

    [Fact]
    public void Diff_Empty_Empty_Is_Empty()
    {
        var diff = WorkflowDiffer.Compute(new Workflow(), new Workflow());
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_Add_One_Print()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"a\")", "Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.Single(diff.StatementChanges);
        var change = diff.StatementChanges[0];
        Assert.Equal(DiffKind.Added, change.Kind);
        Assert.NotNull(change.NewValue);
    }

    [Fact]
    public void Diff_Remove_One_Print()
    {
        var a = Parse("Print(\"a\")", "Print(\"b\")");
        var b = Parse("Print(\"a\")");
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.Single(diff.StatementChanges);
        Assert.Equal(DiffKind.Removed, diff.StatementChanges[0].Kind);
    }

    [Fact]
    public void Diff_Modify_One_Print()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        // Same Kind (Pipeline), different fingerprint → Modified (not Remove+Add).
        var changes = diff.StatementChanges;
        Assert.Contains(changes, c => c.Kind == DiffKind.Modified);
    }

    [Fact]
    public void Diff_Unchanged_Not_Reported()
    {
        var a = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")");
        var b = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")");
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Apply_Idempotent_Add()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"a\")", "Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        var result = WorkflowDiffApply.Apply(a, diff);
        Assert.Equal(b, result);
    }

    [Fact]
    public void Apply_Idempotent_Remove()
    {
        var a = Parse("Print(\"a\")", "Print(\"b\")");
        var b = Parse("Print(\"a\")");
        var diff = WorkflowDiffer.Compute(a, b);
        var result = WorkflowDiffApply.Apply(a, diff);
        Assert.Equal(b, result);
    }

    [Fact]
    public void Apply_Idempotent_Modify()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        var result = WorkflowDiffApply.Apply(a, diff);
        Assert.Equal(b, result);
    }

    [Fact]
    public void Apply_Layout_Preserved_For_Unchanged()
    {
        // Two Print statements; the first has a Layout annotation. Modify the second;
        // the first's Layout must survive the diff+apply round-trip.
        var lit = new KsLiteral { Kind = KsLiteralKind.String, Value = "a", SourceText = "\"a\"" };
        var lit2 = new KsLiteral { Kind = KsLiteralKind.String, Value = "b", SourceText = "\"b\"" };
        var layoutAnn = new Annotation
        {
            Kind = "Layout",
            Key = "node1",
            Value = AnnotationValue.Layout(100, 200),
        };
        var stmt1 = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [new Segment { Target = "Print", Arguments = [lit] }],
            Annotations = [layoutAnn],
        };
        stmt1 = stmt1 with { Fingerprint = Fingerprint.Compute(stmt1) };

        var stmt2Old = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [new KsLiteral { Kind = KsLiteralKind.String, Value = "b", SourceText = "\"b\"" }],
            Segments = [new Segment { Target = "Print" }],
        };
        stmt2Old = stmt2Old with { Fingerprint = Fingerprint.Compute(stmt2Old) };

        var baseline = new Workflow { Body = [stmt1, stmt2Old] };

        // Build new IR: same stmt1, modified stmt2 (Print("c") instead of Print("b"))
        var stmt2New = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [new KsLiteral { Kind = KsLiteralKind.String, Value = "c", SourceText = "\"c\"" }],
            Segments = [new Segment { Target = "Print" }],
        };
        stmt2New = stmt2New with { Fingerprint = Fingerprint.Compute(stmt2New) };
        var newIr = new Workflow { Body = [stmt1, stmt2New] };

        var diff = WorkflowDiffer.Compute(baseline, newIr);
        var result = WorkflowDiffApply.Apply(baseline, diff);

        // stmt1's Layout annotation must be preserved.
        var resultStmt1 = result.Body[0];
        Assert.Contains(resultStmt1.Annotations, a => a.Kind == "Layout" && a.Key == "node1");
    }

    [Fact]
    public void Diff_Recurses_Into_If_Body_Changes()
    {
        var old = Parse("if cond:", "    Print(\"a\")");
        var nws = Parse("if cond:", "    Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.LexicalPath.StartsWith("/0/then"));
    }

    [Fact]
    public void Diff_Recurses_Into_ForEach_Body()
    {
        var old = Parse("forEach Range(0, 3, 1) as i:", "    Print(\"a\")");
        var nws = Parse("forEach Range(0, 3, 1) as i:", "    Print(\"a\")", "    Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.LexicalPath.StartsWith("/0/body") && c.Kind == DiffKind.Added);
    }

    [Fact]
    public void Diff_Top_Level_Still_Produces_Simple_Paths()
    {
        var old = Parse("Print(\"a\")");
        var nws = Parse("Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.All(diff.StatementChanges, c => Assert.False(c.LexicalPath.Substring(1).Contains('/')));
    }

    // ── A3 修复轮: 新增 3 个 P0 测试 ─────────────────────────────────────────

    [Fact]
    public void Diff_Includes_Switch_Default_Arm_Changes()
    {
        var old = Parse("switch sel:",
            "    0:",
            "        Print(\"a\")",
            "    default:",
            "        Print(\"d\")");
        var nws = Parse("switch sel:",
            "    0:",
            "        Print(\"a\")",
            "    default:",
            "        Print(\"e\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.LexicalPath.Contains("/default"));
    }

    [Fact]
    public void Diff_Includes_Container_Non_Body_Field_Changes()
    {
        var old = Parse("if 1:", "    Print(\"yes\")");
        var nws = Parse("if 2:", "    Print(\"yes\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.LexicalPath == "/0" && c.Kind == DiffKind.Modified);
    }

    [Fact]
    public void Apply_Nested_Change_Via_Whole_Container_Modified()
    {
        var old = Parse("if cond:", "    Print(\"a\")");
        var nws = Parse("if cond:", "    Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var applied = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, applied);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // E5.4: WorkflowDiffApply 补测 — 嵌套作用域边界 case
    // ═════════════════════════════════════════════════════════════════════════

    // ── 组 1: 路径处理单元测试（通过 Apply 入口间接测）─────────────────────────

    [Fact]
    public void Apply_Direct_Child_Path_Gets_Applied()
    {
        var baseline = Parse("Print(\"a\")");
        var newPrint = Parse("Print(\"b\")").Body[0];
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/0",
                Fingerprint = newPrint.Fingerprint,
                Kind = DiffKind.Modified,
                NewValue = newPrint,
                Index = 0,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(newPrint.Fingerprint, result.Body[0].Fingerprint);
    }

    [Fact]
    public void Apply_Nested_Only_Path_Is_Skipped()
    {
        var baseline = Parse("if cond:", "    Print(\"a\")");
        var newPrint = Parse("Print(\"b\")").Body[0];
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/0/then/0",
                Fingerprint = newPrint.Fingerprint,
                Kind = DiffKind.Modified,
                NewValue = newPrint,
                Index = 0,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(baseline, result);
    }

    [Fact]
    public void Apply_Different_Branch_Path_Is_Skipped()
    {
        var baseline = Parse("if cond:", "    Print(\"a\")");
        var newPrint = Parse("Print(\"b\")").Body[0];
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/0/else/0",
                Fingerprint = newPrint.Fingerprint,
                Kind = DiffKind.Added,
                NewValue = newPrint,
                Index = 0,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(baseline, result);
    }

    // ── 组 2: 端到端 Apply 场景 ──────────────────────────────────────────────

    [Fact]
    public void Apply_Removed_At_Top_Level_Shifts_Later_Indices()
    {
        var old = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")", "Print(\"d\")");
        var nws = Parse("Print(\"b\")", "Print(\"c\")", "Print(\"d2\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Mixed_Add_Remove_Modify_At_Top_Level()
    {
        var old = Parse("Print(\"a\")", "Print(\"b\")");
        var nws = Parse("Print(\"a2\")", "if cond:", "    Print(\"c\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Modified);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Empty_Diff_Returns_Original()
    {
        var baseline = Parse("Print(\"a\")");
        var diff = new WorkflowDiff { StatementChanges = [] };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Same(baseline, result);
    }

    [Fact]
    public void Apply_Modified_With_Null_NewValue_No_Op()
    {
        var baseline = Parse("Print(\"a\")");
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/0",
                Fingerprint = Fingerprint.Compute("placeholder"),
                Kind = DiffKind.Modified,
                NewValue = null,
                Index = 0,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(baseline, result);
    }

    [Fact]
    public void Apply_Added_At_Specific_Index_Inserts()
    {
        var baseline = Parse("Print(\"b\")");
        var newPrint = Parse("Print(\"a\")").Body[0];
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/0",
                Fingerprint = newPrint.Fingerprint,
                Kind = DiffKind.Added,
                NewValue = newPrint,
                Index = 0,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(2, result.Body.Length);
        var expected = Parse("Print(\"a\")", "Print(\"b\")");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_Added_At_End_Appends()
    {
        var baseline = Parse("Print(\"a\")");
        var newPrint = Parse("Print(\"b\")").Body[0];
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/999",
                Fingerprint = newPrint.Fingerprint,
                Kind = DiffKind.Added,
                NewValue = newPrint,
                Index = 999,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(2, result.Body.Length);
        var expected = Parse("Print(\"a\")", "Print(\"b\")");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_Removed_At_Invalid_Index_No_Op()
    {
        var baseline = Parse("Print(\"a\")");
        var diff = new WorkflowDiff
        {
            StatementChanges = [new StatementChange
            {
                LexicalPath = "/999",
                Fingerprint = baseline.Body[0].Fingerprint,
                Kind = DiffKind.Removed,
                NewValue = null,
                Index = 999,
            }]
        };
        var result = WorkflowDiffApply.Apply(baseline, diff);
        Assert.Equal(baseline, result);
    }

    // ── 组 3: 嵌套作用域（通过整体 Modified 覆盖）──────────────────────────────

    [Fact]
    public void Apply_Nested_While_Body_Change()
    {
        var old = Parse("while cond:", "    Print(\"a\")");
        var nws = Parse("while cond:", "    Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Nested_ForEach_Body_Change()
    {
        var old = Parse("forEach Range(0, 3, 1) as i:", "    Print(\"a\")");
        var nws = Parse("forEach Range(0, 3, 1) as i:", "    Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Nested_Switch_Arm_Change()
    {
        var old = Parse("switch sel:", "    0:", "        Print(\"a\")", "    default:", "        Print(\"d\")");
        var nws = Parse("switch sel:", "    0:", "        Print(\"b\")", "    default:", "        Print(\"d\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Nested_Switch_Default_Change()
    {
        var old = Parse("switch sel:", "    0:", "        Print(\"a\")", "    default:", "        Print(\"d\")");
        var nws = Parse("switch sel:", "    0:", "        Print(\"a\")", "    default:", "        Print(\"e\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Deep_Nesting_Two_Levels()
    {
        var old = Parse("if cond1:", "    if cond2:", "        Print(\"a\")");
        var nws = Parse("if cond1:", "    if cond2:", "        Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    // ── 组 4: Layout 保留 ──────────────────────────────────────────────────

    [Fact]
    public void Apply_Layout_Preserved_For_Unchanged_Container_Body()
    {
        var layoutAnn = new Annotation
        {
            Kind = "Layout",
            Key = "c1",
            Value = AnnotationValue.Layout(100, 200),
        };

        var cond = new KsIdentifier { Name = "cond" };

        var litA = new KsLiteral { Kind = KsLiteralKind.String, Value = "a", SourceText = "\"a\"" };
        var innerA = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [litA],
            Segments = [new Segment { Target = "Print", Arguments = [litA] }],
        };
        innerA = innerA with { Fingerprint = Fingerprint.Compute(innerA) };

        var oldContainer = new IfStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Condition = cond,
            ThenBody = [innerA],
            Annotations = [layoutAnn],
        };
        oldContainer = oldContainer with { Fingerprint = Fingerprint.Compute(oldContainer) };

        var oldWorkflow = new Workflow { Body = [oldContainer] };

        var litB = new KsLiteral { Kind = KsLiteralKind.String, Value = "b", SourceText = "\"b\"" };
        var innerB = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [litB],
            Segments = [new Segment { Target = "Print", Arguments = [litB] }],
        };
        innerB = innerB with { Fingerprint = Fingerprint.Compute(innerB) };

        var newContainer = new IfStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Condition = cond,
            ThenBody = [innerB],
        };
        newContainer = newContainer with { Fingerprint = Fingerprint.Compute(newContainer) };

        var newWorkflow = new Workflow { Body = [newContainer] };

        var diff = WorkflowDiffer.Compute(oldWorkflow, newWorkflow);
        var result = WorkflowDiffApply.Apply(oldWorkflow, diff);

        Assert.Contains(result.Body[0].Annotations, a => a.Kind == "Layout" && a.Key == "c1");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5.5: 声明区（Constants / GlobalVars / HelperFunctions）diff + apply
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Diff_Const_Value_Edit_Is_Reported()
    {
        var old = Parse("const {", "    int x = 5", "}", "x > Print");
        var nws = Parse("const {", "    int x = 6", "}", "x > Print");
        var diff = WorkflowDiffer.Compute(old, nws);
        // Body is unchanged — only the declaration section differs.
        Assert.True(diff.StatementChanges.IsEmpty);
        var decl = Assert.Single(diff.DeclarationChanges);
        Assert.Equal(DeclarationSection.Constants, decl.Section);
        Assert.Equal("x", decl.Name);
        Assert.Equal(DiffKind.Modified, decl.Kind);
        var constant = Assert.IsType<Constant>(decl.NewValue);
        Assert.Equal("6", constant.InitialValueExpression);
    }

    [Fact]
    public void Apply_Const_Value_Edit_Updates_Workflow_Constants()
    {
        var old = Parse("const {", "    int x = 5", "}", "x > Print");
        var nws = Parse("const {", "    int x = 6", "}", "x > Print");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
        Assert.Equal("6", result.Constants["x"].InitialValueExpression);
    }

    [Fact]
    public void Diff_And_Apply_Const_Added_And_Removed()
    {
        var old = Parse("const {", "    int a = 1", "}", "a > Print");
        var nws = Parse("const {", "    int b = 2", "}", "b > Print");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.DeclarationChanges, c =>
            c.Section == DeclarationSection.Constants && c.Name == "a" && c.Kind == DiffKind.Removed);
        Assert.Contains(diff.DeclarationChanges, c =>
            c.Section == DeclarationSection.Constants && c.Name == "b" && c.Kind == DiffKind.Added);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Diff_And_Apply_GlobalVar_Edit()
    {
        var old = Parse("var {", "    int counter", "}", "counter > Print");
        var nws = Parse("var {", "    string counter", "}", "counter > Print");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.True(diff.StatementChanges.IsEmpty);
        var decl = Assert.Single(diff.DeclarationChanges);
        Assert.Equal(DeclarationSection.GlobalVars, decl.Section);
        Assert.Equal("counter", decl.Name);
        Assert.Equal(DiffKind.Modified, decl.Kind);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
        Assert.Equal("string", result.GlobalVars["counter"].Type);
    }

    [Fact]
    public void Diff_Only_Declaration_Change_Is_Not_Empty()
    {
        var old = Parse("const {", "    int x = 5", "}", "x > Print");
        var nws = Parse("const {", "    int x = 6", "}", "x > Print");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void Diff_And_Apply_HelperFunction_Edit()
    {
        var old = new Workflow
        {
            Body = [],
            HelperFunctions =
            [
                new HelperFunction { Name = "H", ReturnType = "int", Code = "return 1;" },
            ],
        };
        var nws = new Workflow
        {
            Body = [],
            HelperFunctions =
            [
                new HelperFunction { Name = "H", ReturnType = "int", Code = "return 2;" },
            ],
        };
        var diff = WorkflowDiffer.Compute(old, nws);
        var decl = Assert.Single(diff.DeclarationChanges);
        Assert.Equal(DeclarationSection.HelperFunctions, decl.Section);
        Assert.Equal("H", decl.Name);
        Assert.Equal(DiffKind.Modified, decl.Kind);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
        Assert.Equal("return 2;", result.HelperFunctions[0].Code);
    }

    [Fact]
    public void Diff_HelperFunctions_Reference_Equal_Not_Reported()
    {
        // Same HelperFunction instance on both sides (the ApplyKsEdit flow reuses
        // session.HelperFunctions) → no declaration change.
        var helper = new HelperFunction { Name = "H", ReturnType = "int", Code = "return 1;" };
        var old = new Workflow { Body = [], HelperFunctions = [helper] };
        var nws = new Workflow { Body = [], HelperFunctions = [helper] };
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.True(diff.IsEmpty);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5.6: move 重排应用（ApplyChangesToScope 按目标索引重建）
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_Pure_Adjacent_Reorder()
    {
        // [A,B] → [B,A]: no remove-then-insert order can represent this; the applier
        // must rebuild the list from the old→new slot mapping.
        var old = Parse("Print(\"a\")", "Print(\"b\")");
        var nws = Parse("Print(\"b\")", "Print(\"a\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Pure_Rotate_Reorder()
    {
        // [A,B,C] → [C,A,B] (cyclic shift).
        var old = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")");
        var nws = Parse("Print(\"c\")", "Print(\"a\")", "Print(\"b\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Reorder_With_Content_Edit()
    {
        // Mixed reorder + content edit: [a,b,c] → [c,b2,a]. Because the edited b2 sits
        // before the LCS boundary it is diffed as Removed(b)+Added(b2) — the applier
        // must still rebuild the target list correctly.
        var old = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")");
        var nws = Parse("Print(\"c\")", "Print(\"b2\")", "Print(\"a\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Modified_Index_Shifts_After_Removal()
    {
        // A removal before a Modified slot shifts the Modified statement's target
        // index: old code applied it against the shrunken list and dropped the edit.
        var old = Parse("Print(\"d\")", "Print(\"a\")", "Print(\"b\")", "Print(\"x\")");
        var nws = Parse("Print(\"a\")", "Print(\"e\")", "Print(\"b\")", "Print(\"x2\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Modified);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Modified_With_Shifted_Old_Index()
    {
        // Modified where the old slot (2) differs from the new slot (1) — the diff
        // must carry OldIndex so the applier replaces the right baseline statement.
        var old = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"x\")");
        var nws = Parse("Print(\"b\")", "Print(\"x2\")");
        var diff = WorkflowDiffer.Compute(old, nws);
        var modified = Assert.Single(diff.StatementChanges, c => c.Kind == DiffKind.Modified);
        Assert.Equal(2, modified.OldIndex);
        Assert.Equal(1, modified.Index);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
    }

    [Fact]
    public void Apply_Reorder_Preserves_Layout_Of_Moved_Statement()
    {
        // A moved (LCS-kept) statement keeps its Layout because the applier reuses
        // the baseline instance for unchanged slots.
        var lit = new KsLiteral { Kind = KsLiteralKind.String, Value = "a", SourceText = "\"a\"" };
        var layoutAnn = new Annotation
        {
            Kind = "Layout",
            Key = "node1",
            Value = AnnotationValue.Layout(100, 200),
        };
        var stmtA = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [new Segment { Target = "Print", Arguments = [lit] }],
            Annotations = [layoutAnn],
        };
        stmtA = stmtA with { Fingerprint = Fingerprint.Compute(stmtA) };
        var stmtB = Parse("Print(\"b\")").Body[0];

        var old = new Workflow { Body = [stmtA, stmtB] };
        var nws = new Workflow { Body = [stmtB, stmtA] };

        var diff = WorkflowDiffer.Compute(old, nws);
        var result = WorkflowDiffApply.Apply(old, diff);
        Assert.Equal(nws, result);
        Assert.Contains(result.Body[1].Annotations, a => a.Kind == "Layout" && a.Key == "node1");
    }
}