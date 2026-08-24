using KitX.Core.Contract.Event;
using KitX.Core.Event;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// EventService 阶段1 回归：强类型话题 API（.NET 类型作 key，编译期类型安全）。
/// </summary>
public class EventServiceTypedTopicTests
{
    private sealed record SamplePayload(int Value);

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
    public void Publish_DeliversToSubscribers()
    {
        var service = new EventService();
        var received = new List<int>();

        service.Subscribe<SamplePayload>(p => received.Add(p.Value));
        service.Subscribe<SamplePayload>(p => received.Add(p.Value * 10));

        service.Publish(new SamplePayload(7));

        Assert.Equal(new[] { 7, 70 }, received);
    }

    [Fact]
    public void Unsubscribe_RemovesHandler()
    {
        var service = new EventService();
        var received = new List<int>();

        Action<SamplePayload> handler = p => received.Add(p.Value);
        service.Subscribe(handler);
        service.Publish(new SamplePayload(1));

        service.Unsubscribe(handler);
        service.Publish(new SamplePayload(2));

        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public void Subscribe_SameHandlerTwice_InvokedOnce()
    {
        var service = new EventService();
        var count = 0;

        Action<SamplePayload> handler = _ => count++;
        service.Subscribe(handler);
        service.Subscribe(handler);

        service.Publish(new SamplePayload(0));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_OnDifferentContext_DispatchesViaCapturedContext()
    {
        var service = new EventService();
        var ui = new RecordingContext();
        var received = 0;

        using (SetContext(ui))
            service.Subscribe<SamplePayload>(_ => received++);

        using (SetContext(new SynchronizationContext()))
            service.Publish(new SamplePayload(0));

        Assert.Equal(1, received);
        Assert.Equal(1, ui.PostCount);
    }

    [Fact]
    public void Publish_OnSameContext_ExecutesSynchronously()
    {
        var service = new EventService();
        var ui = new RecordingContext();
        var received = 0;

        using (SetContext(ui))
        {
            service.Subscribe<SamplePayload>(_ => received++);
            service.Publish(new SamplePayload(0));
        }

        Assert.Equal(1, received);
        Assert.Equal(0, ui.PostCount);
    }

    [Fact]
    public void TypedAndStringTopics_AreIsolated()
    {
        var service = new EventService();
        var typedReceived = 0;
        var stringReceived = 0;

        // Same underlying type name used as both a string topic and a typed topic.
        service.Subscribe<SamplePayload>(_ => typedReceived++);
        service.Subscribe(typeof(SamplePayload).FullName!, (s, e) => stringReceived++);

        service.Publish(new SamplePayload(0));
        service.Publish(typeof(SamplePayload).FullName!, EventArgs.Empty);

        Assert.Equal(1, typedReceived);
        Assert.Equal(1, stringReceived);
    }
}
