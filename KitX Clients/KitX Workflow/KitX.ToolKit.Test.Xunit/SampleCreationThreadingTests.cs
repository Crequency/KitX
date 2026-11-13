using System.Collections.Concurrent;
using KitX.ToolKit.View.Avalonia.Host;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Sample creation, run on a thread that has a <see cref="SynchronizationContext"/> — the shape a UI
/// thread has and the shape every other test in this repository does not.
/// </summary>
/// <remarks>
/// <para><b>Why this test exists at all.</b> Creating the sample writes two <c>.kcs</c> files
/// asynchronously. On 2026-11-13 the first launch of the host window hung forever: the write's
/// continuation was posted back to the Avalonia UI thread, and that thread was sitting inside
/// <c>GetAwaiter().GetResult()</c> waiting for the write. The window never drew, and the disk held
/// <c>scan.kcs.tmp</c> with no <c>scan.kcs</c>.</para>
///
/// <para><b>Why the existing 320 tests could not have caught it.</b> They all run on thread-pool
/// threads with no <see cref="SynchronizationContext"/>, so awaiting a file write resumes on the pool
/// and never needs the blocked thread. The bug is invisible from there by construction — the same
/// code, two hosts, one green and one dead. <c>--self-check</c> is green for exactly this reason: it
/// returns before any Avalonia lifetime is built.</para>
///
/// <para><b>What is simulated and what is real.</b> The context and its pump are a stand-in for
/// Avalonia's. Everything below them is the real thing: the real <see cref="HostSession"/>, the real
/// composition root, the real stores, real files. Only the dispatcher is a stand-in, and it is a
/// faithful one for the single property under test — a continuation must run on this thread, and
/// this thread is the one calling.</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SampleCreationThreadingTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "kitx-host-ctx-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void Creating_the_sample_does_not_deadlock_a_thread_that_has_a_synchronization_context()
    {
        using var pump = new PumpThread();

        HostSession? session = null;
        Task<(string Id, bool Created)>? work = null;

        // The invoke must *return* for the test to observe a hung task. If creation blocks its own
        // thread, this wait expires — that is the deadlock, seen from the outside.
        var returned = pump.Invoke(() =>
        {
            session = new HostSession(_root);
            work = session.EnsureSampleAsync();
        });

        Assert.True(returned,
            "EnsureSampleAsync did not return control to the caller: it blocked the thread it was "
            + "invoked on. On a UI thread that thread also has to run the continuation, so this is "
            + "the deadlock, not merely slowness.");

        Assert.NotNull(work);

        // The continuation is posted to the pump. If the pump thread were blocked, this never
        // completes — a second, independent way for the same defect to show itself.
        Assert.True(work!.Wait(TimeSpan.FromSeconds(60)),
            "the task never completed: its continuation was posted to the pump thread and the pump "
            + "thread was not able to run it.");

        var (id, created) = work.Result;

        Assert.Equal(SampleToolkit.Id, id);
        Assert.True(created, "this test owns a fresh root, so the sample had to be created");

        // The half-written state is the symptom people actually saw: a .tmp with no .kcs.
        var toolkitDir = Path.Combine(_root, SampleToolkit.Id);
        Assert.True(File.Exists(Path.Combine(toolkitDir, "scan.kcs")),
            "scan.kcs is missing — the write never got past its temp file");
        Assert.False(File.Exists(Path.Combine(toolkitDir, "scan.kcs.tmp")),
            "scan.kcs.tmp survived: the swap never happened");

        pump.Invoke(() => session!.Dispose());
    }

    /// <summary>
    /// One thread, a queue, and a loop that drains it — the minimum a UI thread has to be.
    /// </summary>
    /// <remarks>
    /// <see cref="SynchronizationContext.Post"/> enqueues and returns; the loop runs callbacks in
    /// order on the owning thread. That is what makes blocking on this thread a deadlock rather than
    /// a delay: the callback that would release it is in the queue this thread is not draining.
    /// </remarks>
    private sealed class PumpThread : IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;
        private readonly PumpContext _context;

        internal PumpThread()
        {
            _context = new PumpContext(_queue);
            _thread = new Thread(Loop) { IsBackground = true, Name = "synchronization-context pump" };
            _thread.Start();
        }

        private void Loop()
        {
            SynchronizationContext.SetSynchronizationContext(_context);

            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        /// <summary>Runs <paramref name="action"/> on the pump thread.</summary>
        /// <returns>False when it did not finish — the caller has to report that, not hang on it.</returns>
        internal bool Invoke(Action action, int seconds = 30)
        {
            using var done = new ManualResetEventSlim();

            _context.Post(_ =>
            {
                try { action(); }
                finally { done.Set(); }
            }, null);

            return done.Wait(TimeSpan.FromSeconds(seconds));
        }

        public void Dispose()
        {
            // Complete then join: the loop exits when the queue is drained and marked complete.
            // Nothing may be posted after CompleteAdding — doing so throws InvalidOperationException,
            // which is a fault in this harness, not in the code it is measuring.
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private sealed class PumpContext(
            BlockingCollection<(SendOrPostCallback Callback, object? State)> queue) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

            public override void Send(SendOrPostCallback d, object? state) => queue.Add((d, state));

            // The same context, not a copy: a copy would have its own queue with no thread draining
            // it, which would reproduce the deadlock for reasons that have nothing to do with the code
            // under test.
            public override SynchronizationContext CreateCopy() => this;
        }
    }
}
