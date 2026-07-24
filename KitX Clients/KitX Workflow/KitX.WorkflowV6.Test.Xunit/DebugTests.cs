// ─────────────────────────────────────────────────────────────────────────────
// Phase 10 acceptance tests for interactive debugging.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.Debugging;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class DebugTests
{
    private static BuiltinFunctionRegistry Reg() => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static Workflow Parse(string src)
    {
        var lens = new KsTextLens(Reg());
        return lens.Parse(src, []);
    }

    private static StructuredRoslynBackend MakeBackend()
    {
        return new StructuredRoslynBackend(Reg());
    }

    [Fact]
    public async Task Debug_No_Debugger_Fast_Path()
    {
        var ir = new KsTextLens(Reg()).Parse("Print(\"hello\")\n", []);
        var backend = new StructuredRoslynBackend(Reg());
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public void Debug_Codegen_Inserts_Checkpoint_When_HasDebugger()
    {
        var ir = new KsTextLens(Reg()).Parse("Print(\"hello\")\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("Checkpoint", source);
        Assert.Contains("this.Checkpoint(", source);
    }

    [Fact]
    public void Debug_Codegen_No_Checkpoint_When_No_Debugger()
    {
        var ir = new KsTextLens(Reg()).Parse("Print(\"hello\")\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: false);
        Assert.DoesNotContain("Checkpoint", source);
    }

    [Fact]
    public void Debug_Codegen_Handles_Multi_Source_Pipeline()
    {
        var ir = new KsTextLens(Reg()).Parse("guessNum, targetNum > Compare(\"BEQ\")\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("this.Compare(\"BEQ\"", source);
        Assert.DoesNotContain("/* pipeline */", source);
    }

    [Fact]
    public void Debug_Codegen_Handles_Placeholder_Pipeline()
    {
        var ir = new KsTextLens(Reg()).Parse("loopMax > Range(0, _, 1)\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("this.Range(0, this.loopMax, 1)", source);
        Assert.DoesNotContain("/* pipeline */", source);
    }

    [Fact]
    public void Debug_Codegen_Handles_Variable_Tap()
    {
        var ir = new KsTextLens(Reg()).Parse("counter > Add(_, 1) > counter\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("this.counter = ", source);
        Assert.Contains("this.Add(", source);
    }

    [Fact]
    public void Debug_Codegen_Generates_Unique_Pipe_Variables_For_Multiple_Pipelines()
    {
        var src = """
            var {
                int a
                int b
            }
            a > Print
            b > Print
            """;
        var ir = Parse(src);
        var cg = new DebugCodegen(Reg());
        var code = cg.Generate(ir, null, hasDebugger: true);

        Assert.Contains("__pipe_0", code);
        Assert.Contains("__pipe_1", code);
        Assert.DoesNotContain("__pipe_0_0", code);
    }

    [Fact]
    public void Debug_Codegen_Recognizes_Helper_Functions()
    {
        var ir = new Workflow
        {
            Body = [new PipelineStatement
            {
                Sources = [new KsCall { MethodName = "MyHelper", Args = [] }],
                Segments = [],
                Fingerprint = Fingerprint.Compute("test-helper"),
            }],
            HelperFunctions = [new HelperFunction { Name = "MyHelper" }],
        };
        var cg = new DebugCodegen(Reg());
        var code = cg.Generate(ir, null, hasDebugger: true);
        Assert.Contains("this.MyHelper()", code);
        Assert.DoesNotContain("this.MyHelper = ", code);
    }

    [Fact]
    public void Debug_Codegen_Renders_ForEach_Item_As_Local_Variable()
    {
        var ir = Parse("forEach Range(0, 3, 1) as i:\n    i > Print\n");
        var cg = new DebugCodegen(Reg());
        var code = cg.Generate(ir, null, hasDebugger: true);
        Assert.DoesNotContain("this.i)", code);
        Assert.Contains("i)", code);
    }
}