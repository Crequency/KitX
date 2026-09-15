// ─────────────────────────────────────────────────────────────────────────────
// WorkflowRunner unit tests.
//
// Verifies the single shared execution path: constant overrides are applied
// before execution, and a null override set falls back to IR defaults.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Services;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class WorkflowRunnerTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public WorkflowRunnerTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Runner_Applies_Constant_Overrides_Before_Execution()
    {
        var ir = _fixture.ParseKS(
            "const {",
            "    int x = 1",
            "}",
            "x > Print");

        var runner = new WorkflowRunner(_fixture.MakeBackend());

        var result = await runner.ExecuteAsync(
            ir, null,
            new Dictionary<string, string?> { ["x"] = "42" },
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Contains("42", result.Output);
        Assert.DoesNotContain("1", result.Output);
    }

    [Fact]
    public async Task Runner_Null_Overrides_Runs_With_Defaults()
    {
        var ir = _fixture.ParseKS(
            "const {",
            "    string greeting = \"hi\"",
            "}",
            "greeting > Print");

        var runner = new WorkflowRunner(_fixture.MakeBackend());

        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.Contains("hi", result.Output);
    }
}
