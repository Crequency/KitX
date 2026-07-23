// ─────────────────────────────────────────────────────────────────────────────
// Phase 4 E2E tests for StructuredRoslynBackend.
//
// Compiles KS source → IR → C# → runs → asserts on captured OutputLines.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
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
        var lens = new KsTextLens(BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));
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
            if 1, 1 > Compare("BEQ")
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
            while counter, 3 > Compare("BLT")
                counter, 1 > Add > counter
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
                if i, 2 > Compare("BEQ")
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
                if i, 2 > Compare("BEQ")
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

            Add(2, 3) > counter
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

    // ── Phase 3.3: E2E tests for pipeline conditions and new syntax ──

    [Fact]
    public async Task E2E_Pipeline_Condition_Direct()
    {
        // Pipeline condition directly in if — no intermediate variable needed.
        var src = """
            if 1, 1 > Compare("BEQ")
                Print("equal")
            else
                Print("not equal")
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("equal", result.Output);
        Assert.DoesNotContain("not equal", result.Output);
    }

    [Fact]
    public async Task E2E_Pipeline_Condition_With_Variables()
    {
        var src = """
            var {
                int a
                int b
            }

            3 > a
            5 > b
            if a, b > Compare("BLT")
                Print("a less than b")
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("a less than b", result.Output);
    }

    [Fact]
    public async Task E2E_Variable_Tap_Pipeline()
    {
        // `0 > counter > Print` — counter is both written and read in one chain.
        var src = """
            var {
                int counter
            }

            0 > counter > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("0", result.Output);
    }

    [Fact]
    public async Task E2E_Nested_Control_Flow()
    {
        // Nested forEach + if/else + if/else (mini guess-number).
        // target=3, range 0..5: i=0,1,2 → "too low", i=3 → "found it!", break.
        var src = """
            const {
                int target = 3
            }

            var {
                int guess
                int hit
            }

            0 > hit
            forEach Range(0, 5, 1) as i
                i > guess
                if guess, target > Compare("BEQ")
                    1 > hit
                    Print("found it!")
                    break
                else
                    if guess, target > Compare("BLT")
                        Print("too low")
                    else
                        Print("too high")
            hit > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        // i=0,1,2 are too low; i=3 matches.
        Assert.Equal(3, result.Output.Count(x => x == "too low"));
        Assert.Contains("found it!", result.Output);
        Assert.Contains("1", result.Output);  // hit = 1
    }

    [Fact]
    public async Task E2E_Placeholder_Pipeline_ForEach()
    {
        // `forEach loopMax > Range(0, _, 1) as i` — the `_` placeholder is replaced
        // by the pipeline source `loopMax`, producing Range(0, 3, 1).
        var src = """
            const {
                int loopMax = 3
            }

            forEach loopMax > Range(0, _, 1) as i
                i > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "0", "1", "2" }, result.Output);
    }

    [Fact]
    public async Task E2E_Switch_Statement()
    {
        // selector=1 → arm 1 prints "one"; arms 0 and default not taken.
        var src = """
            var {
                int sel
            }

            1 > sel
            switch sel
                0:
                    Print("zero")
                1:
                    Print("one")
                default:
                    Print("other")
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "one" }, result.Output);
    }

    [Fact]
    public async Task E2E_User_Helper_Function()
    {
        // Define a user helper function `int Double(int x) { return x * 2; }`
        // and call it from KS: `5 > Double > Print` → output "10".
        var helpers = new List<KitX.Core.Contract.Workflow.HelperFunction>
        {
            new()
            {
                Name = "Double",
                ReturnType = "int",
                Parameters =
                [
                    new() { Name = "x", Type = "int" },
                ],
                Code = "return x * 2;",
            },
        };
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("5 > Double > Print\n", helpers);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("10", result.Output);
    }

    [Fact]
    public async Task E2E_Helper_Return_Type_Inference()
    {
        // Helper returns int → PubVar assigned from helper should be strongly typed as int.
        // `5 > Double > result > Print(result)` — result inferred as int, not object.
        var helpers = new List<KitX.Core.Contract.Workflow.HelperFunction>
        {
            new()
            {
                Name = "Double",
                ReturnType = "int",
                Parameters = [new() { Name = "x", Type = "int" }],
                Code = "return x * 2;",
            },
        };
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("""
            var {
                int result
            }

            5 > Double > result
            result > Print
            """, helpers);
        // Verify the PubVar type was inferred as int (not object).
        Assert.True(ir.GlobalVars.TryGetValue("result", out var gv));
        Assert.Equal("int", gv.Type);
        // Execute to verify strong-typed field works.
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("10", result.Output);
    }

    [Fact]
    public async Task E2E_Branch_Condition_Type_Inference()
    {
        // var { object flag } + true > flag + if flag → flag should be inferred as bool.
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("""
            var {
                object flag
            }

            true > flag
            if flag
                Print("yes")
            """, []);
        // Verify the PubVar type was refined to bool by the Demand pass.
        Assert.True(ir.GlobalVars.TryGetValue("flag", out var gv));
        Assert.Equal("bool", gv.Type);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("yes", result.Output);
    }

    [Fact]
    public async Task E2E_Helper_Param_Type_Inference()
    {
        // Helper Greet(string name) — PubVar passed as arg should be inferred as string.
        var helpers = new List<KitX.Core.Contract.Workflow.HelperFunction>
        {
            new()
            {
                Name = "Greet",
                ReturnType = "string",
                Parameters = [new() { Name = "name", Type = "string" }],
                Code = "return \"hello, \" + name;",
            },
        };
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        var lens = new KsTextLens(registry);
        var ir = lens.Parse("""
            var {
                object who
            }

            "world" > who
            who > Greet > Print
            """, helpers);
        // The Demand pass should refine `who` from object to string.
        Assert.True(ir.GlobalVars.TryGetValue("who", out var gv));
        Assert.Equal("string", gv.Type);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("hello, world", result.Output);
    }

    [Fact]
    public async Task E2E_Cache_Hit_On_Second_Execution()
    {
        // Same IR executed twice — second call should hit the in-memory cache
        // (assembly reuse). Verify output is identical.
        var ir = ParseToIr("Print(\"cached\")\n");
        var backend = MakeBackend();
        var result1 = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        var result2 = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result1.IsSuccess, $"First execution failed: {result1.ErrorMessage}");
        Assert.True(result2.IsSuccess, $"Second execution failed: {result2.ErrorMessage}");
        Assert.Equal(result1.Output, result2.Output);
        Assert.Contains("cached", result2.Output);
    }

    [Fact]
    public async Task E2E_Cache_Invalidation_On_IR_Change()
    {
        // Different IR should produce different output (no stale cache hit).
        var ir1 = ParseToIr("Print(\"first\")\n");
        var ir2 = ParseToIr("Print(\"second\")\n");
        var backend = MakeBackend();
        var result1 = await backend.ExecuteAsync(ir1, null, CancellationToken.None);
        var result2 = await backend.ExecuteAsync(ir2, null, CancellationToken.None);
        Assert.Contains("first", result1.Output);
        Assert.Contains("second", result2.Output);
        Assert.DoesNotContain("second", result1.Output);
    }

    [Fact]
    public async Task E2E_Arithmetic_Four_Operations()
    {
        var src = """
            Sub(10, 3) > Print
            Mul(4, 5) > Print
            Div(20, 4) > Print
            Mod(10, 3) > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "7", "20", "5", "1" }, result.Output);
    }

    [Fact]
    public async Task E2E_Arithmetic_In_Computation()
    {
        // 3 * 4 = 12, then 12 - 5 = 7. Uses pipeline chaining with placeholder.
        var src = """
            3, 4 > Mul > Sub(_, 5) > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("7", result.Output);
    }

    [Fact]
    public async Task E2E_Pause_Then_Print()
    {
        var src = """
            Pause(1)
            Print("after pause")
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("after pause", result.Output);
    }

    [Fact]
    public async Task E2E_Write_And_Read_File()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitx_test_{Guid.NewGuid():N}.txt");
        try
        {
            var src = $"""
                WriteTextFile("{tempFile}", "hello world")
                ReadTextFile("{tempFile}") > Print
                """;
            var ir = ParseToIr(src);
            var backend = MakeBackend();
            var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
            Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
            Assert.Contains("hello world", result.Output);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task E2E_Len_Of_String()
    {
        var src = """
            Len("hello") > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("5", result.Output);
    }

    [Fact]
    public async Task E2E_Len_Of_Range_Array()
    {
        var src = """
            Range(0, 5, 1) > Len > Print
            """;
        var ir = ParseToIr(src);
        var backend = MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Failed: {result.ErrorMessage}");
        Assert.Contains("5", result.Output);
    }
}