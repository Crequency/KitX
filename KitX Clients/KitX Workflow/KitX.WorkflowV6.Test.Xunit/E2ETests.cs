// ─────────────────────────────────────────────────────────────────────────────
// Phase 4 E2E tests for StructuredRoslynBackend.
//
// Compiles BS source → IR → C# → runs → asserts on captured OutputLines.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class E2ETests
{
    private static StructuredRoslynBackend MakeBackend()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        return new StructuredRoslynBackend(registry);
    }

    private static Workflow ParseToIr(string src)
    {
        var lens = new BsTextLens(BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));
        return lens.Parse(src, []);
    }

    [Fact]
    public async Task E2E_Hello_World()
    {
        var ir = ParseToIr("Print(\"hello\")\n");
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task E2E_ForEach_Range_Prints_0_1_2()
    {
        var src = """
            forEach Range(0, 3, 1) as i
                i > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "0", "1", "2" }, result.Output);
    }

    [Fact]
    public async Task E2E_If_Else_True_Branch()
    {
        var src = """
            if HelperFuncCompare("BEQ", 1, 1)
                Print("yes")
            else
                Print("no")
            """;        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("yes", result.Output);
        Assert.DoesNotContain("no", result.Output);
    }

    [Fact]
    public async Task E2E_While_Loop_Terminates()
    {
        var src = """
            var {
                int counter
            }

            0 > counter
            while HelperFuncCompare("BLT", counter, 3)
                HelperFuncAdd(counter, 1) > counter
                Print("tick")
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(3, result.Output.Count(x => x == "tick"));
    }

    [Fact]
    public async Task E2E_Break_Exits_ForEach()
    {
        var src = """
            forEach Range(0, 10, 1) as i
                if HelperFuncCompare("BEQ", i, 2)
                    break
                i > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        // i=0 prints 0, i=1 prints 1, i=2 breaks before printing.
        Assert.Equal(new[] { "0", "1" }, result.Output);
    }

    [Fact]
    public async Task E2E_Continue_Skips_ForEach_Iteration()
    {
        var src = """
            forEach Range(0, 5, 1) as i
                if HelperFuncCompare("BEQ", i, 2)
                    continue
                i > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        // i=0,1,3,4 print; i=2 is skipped.
        Assert.Equal(new[] { "0", "1", "3", "4" }, result.Output);
    }

    [Fact]
    public async Task E2E_Exit_Terminates_Workflow()
    {
        var src = """
            Print("before")
            exit()
            Print("after")
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("before", result.Output);
        Assert.DoesNotContain("after", result.Output);
    }

    [Fact]
    public async Task E2E_Strong_Typed_PubVar()
    {
        var src = """
            var {
                int counter
            }

            HelperFuncAdd(2, 3) > counter
            counter > Print
            """;        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("5", result.Output);
    }

    [Fact]
    public async Task E2E_StringConcat()
    {
        var src = """
            StringConcat("hello, ", "world") > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("hello, world", result.Output);
    }
}