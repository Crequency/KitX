using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Panels;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class PanelRuntimeTests
{
    private static Toolkit PanelToolkit()
        => new()
        {
            Id = "tk-panel",
            Meta = new ToolkitMeta { Name = "panel" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            UiPanel = new UiPanel
            {
                Controls =
                [
                    new UiControl { Type = "Input", Id = "input" },
                    new UiControl { Type = "Button", Id = "btn" },
                    new UiControl { Type = "Log", Id = "log" },
                ],
            },
            Triggers =
            [
                new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
                new Trigger { Id = "trg-btn", Type = TriggerType.UIEvent,
                    Config = new TriggerConfig { Control = "btn", Event = "Click" },
                    Bindings = [new() { Workflow = "wf" }] },
            ],
        };

    private static (ToolkitInstanceManager Manager, RecordingExecutor Executor, DataStore Store, PanelRuntime Runtime) Build()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));
        var runtime = new PanelRuntime(store, manager);
        return (manager, executor, store, runtime);
    }

    [Fact]
    public void SetControlValue_Writes_Main_Property_Key()
    {
        var (manager, _, store, runtime) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            runtime.SetControlValue(id, "input", "hello");

            var key = PanelScope.Key("tk-panel", id, "input", "value");
            Assert.Equal("hello", store.Get(key)?.GetString());
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }

    [Fact]
    public void SetControlValue_Rejects_Unknown_Control()
    {
        var (manager, _, store, runtime) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            runtime.SetControlValue(id, "nope", "x");

            Assert.DoesNotContain(store.Keys(), k => k.Contains("/panel/", StringComparison.Ordinal));
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }

    [Fact]
    public void GetControlValue_Reads_Main_Property()
    {
        var (manager, _, store, runtime) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            store.Set(PanelScope.Key("tk-panel", id, "input", "value"), "world");

            Assert.Equal("world", runtime.GetControlValue(id, "input")?.GetString());
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }

    [Fact]
    public void BuiltinUiPlugin_Set_Dispatches_To_Panel()
    {
        var (manager, _, store, _) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            var plugin = new BuiltinUiPlugin(
                new PanelRuntime(store, manager), store, manager);

            Assert.True(plugin.HasMethod("set"));
            plugin.Invoke("set", [id, "input", "hello"]);

            Assert.Equal("hello", store.Get(PanelScope.Key("tk-panel", id, "input", "value"))?.GetString());
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }

    [Fact]
    public void BuiltinUiPlugin_Log_Appends_To_Log_Key()
    {
        var (manager, _, store, _) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            var plugin = new BuiltinUiPlugin(new PanelRuntime(store, manager), store, manager);

            plugin.Invoke("log", [id, "log", "entry-1"]);
            plugin.Invoke("log", [id, "log", "entry-2"]);

            var arr = store.Get(PanelScope.Key("tk-panel", id, "log", "log"));
            Assert.NotNull(arr);
            Assert.Equal(2, arr.Value.GetArrayLength());
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }

    [Fact]
    public async Task RaiseControlEvent_Starts_UIEvent_Workflow()
    {
        var (manager, executor, _, runtime) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            runtime.RaiseControlEvent(id, "btn", "Click", null);

            // The UIEvent-triggered run starts asynchronously; give it a moment.
            await Task.Delay(200);
            Assert.Contains(executor.Calls, c => c.WorkflowId == "wf");
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }

    [Fact]
    public void BuiltinUiPlugin_OpenPanel_Raises_Request()
    {
        var (manager, _, store, _) = Build();
        manager.Mount(PanelToolkit());
        try
        {
            var id = manager.Spawn("tk-panel", "manual")!;
            var requested = new TaskCompletionSource();
            manager.BenchEvent += (_, e) =>
            {
                if (e is PanelOpenRequestedEvent)
                    requested.TrySetResult();
            };

            var plugin = new BuiltinUiPlugin(new PanelRuntime(store, manager), store, manager);
            plugin.Invoke("openpanel", [id]);

            Assert.True(requested.Task.IsCompleted);
        }
        finally
        {
            manager.Unmount("tk-panel");
        }
    }
}
