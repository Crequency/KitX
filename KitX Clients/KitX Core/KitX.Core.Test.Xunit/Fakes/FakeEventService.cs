using System.Collections.Concurrent;
using KitX.Core.Contract.Event;

namespace KitX.Core.Test.Xunit.Fakes;

/// <summary>
/// 最小 IEventService 实现：订阅/发布均为空操作，并记录所有已发布的事件名。
/// </summary>
public class FakeEventService : IEventService
{
    public ConcurrentBag<string> PublishedEvents { get; } = new();

    public void Subscribe(string eventName, EventHandler<EventArgs> handler) { }

    public void Unsubscribe(string eventName, EventHandler<EventArgs> handler) { }

    public void Publish(string eventName, EventArgs args) => PublishedEvents.Add(eventName);

    public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs { }

    public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs { }

    public void Publish<TEventArgs>(string eventName, TEventArgs args)
        where TEventArgs : EventArgs => PublishedEvents.Add(eventName);

    public void Subscribe<TEvent>(Action<TEvent> handler) { }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) { }

    public void Publish<TEvent>(TEvent payload) { }

    public bool WasPublished(string eventName) => PublishedEvents.Contains(eventName);
}
