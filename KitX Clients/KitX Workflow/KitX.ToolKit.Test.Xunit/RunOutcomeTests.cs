using System.Collections.Concurrent;
using KitX.ToolKit.Run;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Acceptance tests for the run verdict surface the Bench refactor introduced (ADR-0009): the
/// run — not the instance — decides whether one execution succeeded, failed or was cancelled,
/// and it reports that decision only through events and readable state.
/// </summary>
/// <remarks>
/// <para>Every assertion here reads something a consumer can read: a raised event,
/// <see cref="WorkflowRun.Outcome"/>, <see cref="WorkflowRun.NodeStates"/>, or the executor's
/// call log. Nothing reaches into private state, so these tests survive a rewrite of the
/// internals for as long as the contract holds — which is what pinning a contract means.</para>
///
/// <para><b>Test 3 is the reason this file exists at all.</b> The old implementation kept an
/// <c>_anyFailed</c> flag on the instance that was set by a failure and never reset, so a single
/// early failure made every subsequent run report failure. That is a bug no single-run test can
/// catch: it only shows up in the run *after* the failing one.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class RunOutcomeTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────────

    /// <summary>A ToolKit with the given workflows and triggers; an empty <c>Id</c> would make
    /// every run's data namespace unnameable, so give it one.</summary>
    private static Toolkit ToolkitWith(string[] workflowIds, params Trigger[] triggers)
        => new()
        {
            Id = "tk-run-outcome",
            Meta = new ToolkitMeta { Name = "run-outcome" },
            Workflows =
                [.. workflowIds.Select(id => new ToolkitWorkflow { Id = id, Name = id, File = $"{id}.kcs" })],
            Triggers = [.. triggers],
        };

    /// <summary>A manual trigger that starts every named workflow at once (parallel roots).</summary>
    private static Trigger Manual(params string[] workflowIds)
        => new()
        {
            Id = "manual",
            Type = TriggerType.Manual,
            Bindings = [.. workflowIds.Select(id => new TriggerBinding { Workflow = id })],
        };

    /// <summary>A completion edge (<paramref name="from"/> → <paramref name="to"/>).</summary>
    private static Trigger Edge(string from, string to)
        => new()
        {
            Id = $"edge-{from}-{to}",
            Type = TriggerType.WorkflowCompletion,
            Config = new TriggerConfig { From = from },
            Bindings = [new TriggerBinding { Workflow = to }],
        };

    private static (RunScheduler Scheduler, DelegatingExecutor Executor) Build(
        Toolkit toolkit,
        Func<string, int, CancellationToken, Task<WorkflowExecutionResult>> execute,
        int maxConcurrentNodes = 0)
    {
        var store = new DataStore();
        var executor = new DelegatingExecutor(execute);
        var options = new ToolKitRunOptions { MaxConcurrentNodes = maxConcurrentNodes };
        var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit), executor, store,
            new ToolkitFileStore(Path.GetTempPath()), options: options);
        return (scheduler, executor);
    }

    /// <summary>
    /// Awaits a signalled task, failing the test with a message naming what never happened
    /// instead of hanging until the test host's own timeout.
    /// </summary>
    private static async Task AwaitSignal(Task task, string what, int timeoutMs = 5000)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeoutMs));
        Assert.True(finished == task, $"{what} did not happen within {timeoutMs} ms");
    }

    /// <summary>
    /// Scripted <see cref="IWorkflowExecutor"/>: the test supplies the body, so one fake covers
    /// failure injection, cancellation and concurrency accounting without three near-identical
    /// classes. The call <i>ordinal</i> is handed to the body so a test can fail exactly one run
    /// in a sequence of otherwise identical runs.
    /// </summary>
    private sealed class DelegatingExecutor : IWorkflowExecutor
    {
        private readonly Func<string, int, CancellationToken, Task<WorkflowExecutionResult>> _body;
        private int _calls;

        internal DelegatingExecutor(Func<string, int, CancellationToken, Task<WorkflowExecutionResult>> body)
            => _body = body;

        /// <summary>Workflow ids in the order the scheduler invoked them.</summary>
        internal ConcurrentQueue<string> Invoked { get; } = new();

        /// <summary>Total invocations so far.</summary>
        internal int Calls => Volatile.Read(ref _calls);

        public async Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId,
            string filePath,
            IReadOnlyDictionary<string, string?>? overrides,
            CancellationToken ct)
        {
            Invoked.Enqueue(workflowId);
            var ordinal = Interlocked.Increment(ref _calls);
            return await _body(workflowId, ordinal, ct).ConfigureAwait(false);
        }
    }

    // ── 1. Failure truncation ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Failure_Truncation_Raises_A_Skipped_Nodes_Event()
    {
        // ADR-0009 acceptance #1. A → B → C with A failing leaves B and C unreachable. Without
        // the NodesSkipped event they are observationally identical to nodes that were never in
        // the graph: nothing starts, so nothing reports, so a consumer counting completions
        // concludes the run "finished early" instead of "was cut short".
        var toolkit = ToolkitWith(["A", "B", "C"], Manual("A"), Edge("A", "B"), Edge("B", "C"));

        var (scheduler, executor) = Build(toolkit, (id, _, _) => Task.FromResult(
            new WorkflowExecutionResult(id, id != "A", id == "A" ? "injected failure" : null, null)));

        var skippedWaves = new ConcurrentQueue<RunNodesSkippedEventArgs>();
        var completed = new TaskCompletionSource<RunCompletedEventArgs>();
        scheduler.RunCompleted += (_, e) => completed.TrySetResult(e);

        var run = scheduler.StartRun("manual", onRunCreated: r =>
            r.NodesSkipped += (_, e) => skippedWaves.Enqueue(e));

        await AwaitSignal(completed.Task, "the run to settle");
        var settled = await completed.Task;

        Assert.NotNull(run);
        Assert.Equal(RunOutcome.Failed, settled.Outcome);
        Assert.Equal(RunOutcome.Failed, run!.Outcome);
        Assert.Equal(1, run.FailedRuns);

        var states = run.NodeStates;
        Assert.Equal(RunNodeState.Failed, states["A"]);
        Assert.Equal(RunNodeState.Skipped, states["B"]);
        Assert.Equal(RunNodeState.Skipped, states["C"]);

        // Exactly one truncation wave, attributed to the node whose failure caused it, naming
        // everything it made unreachable — transitively, not just A's immediate neighbours.
        var wave = Assert.Single(skippedWaves);
        Assert.Equal(run.RunId, wave.RunId);
        Assert.Equal("A", wave.FailedNodeId);
        Assert.Equal(new[] { "B", "C" }, wave.NodeIds.OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // Skipping means the bodies never ran, so the executor must never have seen them.
        Assert.DoesNotContain("B", executor.Invoked);
        Assert.DoesNotContain("C", executor.Invoked);
    }

    // ── 2. Cancellation is not failure ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_Settles_As_Cancelled_Not_Failed()
    {
        // ADR-0009 acceptance #2. The old implementation caught OperationCanceledException and
        // turned it into a failed run, so "the user pressed stop" and "it broke" arrived at the
        // caller as the same answer. A run whose only node blocks until cancelled must settle as
        // Cancelled and count no failures.
        var toolkit = ToolkitWith(["block"], Manual("block"));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (scheduler, _) = Build(toolkit, async (id, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct); // only leaves via cancellation
            return new WorkflowExecutionResult(id, true, null, null);
        });

        var completedCount = 0;
        var completed = new TaskCompletionSource<RunCompletedEventArgs>();
        var run = scheduler.StartRun("manual", onRunCreated: r =>
            r.Completed += (_, e) =>
            {
                Interlocked.Increment(ref completedCount);
                completed.TrySetResult(e);
            });

        await AwaitSignal(entered.Task, "the node body to start");
        Assert.Null(run!.Outcome); // still in flight: no verdict guessed yet

        run.Cancel();

        await AwaitSignal(completed.Task, "the run to settle after cancellation");
        var settled = await completed.Task;

        Assert.Equal(RunOutcome.Cancelled, settled.Outcome);
        Assert.Equal(RunOutcome.Cancelled, run.Outcome);
        Assert.Equal(0, run.FailedRuns);
        Assert.Equal(RunNodeState.Cancelled, run.NodeStates["block"]);

        // Completed is the settle-once signal; firing it twice would double-count the run.
        Assert.Equal(1, Volatile.Read(ref completedCount));
    }

    // ── 3. One failure does not poison later runs ───────────────────────────────────────────

    [Fact]
    public async Task One_Failed_Run_Does_Not_Poison_Later_Runs()
    {
        // ADR-0009 acceptance #3: the regression test for the old _anyFailed flag. Fifty runs of
        // the same single-node ToolKit, failing only the 25th. A verdict that leaks across runs
        // shows up solely in runs #26..#50 — after the failure — so this cannot be shortened into
        // "fail a run and check it" without losing the very thing it guards.
        const int runCount = 50;
        const int failingRun = 25;

        var toolkit = ToolkitWith(["A"], Manual("A"));

        var (scheduler, _) = Build(toolkit, (id, ordinal, _) => Task.FromResult(
            new WorkflowExecutionResult(
                id,
                ordinal != failingRun,
                ordinal == failingRun ? "injected failure" : null,
                null)));

        // One subscription for the whole sequence. The runs are awaited strictly one at a time,
        // so delivery order *is* start order — the queue index is the run number, which is what
        // makes "fail the 25th" checkable without threading a counter through the event.
        TaskCompletionSource? pending = null;
        scheduler.RunCompleted += (_, _) => pending?.TrySetResult();

        var outcomes = new List<RunOutcome>();
        var runIds = new List<string>();
        var namespaces = new List<string>();

        for (var i = 1; i <= runCount; i++)
        {
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = settled; // assigned before StartRun, so a synchronous settle still lands here

            var run = scheduler.StartRun("manual");
            await AwaitSignal(settled.Task, $"run #{i} to settle");

            Assert.NotNull(run);
            var outcome = run!.Outcome;
            Assert.NotNull(outcome);

            outcomes.Add(outcome.Value);
            runIds.Add(run.RunId);
            namespaces.Add(run.NamespaceId);
        }

        Assert.Equal(RunOutcome.Failed, outcomes[failingRun - 1]);
        for (var i = 0; i < runCount; i++)
            if (i != failingRun - 1)
                Assert.Equal(RunOutcome.Succeeded, outcomes[i]);

        // No state leaks between runs: every run minted its own id and its own data namespace,
        // and none of them is still registered as active.
        Assert.Equal(runCount, runIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(runCount, namespaces.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(0, scheduler.ActiveInstanceCount);
    }

    // ── 4. Bounded node concurrency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Node_Concurrency_Is_Capped_Without_Dropping_Nodes()
    {
        // ADR-0008 D2 (S3 criterion ④). Six parallel roots through a MaxConcurrentNodes = 2 gate.
        // The limit must queue work, never reject it: exceeding it makes the run slower, not
        // smaller. A gate that dropped nodes would still pass a "peak <= 2" assertion, which is
        // why the executed-count assertion below is the one that matters.
        string[] ids = ["N1", "N2", "N3", "N4", "N5", "N6"];
        var toolkit = ToolkitWith(ids, Manual(ids));

        var inFlight = 0;
        var levels = new ConcurrentQueue<int>();

        // Two nodes must be in flight before either is allowed to finish. That makes "the cap was
        // reached" a fact rather than a timing accident, and the bounded wait keeps a broken gate
        // reporting as a failed assertion instead of hanging the test run.
        var rendezvous = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (scheduler, executor) = Build(toolkit, async (id, _, ct) =>
        {
            // Sampling the level on entry is sufficient: concurrency only ever falls between two
            // increments, so the maximum entry sample *is* the observed peak.
            var level = Interlocked.Increment(ref inFlight);
            levels.Enqueue(level);
            if (level >= 2)
                rendezvous.TrySetResult();

            // The delay is a safety net, not a sleep: every wave leaves as soon as its partner
            // arrives, so only a gate that genuinely never grants a second slot waits it out.
            await Task.WhenAny(rendezvous.Task, Task.Delay(400, ct));

            Interlocked.Decrement(ref inFlight);
            return new WorkflowExecutionResult(id, true, null, null);
        }, maxConcurrentNodes: 2);

        var completed = new TaskCompletionSource<RunCompletedEventArgs>();
        scheduler.RunCompleted += (_, e) => completed.TrySetResult(e);
        var run = scheduler.StartRun("manual");

        await AwaitSignal(completed.Task, "all six nodes to finish");
        var settled = await completed.Task;

        Assert.All(levels, level => Assert.InRange(level, 1, 2));
        Assert.Equal(2, levels.Max());          // the cap was actually reached, not merely respected
        Assert.Equal(ids.Length, levels.Count); // all six entered the body: queued, never dropped
        Assert.Equal(ids.Length, executor.Calls);

        Assert.Equal(RunOutcome.Succeeded, settled.Outcome);
        Assert.Equal(RunOutcome.Succeeded, run!.Outcome);
    }

    // ── 5. No verdict before the run settles ────────────────────────────────────────────────

    [Fact]
    public async Task Outcome_Is_Null_Until_The_Run_Settles()
    {
        // The run produces its verdict, it does not guess one (ADR-0003 D3). Exposing
        // "Succeeded" for a run whose only node is still in flight would let a caller act on a
        // result that has not happened yet — and it is exactly the shape of bug that a nullable
        // Outcome is meant to make unrepresentable.
        var toolkit = ToolkitWith(["A"], Manual("A"));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (scheduler, _) = Build(toolkit, async (id, _, ct) =>
        {
            entered.TrySetResult();
            await gate.Task.WaitAsync(ct);
            return new WorkflowExecutionResult(id, true, null, null);
        });

        var completed = new TaskCompletionSource<RunCompletedEventArgs>();
        scheduler.RunCompleted += (_, e) => completed.TrySetResult(e);
        var run = scheduler.StartRun("manual");

        Assert.NotNull(run);
        Assert.Null(run!.Outcome); // nothing has settled yet

        await AwaitSignal(entered.Task, "the node body to start");
        Assert.Null(run.Outcome); // still null once the body is genuinely in flight

        gate.TrySetResult();

        await AwaitSignal(completed.Task, "the run to settle");
        var settled = await completed.Task;

        Assert.NotNull(run.Outcome);
        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Equal(RunOutcome.Succeeded, settled.Outcome);
    }
}
