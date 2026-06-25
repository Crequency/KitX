using Xunit;
using KitX.Workflow.Abstractions;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Suite D — lightweight validation interface.
/// Front-end feature dependency: side-by-side sync triggers BS→BP render only when
/// "saved AND syntactically valid" (Blueprint-Editor-Redesign-Plan §功能C).
///
/// These exercise the existing <see cref="IBlockScriptParser.Validate"/> path explicitly
/// (the console-harness tests cover parsing/execution, but not the standalone Validate entry
/// point that the side-by-side UI will poll on save). These may already be GREEN.
/// </summary>
public class ValidationTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public ValidationTests(WorkflowFixture fx) => _fx = fx;

    private const string Valid = """
        #MainBlock
        Print("hi");
        Goto("End");
        """ + TestData.End;

    private const string MissingMainBlock = """
        #Block Only
        Print("no main");
        Exit();
        """;

    [Fact]
    public void Validate_ValidScript_ReturnsClean()
    {
        var result = _fx.Parser.Validate(Valid);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validate_MissingMainBlock_ReturnsError()
    {
        var result = _fx.Parser.Validate(MissingMainBlock);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_EmptySource_ReturnsError()
    {
        var result = _fx.Parser.Validate("");
        Assert.False(result.IsValid);
    }
}
