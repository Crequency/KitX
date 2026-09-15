// ─────────────────────────────────────────────────────────────────────────────
// W-2 + W-7 tests: WorkflowOverrides.RenderLiteral must never splice untrusted
// text into generated C# source. Non-string types are strictly validated (code
// injection payloads are rejected with a diagnostic); string escaping reuses the
// shared codec (so \r and \0 are escaped and survive compilation+execution).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Ir;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class WorkflowOverridesSecurityTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public WorkflowOverridesSecurityTests(WorkflowTestFixture fixture) => _fixture = fixture;

    // ── Rejection: injection / expression payloads must never reach codegen ──

    [Theory]
    [InlineData("0; File.WriteAllText(\"pwned.txt\", \"x\") //", "int")]
    [InlineData("0x1A", "int")]
    [InlineData("1+1", "int")]
    [InlineData("1-1", "int")]
    [InlineData("true", "int")]
    [InlineData("1_000", "int")]
    [InlineData(" 42", "int")]
    [InlineData("42 ", "int")]
    [InlineData("99999999999999999999", "int")]        // range overflow
    [InlineData("3.14", "long")]
    [InlineData("9223372036854775808", "long")]        // long overflow
    [InlineData("true;", "bool")]
    [InlineData("1", "bool")]
    [InlineData("yes", "bool")]
    [InlineData("TRUE; //", "bool")]
    [InlineData("1;2", "double")]
    [InlineData("0x1p3", "double")]
    [InlineData("NaN", "double")]
    [InlineData("Infinity", "float")]
    [InlineData("1e", "double")]
    [InlineData("1e+", "double")]
    [InlineData(".5", "double")]
    [InlineData("5.", "double")]
    public void Injection_Payloads_Are_Rejected(string text, string type)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WorkflowOverrides.RenderLiteral(text, type));
        Assert.Contains(type, ex.Message);
    }

    // ── Acceptance: valid literals pass through untouched (or canonicalised) ──

    [Theory]
    [InlineData("42", "int", "42")]
    [InlineData("-7", "int", "-7")]
    [InlineData("+3", "int", "+3")]
    [InlineData("0", "int", "0")]
    [InlineData("9223372036854775807", "long", "9223372036854775807")]
    [InlineData("-12.5e3", "double", "-12.5e3")]
    [InlineData("3.14", "double", "3.14")]
    [InlineData("1E5", "double", "1E5")]
    [InlineData("-0.5", "float", "-0.5")]
    [InlineData("2.5e-3", "double", "2.5e-3")]
    public void Valid_Literals_Are_Accepted(string text, string type, string expected)
        => Assert.Equal(expected, WorkflowOverrides.RenderLiteral(text, type));

    [Theory]
    [InlineData("true", "true")]
    [InlineData("TRUE", "true")]
    [InlineData("False", "false")]
    [InlineData("false", "false")]
    public void Bool_Values_Are_Canonicalised(string text, string expected)
        => Assert.Equal(expected, WorkflowOverrides.RenderLiteral(text, "bool"));

    // ── End-to-end: a validated override compiles and runs ──

    [Fact]
    public async Task Valid_Override_Compiles_And_Runs()
    {
        var ir = _fixture.KsLens.Parse("""
            const {
                int x = 1
                double y = 0.5
            }
            x > Print
            y > Print
            """, []);
        var applied = WorkflowOverrides.ApplyConstantOverrides(ir,
            new Dictionary<string, string?> { ["x"] = "42", ["y"] = "-12.5e3" });

        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(applied, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Equal(new[] { "42", "-12500" }, result.Output);
    }

    [Fact]
    public void ApplyConstantOverrides_Error_Carries_The_Name_And_Value()
    {
        var ir = _fixture.KsLens.Parse("""
            const {
                int x = 1
            }
            x > Print
            """, []);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkflowOverrides.ApplyConstantOverrides(ir,
                new Dictionary<string, string?> { ["x"] = "1; File.Delete(\"x\") //" }));
        Assert.Contains("'x'", ex.Message);
        Assert.Contains("File.Delete", ex.Message);
    }

    // ── W-7: string escaping reuses the shared codec (covers \r and \0) ──

    [Fact]
    public async Task String_Override_With_Control_Chars_Compiles_And_Runs()
    {
        var ir = _fixture.KsLens.Parse("""
            const {
                string s = "x"
            }
            s > Print
            """, []);
        // \r, \0, \n, \t, \\, \" — every escape the codec must handle. Before W-7 the
        // hand-written chain missed \r and \0, producing invalid C# (CS1009) at compile.
        const string payload = "a\rb\0c\nd\te\\f\"g";
        var applied = WorkflowOverrides.ApplyConstantOverrides(ir,
            new Dictionary<string, string?> { ["s"] = payload });

        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(applied, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Equal(payload, Assert.Single(result.Output));
    }

    [Fact]
    public async Task Char_Override_Compiles_And_Runs()
    {
        var ir = _fixture.KsLens.Parse("""
            const {
                char c = 'x'
            }
            c > Print
            """, []);
        var applied = WorkflowOverrides.ApplyConstantOverrides(ir,
            new Dictionary<string, string?> { ["c"] = "'" });
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(applied, null, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Equal("'", Assert.Single(result.Output));
    }
}
