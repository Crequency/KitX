namespace KitX.Core.Event;

/// <summary>
/// Event names for the event bus
/// </summary>
public static class EventNames
{
    /// <summary>
    /// Language changed event
    /// </summary>
    public const string LanguageChanged = "LanguageChanged";

    /// <summary>
    /// Greeting text interval updated event
    /// </summary>
    public const string GreetingTextIntervalUpdated = "GreetingTextIntervalUpdated";

    /// <summary>
    /// App config changed event
    /// </summary>
    public const string AppConfigChanged = "AppConfigChanged";

    /// <summary>
    /// Plugins config changed event
    /// </summary>
    public const string PluginsConfigChanged = "PluginsConfigChanged";

    /// <summary>
    /// Mica opacity changed event
    /// </summary>
    public const string MicaOpacityChanged = "MicaOpacityChanged";

    /// <summary>
    /// Develop settings changed event
    /// </summary>
    public const string DevelopSettingsChanged = "DevelopSettingsChanged";

    /// <summary>
    /// Log config updated event
    /// </summary>
    public const string LogConfigUpdated = "LogConfigUpdated";

    /// <summary>
    /// Theme config changed event
    /// </summary>
    public const string ThemeConfigChanged = "ThemeConfigChanged";

    /// <summary>
    /// Use statistics changed event
    /// </summary>
    public const string UseStatisticsChanged = "UseStatisticsChanged";

    /// <summary>
    /// Devices server port changed event
    /// </summary>
    public const string DevicesServerPortChanged = "DevicesServerPortChanged";

    /// <summary>
    /// Plugins server port changed event
    /// </summary>
    public const string PluginsServerPortChanged = "PluginsServerPortChanged";

    /// <summary>
    /// Activities updated event
    /// </summary>
    public const string OnActivitiesUpdated = "OnActivitiesUpdated";

    /// <summary>
    /// Receive cancel exchanging device key event
    /// </summary>
    public const string OnReceiveCancelExchangingDeviceKey = "OnReceiveCancelExchangingDeviceKey";

    /// <summary>
    /// Exiting event
    /// </summary>
    public const string OnExiting = "OnExiting";

    /// <summary>
    /// Receiving device info event
    /// </summary>
    public const string OnReceivingDeviceInfo = "OnReceivingDeviceInfo";

    /// <summary>
    /// Config hot reloaded event
    /// </summary>
    public const string OnConfigHotReloaded = "OnConfigHotReloaded";

    /// <summary>
    /// Accepting device key event
    /// </summary>
    public const string OnAcceptingDeviceKey = "OnAcceptingDeviceKey";
}
