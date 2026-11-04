using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// S1 acceptance criterion ②: the same config produces the same diagnostics on any platform.
/// </summary>
/// <remarks>
/// <para>What actually varied by platform before this step was the <i>path rule</i>:
/// <c>Path.GetInvalidFileNameChars()</c> returns a different set per OS, so an id containing a
/// backslash was rejected on Windows and accepted on Unix. The diagnostics here therefore assert
/// on the guard's own vocabulary rather than on whatever the local OS forbids, and the
/// cross-platform case is exercised with input that would genuinely diverge.</para>
///
/// <para>Determinism also means <i>repeatability within one machine</i>: two compiles of equal
/// configs must produce equal codes in equal order. That is checked separately, because it is the
/// failure mode that makes a diagnostic list useless in review.</para>
/// </remarks>
public class CompileDeterminismTests
{
    private static Toolkit ConfigWithId(string id)
        => new()
        {
            Id = id,
            Meta = new ToolkitMeta { Name = "determinism" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers =
            [
                new Trigger
                {
                    Id = "t",
                    Type = TriggerType.Manual,
                    Bindings = [new() { Workflow = "wf" }],
                },
            ],
        };

    /// <summary>
    /// A backslash is the character that used to behave differently per platform. It must be
    /// rejected with the same code and the same message wherever this test runs.
    /// </summary>
    [Theory]
    [InlineData("tk\\x")]
    [InlineData("tk/x")]
    [InlineData("tk:x")]
    [InlineData("tk*x")]
    public void Separators_are_rejected_with_a_stable_code(string id)
    {
        var result = ToolkitCompiler.Compile(ConfigWithId(id), out var diagnostics);

        Assert.False(result.Succeeded);
        var invalid = Assert.Single(diagnostics, d => d.Code == "toolkit.id.invalid");
        Assert.Contains("id.invalid-character", invalid.Message);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Dot_segments_are_rejected_with_a_stable_code(string id)
    {
        var result = ToolkitCompiler.Compile(ConfigWithId(id), out var diagnostics);

        Assert.False(result.Succeeded);
        var invalid = Assert.Single(diagnostics, d => d.Code == "toolkit.id.invalid");
        Assert.Contains("id.reserved-segment", invalid.Message);
    }

    /// <summary>
    /// Compiling the same config twice must produce the same diagnostics in the same order.
    /// A diagnostic list whose order depends on hash iteration is not reviewable.
    /// </summary>
    [Fact]
    public void Diagnostics_are_stable_across_repeated_compiles()
    {
        var config = new Toolkit
        {
            Id = "../bad",
            Meta = new ToolkitMeta { Name = "" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "", Name = "", File = "" },
                new ToolkitWorkflow { Id = "dup", Name = "dup", File = "d.kcs" },
                new ToolkitWorkflow { Id = "dup", Name = "dup", File = "d.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "", Type = TriggerType.Manual, Bindings = [] },
                new Trigger { Id = "x", Type = TriggerType.Timer, Config = new() { DueTimeMs = -1 } },
            ],
        };

        var first = ToolkitCompiler.Compile(config, out var firstDiagnostics);
        var second = ToolkitCompiler.Compile(config, out var secondDiagnostics);

        Assert.False(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(
            firstDiagnostics.Select(d => (d.Severity, d.Code)),
            secondDiagnostics.Select(d => (d.Severity, d.Code)));
    }

    /// <summary>
    /// The graph's iteration order must not depend on dictionary insertion or hashing: it is
    /// ordinally sorted, so two compiles of the same config compare equal as sequences.
    /// </summary>
    [Fact]
    public void Graph_node_order_is_ordinal_and_repeatable()
    {
        var config = new Toolkit
        {
            Id = "tk-order",
            Meta = new ToolkitMeta { Name = "order" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "zebra", Name = "z", File = "z.kcs" },
                new ToolkitWorkflow { Id = "apple", Name = "a", File = "a.kcs" },
            ],
            Triggers =
            [
                new Trigger
                {
                    Id = "t",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new() { From = "zebra" },
                    Bindings = [new() { Workflow = "apple" }],
                },
            ],
        };

        var a = ToolkitCompiler.Compile(config, out _);
        var b = ToolkitCompiler.Compile(config, out _);

        Assert.Equal(["apple", "zebra"], a.Value!.Graph.Nodes);
        Assert.Equal(a.Value.Graph.Nodes, b.Value!.Graph.Nodes);
    }
}
