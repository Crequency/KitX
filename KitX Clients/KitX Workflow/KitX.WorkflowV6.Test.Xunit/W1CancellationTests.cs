// ─────────────────────────────────────────────────────────────────────────────
// W-1 tests: non-debug execution must be cancellable (the generated code carries
// cancellation checks) and the calling thread must never freeze on an infinite
// loop. Also guards the instrumentation overhead on large loops.
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using KitX.WorkflowV6.Ir;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class W1CancellationTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public W1CancellationTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Infinite_While_Workflow_Is_Cancellable_And_Does_Not_Freeze()
    {
        // `while true` with a tight (no-op-ish) body: before W-1 the generated code
        // had NO cancellation checks on the non-debug path, so this loop would run
        // forever and Stop/cancellation would never take effect.
        var ir = _fixture.KsLens.Parse("""
            var {
                int counter
            }
            while true:
                1 > counter
            """, []);
        var backend = _fixture.MakeBackend();
        using var cts = new CancellationTokenSource();

        var runTask = Task.Run(async () =>
        {
            try
            {
                await backend.ExecuteAsync(ir, null, cts.Token);
                return "completed";
            }
            catch (OperationCanceledException)
            {
                return "cancelled";
            }
        });

        // Give the loop time to spin, then cancel.
        await Task.Delay(300);
        cts.Cancel();

        // The run must terminate promptly after cancellation (never freeze the
        // calling thread / run task).
        var finished = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(runTask, finished);
        Assert.Equal("cancelled", await runTask);
    }

    [Fact]
    public async Task Large_Loop_Execution_Overhead_Is_Bounded()
    {
        // 2M iterations, each paying a per-iteration cancellation check plus the
        // emitted statements. Guards against pathological instrumentation (per-
        // iteration allocation, file IO, unbounded counter growth, ...).
        var ir = _fixture.KsLens.Parse("""
            var {
                int counter
            }
            forEach Range(0, 2000000, 1) as x:
                counter, x > Add > counter
            """, []);
        var backend = _fixture.MakeBackend();

        var sw = Stopwatch.StartNew();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess, $"Execution failed: {result.ErrorMessage}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"2M-iteration loop took {sw.Elapsed} — instrumentation overhead out of bounds");
    }

    [Fact]
    public async Task While_Loop_With_Empty_Body_Is_Still_Cancellable()
    {
        // The per-iteration check must be emitted INSIDE the loop even when the body
        // is empty (an empty-body infinite loop would otherwise never hit a check).
        var ir = _fixture.KsLens.Parse("""
            while true:
                1
            """, []);
        var backend = _fixture.MakeBackend();
        using var cts = new CancellationTokenSource();

        var runTask = Task.Run(async () =>
        {
            try
            {
                await backend.ExecuteAsync(ir, null, cts.Token);
                return "completed";
            }
            catch (OperationCanceledException)
            {
                return "cancelled";
            }
        });

        await Task.Delay(300);
        cts.Cancel();

        var finished = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(runTask, finished);
        Assert.Equal("cancelled", await runTask);
    }

    [Fact]
    public async Task Foreach_Over_Large_Source_Is_Cancellable()
    {
        // foreach over a huge range: per-iteration checks must keep it cancellable.
        var ir = _fixture.KsLens.Parse("""
            forEach Range(0, 100000000, 1) as x:
                x
            """, []);
        var backend = _fixture.MakeBackend();
        using var cts = new CancellationTokenSource();

        var runTask = Task.Run(async () =>
        {
            try
            {
                await backend.ExecuteAsync(ir, null, cts.Token);
                return "completed";
            }
            catch (OperationCanceledException)
            {
                return "cancelled";
            }
        });

        await Task.Delay(300);
        cts.Cancel();

        var finished = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(runTask, finished);
        Assert.Equal("cancelled", await runTask);
    }
}
