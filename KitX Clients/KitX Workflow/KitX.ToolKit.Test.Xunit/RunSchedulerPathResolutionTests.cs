using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Run;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The scheduler's third argument is the root a toolkit's workflow files live under, and the path
/// it hands the executor has to sit inside that root under the toolkit's own id.
///
/// <para><b>Why this needs its own test.</b> The parameter used to be a <c>ToolkitFileStore</c>,
/// whose constructor accepts any directory. Eight test sites passed
/// <c>new ToolkitFileStore(Path.GetTempPath())</c> and every one of them compiled — so the type
/// said "a store" while the contract meant "this toolkit's root", and nothing could tell the
/// difference. Narrowing the parameter to a root makes the wrong call impossible to write at eight
/// sites, but a root is still just a string: it cannot stop a caller from passing the wrong one.
/// This test pins what the scheduler does with it, so a future change that quietly stops using the
/// root (an <c>AbsolutePath</c> that is allowed to be empty, a cache keyed by workflow id alone)
/// shows up as a path assertion rather than as an unexplained file-not-found in production.</para>
///
/// <para><b>Why the expected value is derived, not hard-coded.</b> An assertion like
/// <c>EndsWith("a.kcs")</c> passes for any directory at all, which is exactly the defect. The test
/// builds the expectation from the root it passed in and requires the toolkit id to appear between
/// them.</para>
/// </summary>
[Trait("Category", "Unit")]
public class RunSchedulerPathResolutionTests
{
    private const string TkId = "path-tk";

    [Fact]
    public async Task Executor_receives_the_workflow_path_under_the_root_and_toolkit_id()
    {
        var toolkit = ToolkitWith();
        var observed = new CapturingExecutor();

        // A path that is obviously not the process temp directory, so a scheduler that ignored the
        // root could not produce it by accident.
        var root = Path.Combine(Path.GetTempPath(), "kitx-path-" + Guid.NewGuid().ToString("N"));

        using var scheduler = new RunScheduler(CompileFixtures.Compile(toolkit), observed, root);

        var done = new TaskCompletionSource();
        scheduler.RunCompleted += (_, _) => done.TrySetResult();
        scheduler.StartRun("manual");

        var finished = await Task.WhenAny(done.Task, Task.Delay(10_000));
        Assert.True(ReferenceEquals(finished, done.Task), "run did not complete in time");

        var path = Assert.Single(observed.Paths);

        var expected = Path.GetFullPath(Path.Combine(root, TkId, "a.kcs"));
        Assert.Equal(expected, path);

        // The two halves said separately: it is inside the root the caller gave, and the toolkit id
        // is the segment that makes it this toolkit's directory rather than a sibling's.
        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
        Assert.Contains(TkId + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_schedulers_over_different_roots_do_not_produce_the_same_path()
    {
        // The complement of the test above: if the scheduler ignored its root and resolved against
        // some process-wide default, both of these would name the same file and the run of one
        // toolkit would read another's workflow.
        var toolkit = ToolkitWith();

        var first = new CapturingExecutor();
        var second = new CapturingExecutor();

        using var a = new RunScheduler(
            CompileFixtures.Compile(toolkit), first,
            Path.Combine(Path.GetTempPath(), "kitx-root-a-" + Guid.NewGuid().ToString("N")));
        using var b = new RunScheduler(
            CompileFixtures.Compile(toolkit), second,
            Path.Combine(Path.GetTempPath(), "kitx-root-b-" + Guid.NewGuid().ToString("N")));

        await RunToCompletionAsync(a);
        await RunToCompletionAsync(b);

        Assert.NotEqual(Assert.Single(first.Paths), Assert.Single(second.Paths));
    }

    private static Toolkit ToolkitWith()
        => new()
        {
            Id = TkId,
            Meta = new ToolkitMeta { Name = "path-demo" },
            Workflows = [new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" }],
            Triggers =
            [
                new Trigger
                {
                    Id = "manual", Type = TriggerType.Manual,
                    Bindings = [new TriggerBinding { Workflow = "A" }],
                },
            ],
        };

    private static async Task RunToCompletionAsync(RunScheduler scheduler)
    {
        var done = new TaskCompletionSource();
        scheduler.RunCompleted += (_, _) => done.TrySetResult();
        scheduler.StartRun("manual");

        var finished = await Task.WhenAny(done.Task, Task.Delay(10_000));
        Assert.True(ReferenceEquals(finished, done.Task), "run did not complete in time");
    }

    /// <summary>Records the path it was asked to run, and never touches disk.</summary>
    private sealed class CapturingExecutor : IWorkflowExecutor
    {
        public List<string> Paths { get; } = [];

        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides,
            CancellationToken ct)
        {
            Paths.Add(filePath);
            return Task.FromResult(new WorkflowExecutionResult(
                workflowId, true, null, JsonSerializer.SerializeToElement(new { ok = true })));
        }
    }
}
