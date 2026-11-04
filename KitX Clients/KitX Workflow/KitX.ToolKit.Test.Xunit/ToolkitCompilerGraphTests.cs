using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;

using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// S1 acceptance: the compiled graph must agree with what the old scheduler derived from
/// config, item by item (ADR-0009 D3 "dual-run comparison").
/// </summary>
/// <remarks>
/// <para>The old <c>BenchScheduler.BuildGraph</c> accumulated one successor entry per
/// <i>binding</i> and counted predecessors per binding; the old <c>ConfigValidator</c>
/// de-duplicated by <c>To</c>. Those two readings are only equivalent when no
/// <c>(From, To)</c> pair repeats — which is exactly what ADR-0002 D5 now rejects at compile
/// time. These tests pin that down: for configs the compiler accepts, the two readings agree,
/// and for configs with a repeated pair the compiler refuses rather than picking a winner.</para>
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
    /// The dual-run comparison: build the graph the way <c>BenchScheduler.BuildGraph</c> did
    /// and compare against the compiled one.
    /// </summary>
    [Fact]
    public void Compiled_graph_matches_legacy_scheduler_derivation()
    {
        var config = Config(
            Completion("e1", "a", "b", "c"),
            Completion("e2", "b", "c"));

        var result = ToolkitCompiler.Compile(config, out var diagnostics);

        Assert.True(result.Succeeded, string.Join("; ", diagnostics.Select(d => d.Message)));
        var graph = result.Value!.Graph;

        // Legacy derivation, transcribed from BenchScheduler.BuildGraph before the rewrite:
        // one successor entry per binding, predecessor counted per binding.
        var legacySucc = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var legacyPred = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var trigger in config.Triggers.Where(t => t.Type == TriggerType.WorkflowCompletion))
        {
            var from = trigger.Config?.From;
            if (string.IsNullOrWhiteSpace(from))
                continue;

            foreach (var binding in trigger.Bindings.Where(b => !string.IsNullOrWhiteSpace(b.Workflow)))
            {
                if (!legacySucc.TryGetValue(from, out var list))
                    legacySucc[from] = list = [];
                list.Add(binding.Workflow);
                legacyPred[binding.Workflow] = legacyPred.TryGetValue(binding.Workflow, out var c) ? c + 1 : 1;
            }
        }

        // Node sets equal.
        var legacyNodes = legacySucc.Keys
            .Concat(legacyPred.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(legacyNodes, graph.Nodes);

        // Edge sets equal, as (From, To) pairs.
        var legacyEdges = legacySucc
            .SelectMany(kv => kv.Value.Select(to => (From: kv.Key, To: to)))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        var compiledEdges = graph.Edges
            .Select(e => (e.From, e.To))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(legacyEdges, compiledEdges);

        // Successor lists equal, per node.
        foreach (var node in legacySucc.Keys)
        {
            var expected = legacySucc[node].OrderBy(x => x, StringComparer.Ordinal).ToList();
            var actual = graph.Successors(node).Select(e => e.To).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, actual);
        }

        // Predecessor counts equal ("join arity").
        foreach (var node in legacyPred.Keys)
            Assert.Equal(legacyPred[node], graph.PredecessorCount(node));
    }

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

        Assert.False(result.Succeeded);
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

        Assert.False(result.Succeeded);
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

        Assert.False(result.Succeeded);
        Assert.NotEmpty(diagnostics);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Valid_config_produces_an_error_free_result()
    {
        var config = Config(Completion("e1", "a", "b"));

        var result = ToolkitCompiler.Compile(config, out var diagnostics);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("tk-graph", result.Value!.ToolkitId);
        Assert.Equal(3, result.Value.Workflows.Count);
    }

    [Fact]
    public void Graph_is_immutable_and_carries_no_semantics_of_its_own()
    {
        var config = Config(Completion("e1", "a", "b"));
        var result = ToolkitCompiler.Compile(config, out _);
        var graph = result.Value!.Graph;

        // Nodes are ordinally sorted, so iteration order is stable across runs.
        Assert.Equal(graph.Nodes.OrderBy(x => x, StringComparer.Ordinal).ToList(), graph.Nodes);

        // Successors of a node with no outgoing edges is empty, never null.
        Assert.NotNull(graph.Successors("c"));
        Assert.Empty(graph.Successors("c"));

        Assert.True(graph.IsEntry("a"));
        Assert.False(graph.IsEntry("b"));
    }
}
