using System;
using System.Collections.Generic;
using System.Threading;
using KitX.Core.Contract.Event;
using Serilog;
using KitX.Core.DI;

namespace KitX.Core.Event;

/// <summary>
/// Event service for global event bus
/// </summary>
public class EventService : IEventService
{
    private readonly Dictionary<string, List<EventHandler<EventArgs>>> _eventHandlers = new();

    /// <summary>
    /// Lock object for thread-safe access to handlers
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Maps (eventName, originalHandler) to wrapper lambda for typed subscribe/unsubscribe
    /// </summary>
    private readonly Dictionary<(string eventName, Delegate handler), EventHandler<EventArgs>> _typedWrapperMap = new();

    /// <summary>
    /// Thread-local counter to track publish depth for recursion detection
    /// </summary>
    private readonly ThreadLocal<int?> _publishDepth = new();

    /// <summary>
    /// Maximum publish depth before considering it recursive
    /// </summary>
    private const int MaxPublishDepth = 10;

    /// <summary>
    /// Creates a new event service
    /// </summary>
    public EventService() { }

    /// <summary>
    /// Subscribes to an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public void Subscribe(string eventName, EventHandler<EventArgs> handler)
    {
        lock (_lock)
        {
            if (!_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName] = new List<EventHandler<EventArgs>>();
            }

            _eventHandlers[eventName].Add(handler);
        }
    }

    /// <summary>
    /// Unsubscribes from an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public void Unsubscribe(string eventName, EventHandler<EventArgs> handler)
    {
        lock (_lock)
        {
            if (_eventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Remove(handler);
            }
        }
    }

    /// <summary>
    /// Publishes an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="args">The event arguments</param>
    public void Publish(string eventName, EventArgs args)
    {
        // Prevent excessive recursion
        _publishDepth.Value = (_publishDepth.Value ?? 0) + 1;
        if (_publishDepth.Value > MaxPublishDepth)
        {
            Log.Error("[EventService] Possible infinite recursion detected! Event: {EventName}, Depth: {Depth}",
                eventName, _publishDepth.Value);
            _publishDepth.Value = (_publishDepth.Value ?? 1) - 1;
            return;
        }

        try
        {
            // Take a snapshot of handlers under lock to avoid concurrent modification
            List<EventHandler<EventArgs>> snapshot;
            lock (_lock)
            {
                if (!_eventHandlers.TryGetValue(eventName, out var handlers))
                    return;
                snapshot = new List<EventHandler<EventArgs>>(handlers);
            }

            foreach (var handler in snapshot)
            {
                handler.Invoke(this, args);
            }
        }
        finally
        {
            _publishDepth.Value = (_publishDepth.Value ?? 1) - 1;
        }
    }

    /// <summary>
    /// Subscribes to a typed event
    /// </summary>
    /// <typeparam name="TEventArgs">The event args type</typeparam>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
    {
        lock (_lock)
        {
            if (!_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName] = new List<EventHandler<EventArgs>>();
            }

            EventHandler<EventArgs> wrapper = (sender, args) =>
            {
                if (args is TEventArgs typedArgs)
                {
                    handler(sender, typedArgs);
                }
            };

            _typedWrapperMap[(eventName, handler)] = wrapper;
            _eventHandlers[eventName].Add(wrapper);
        }
    }

    /// <summary>
    /// Unsubscribes from a typed event
    /// </summary>
    /// <typeparam name="TEventArgs">The event args type</typeparam>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
    {
        lock (_lock)
        {
            var key = (eventName, handler);
            if (_typedWrapperMap.TryGetValue(key, out var wrapper))
            {
                if (_eventHandlers.TryGetValue(eventName, out var handlers))
                {
                    handlers.Remove(wrapper);
                }
                _typedWrapperMap.Remove(key);
            }
        }
    }

    /// <summary>
    /// Publishes a typed event
    /// </summary>
    /// <typeparam name="TEventArgs">The event args type</typeparam>
    /// <param name="eventName">The event name</param>
    /// <param name="args">The event arguments</param>
    public void Publish<TEventArgs>(string eventName, TEventArgs args)
        where TEventArgs : EventArgs
    {
        // Must cast to EventArgs to call the non-generic overload, avoiding infinite recursion
        Publish(eventName, (EventArgs)args);
    }

}
