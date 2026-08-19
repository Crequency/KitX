using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class BenchSchedulerTests
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

    private static (BenchScheduler Scheduler, RecordingExecutor Executor) Build(
        Toolkit toolkit, bool writeOutput = true)
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store, writeOutput: writeOutput);
        var scheduler = new BenchScheduler(toolkit, executor, store, new ToolkitFileStore(Path.GetTempPath()));
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
        using var scheduler = new BenchScheduler(toolkit, executor, store, new ToolkitFileStore(Path.GetTempPath()));

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
