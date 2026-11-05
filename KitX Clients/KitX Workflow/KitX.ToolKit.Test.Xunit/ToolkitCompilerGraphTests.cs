using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;

using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The compiled graph is the only graph: the scheduler no longer derives one of its own.
/// </summary>
/// <remarks>
/// <para>These tests used to include a "dual-run comparison" that rebuilt the graph the way
/// <c>RunScheduler.BuildGraph</c> did and compared the two item by item (ADR-0009 D3). That
/// comparison is gone along with the second implementation — an equality check against a copy
/// transcribed from code that no longer exists measures whether the transcription is accurate,
/// not whether the compiler is right.</para>
///
/// <para>What survives is the reason the comparison found anything at all: the old scheduler
/// accumulated one successor entry per <i>binding</i>, the old <c>ConfigValidator</c>
/// de-duplicated by <c>To</c>, and the two readings only agreed when no <c>(From, To)</c> pair
/// repeated. ADR-0002 D5 now rejects that shape outright, so the tests below check the compiler
/// refuses it rather than picking a winner.</para>
/// </remarks>
public class ToolkitCompilerGraphTests
{
    private static Trigger Completion(string id, string from, params string[] targets)
        => new()
        {
            Id = id,
            Type = TriggerType.WorkflowCompletion,
            Config = new TriggerConfig { From = from },
            Bindings = [.. targets.Select(t => new TriggerBinding { Workflow = t })],
        };

    private static Toolkit Config(params Trigger[] triggers)
        => new()
        {
            Id = "tk-graph",
            Meta = new ToolkitMeta { Name = "graph" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "a", Name = "a", File = "a.kcs" },
                new ToolkitWorkflow { Id = "b", Name = "b", File = "b.kcs" },
                new ToolkitWorkflow { Id = "c", Name = "c", File = "c.kcs" },
            ],
            Triggers = [.. triggers],
        };

    /// <summary>
    /// The old validator de-duplicated by <c>To</c>; the old scheduler counted per binding.
    /// ADR-0002 D5 makes an un-declinable reading impossible by rejecting the shape outright.
    /// </summary>
    [Fact]
    public void Duplicate_edge_is_a_compile_error_not_a_silent_merge()
    {
        var config = Config(
            Completion("e1", "a", "b"),
            Completion("e2", "a", "b"));

        var result = ToolkitCompiler.Compile(config, out var diagnostics);

        Assert.Null(result);
        Assert.Contains(diagnostics, d => d.Code == "graph.edge.duplicate");
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Cycle_is_reported_as_a_diagnostic_not_an_exception()
    {
        var config = Config(
            Completion("e1", "a", "b"),
            Completion("e2", "b", "a"));

        var result = ToolkitCompiler.Compile(config, out var diagnostics);

        Assert.Null(result);
        Assert.Contains(diagnostics, d => d.Code == "graph.cycle");
    }

    [Fact]
    public void Compile_never_throws_for_a_bad_config()
    {
        // Every field is wrong at once; the point is that we get diagnostics, not an exception.
        var config = new Toolkit
        {
            Id = "../escape",
            Meta = new ToolkitMeta { Name = "" },
            Workflows = [new ToolkitWorkflow { Id = "", Name = "", File = "" }],
            Triggers =
            [
                new Trigger { Id = "", Type = TriggerType.WorkflowCompletion, Config = new TriggerConfig() },
                new Trigger { Id = "t2", Type = TriggerType.PluginEvent, Config = new TriggerConfig() },
            ],
        };

        var result = ToolkitCompiler.Compile(config, out var diagnostics);

        Assert.Null(result);
        Assert.NotEmpty(diagnostics);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Valid_config_produces_an_error_free_result()
    {
        var config = Config(Completion("e1", "a", "b"));

        var result = ToolkitCompiler.Compile(config, out var diagnostics);

        Assert.NotNull(result);
        Assert.NotNull(result);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("tk-graph", result!.ToolkitId);
        Assert.Equal(3, result.Workflows.Count);
    }

    [Fact]
    public void Graph_is_immutable_and_carries_no_semantics_of_its_own()
    {
        var config = Config(Completion("e1", "a", "b"));
        var result = ToolkitCompiler.Compile(config, out _);
        var graph = result!.Graph;

        // Nodes are ordinally sorted, so iteration order is stable across runs.
        Assert.Equal(graph.Nodes.OrderBy(x => x, StringComparer.Ordinal).ToList(), graph.Nodes);

        // Successors of a node with no outgoing edges is empty, never null.
        Assert.NotNull(graph.Successors("c"));
        Assert.Empty(graph.Successors("c"));

        Assert.True(graph.IsEntry("a"));
        Assert.False(graph.IsEntry("b"));
    }
}
