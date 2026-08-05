// ─────────────────────────────────────────────────────────────────────────────
// W-9 tests: Parse / ParseLowering must throw KsParseException on error-laden
// source instead of returning a partial IR; ParseAstWithDiagnostics stays the
// diagnostics-only path.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class KsParseStrictnessTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public KsParseStrictnessTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Print(a)")]                        // KS051 identifier argument
    [InlineData("if Compare(\"BEQ\", 1, 1)")]       // missing body colon
    [InlineData("while true:\n\tPrint(\"tab\")")]   // KS001 tab indentation
    public void Error_Laden_Source_Throws_Instead_Of_Partial_IR(string src)
    {
        var ex = Assert.Throws<KsParseException>(() => _fixture.KsLens.Parse(src, []));
        Assert.NotEmpty(ex.Diagnostics);
        Assert.Contains(ex.Diagnostics, d => d.Severity == KsDiagnosticSeverity.Error);
    }

    [Fact]
    public void ParseLowering_Throws_With_Diagnostics()
    {
        var ex = Assert.Throws<KsParseException>(() =>
            _fixture.KsLens.ParseLowering("Print(a)", [], null));
        Assert.NotEmpty(ex.Diagnostics);
        Assert.Contains(ex.Diagnostics, d => d.Code == "KS051");
        // The exception message carries a readable summary of the errors.
        Assert.Contains("KS051", ex.Message);
    }

    [Fact]
    public void Valid_Source_Still_Parses()
    {
        var ir = _fixture.KsLens.Parse("a > Print\n", []);
        Assert.Single(ir.Body);
    }

    [Fact]
    public void ParseAstWithDiagnostics_Remains_The_Diagnostics_Path()
    {
        // The low-level diagnostic entry does NOT throw — callers inspect errors.
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics("Print(a)");
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS051");
    }

    [Fact]
    public void ParseAst_Returns_Recovered_Tree_Without_Throwing()
    {
        // ParseAst is a structural convenience (no lowering) — it keeps the old
        // recover-and-return semantics.
        var ast = _fixture.KsLens.ParseAst("Print(a)");
        Assert.NotNull(ast);
    }
}
