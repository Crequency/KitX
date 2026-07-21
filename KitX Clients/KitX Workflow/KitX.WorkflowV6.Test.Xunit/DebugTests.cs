// ─────────────────────────────────────────────────────────────────────────────
// Phase 10 acceptance tests for interactive debugging.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend.Debugging;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class DebugTests
{
    private static BuiltinFunctionRegistry Reg() => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    [Fact]
    public async Task Debug_No_Debugger_Fast_Path()
    {
        var ir = new BsTextLens(Reg()).Parse("Print(\"hello\")\n", []);
        var backend = new StructuredRoslynBackend(Reg());
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public void Debug_Codegen_Inserts_Checkpoint_When_HasDebugger()
    {
        var ir = new BsTextLens(Reg()).Parse("Print(\"hello\")\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: true);
        Assert.Contains("Checkpoint", source);
        Assert.Contains("this.Checkpoint(", source);
    }

    [Fact]
    public void Debug_Codegen_No_Checkpoint_When_No_Debugger()
    {
        var ir = new BsTextLens(Reg()).Parse("Print(\"hello\")\n", []);
        var codegen = new DebugCodegen(Reg());
        var source = codegen.Generate(ir, null, hasDebugger: false);
        Assert.DoesNotContain("Checkpoint", source);
    }
}