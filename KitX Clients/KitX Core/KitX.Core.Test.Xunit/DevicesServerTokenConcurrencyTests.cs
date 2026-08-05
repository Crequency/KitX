using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Device;
using KitX.Core.Security;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// DevicesServer 签入 token 字典的并发测试（C-3 回归）。
/// 覆盖：并发签入/查询不抛异常且索引一致、同一设备并发签入返回同一 token。
/// </summary>
public class DevicesServerTokenConcurrencyTests : IDisposable
{
    private readonly SecurityManager _securityManager;
    private readonly DevicesServer _server;

    public DevicesServerTokenConcurrencyTests()
    {
        // C-2 场景：同一 SecurityManager 实例同时充当两个接口
        _securityManager = new SecurityManager(new FakeConfigService(), new FakeDeviceDiscoveryService());
        _server = new DevicesServer(
            _securityManager,
            _securityManager,
            new FakeEventService(),
            new FakePluginServer(),
            new FakeDeviceDiscoveryService());
    }

    [Fact]
    public async Task ConcurrentSignInAndLookup_IsConsistent()
    {
        const int deviceCount = 64;
        var locators = Enumerable.Range(0, deviceCount)
            .Select(i => new DeviceLocator { DeviceName = $"Dev-{i}", MacAddress = $"AA-BB-CC-DD-{i:0000}" })
            .ToArray();

        // 并发签入
        await Parallel.ForEachAsync(locators, async (locator, ct) =>
        {
            await Task.Yield();
            var token = _server.SignInDevice(locator);
            Assert.False(string.IsNullOrEmpty(token));
        });

        Assert.Equal(deviceCount, _server.GetSignedInDevices().Count);

        // 并发按 token 反向查找 + 存在性检查，索引必须一致
        await Parallel.ForEachAsync(locators, async (locator, ct) =>
        {
            await Task.Yield();
            var token = _server.GetDeviceToken(locator);
            Assert.NotNull(token);
            Assert.True(_server.IsDeviceTokenExist(token));
            Assert.True(locator.Equals(_server.SearchDeviceByToken(token)));
        });
    }

    [Fact]
    public async Task ConcurrentSignInSameDevice_ReturnsSameToken()
    {
        var locator = new DeviceLocator { DeviceName = "Same", MacAddress = "AA-BB-CC-DD-EE-99" };

        var tokens = new string[32];
        await Parallel.ForEachAsync(Enumerable.Range(0, tokens.Length), async (i, ct) =>
        {
            await Task.Yield();
            tokens[i] = _server.SignInDevice(locator);
        });

        // 同一设备只保留一个 token，且能正常反向查找
        Assert.All(tokens, t => Assert.Equal(tokens[0], t));
        Assert.Single(_server.GetSignedInDevices());
        Assert.True(locator.Equals(_server.SearchDeviceByToken(tokens[0])));
    }

    public void Dispose() => _securityManager.Dispose();

    /// <summary>
    /// 最小 IPluginServer 实现 —— 并发测试只使用 DevicesServer 的 token 索引 API，
    /// 不触发任何插件连接逻辑。
    /// </summary>
    private sealed class FakePluginServer : IPluginServer
    {
        public int? Port => null;

        public IReadOnlyList<IPluginConnection> Connections => Array.Empty<IPluginConnection>();

        public IPluginServer Run() => this;

        public void Stop() { }

        public IPluginConnector? FindConnector(PluginInfo pluginInfo) => null;

        public IPluginConnection? FindConnection(string connectionId) => null;

        public event EventHandler<int>? PortChanged;

        public event EventHandler<PluginConnectedEventArgs>? PluginConnected;

        public event EventHandler<PluginDisconnectedEventArgs>? PluginDisconnected;

        public event EventHandler<PluginMessageReceivedEventArgs>? PluginMessageReceived;

        public event EventHandler<PluginRegisteredEventArgs>? PluginRegistered;

        public event EventHandler<PluginUnregisteredEventArgs>? PluginUnregistered;

        public event EventHandler<PluginResponseEventArgs>? PluginResponse;
    }
}
