using KitX.Core.Contract.Event;
using Serilog;

namespace KitX.Core.Event;

/// <summary>
/// Raised by <see cref="EventService"/> when a typed handler receives a payload of the
/// wrong type and <see cref="EventService.ThrowOnTypeMismatch"/> is enabled. Deliberately
/// propagates out of the bus (unlike ordinary handler exceptions) so contract violations
/// surface during development.
/// </summary>
public sealed class EventTypeMismatchException : InvalidOperationException
{
    public EventTypeMismatchException(string message) : base(message) { }
}

/// <summary>
/// Event service for global event bus
/// </summary>
public class EventService : IEventService
{
    /// <summary>
    /// A single subscription: the handler plus the <see cref="SynchronizationContext"/>
    /// captured at subscribe time. When a publish happens on a different context, the
    /// handler is dispatched onto its captured context (automatic UI-thread marshalling).
    /// </summary>
    private sealed record Subscription(Delegate Handler, SynchronizationContext? Context);

    private readonly Dictionary<string, List<Subscription>> _eventHandlers = new();

    /// <summary>
    /// Strongly-typed topic handlers, keyed by <c>typeof(TEvent).FullName</c>. Kept separate
    /// from <see cref="_eventHandlers"/> so the string-keyed and type-keyed namespaces never
    /// collide. Handlers are <see cref="Action{T}"/> (no sender/args ceremony).
    /// </summary>
    private readonly Dictionary<string, List<Subscription>> _typedEventHandlers = new();

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
    /// When true, a typed handler receiving a payload of the wrong type throws instead of
    /// being silently dropped. Defaults to false (log-only) so a single mismatched publish
    /// cannot crash the bus; enable in debug to surface contract violations eagerly.
    /// </summary>
    public bool ThrowOnTypeMismatch { get; set; } = false;

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
            if (!_eventHandlers.TryGetValue(eventName, out var list))
                _eventHandlers[eventName] = list = new();

            list.Add(new Subscription(handler, SynchronizationContext.Current));
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
            if (_eventHandlers.TryGetValue(eventName, out var list))
                list.RemoveAll(s => ReferenceEquals(s.Handler, handler));
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
            List<Subscription> snapshot;
            lock (_lock)
            {
                if (!_eventHandlers.TryGetValue(eventName, out var handlers))
                    return;
                snapshot = new List<Subscription>(handlers);
            }

            var currentContext = SynchronizationContext.Current;

            foreach (var sub in snapshot)
            {
                var captured = sub.Context;

                // Dispatch onto the subscriber's captured context when the publish happens
                // on a different one (e.g. a background thread publishing a UI-bound event).
                // Same-context publishes stay synchronous to preserve existing semantics.
                if (captured is not null && !ReferenceEquals(captured, currentContext))
                {
                    captured.Post(_ => InvokeSafe(sub.Handler, eventName, args), null);
                }
                else
                {
                    InvokeSafe(sub.Handler, eventName, args);
                }
            }
        }
        finally
        {
            _publishDepth.Value = (_publishDepth.Value ?? 1) - 1;
        }
    }

    /// <summary>
    /// Invokes a single handler, isolating exceptions so a faulty handler cannot prevent
    /// subsequent handlers from receiving the event. A <see cref="EventTypeMismatchException"/>
    /// (raised only when <see cref="ThrowOnTypeMismatch"/> is enabled) is deliberately
    /// rethrown so a contract violation surfaces instead of being swallowed.
    /// </summary>
    private void InvokeSafe(Delegate handler, string eventName, EventArgs args)
    {
        try
        {
            if (handler is EventHandler<EventArgs> typed)
                typed(this, args);
            else
                handler.DynamicInvoke(this, args);
        }
        catch (EventTypeMismatchException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EventService] Handler threw while processing event {EventName}", eventName);
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
            if (!_eventHandlers.TryGetValue(eventName, out var list))
                _eventHandlers[eventName] = list = new();

            EventHandler<EventArgs> wrapper = (sender, args) =>
            {
                if (args is TEventArgs typedArgs)
                {
                    handler(sender, typedArgs);
                }
                else
                {
                    // A mismatched payload is a contract violation — log it loudly instead of
                    // silently dropping the handler (the old behavior hid real bugs, e.g. the
                    // OnAcceptingDeviceKey window never closing).
                    Log.Warning(
                        "[EventService] Type mismatch on {EventName}: expected {Expected} got {Actual}, handler dropped",
                        eventName, typeof(TEventArgs).Name, args?.GetType().Name ?? "null");

                    if (ThrowOnTypeMismatch)
                        throw new EventTypeMismatchException(
                            $"EventService type mismatch on '{eventName}': expected {typeof(TEventArgs).Name}, got {args?.GetType().Name ?? "null"}");
                }
            };

            // Re-subscribing with the same (eventName, handler) must replace the old
            // wrapper instead of stacking a second subscription — otherwise a single
            // Subscribe call would trigger the handler multiple times per publish.
            var key = (eventName, (Delegate)handler);
            if (_typedWrapperMap.TryGetValue(key, out var existingWrapper))
            {
                list.RemoveAll(s => ReferenceEquals(s.Handler, existingWrapper));
            }

            _typedWrapperMap[key] = wrapper;
            list.Add(new Subscription(wrapper, SynchronizationContext.Current));
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
            var key = (eventName, (Delegate)handler);
            if (_typedWrapperMap.TryGetValue(key, out var wrapper))
            {
                if (_eventHandlers.TryGetValue(eventName, out var list))
                {
                    list.RemoveAll(s => ReferenceEquals(s.Handler, wrapper));
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

    /// <inheritdoc/>
    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        var key = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        lock (_lock)
        {
            if (!_typedEventHandlers.TryGetValue(key, out var list))
                _typedEventHandlers[key] = list = new();

            // Re-subscribing the same handler must not stack a second subscription.
            if (list.Any(s => ReferenceEquals(s.Handler, handler)))
                return;

            list.Add(new Subscription(handler, SynchronizationContext.Current));
        }
    }

    /// <inheritdoc/>
    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        var key = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        lock (_lock)
        {
            if (_typedEventHandlers.TryGetValue(key, out var list))
                list.RemoveAll(s => ReferenceEquals(s.Handler, handler));
        }
    }

    /// <inheritdoc/>
    public void Publish<TEvent>(TEvent payload)
    {
        var key = typeof(TEvent).FullName ?? typeof(TEvent).Name;

        List<Subscription> snapshot;
        lock (_lock)
        {
            if (!_typedEventHandlers.TryGetValue(key, out var handlers))
                return;
            snapshot = new List<Subscription>(handlers);
        }

        var currentContext = SynchronizationContext.Current;

        foreach (var sub in snapshot)
        {
            var captured = sub.Context;

            if (captured is not null && !ReferenceEquals(captured, currentContext))
                captured.Post(_ => InvokeTypedSafe(sub.Handler, payload), null);
            else
                InvokeTypedSafe(sub.Handler, payload);
        }
    }

    /// <summary>
    /// Invokes a strongly-typed handler, isolating exceptions so a faulty handler cannot
    /// prevent subsequent handlers from receiving the event.
    /// </summary>
    private void InvokeTypedSafe<TEvent>(Delegate handler, TEvent payload)
    {
        try
        {
            if (handler is Action<TEvent> action)
                action(payload);
            else
                handler.DynamicInvoke(payload);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EventService] Typed handler threw while processing {EventType}", typeof(TEvent).Name);
        }
    }

}
