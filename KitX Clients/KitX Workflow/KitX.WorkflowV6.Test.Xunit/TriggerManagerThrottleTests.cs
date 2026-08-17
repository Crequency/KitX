// ─────────────────────────────────────────────────────────────────────────────
// W-10 tests: TriggerManager throttles trigger storms — while a trigger-fired run
// of a workflow is in flight, further firings of the SAME workflow are skipped
// instead of spawning a new Task.Run per firing.
// ─────────────────────────────────────────────────────────────────────────────

#pragma warning disable CS0067  // interface-required events on the fakes are unused

using System.Text.Json;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Contract.Workflow;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using KitX.WorkflowV6.Services;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class TriggerManagerThrottleTests
{
    private sealed class FakePluginServer : IPluginServer
    {
        public List<IPluginConnection> ConnectionsList { get; } = [];
        public int? Port { get; set; }
        public IReadOnlyList<IPluginConnection> Connections => ConnectionsList;
        public IPluginServer Run() => this;
        public void Stop() { }
        public IPluginConnector? FindConnector(PluginInfo pluginInfo) => null;
        public IPluginConnection? FindConnection(string connectionId) => null;
        public event EventHandler<int>? PortChanged;

        public event EventHandler<PluginDisconnectedEventArgs>? PluginDisconnected;
        public event EventHandler<PluginMessageReceivedEventArgs>? PluginMessageReceived;
        public event EventHandler<PluginRegisteredEventArgs>? PluginRegistered;
        public event EventHandler<PluginUnregisteredEventArgs>? PluginUnregistered;
        public event EventHandler<PluginResponseEventArgs>? PluginResponse;

        public void Fire(string message)
            => PluginMessageReceived?.Invoke(this, new PluginMessageReceivedEventArgs { ConnectionId = "c1", Message = message });
    }

    private sealed class FakeConnection : IPluginConnection
    {
        public PluginInfo? PluginInfo { get; set; }
        public ServerStatus Status { get; set; }
        public string? ConnectionId { get; set; }
        public void Request(object request) { }
        public void Initialize() { }
        public void Send(string message) { }
        public Task CloseAsync() => Task.CompletedTask;
        public event EventHandler<string>? MessageReceived;
        public event EventHandler? Closed;
        public event EventHandler<PluginResponseEventArgs>? PluginResponse;
        public event EventHandler<PluginStatusReportEventArgs>? StatusReport;
        public void Dispose() { }
    }

    /// <summary>Management fake: the first run blocks until released (simulates a long-running workflow).</summary>
    private sealed class BlockingManagement : IWorkflowManagementService
    {
        public readonly List<string> RunCalls = new();
        private readonly TaskCompletionSource _release = new();
        private int _calls;

        public Task<bool> RunWorkflowAsync(string workflowId) => Task.FromResult(true);

        public async Task<WorkflowRunResult> RunWorkflowWithDetailsAsync(string workflowId)
        {
            lock (RunCalls) RunCalls.Add(workflowId);
            Interlocked.Increment(ref _calls);
            if (_calls == 1)
                await _release.Task;   // first run stays in flight until released
            return new WorkflowRunResult(true, null, null);
        }

        public Task<bool> StopWorkflowAsync(string workflowId) => Task.FromResult(false);

        public Task<bool> CompileAndPersistWorkflowAsync(string workflowId) => Task.FromResult(true);

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingEventService : IEventService
    {
        public readonly List<(string Name, EventArgs Args)> Published = new();
        public void Subscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Unsubscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
        public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler) where TEventArgs : EventArgs { }
        public void Publish(string eventName, EventArgs args) => Published.Add((eventName, args));
        public void Publish<TEventArgs>(string eventName, TEventArgs args) where TEventArgs : EventArgs
            => Published.Add((eventName, args));
        public void Subscribe<TEvent>(Action<TEvent> handler) { }
        public void Unsubscribe<TEvent>(Action<TEvent> handler) { }
        public void Publish<TEvent>(TEvent payload) { }
    }

    private static string BuildTriggerMessage(string triggerName = "TestTrigger")
    {
        var inner = JsonSerializer.Serialize(new Command
        {
            Request = CommandRequestInfo.TriggerFired,
            Tags = new Dictionary<string, string> { ["TriggerName"] = triggerName },
        });
        return JsonSerializer.Serialize(new Request { Content = inner });
    }

    [Fact]
    public async Task Trigger_Burst_While_Running_Is_Throttled_To_One_Run_Per_Workflow()
    {
        var server = new FakePluginServer();
        server.ConnectionsList.Add(new FakeConnection
        {
            ConnectionId = "c1",
            PluginInfo = new PluginInfo { Name = "TestPlugin" },
        });
        var management = new BlockingManagement();
        var events = new RecordingEventService();

        var manager = new TriggerManager(server, management, events);
        manager.RegisterWorkflowTrigger("wf-1", new TriggerConfig
        {
            TriggerType = "PluginEvent",
            PluginName = "TestPlugin",
            TriggerName = "TestTrigger",
        });

        // Fire a burst: the first run goes in flight (blocked), the rest must be
        // throttled away while it is still running.
        var msg = BuildTriggerMessage();
        for (int i = 0; i < 5; i++)
            server.Fire(msg);

        // Give the async dispatch time to run.
        await Task.Delay(300);
        Assert.Single(management.RunCalls);

        // Release the in-flight run; the next fire is allowed again.
        management.Release();
        await Task.Delay(300);
        server.Fire(msg);
        await Task.Delay(300);
        Assert.Equal(2, management.RunCalls.Count);

        manager.UnregisterWorkflowTrigger("wf-1");
    }

    [Fact]
    public async Task Different_Workflows_Are_Not_Throttled_Against_Each_Other()
    {
        var server = new FakePluginServer();
        server.ConnectionsList.Add(new FakeConnection
        {
            ConnectionId = "c1",
            PluginInfo = new PluginInfo { Name = "TestPlugin" },
        });
        var management = new BlockingManagement();
        var events = new RecordingEventService();

        var manager = new TriggerManager(server, management, events);
        manager.RegisterWorkflowTrigger("wf-1", new TriggerConfig
        {
            TriggerType = "PluginEvent",
            PluginName = "TestPlugin",
            TriggerName = "TestTrigger",
        });
        manager.RegisterWorkflowTrigger("wf-2", new TriggerConfig
        {
            TriggerType = "PluginEvent",
            PluginName = "TestPlugin",
            TriggerName = "TestTrigger",
        });

        // Both workflows match the same trigger; each must be started once.
        var msg = BuildTriggerMessage();
        server.Fire(msg);
        await Task.Delay(300);
        Assert.Equal(2, management.RunCalls.Count);
        Assert.Contains("wf-1", management.RunCalls);
        Assert.Contains("wf-2", management.RunCalls);

        manager.UnregisterWorkflowTrigger("wf-1");
        manager.UnregisterWorkflowTrigger("wf-2");
    }
}
