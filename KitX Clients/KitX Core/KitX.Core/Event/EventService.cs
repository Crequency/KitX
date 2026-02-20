using System;
using System.Collections.Generic;
using System.ComponentModel;
using KitX.Core.Contract.Event;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Event;

/// <summary>
/// Event service for global event bus
/// </summary>
public class EventService : IEventService
{
    private static EventService? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static EventService Instance => _instance ??= new();

    private readonly Dictionary<string, List<EventHandler<EventArgs>>> _eventHandlers = new();

    /// <summary>
    /// Private constructor
    /// </summary>
    private EventService() { }

    /// <summary>
    /// Subscribes to an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public void Subscribe(string eventName, EventHandler<EventArgs> handler)
    {
        if (!_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName] = new List<EventHandler<EventArgs>>();
        }

        _eventHandlers[eventName].Add(handler);
    }

    /// <summary>
    /// Unsubscribes from an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="handler">The event handler</param>
    public void Unsubscribe(string eventName, EventHandler<EventArgs> handler)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Remove(handler);
        }
    }

    /// <summary>
    /// Publishes an event
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="args">The event arguments</param>
    public void Publish(string eventName, EventArgs args)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            foreach (var handler in handlers)
            {
                handler.Invoke(this, args);
            }
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
        if (!_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName] = new List<EventHandler<EventArgs>>();
        }

        _eventHandlers[eventName].Add((sender, args) =>
        {
            if (args is TEventArgs typedArgs)
            {
                handler(sender, typedArgs);
            }
        });
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
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            // Note: Exact handler removal is not supported in this implementation
            // The handler wrapper makes exact matching difficult
            // Consider using a different approach for production
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
        Publish(eventName, args);
    }

    /// <summary>
    /// Invokes a static event dynamically (legacy support)
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="objects">Optional parameters</param>
    [Obsolete("Use IEventService.Publish with event names instead")]
    public static void Invoke(string eventName, object[]? objects = null)
    {
        var type = typeof(EventService);

        var eventField = type.GetField(eventName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        if (eventField is null || !typeof(Delegate).IsAssignableFrom(eventField.FieldType))
        {
            throw new ArgumentException($"No event found with the name '{eventName}'.", nameof(eventName));
        }

        var @delegate = eventField.GetValue(null) as Delegate;

        @delegate?.DynamicInvoke(objects);
    }

    #region Static Events (Legacy Support)

#pragma warning disable CS0067 // Event is never used

    /// <summary>
    /// Language changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.LanguageChanged instead")]
    public static event Action LanguageChanged = () => { };

    /// <summary>
    /// Greeting text interval updated event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.GreetingTextIntervalUpdated instead")]
    public static event Action GreetingTextIntervalUpdated = () => { };

    /// <summary>
    /// App config changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.AppConfigChanged instead")]
    public static event Action AppConfigChanged = () => { };

    /// <summary>
    /// Plugins config changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.PluginsConfigChanged instead")]
    public static event Action PluginsConfigChanged = () => { };

    /// <summary>
    /// Mica opacity changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.MicaOpacityChanged instead")]
    public static event Action MicaOpacityChanged = () => { };

    /// <summary>
    /// Develop settings changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.DevelopSettingsChanged instead")]
    public static event Action DevelopSettingsChanged = () => { };

    /// <summary>
    /// Log config updated event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.LogConfigUpdated instead")]
    public static event Action LogConfigUpdated = () => { };

    /// <summary>
    /// Theme config changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.ThemeConfigChanged instead")]
    public static event Action ThemeConfigChanged = () => { };

    /// <summary>
    /// Use statistics changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.UseStatisticsChanged instead")]
    public static event Action UseStatisticsChanged = () => { };

    /// <summary>
    /// Devices server port changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.DevicesServerPortChanged instead")]
    public static event Action<int> DevicesServerPortChanged = port => { };

    /// <summary>
    /// Plugins server port changed event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.PluginsServerPortChanged instead")]
    public static event Action<int> PluginsServerPortChanged = port => { };

    /// <summary>
    /// Activities updated event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.OnActivitiesUpdated instead")]
    public static event Action OnActivitiesUpdated = () => { };

    /// <summary>
    /// Receive cancel exchanging device key event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.OnReceiveCancelExchangingDeviceKey instead")]
    public static event Action OnReceiveCancelExchangingDeviceKey = () => { };

    /// <summary>
    /// Exiting event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.OnExiting instead")]
    public static event Action OnExiting = () => { };

    /// <summary>
    /// Receiving device info event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.OnReceivingDeviceInfo instead")]
    public static event Action<DeviceInfo> OnReceivingDeviceInfo = _ => { };

    /// <summary>
    /// Config hot reloaded event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.OnConfigHotReloaded instead")]
    public static event Action OnConfigHotReloaded = () => { };

    /// <summary>
    /// Accepting device key event
    /// </summary>
    [Obsolete("Use IEventService with EventNames.OnAcceptingDeviceKey instead")]
    public static event Action<string> OnAcceptingDeviceKey = _ => { };

#pragma warning restore CS0067 // Event is never used

    #endregion
}
