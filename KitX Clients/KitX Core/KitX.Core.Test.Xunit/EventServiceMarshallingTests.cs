using KitX.Core.Contract.Event;
using KitX.Core.Event;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// EventService 阶段0 加固回归：SynchronizationContext 自动编组 + 类型不匹配不再静默丢弃。
/// </summary>
public class EventServiceMarshallingTests
{
    /// <summary>Records Post calls and executes the callback synchronously so assertions are deterministic.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            d(state);
        }
    }

    private static IDisposable SetContext(SynchronizationContext? ctx)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ctx);
        return new RestoreContext(previous);
    }

    private sealed class RestoreContext(SynchronizationContext? previous) : IDisposable
    {
        public void Dispose() => SynchronizationContext.SetSynchronizationContext(previous);
    }

    [Fact]
    public void Publish_OnDifferentContext_DispatchesViaCapturedContext()
    {
        var service = new EventService();
        var ui = new RecordingContext();
        var delivered = 0;

        using (SetContext(ui))
            service.Subscribe("test.marshal", (s, e) => delivered++);

        // Publish from a different context (simulating a background thread).
        using (SetContext(new SynchronizationContext()))
            service.Publish("test.marshal", EventArgs.Empty);

        Assert.Equal(1, delivered);
        Assert.Equal(1, ui.PostCount);
    }

    [Fact]
    public void Publish_OnSameContext_ExecutesSynchronously()
    {
        var service = new EventService();
        var ui = new RecordingContext();
        var delivered = 0;

        using (SetContext(ui))
        {
            service.Subscribe("test.sync", (s, e) => delivered++);
            service.Publish("test.sync", EventArgs.Empty);
        }

        Assert.Equal(1, delivered);
        Assert.Equal(0, ui.PostCount); // same context → no marshalling
    }

    [Fact]
    public void Publish_BackgroundSubscriber_ExecutesSynchronously()
    {
        var service = new EventService();
        var delivered = 0;

        // No SynchronizationContext on the subscribing thread → captured context is null.
        using (SetContext(null))
            service.Subscribe("test.bg", (s, e) => delivered++);

        using (SetContext(new SynchronizationContext()))
            service.Publish("test.bg", EventArgs.Empty);

        Assert.Equal(1, delivered);
    }

    [Fact]
    public void TypedSubscribe_TypeMismatch_LogsAndDrops_DoesNotThrow()
    {
        var service = new EventService();
        var delivered = 0;

        EventHandler<PortChangedEventArgs> handler = (s, e) => delivered++;
        service.Subscribe("test.mismatch", handler);

        // Publish EventArgs.Empty while the handler expects PortChangedEventArgs.
        service.Publish("test.mismatch", EventArgs.Empty);

        Assert.Equal(0, delivered); // dropped, but no exception
    }

    [Fact]
    public void TypedSubscribe_TypeMismatch_ThrowOnTypeMismatch_Throws()
    {
        var service = new EventService { ThrowOnTypeMismatch = true };
        var delivered = 0;

        EventHandler<PortChangedEventArgs> handler = (s, e) => delivered++;
        service.Subscribe("test.mismatch2", handler);

        Assert.Throws<EventTypeMismatchException>(() =>
            service.Publish("test.mismatch2", EventArgs.Empty));

        Assert.Equal(0, delivered);
    }

    [Fact]
    public void TypedSubscribe_TypeMatch_StillDelivers()
    {
        var service = new EventService();
        var delivered = 0;

        EventHandler<PortChangedEventArgs> handler = (s, e) => delivered++;
        service.Subscribe("test.match", handler);

        service.Publish<PortChangedEventArgs>("test.match", new PortChangedEventArgs { Port = 1 });

        Assert.Equal(1, delivered);
    }
}
