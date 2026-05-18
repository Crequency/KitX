using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;
using KitX.Core.DI;

using PluginMessageReceivedEventArgs = KitX.Core.Contract.Plugin.Events.PluginMessageReceivedEventArgs;

namespace KitX.Core.Workflow;

/// <summary>
/// 管理触发器路由：从插件接收触发信号，路由到匹配的工作流并执行。
/// 触发器是纯信号（等同于"运行"按钮），不携带业务数据。
/// </summary>
public class TriggerManager
{
    private readonly IPluginServer _pluginServer;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 订阅索引：Key = "PluginName.TriggerName" 或 "PluginName.*"，Value = workflow ID 列表
    /// </summary>
    private Dictionary<string, List<string>> _triggerSubscriptions = new();

    /// <summary>
    /// 工作流触发器配置：Key = workflowId, Value = TriggerConfig
    /// </summary>
    private readonly Dictionary<string, TriggerConfig> _workflowTriggers = new();

    /// <summary>
    /// Creates a new trigger manager
    /// </summary>
    public TriggerManager()
    {
        _pluginServer = ServiceHost.IsInitialized
            ? ServiceHost.GetRequiredService<IPluginServer>()
            : new KitX.Core.Device.PluginsServer(new KitX.Core.Event.EventService());
        _pluginServer.PluginMessageReceived += OnPluginMessageReceived;
        Log.Information("[TriggerManager] Initialized and subscribed to PluginMessageReceived");
    }

    /// <summary>
    /// 注册工作流的触发器配置
    /// </summary>
    /// <param name="workflowId">工作流 ID</param>
    /// <param name="config">触发器配置</param>
    public void RegisterWorkflowTrigger(string workflowId, TriggerConfig config)
    {
        if (config.TriggerType != "PluginEvent" || string.IsNullOrEmpty(config.PluginName))
            return;

        _workflowTriggers[workflowId] = config;
        RebuildSubscriptionIndex();

        Log.Information("[TriggerManager] Registered trigger for workflow {WorkflowId}: " +
            "PluginName={PluginName}, TriggerName={TriggerName}",
            workflowId, config.PluginName, config.TriggerName);
    }

    /// <summary>
    /// 注销工作流的触发器
    /// </summary>
    /// <param name="workflowId">工作流 ID</param>
    public void UnregisterWorkflowTrigger(string workflowId)
    {
        if (_workflowTriggers.Remove(workflowId))
        {
            RebuildSubscriptionIndex();
            Log.Information("[TriggerManager] Unregistered trigger for workflow {WorkflowId}", workflowId);
        }
    }

    /// <summary>
    /// Initialize trigger subscriptions from persisted workflow configurations.
    /// Should be called once after DI initialization is complete, before plugins connect.
    /// This ensures that TriggerFired events from plugins can be routed to the correct workflows
    /// even if they arrive before the user opens the workflow page.
    /// </summary>
    public async void InitializeFromPersistedWorkflows()
    {
        try
        {
            if (!ServiceHost.IsInitialized)
            {
                Log.Error("[TriggerManager] Cannot initialize from persisted workflows: ServiceHost not initialized");
                return;
            }

            var storageService = ServiceHost.GetRequiredService<IWorkflowStorageService>();
            var workflows = await storageService.DiscoverWorkflowsAsync();

            foreach (var workflow in workflows)
            {
                if (workflow.TriggerConfig != null
                    && !string.IsNullOrEmpty(workflow.TriggerConfig.PluginName))
                {
                    RegisterWorkflowTrigger(workflow.Id, workflow.TriggerConfig);
                }
            }

            Log.Information("[TriggerManager] Initialized {Count} trigger subscriptions from persisted workflows",
                _triggerSubscriptions.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TriggerManager] Failed to initialize from persisted workflows");
        }
    }

    /// <summary>
    /// 重建订阅索引（在配置变更时调用）
    /// </summary>
    private void RebuildSubscriptionIndex()
    {
        var newIndex = new Dictionary<string, List<string>>();

        foreach (var (workflowId, config) in _workflowTriggers)
        {
            if (config.TriggerType != "PluginEvent" || string.IsNullOrEmpty(config.PluginName))
                continue;

            var key = string.IsNullOrEmpty(config.TriggerName)
                ? $"{config.PluginName}.*"
                : $"{config.PluginName}.{config.TriggerName}";

            if (!newIndex.ContainsKey(key))
                newIndex[key] = [];
            newIndex[key].Add(workflowId);
        }

        _triggerSubscriptions = newIndex;
    }

    /// <summary>
    /// 处理插件消息，识别 TriggerFired 并路由到匹配的工作流
    /// </summary>
    private void OnPluginMessageReceived(object? sender, PluginMessageReceivedEventArgs e)
    {
        try
        {
            if (e.Message is null) return;

            var kwc = JsonSerializer.Deserialize<Request>(e.Message, _serializerOptions);
            if (kwc?.Content is null) return;

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, _serializerOptions);
            if (command.Request != CommandRequestInfo.TriggerFired) return;

            // 查找发送此消息的插件名称
            var connectionId = e.ConnectionId;
            var connection = _pluginServer.Connections
                .FirstOrDefault(c => c.ConnectionId == connectionId);
            var pluginName = connection?.PluginInfo?.Name ?? "Unknown";

            var triggerName = command.Tags?.TryGetValue("TriggerName", out var name) == true
                ? name : "Unknown";

            Log.Information("[TriggerManager] Trigger '{TriggerName}' fired by plugin '{PluginName}'",
                triggerName, pluginName);

            // 查找匹配的工作流
            var specificKey = $"{pluginName}.{triggerName}";
            var wildcardKey = $"{pluginName}.*";

            var matchingWorkflowIds = new HashSet<string>();
            if (_triggerSubscriptions.TryGetValue(specificKey, out var specific))
                foreach (var id in specific) matchingWorkflowIds.Add(id);
            if (_triggerSubscriptions.TryGetValue(wildcardKey, out var wildcard))
                foreach (var id in wildcard) matchingWorkflowIds.Add(id);

            foreach (var workflowId in matchingWorkflowIds)
            {
                Log.Information("[TriggerManager] Triggering workflow: {WorkflowId}", workflowId);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    bool success = await ServiceHost.GetRequiredService<IWorkflowManagementService>().RunWorkflowAsync(workflowId);
                    ServiceHost.GetRequiredService<IEventService>().Publish(
                        KitX.Core.Event.EventNames.WorkflowExecutionResult,
                        new KitX.Core.Contract.Event.WorkflowExecutionResultEventArgs(
                            workflowId, success,
                            success ? null : "Workflow execution failed"));
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TriggerManager] Error processing trigger message");
        }
    }
}
