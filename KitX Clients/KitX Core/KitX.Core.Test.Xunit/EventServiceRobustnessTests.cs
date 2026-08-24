using KitX.Core.Contract.Event;
using KitX.Core.Event;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// EventService 发布健壮性测试（C-14 回归）。
/// 覆盖：单个处理器抛异常不中断后续处理器、重复订阅同一 (eventName, handler)
/// 不会导致处理器被重复调用。
/// </summary>
public class EventServiceRobustnessTests
{
    [Fact]
    public void Publish_HandlerThrows_DoesNotBreakSubsequentHandlers()
    {
        var service = new EventService();
        var delivered = new List<string>();

        service.Subscribe("test.event", (s, e) => throw new InvalidOperationException("boom"));
        service.Subscribe("test.event", (s, e) => delivered.Add("second"));
        service.Subscribe("test.event", (s, e) => delivered.Add("third"));

        // 不应抛出异常
        service.Publish("test.event", EventArgs.Empty);

        Assert.Equal(new[] { "second", "third" }, delivered);
    }

    [Fact]
    public void Subscribe_Twice_SameHandler_IsInvokedOnce()
    {
        var service = new EventService();
        var callCount = 0;

        EventHandler<PortChangedEventArgs> handler = (s, e) => callCount++;
        service.Subscribe("test.typed", handler);
        service.Subscribe("test.typed", handler);

        service.Publish<PortChangedEventArgs>("test.typed", new PortChangedEventArgs { Port = 1 });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Unsubscribe_Typed_RemovesHandler()
    {
        var service = new EventService();
        var callCount = 0;

        EventHandler<PortChangedEventArgs> handler = (s, e) => callCount++;
        service.Subscribe("test.typed", handler);
        service.Publish<PortChangedEventArgs>("test.typed", new PortChangedEventArgs { Port = 1 });

        service.Unsubscribe("test.typed", handler);
        service.Publish<PortChangedEventArgs>("test.typed", new PortChangedEventArgs { Port = 2 });

        Assert.Equal(1, callCount);
    }
}
