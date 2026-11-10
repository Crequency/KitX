using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;

using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The editor and the compiler must agree about what a cycle <em>is</em>, even though they build
/// their edges from different things.
/// </summary>
/// <remarks>
/// <para><b>Why this needs its own file.</b> Both sides had their own depth-first search, and they
/// emitted the same sentence — so an assertion on <c>Contains("cycle")</c> passed on either one and
/// told you nothing about whether they agreed. The only observation that distinguishes "one shared
/// search" from "two searches that happen to match today" is to feed both the same cycle and compare
/// the exact path they name.</para>
///
/// <para><b>The comparison is on the joined path, not on a substring.</b> Two searches can both
/// detect <c>A -> B -> A</c> and yet name it <c>B -> A -> B</c>, because which node they happen to
/// start from is an implementation detail. The message is what a user reads, so the message is what
/// is compared — and the shared search makes it deterministic in ordinal order, which is what lets
/// it be asserted at all.</para>
///
/// <para><b>What this does not prove.</b> The two inputs are built by different rules on purpose:
/// the editor reads declared bindings, the compiler reads the compiled graph. This verifies they
/// agree where both can see an edge. It does not, and cannot, prove they agree where only one of
/// them knows an edge exists — that difference is real and is the reason the adjacency is not
/// shared.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class WorkflowCycleAgreementTests
{
    private const string CycleMessage = "WorkflowCompletion cycle detected";

    /// <summary>
    /// The same three-node cycle, described once as declarations and once as a compiled config,
    /// must produce the same path in both messages.
    /// </summary>
    [Fact]
    public void The_editor_and_the_compiler_name_the_same_cycle_for_the_same_edges()
    {
        var config = CyclicToolkit(["A", "B"], ["B", "C"], ["C", "A"]);

        var editorErrors = new ConfigValidator().Validate(config).Errors;
        ToolkitCompiler.Compile(config, out var compilerDiagnostics);

        var editor = Assert.Single(editorErrors, e => e.Contains(CycleMessage, StringComparison.Ordinal));
        var compiled = Assert.Single(compilerDiagnostics, d => d.Code == "graph.cycle");

        Assert.Equal(editor, compiled.Message);
    }

    /// <summary>
    /// The path must be the whole cycle and must come back to where it started.
    /// </summary>
    /// <remarks>
    /// Asserting only that a cycle was found would accept a message naming one edge of it. The
    /// repetition of the first node at the end is what makes the path read as a loop rather than as
    /// a three-item list, so it is asserted rather than left to the formatter.
    /// </remarks>
    [Fact]
    public void The_reported_path_returns_to_the_node_it_started_from()
    {
        var config = CyclicToolkit(["A", "B"], ["B", "C"], ["C", "A"]);

        var errors = new ConfigValidator().Validate(config).Errors;
        var message = Assert.Single(errors, e => e.Contains(CycleMessage, StringComparison.Ordinal));

        var path = message[(message.IndexOf(CycleMessage, StringComparison.Ordinal) + CycleMessage.Length)..]
            .Trim(' ', ':')
            .TrimEnd('.');

        var nodes = path.Split(" -> ", StringSplitOptions.None);

        Assert.Equal(["A", "B", "C", "A"], nodes);
    }

    /// <summary>
    /// An acyclic chain must not produce a cycle from either side.
    /// </summary>
    /// <remarks>
    /// Without this, a search that reported everything as cyclic would pass both tests above.
    /// </remarks>
    [Fact]
    public void An_acyclic_chain_produces_no_cycle_on_either_side()
    {
        var config = CyclicToolkit(["A", "B"], ["B", "C"], ["C", "D"]);

        var errors = new ConfigValidator().Validate(config).Errors;
        ToolkitCompiler.Compile(config, out var compilerDiagnostics);

        Assert.DoesNotContain(errors, e => e.Contains(CycleMessage, StringComparison.Ordinal));
        Assert.DoesNotContain(compilerDiagnostics, d => d.Code == "graph.cycle");
    }

    /// <summary>
    /// With two cycles present, the one reported must not depend on the order the triggers were
    /// written in.
    /// </summary>
    /// <remarks>
    /// <para>This is the property that was actually lost when the two searches were separate: each
    /// walked its own dictionary, so the reported cycle followed insertion order. Reordering the
    /// triggers renamed the cycle without changing what the config means.</para>
    /// <para>Asserted by building the same two cycles twice, in opposite order, and requiring the
    /// same message. A search that reported whichever cycle it stumbled into would fail one.</para>
    /// </remarks>
    [Fact]
    public void The_reported_cycle_does_not_depend_on_the_order_the_triggers_were_written_in()
    {
        var first = Assert.Single(
            new ConfigValidator().Validate(TwoCycles(abFirst: true)).Errors,
            e => e.Contains(CycleMessage, StringComparison.Ordinal));

        var second = Assert.Single(
            new ConfigValidator().Validate(TwoCycles(abFirst: false)).Errors,
            e => e.Contains(CycleMessage, StringComparison.Ordinal));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A two-node cycle is found by both sides.
    /// </summary>
    /// <remarks>
    /// Worth pinning separately: a search that only looked for a return to the root after at least
    /// one intermediate hop would miss <c>A -> B -> A</c> while catching everything longer.
    /// </remarks>
    [Fact]
    public void A_two_node_cycle_is_found_by_both_sides()
    {
        var config = CyclicToolkit(["A", "B"], ["B", "A"]);

        var errors = new ConfigValidator().Validate(config).Errors;
        ToolkitCompiler.Compile(config, out var compilerDiagnostics);

        Assert.Contains(errors, e => e.Contains("A -> B -> A", StringComparison.Ordinal));
        Assert.Single(compilerDiagnostics, d => d.Code == "graph.cycle");
    }

    /// <summary>One workflow-completion binding per edge, in the order given.</summary>
    private static Toolkit CyclicToolkit(params string[][] edges)
    {
        var workflows = edges
            .SelectMany(edge => edge)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new ToolkitWorkflow { Id = id, Name = id, File = id.ToLowerInvariant() + ".kcs" })
            .ToList();

        var triggers = edges
            .Select((edge, index) => new Trigger
            {
                Id = "e" + index,
                Type = TriggerType.WorkflowCompletion,
                Config = new() { From = edge[0] },
                Bindings = [new TriggerBinding { Workflow = edge[1] }],
            })
            .ToList();

        return new Toolkit { Workflows = workflows, Triggers = triggers };
    }

    /// <summary>
    /// Two disjoint two-node cycles in one config — <c>A&lt;-&gt;B</c> and <c>X&lt;-&gt;Y</c> —
    /// with the <c>A/B</c> pair declared first when <paramref name="abFirst"/> is set, and the
    /// <c>X/Y</c> pair first otherwise.
    /// </summary>
    /// <remarks>
    /// <b>Both cycles must be present.</b> A helper that emitted only one of them would make the
    /// ordering test pass no matter which node the search started from, because there would be
    /// nothing to choose between.
    /// </remarks>
    private static Toolkit TwoCycles(bool abFirst)
    {
        string[][] ab = [["A", "B"], ["B", "A"]];
        string[][] xy = [["X", "Y"], ["Y", "X"]];

        return abFirst
            ? CyclicToolkit([.. ab, .. xy])
            : CyclicToolkit([.. xy, .. ab]);
    }
}
