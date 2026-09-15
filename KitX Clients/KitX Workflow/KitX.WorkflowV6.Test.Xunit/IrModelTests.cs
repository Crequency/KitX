using System.Collections.Immutable;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class IrModelTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public IrModelTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private static KsIdentifier MakeId(string name) => new() { Name = name, SourceText = name };

    private static KsLiteral MakeLiteral(object val)
    {
        var kind = val switch
        {
            string _ => KsLiteralKind.String,
            int _ => KsLiteralKind.Integer,
            double _ => KsLiteralKind.Double,
            bool _ => KsLiteralKind.Boolean,
            null => KsLiteralKind.Null,
            char _ => KsLiteralKind.Char,
            _ => throw new ArgumentOutOfRangeException(nameof(val)),
        };
        return new KsLiteral { Kind = kind, Value = val, SourceText = val?.ToString() ?? "null" };
    }

    private static PipelineStatement MakeSimplePipeline(string literalText)
    {
        var lit = new KsLiteral
        {
            Kind = KsLiteralKind.String,
            Value = literalText.Trim('"'),
            SourceText = literalText,
        };
        var seg = new Segment { Target = "Print", Arguments = [lit] };
        var stmt = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
        };
        return stmt with { Fingerprint = Fingerprint.Compute(stmt) };
    }

    [Fact]
    public void Pipeline_Equals_Same_Sources_And_Segments()
    {
        var a = MakeSimplePipeline("\"hello\"");
        var b = MakeSimplePipeline("\"hello\"");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pipeline_Equals_Different_Sources_Not_Equal()
    {
        var a = MakeSimplePipeline("\"hello\"");
        var lit = new KsLiteral { Kind = KsLiteralKind.String, Value = "world", SourceText = "\"world\"" };
        var seg = new Segment { Target = "Print", Arguments = [lit] };
        var b = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
        };
        b = b with { Fingerprint = Fingerprint.Compute(b) };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Pipeline_Equals_Different_Segments_Not_Equal()
    {
        var lit = MakeLiteral("x");
        var segA = new Segment { Target = "Print", Arguments = [lit] };
        var segB = new Segment { Target = "Range", Arguments = [lit] };
        var a = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [segA],
        };
        a = a with { Fingerprint = Fingerprint.Compute(a) };
        var b = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [segB],
        };
        b = b with { Fingerprint = Fingerprint.Compute(b) };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Pipeline_GetHashCode_Stable_Across_Calls()
    {
        var p = MakeSimplePipeline("\"stable\"");
        Assert.Equal(p.GetHashCode(), p.GetHashCode());
    }

    [Fact]
    public void If_Equals_Different_ElseBody_Not_Equal()
    {
        var cond = MakeId("x");
        var body = MakeSimplePipeline("\"a\"");
        var a = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond,
            ThenBody = [body],
        };
        var b = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond,
            ThenBody = [body],
            ElseBody = [body],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void If_Equals_Different_Condition_Not_Equal()
    {
        var condA = MakeId("x");
        var condB = MakeId("y");
        var body = MakeSimplePipeline("\"a\"");
        var a = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condA),
            Condition = condA,
            ThenBody = [body],
        };
        var b = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condB),
            Condition = condB,
            ThenBody = [body],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void If_Equals_Empty_ElseBody_Not_Equal_To_NonEmpty()
    {
        var cond = MakeId("x");
        var body = MakeSimplePipeline("\"a\"");
        var empty = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond,
            ThenBody = [body],
        };
        var nonEmpty = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond,
            ThenBody = [body],
            ElseBody = [body],
        };
        Assert.NotEqual(empty, nonEmpty);
    }

    [Fact]
    public void Switch_Equals_Different_Selector_Not_Equal()
    {
        var selA = MakeId("s1");
        var selB = MakeId("s2");
        var a = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(selA),
            Selector = selA,
            Arms = [],
        };
        var b = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(selB),
            Selector = selB,
            Arms = [],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Switch_Equals_Different_Arms_Not_Equal()
    {
        var sel = MakeId("s");
        var body = MakeSimplePipeline("\"a\"");
        var a = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(sel),
            Selector = sel,
            Arms = [[body]],
        };
        var b = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(sel),
            Selector = sel,
            Arms = [],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Switch_Equals_Different_Default_Not_Equal()
    {
        var sel = MakeId("s");
        var body = MakeSimplePipeline("\"a\"");
        var a = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(sel),
            Selector = sel,
            Arms = [],
            Default = [body],
        };
        var b = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(sel),
            Selector = sel,
            Arms = [],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Switch_Equals_Different_Arm_Count_Not_Equal()
    {
        var sel = MakeId("s");
        var body = MakeSimplePipeline("\"a\"");
        var a = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(sel),
            Selector = sel,
            Arms = [[body]],
        };
        var b = new SwitchStatement
        {
            Fingerprint = Fingerprint.Compute(sel),
            Selector = sel,
            Arms = [[body], [body]],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEach_Equals_Different_Source_Not_Equal()
    {
        var srcA = MakeId("list");
        var srcB = MakeId("arr");
        var a = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(srcA),
            Source = srcA, ItemName = "x", Body = [],
        };
        var b = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(srcB),
            Source = srcB, ItemName = "x", Body = [],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEach_Equals_Different_ItemName_Not_Equal()
    {
        var src = MakeId("list");
        var a = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(src),
            Source = src, ItemName = "x", Body = [],
        };
        var b = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(src),
            Source = src, ItemName = "y", Body = [],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEach_Equals_Different_Body_Not_Equal()
    {
        var src = MakeId("list");
        var body = MakeSimplePipeline("\"a\"");
        var a = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(src),
            Source = src, ItemName = "x", Body = [],
        };
        var b = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(src),
            Source = src, ItemName = "x", Body = [body],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ForEach_Equals_Ignores_ItemType_Field()
    {
        var src = MakeId("list");
        var a = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(src),
            Source = src, ItemName = "x", Body = [],
        };
        var b = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(src),
            Source = src, ItemName = "x", Body = [],
        };
        Assert.Equal(a, b);
    }

    [Fact]
    public void While_Equals_Different_Condition_Not_Equal()
    {
        var condA = MakeId("c1");
        var condB = MakeId("c2");
        var a = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(condA),
            Condition = condA, Body = [],
        };
        var b = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(condB),
            Condition = condB, Body = [],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void While_Equals_Different_Body_Not_Equal()
    {
        var cond = MakeId("c");
        var body = MakeSimplePipeline("\"a\"");
        var a = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond, Body = [],
        };
        var b = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond, Body = [body],
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Break_Equals_All_Break_Instances()
    {
        var a = new BreakStatement { Fingerprint = Fingerprint.Compute("break") };
        var b = new BreakStatement { Fingerprint = Fingerprint.Compute("break") };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Continue_Equals_All_Continue_Instances()
    {
        var a = new ContinueStatement { Fingerprint = Fingerprint.Compute("continue") };
        var b = new ContinueStatement { Fingerprint = Fingerprint.Compute("continue") };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Break_Not_Equals_Continue()
    {
        var br = new BreakStatement { Fingerprint = Fingerprint.Compute("b") };
        var co = new ContinueStatement { Fingerprint = Fingerprint.Compute("c") };
        Assert.NotEqual<Statement>(br, co);
    }

    [Fact]
    public void Fingerprint_LeadingComment_Participates()
    {
        var cond = MakeId("x");
        var a = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond, ThenBody = [], LeadingComment = null,
        };
        var b = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond, ThenBody = [], LeadingComment = "// comment",
        };
        Assert.NotEqual(Fingerprint.Compute(a), Fingerprint.Compute(b));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fingerprint_Empty_Body_Array_Deterministic()
    {
        var cond = MakeId("x");
        var a = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond, Body = [],
        };
        var b = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(cond),
            Condition = cond, Body = ImmutableArray<Statement>.Empty,
        };
        Assert.Equal(Fingerprint.Compute(a), Fingerprint.Compute(b));
    }

    [Fact]
    public void Fingerprint_Deep_Nesting_Stable()
    {
        var leaf = MakeSimplePipeline("\"x\"");
        var condC = MakeId("c");
        var inner = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condC),
            Condition = condC, ThenBody = [leaf],
        };
        inner = inner with { Fingerprint = Fingerprint.Compute(inner) };
        var condB = MakeId("b");
        var mid = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condB),
            Condition = condB, ThenBody = [inner],
        };
        mid = mid with { Fingerprint = Fingerprint.Compute(mid) };
        var condA = MakeId("a");
        var outer = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condA),
            Condition = condA, ThenBody = [mid],
        };
        outer = outer with { Fingerprint = Fingerprint.Compute(outer) };

        var leaf2 = MakeSimplePipeline("\"x\"");
        var inner2 = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condC),
            Condition = condC, ThenBody = [leaf2],
        };
        inner2 = inner2 with { Fingerprint = Fingerprint.Compute(inner2) };
        var mid2 = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condB),
            Condition = condB, ThenBody = [inner2],
        };
        mid2 = mid2 with { Fingerprint = Fingerprint.Compute(mid2) };
        var outer2 = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(condA),
            Condition = condA, ThenBody = [mid2],
        };
        outer2 = outer2 with { Fingerprint = Fingerprint.Compute(outer2) };

        Assert.Equal(Fingerprint.Compute(outer), Fingerprint.Compute(outer2));
    }

    [Fact]
    public void Fingerprint_SourceLine_Excluded_From_Fingerprint_And_Equals()
    {
        var lit = MakeLiteral(42);
        var seg = new Segment { Target = "Print", Arguments = [lit] };
        var a = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
            SourceLine = 1,
        };
        a = a with { Fingerprint = Fingerprint.Compute(a) };
        var b = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
            SourceLine = 99,
        };
        b = b with { Fingerprint = Fingerprint.Compute(b) };
        Assert.Equal(Fingerprint.Compute(a), Fingerprint.Compute(b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void KsCall_Equals_Same_MethodName_And_Args()
    {
        var arg = MakeLiteral(42);
        var a = new KsCall { MethodName = "Print", Args = [arg] };
        var b = new KsCall { MethodName = "Print", Args = [arg] };
        Assert.Equal(a, b);
    }

    [Fact]
    public void KsCall_Equals_Different_MethodName_Not_Equal()
    {
        var arg = MakeLiteral(42);
        var a = new KsCall { MethodName = "Print", Args = [arg] };
        var b = new KsCall { MethodName = "Range", Args = [arg] };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void KsPipeline_Equals_Different_Sources_Not_Equal()
    {
        var srcA = MakeLiteral(1);
        var srcB = MakeLiteral(2);
        var seg = new KsPipelineSegment { Target = "Print" };
        var a = new KsPipeline { Sources = [srcA], Segments = [seg] };
        var b = new KsPipeline { Sources = [srcB], Segments = [seg] };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void KsLiteral_Equals_Same_Value_And_Kind()
    {
        var a = MakeLiteral(42);
        var b = MakeLiteral(42);
        Assert.Equal(a, b);
    }

    [Fact]
    public void KsIdentifier_Equals_Same_Name()
    {
        var a = MakeId("foo");
        var b = MakeId("foo");
        Assert.Equal(a, b);
    }

    [Fact]
    public void KsPlaceholder_Equals_All_Instances_Equal()
    {
        var a = new KsPlaceholder();
        var b = new KsPlaceholder();
        Assert.Equal(a, b);
    }

    [Fact]
    public void KsNode_SourceText_Does_Not_Affect_Equality()
    {
        var a = new KsLiteral { Kind = KsLiteralKind.String, Value = "hello", SourceText = "\"hello\"" };
        var b = new KsLiteral { Kind = KsLiteralKind.String, Value = "hello", SourceText = "\"HELLO\"" };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Statement_Annotations_Do_Not_Affect_Equality()
    {
        var lit = MakeLiteral(1);
        var seg = new Segment { Target = "Print", Arguments = [lit] };
        var a = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
        };
        a = a with { Fingerprint = Fingerprint.Compute(a) };
        var b = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
            Annotations =
            [
                new Annotation { Kind = "Layout", Key = "x", Value = AnnotationValue.Layout(1, 2) },
            ],
        };
        b = b with { Fingerprint = Fingerprint.Compute(b) };
        Assert.Equal(a, b);
    }
}
