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

    /// <summary>
    /// Receive exchange device key request event.
    /// Published when a key exchange request is received, requiring user confirmation.
    /// </summary>
    public const string OnReceiveExchangeDeviceKey = "OnReceiveExchangeDeviceKey";

    /// <summary>
    /// Plugin connected event
    /// </summary>
    public const string PluginConnected = "PluginConnected";

    /// <summary>
    /// Plugin disconnected event
    /// </summary>
    public const string PluginDisconnected = "PluginDisconnected";

    /// <summary>
    /// Plugin registered event
    /// </summary>
    public const string PluginRegistered = "PluginRegistered";

    /// <summary>
    /// Plugin unregistered event
    /// </summary>
    public const string PluginUnregistered = "PluginUnregistered";

    /// <summary>
    /// Plugin message received event
    /// </summary>
    public const string PluginMessageReceived = "PluginMessageReceived";

    /// <summary>
    /// Plugin response event (has RequestId)
    /// </summary>
    public const string PluginResponse = "PluginResponse";

    /// <summary>
    /// Workflow created event
    /// </summary>
    public const string WorkflowCreated = "WorkflowCreated";

    /// <summary>
    /// Workflow deleted event
    /// </summary>
    public const string WorkflowDeleted = "WorkflowDeleted";

    /// <summary>
    /// Workflow renamed event
    /// </summary>
    public const string WorkflowRenamed = "WorkflowRenamed";

    /// <summary>
    /// Workflow data saved event
    /// </summary>
    public const string WorkflowDataSaved = "WorkflowDataSaved";

    /// <summary>
    /// Trigger fired event
    /// </summary>
    public const string TriggerFired = "TriggerFired";

    /// <summary>
    /// Workflow triggered event (a workflow was started by a trigger)
    /// </summary>
    public const string WorkflowTriggered = "WorkflowTriggered";

    /// <summary>
    /// Workflow execution result event (success or failure)
    /// </summary>
    public const string WorkflowExecutionResult = "WorkflowExecutionResult";
}
