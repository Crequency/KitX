using KitX.ToolKit.Run;
using KitX.ToolKit.Data;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class RunSchedulerTests
{
    private static Toolkit ToolkitWith(params Trigger[] triggers)
        => new()
        {
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "B", Name = "B", File = "b.kcs" },
                new ToolkitWorkflow { Id = "C", Name = "C", File = "c.kcs" },
            ],
            Triggers = [.. triggers],
        };

    private static (RunScheduler Scheduler, RecordingExecutor Executor) Build(
        Toolkit toolkit, bool writeOutput = true)
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store, writeOutput: writeOutput);
        var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit), executor, new ToolkitFileStore(Path.GetTempPath()));
        return (scheduler, executor);
    }

    private static async Task AwaitAsync(Task task, int timeoutMs = 5000)
    {
        var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
        Assert.True(done == task, "timed out");
        await task;
    }

    [Fact]
    public async Task FanOut_Starts_All_Successors_On_Completion()
    {
        // Manual → A ; A completes → fan out to B and C (WorkflowCompletion edges).
        var (scheduler, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "A" }] },
            new Trigger { Id = "e1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                Bindings = [new() { Workflow = "B" }, new() { Workflow = "C" }] }));

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, _) => completed.TrySetResult();
        scheduler.StartRun("manual");
        await AwaitAsync(completed.Task);

        var calls = executor.Calls.Select(c => c.WorkflowId).ToList();
        Assert.Contains("A", calls);
        Assert.Contains("B", calls);
        Assert.Contains("C", calls);
        // B and C only after A.
        Assert.True(calls.IndexOf("A") < calls.IndexOf("B"));
        Assert.True(calls.IndexOf("A") < calls.IndexOf("C"));
    }

    [Fact]
    public async Task AndJoin_Starts_Target_Only_After_All_Predecessors_Complete()
    {
        // Manual fires both A and B (roots); edges A→C and B→C; C is an AND-join node.
        var (scheduler, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "A" }, new() { Workflow = "B" }] },
            new Trigger { Id = "e1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                Bindings = [new() { Workflow = "C" }] },
            new Trigger { Id = "e2", Type = TriggerType.WorkflowCompletion, Config = new() { From = "B" },
                Bindings = [new() { Workflow = "C" }] }));

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, _) => completed.TrySetResult();
        scheduler.StartRun("manual");
        await AwaitAsync(completed.Task);

        var calls = executor.Calls.Select(c => c.WorkflowId).ToList();
        Assert.Contains("C", calls);
        // C must be recorded only after both A and B.
        Assert.True(calls.IndexOf("A") < calls.IndexOf("C"));
        Assert.True(calls.IndexOf("B") < calls.IndexOf("C"));
    }

    [Fact]
    public async Task AndJoin_Does_Not_Start_When_A_Predecessor_Fails()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store, writeOutput: true, failWorkflows: ["B"]);
        var toolkit = ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "A" }, new() { Workflow = "B" }] },
            new Trigger { Id = "e1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                Bindings = [new() { Workflow = "C" }] },
            new Trigger { Id = "e2", Type = TriggerType.WorkflowCompletion, Config = new() { From = "B" },
                Bindings = [new() { Workflow = "C" }] });
        using var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit), executor, new ToolkitFileStore(Path.GetTempPath()));

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, e) => { if (e.IsSuccess) completed.TrySetResult(); };
        scheduler.StartRun("manual");
        await Task.Delay(500); // let A/B finish and the join gate evaluate

        var calls = executor.Calls.Select(c => c.WorkflowId).ToList();
        Assert.Contains("A", calls);
        Assert.Contains("B", calls);
        Assert.DoesNotContain("C", calls);
        Assert.Contains("B", executor.Failed);
        Assert.False(completed.Task.IsCompleted);
    }

    [Fact]
    public async Task Root_And_Join_Target_Activates_Exactly_Once()
    {
        // W is BOTH a Manual root binding target AND the completion successor of A. The
        // scheduler must not double-activate it (it would otherwise run once from the
        // Manual fire and again when A completes) — it runs exactly once.
        var toolkit = new Toolkit
        {
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "W", Name = "W", File = "w.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "manual", Type = TriggerType.Manual,
                    Bindings = [new() { Workflow = "A" }, new() { Workflow = "W" }] },
                new Trigger { Id = "e1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                    Bindings = [new() { Workflow = "W" }] },
            ],
        };
        var (scheduler, executor) = Build(toolkit);

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, _) => completed.TrySetResult();
        scheduler.StartRun("manual");
        await AwaitAsync(completed.Task);

        var calls = executor.Calls.Select(c => c.WorkflowId).ToList();
        Assert.Equal(1, calls.Count(id => id == "W"));
        Assert.Contains("A", calls);
    }

    [Fact]
    public void StartRun_Ignores_WorkflowCompletion_Trigger()
    {
        var (scheduler, _) = Build(ToolkitWith(
            new Trigger { Id = "edge", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                Bindings = [new() { Workflow = "B" }] }));

        var instance = scheduler.StartRun("edge");
        Assert.Null(instance);
    }

    [Fact]
    public async Task AndJoin_Refuses_When_Two_Branches_Publish_The_Same_Property()
    {
        // Design Q69: a join that receives one name from two branches is a modelling error, not a
        // race to resolve. Picking a winner would make the node's inputs depend on which branch
        // finished first — same inputs, different results.
        //
        // A fixed-name executor is what makes the collision happen: every node publishes "shared".
        var store = new DataStore();
        var executor = new FixedNameExecutor("shared");
        var toolkit = ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "A" }, new() { Workflow = "B" }] },
            new Trigger { Id = "e1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                Bindings = [new() { Workflow = "C" }] },
            new Trigger { Id = "e2", Type = TriggerType.WorkflowCompletion, Config = new() { From = "B" },
                Bindings = [new() { Workflow = "C" }] });

        var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit), executor, new ToolkitFileStore(Path.GetTempPath()));

        RunCompletedEventArgs? completed = null;
        var done = new TaskCompletionSource();
        scheduler.RunCompleted += (_, e) => { completed = e; done.TrySetResult(); };

        scheduler.StartRun("manual");
        await AwaitAsync(done.Task);

        // C never ran: its join could not be assembled.
        Assert.DoesNotContain("C", executor.Calls);

        Assert.NotNull(completed);
        Assert.Equal(RunOutcome.Failed, completed.Outcome);
        Assert.NotNull(completed.Failure);
        Assert.Equal("flow.join.conflict", completed.Failure!.Code);
        Assert.Equal("shared", completed.Failure.Path?.Key);

        scheduler.Dispose();
    }

    [Fact]
    public async Task A_node_that_threw_reports_the_threw_code_not_a_generic_failure()
    {
        // The four ways a node can fail used to arrive at the run as bare text and were all
        // recorded under one code, so a consumer could not tell "the file is gone" from "the body
        // threw" without parsing English. This pins two of them apart.
        var toolkit = ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "A" }] });

        var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit),
            new ThrowingExecutor(),
            new ToolkitFileStore(Path.GetTempPath()));

        RunCompletedEventArgs? completed = null;
        var done = new TaskCompletionSource();
        scheduler.RunCompleted += (_, e) => { completed = e; done.TrySetResult(); };

        scheduler.StartRun("manual");
        await AwaitAsync(done.Task);

        Assert.NotNull(completed);
        Assert.Equal(RunOutcome.Failed, completed.Outcome);
        Assert.Equal("flow.workflow.threw", completed.Failure!.Code);
        Assert.Equal("A", completed.Failure.Path?.WorkflowId);

        scheduler.Dispose();
    }

    [Fact]
    public async Task A_node_whose_workflow_is_missing_reports_the_not_found_code()
    {
        var toolkit = ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "A" }] });

        // A scheduler with no compiled workflow table entry for "A" would mean a broken config;
        // what this exercises is the executor reporting the file could not be loaded. The
        // recording executor below answers with that code directly, standing in for the real
        // one — the assertion is about what the run does with the code, not about file I/O.
        var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit),
            new MissingWorkflowExecutor(),
            new ToolkitFileStore(Path.GetTempPath()));

        RunCompletedEventArgs? completed = null;
        var done = new TaskCompletionSource();
        scheduler.RunCompleted += (_, e) => { completed = e; done.TrySetResult(); };

        scheduler.StartRun("manual");
        await AwaitAsync(done.Task);

        Assert.NotNull(completed);
        Assert.Equal("flow.workflow.not-found", completed.Failure!.Code);

        scheduler.Dispose();
    }

    /// <summary>Always throws — the run must report `flow.workflow.threw`.</summary>
    private sealed class ThrowingExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    /// <summary>Reports a missing workflow file the way the real executor does.</summary>
    private sealed class MissingWorkflowExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(
                workflowId, false, $"Workflow '{workflowId}' not found", null, "flow.workflow.not-found"));
    }

    /// <summary>Publishes one fixed property name from every workflow, so joins collide.</summary>
    private sealed class FixedNameExecutor(string propertyName) : IWorkflowExecutor
    {
        public List<string> Calls { get; } = [];

        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
        {
            lock (Calls)
                Calls.Add(workflowId);

            return Task.FromResult(new WorkflowExecutionResult(
                workflowId, true, null,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new Dictionary<string, string> { [propertyName] = $"from:{workflowId}" })));
        }
    }

    [Fact]
    public async Task Concurrent_Fires_Are_Instance_Scoped()
    {
        // Two manual fires fan out to the same workflow; each run must get its own
        // instance-scoped output namespace (distinct DataStore keys).
        var (scheduler, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "A" }] }));

        var completed = new TaskCompletionSource();
        int remaining = 2;
        scheduler.RunCompleted += (_, _) => { if (Interlocked.Decrement(ref remaining) == 0) completed.TrySetResult(); };

        scheduler.StartRun("manual");
        scheduler.StartRun("manual");
        await AwaitAsync(completed.Task);

        var namespaces = executor.Calls
            .Where(c => c.Overrides.TryGetValue(DataStoreScope.OutputNamespaceConstant, out _))
            .Select(c => c.Overrides[DataStoreScope.OutputNamespaceConstant]!)
            .ToList();

        Assert.Equal(2, namespaces.Count);
        Assert.Equal(2, namespaces.Distinct().Count());
    }
}
