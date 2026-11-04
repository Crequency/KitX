namespace KitX.ToolKit.Run;

/// <summary>
/// A bounded-concurrency gate for node bodies (ADR-0008 D2). Nodes queue here instead of each
/// spawning its own <see cref="Task"/>.
/// </summary>
/// <remarks>
/// <para><b>Why bounded, and why at node level.</b> The old scheduler called
/// <c>Task.Run</c> once per node. A wide graph plus many concurrent runs therefore produced an
/// unbounded number of threads all competing for the same CPUs and the same script compiler
/// cache (which is not thread-safe — ADR-0008 D3, fixed outside this assembly). A limit does
/// not repair that cache, but it bounds how many threads can be inside it at once.</para>
///
/// <para><b>Two limits, not one.</b> This type governs how many nodes of any single run may be
/// in flight. How many runs may exist at once is a separate concern enforced by the scheduler
/// (admission control, Q31 "reject the newest"). Merging them would make "one run using the
/// whole budget" look the same as "many runs each using one slot".</para>
///
/// <para><b>Admission is not cancellation.</b> A caller waiting for a slot observes the run's
/// token. Cancelling the run while a node is queued makes the wait throw; the scheduler turns
/// that into an <see cref="RunNodeState.Cancelled"/> node, not a failure.</para>
/// </remarks>
internal sealed class BoundedNodeScheduler : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private bool _disposed;

    /// <summary>
    /// Creates the gate. <paramref name="maxConcurrency"/> of zero or less means "one per
    /// CPU", which is the behaviour most callers want and the least surprising default.
    /// </summary>
    internal BoundedNodeScheduler(int maxConcurrency)
    {
        var limit = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
        MaxConcurrency = Math.Max(1, limit);
        _slots = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    }

    /// <summary>The effective limit, after the default was resolved.</summary>
    internal int MaxConcurrency { get; }

    /// <summary>Number of slots currently held. Exposed for diagnostics and tests.</summary>
    internal int InFlight => MaxConcurrency - _slots.CurrentCount;

    /// <summary>
    /// Waits for a slot. Throws <see cref="OperationCanceledException"/> when
    /// <paramref name="ct"/> fires first — the caller is expected to treat that as a cancelled
    /// node, not as an error.
    /// </summary>
    internal async Task AcquireAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _slots.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a slot without observing a cancellation token. Used only on the shutdown path,
    /// where the run is being torn down and there is nothing left to cancel.
    /// </summary>
    internal Task AcquireAsync() => _slots.WaitAsync();

    /// <summary>Releases a slot. Safe to call from a <c>finally</c> after disposal.</summary>
    internal void Release()
    {
        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // The run was disposed while a node body was unwinding. Nothing to release.
        }
        catch (SemaphoreFullException)
        {
            // Defensive: a double release would corrupt the count, so swallow rather than let
            // a bookkeeping bug surface as a crash in the middle of a workflow.
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _slots.Dispose();
    }
}
