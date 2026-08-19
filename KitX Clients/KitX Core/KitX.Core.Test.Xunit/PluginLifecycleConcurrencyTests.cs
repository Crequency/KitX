using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Device;
using KitX.Core.Plugin;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Plugin;
using ServerStatus = KitX.Core.Contract.Device.ServerStatus;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// C-7/C-8: PluginsManager 并发安全（ConcurrentDictionary 快照）与 loader 进程
/// Exited 清理。C-9: PluginsServer 按 Name 查找连接。C-11: PluginHostAdapter
/// 9 个函数的真实桥接（fake 服务注入）。
/// </summary>
public class PluginLifecycleConcurrencyTests
{
    // ── C-7: 并发访问不抛 InvalidOperationException ──

    [Fact]
    public async Task ConcurrentAccess_NoInvalidOperationException()
    {
        var manager = new PluginsManager();
        var pluginsField = GetField(manager, "_plugins");
        var byNameField = GetField(manager, "_pluginsByName");
        var plugins = (ConcurrentDictionary<Guid, PluginInstallation>)pluginsField.GetValue(manager)!;
        var byName = (ConcurrentDictionary<string, PluginInstallation>)byNameField.GetValue(manager)!;

        var seeded = new List<PluginInstallation>();
        for (var i = 0; i < 4; i++)
        {
            var info = new PluginInfo
            {
                Name = $"demo{i}",
                Version = "1.0.0",
                PublisherName = "pub",
                AuthorName = "author"
            };
            var installation = new PluginInstallation
            {
                Id = PluginsManager.GeneratePluginId(info),
                InstallPath = null,
                PluginInfo = info,
                LoaderInfo = new KitX.Shared.CSharp.Loader.LoaderInfo()
            };
            plugins[installation.Id] = installation;
            byName[info.Name] = installation;
            seeded.Add(installation);
        }

        var errors = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 8).Select(worker =>
            Task.Run(() =>
            {
                var random = new Random(worker);
                for (var i = 0; i < 300; i++)
                {
                    try
                    {
                        switch (i % 5)
                        {
                            case 0:
                                manager.OnPluginStatusChanged($"demo{random.Next(seeded.Count)}",
                                    PluginStatus.Running);
                                break;
                            case 1:
                                _ = manager.GetInstalledPlugins();
                                break;
                            case 2:
                                _ = manager.Plugins;
                                break;
                            case 3:
                                _ = manager.GetPlugin(seeded[random.Next(seeded.Count)].Id);
                                break;
                            case 4:
                                manager.OnPluginStatusChanged($"demo{random.Next(seeded.Count)}",
                                    PluginStatus.Stopped);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(errors);

        // Status changes must be visible through the snapshot API afterwards.
        manager.OnPluginStatusChanged("demo0", PluginStatus.Running);
        var running = manager.GetInstalledPlugins().FirstOrDefault(p => p.PluginInfo?.Name == "demo0");
        Assert.NotNull(running);
        Assert.True(((PluginInstallation)running!).IsRunning);
    }

    // ── C-8: 进程退出后条目清理 + 状态复位 ──

    [Fact]
    public void LoaderProcessExit_CleansUpAndResetsState()
    {
        var manager = new PluginsManager();
        var pluginsField = GetField(manager, "_plugins");
        var processesField = GetField(manager, "_pluginProcesses");
        var handlerMethod = typeof(PluginsManager).GetMethod("HandleLoaderProcessExit",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var plugins = (ConcurrentDictionary<Guid, PluginInstallation>)pluginsField.GetValue(manager)!;
        var processes = (ConcurrentDictionary<Guid, Process>)processesField.GetValue(manager)!;

        var info = new PluginInfo { Name = "crashy", Version = "1.0.0" };
        var installation = new PluginInstallation
        {
            Id = PluginsManager.GeneratePluginId(info),
            PluginInfo = info,
            InstallPath = null,
            LoaderInfo = new KitX.Shared.CSharp.Loader.LoaderInfo()
        };
        plugins[installation.Id] = installation;
        installation.IsRunning = true;

        var statusEvents = new List<PluginStatusChangedEventArgs>();
        manager.PluginStatusChanged += (_, e) => statusEvents.Add(e);

        // A real process that exits quickly — simulates a crashed loader.
        using var exitedProcess = new Process
        {
            StartInfo = new ProcessStartInfo("cmd.exe", "/c exit 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        exitedProcess.EnableRaisingEvents = true;
        Assert.True(exitedProcess.Start());
        exitedProcess.WaitForExit();

        processes[installation.Id] = exitedProcess;

        handlerMethod.Invoke(manager, new object[] { installation.Id, "crashy" });

        Assert.False(installation.IsRunning);
        Assert.False(processes.ContainsKey(installation.Id), "进程条目应在 Exited 处理后被清除");
        Assert.Single(statusEvents);
        Assert.Equal(PluginStatus.Running, statusEvents[0].OldStatus);
        Assert.Equal(PluginStatus.Stopped, statusEvents[0].NewStatus);

        // 去重：再次触发（例如 WebSocket 关闭通知）不应重复发布状态变更。
        handlerMethod.Invoke(manager, new object[] { installation.Id, "crashy" });
        Assert.Single(statusEvents);
    }

    // ── C-9: FindConnection(PluginInfo) 按 Name 匹配 ──

    [Fact]
    public void FindConnectionByPluginInfo_MatchesByName_NotByReference()
    {
        var server = new PluginsServer(new FakeEventService());
        var connectionsField = typeof(PluginsServer).GetField("_connections",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var connections = (ConcurrentDictionary<string, IPluginConnection>)connectionsField.GetValue(server)!;

        var registered = new PluginInfo
        {
            Name = "alpha",
            Version = "1.0.0",
            Tags = new Dictionary<string, string>
            {
                ["ConnectionId"] = Guid.NewGuid().ToString(),
                ["JoinTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss(FF)")
            }
        };
        var conn = new FakePluginConnection("conn-1", registered);
        connections["conn-1"] = conn;

        // 不同实例、不同 Tags —— 旧引用比较恒返回 null。
        var query = new PluginInfo { Name = "alpha", Version = "9.9.9" };

        var found = server.FindConnection(query);
        Assert.Same(conn, found);
        Assert.Same(conn, server.FindConnector(query));
    }

    // ── C-11: PluginHostAdapter 9 函数桥接 ──

    [Fact]
    public void PluginHostAdapter_PluginFunctions_BridgeToPluginService()
    {
        var pluginService = new FakePluginService();
        var alpha = pluginService.Add("alpha", "1.0.0");
        var beta = pluginService.Add("beta", "2.0.0");

        var adapter = new PluginHostAdapter(
            new FakePluginManager(),
            pluginService);

        // StartPlugin / StopPlugin
        Assert.True(adapter.StartPlugin("alpha"));
        Assert.Equal(alpha.Id, pluginService.LastStartedId);
        Assert.True(adapter.StopPlugin("alpha"));
        Assert.Equal(alpha.Id, pluginService.LastStoppedId);
        Assert.False(adapter.StartPlugin("missing"));

        // InstallPlugin
        Assert.True(adapter.InstallPlugin(@"C:\tmp\demo.kxp"));
        Assert.Equal(@"C:\tmp\demo.kxp", pluginService.LastImportPath);
        pluginService.ImportResult = false;
        Assert.False(adapter.InstallPlugin(@"C:\tmp\bad.kxp"));

        // GetPluginInfoByName — JSON 序列化的 PluginInfo
        var json = adapter.GetPluginInfoByName("beta");
        Assert.Contains("\"beta\"", json);
        Assert.Contains("2.0.0", json);
        Assert.Equal(string.Empty, adapter.GetPluginInfoByName("missing"));

        // ListPluginNames — JSON 数组
        var names = JsonSerializer.Deserialize<List<string>>(adapter.ListPluginNames());
        Assert.NotNull(names);
        Assert.Equal(new[] { "alpha", "beta" }, names.OrderBy(n => n));

        // 未注入 pluginService 时安全降级
        var bare = new PluginHostAdapter(new FakePluginManager());
        Assert.False(bare.StartPlugin("alpha"));
        Assert.False(bare.InstallPlugin("x.kxp"));
        Assert.Equal("[]", bare.ListPluginNames());
        Assert.Equal(string.Empty, bare.GetPluginInfoByName("alpha"));
    }

    // ── Fakes ──

    private static FieldInfo GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed class FakePluginManager : IPluginManager
    {
        public T Call<T>(PluginCallInfo callInfo) => default!;
        public void Call(PluginCallInfo callInfo) { }
        public bool IsPluginExists(string pluginName) => false;
        public bool IsMethodExists(string pluginName, string methodName) => false;
    }

    private sealed class FakePluginService : IPluginService
    {
        private readonly List<PluginInstallation> _installed = new();

        public Guid? LastStartedId { get; private set; }
        public Guid? LastStoppedId { get; private set; }
        public string? LastImportPath { get; private set; }
        public bool ImportResult { get; set; } = true;

        public event EventHandler<PluginStatusChangedEventArgs>? PluginStatusChanged;

        public PluginInstallation Add(string name, string version)
        {
            var info = new PluginInfo { Name = name, Version = version, PublisherName = "p", AuthorName = "a" };
            var installation = new PluginInstallation
            {
                Id = PluginsManager.GeneratePluginId(info),
                PluginInfo = info,
                InstallPath = null,
                LoaderInfo = new KitX.Shared.CSharp.Loader.LoaderInfo()
            };
            _installed.Add(installation);
            return installation;
        }

        public IReadOnlyList<IPluginInstallation> GetInstalledPlugins() => _installed.ToList();

        public IPluginInstallation? GetPlugin(Guid pluginId) =>
            _installed.FirstOrDefault(p => p.Id == pluginId);

        public Task<bool> ImportPluginAsync(string kxpFilePath)
        {
            LastImportPath = kxpFilePath;
            return Task.FromResult(ImportResult);
        }

        public Task<bool> RemovePluginAsync(Guid pluginId)
        {
            _installed.RemoveAll(p => p.Id == pluginId);
            return Task.FromResult(true);
        }

        public Task<bool> StartPluginAsync(Guid pluginId)
        {
            LastStartedId = pluginId;
            return Task.FromResult(_installed.Any(p => p.Id == pluginId));
        }

        public Task<bool> StopPluginAsync(Guid pluginId)
        {
            LastStoppedId = pluginId;
            return Task.FromResult(true);
        }

        public Task<object?> CallPluginFunctionAsync(Guid pluginId, string functionName,
            Dictionary<string, object>? parameters = null) => Task.FromResult<object?>(null);
    }

    private sealed class FakePluginConnection : IPluginConnection
    {
        public FakePluginConnection(string connectionId, PluginInfo pluginInfo)
        {
            ConnectionId = connectionId;
            PluginInfo = pluginInfo;
        }

        public string? ConnectionId { get; }
        public PluginInfo? PluginInfo { get; set; }
        public ServerStatus Status => ServerStatus.Running;

        public event EventHandler<PluginMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler? Closed;
        public event EventHandler<PluginResponseEventArgs>? PluginResponse;
        public event EventHandler<PluginStatusReportEventArgs>? StatusReport;

        public void Initialize() { }
        public void Send(string message) { }
        public void Request(object request) { }
        public Task CloseAsync() => Task.CompletedTask;
    }
}
