using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using KitX.Core.Configuration;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Device;
using KitX.Core.Security;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// 设备加密认证连接（DeviceConnectionClient ↔ DevicesServer）端到端测试。
/// 覆盖：密钥交换→连接签发 token 全链路、错误密码重输环、未交换即连接被拒、签名往返。
/// </summary>
public class DeviceConnectionClientTests : IDisposable
{
    private readonly SecurityManager _deviceB;
    private readonly DeviceLocator _locatorB;
    private readonly DevicesServer _serverB;
    private readonly FakeEventService _eventServiceB;

    private readonly SecurityManager _deviceA;
    private readonly DeviceLocator _locatorA;
    private readonly DeviceConnectionClient _clientA;
    private readonly int _port;

    public DeviceConnectionClientTests()
    {
        _port = GetFreePort();

        // Device B (receiver): distinct identity + its own SecurityManager + running DevicesServer.
        _deviceB = BuildDevice("DeviceB", "AA-BB-CC-DD-00-02", out _locatorB);
        _eventServiceB = new FakeEventService();
        _serverB = new DevicesServer(
            _deviceB,
            _deviceB,
            _eventServiceB,
            new FakePluginServer(),
            new FakeDeviceDiscoveryService { DefaultDeviceInfo = new DeviceInfo { Device = _locatorB } });
        _serverB.ConfigurePort(_port);
        _serverB.Run();

        // Device A (initiator): distinct identity + its own SecurityManager + outbound client.
        _deviceA = BuildDevice("DeviceA", "AA-BB-CC-DD-00-01", out _locatorA);
        _clientA = new DeviceConnectionClient(_deviceA, _deviceA);
    }

    private DeviceInfo TargetInfo() => new()
    {
        Device = _locatorB,
        DevicesServerPort = _port
    };

    [Fact]
    public async Task ExchangeThenConnect_IssuesSessionToken()
    {
        const string password = "12345678";

        // Initiator A blocks until receiver B's user accepts the exchange.
        var exchangeTask = _clientA.ExchangeKeyAsync(TargetInfo(), password);

        await AcceptExchangeWithRetryAsync(password);

        var result = await exchangeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success, $"Exchange failed: {result.Error}");
        Assert.NotNull(result.RemoteDeviceKey);

        // A has now stored B's public key and can connect.
        var token = await _clientA.ConnectAsync(TargetInfo());
        Assert.False(string.IsNullOrEmpty(token), "ConnectAsync returned null");

        // B considers A signed in and holds a token for A.
        Assert.True(_serverB.IsDeviceSignedIn(_locatorA));
        Assert.Equal(token, _serverB.GetDeviceToken(_locatorA));
    }

    [Fact]
    public async Task Exchange_WrongPasswordThenCorrect_RepromptsAndSucceeds()
    {
        const string correct = "87654321";
        const string wrong = "11111111";

        var exchangeTask = _clientA.ExchangeKeyAsync(TargetInfo(), correct);

        // First acceptance with a wrong password → decrypt fails → server re-prompts.
        await AcceptExchangeWithRetryAsync(wrong);

        // The receiver-side event must have been published again for re-entry.
        Assert.True(_eventServiceB.WasPublished(KitX.Core.Contract.Event.EventNames.OnReceiveExchangeDeviceKey));

        await AcceptExchangeWithRetryAsync(correct);

        var result = await exchangeTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success, $"Exchange failed: {result.Error}");
    }

    [Fact]
    public async Task ConnectWithoutExchange_ReturnsNull()
    {
        // A has not exchanged keys with B yet, so it has no public key for B.
        var token = await _clientA.ConnectAsync(TargetInfo());
        Assert.Null(token);
    }

    [Fact]
    public void SignThenVerify_RoundTrips_AndWrongKeyRejected()
    {
        var signer = _deviceA.LocalDeviceKey!;
        var signature = _deviceA.RsaSignString(signer, signer.Device.DeviceName);

        Assert.False(string.IsNullOrEmpty(signature));

        // Verify against the matching public key.
        var publicOnly = new DeviceKey
        {
            Device = signer.Device,
            RsaPublicKeyPem = signer.RsaPublicKeyPem
        };
        Assert.True(_deviceA.RsaVerifySignature(publicOnly, signer.Device.DeviceName, signature!));

        // Verify against a different device's public key must fail.
        var other = _deviceB.LocalDeviceKey!;
        var otherPublicOnly = new DeviceKey
        {
            Device = other.Device,
            RsaPublicKeyPem = other.RsaPublicKeyPem
        };
        Assert.False(_deviceA.RsaVerifySignature(otherPublicOnly, signer.Device.DeviceName, signature!));
    }

    [Fact]
    public void IsSameDevice_IsCaseInsensitiveOnName()
    {
        // Regression: the WSL hostname ("StarInk") and Environment.MachineName ("STARINK")
        // differ only in case. Device identity must not be case-sensitive, otherwise the
        // local key lookup misses and the exchange fails with "Failed to get local key".
        var a = new DeviceLocator { DeviceName = "STARINK", MacAddress = "505A654FBFDD" };
        var b = new DeviceLocator { DeviceName = "StarInk", MacAddress = "50:5A:65:4F:BF:DD" };

        Assert.True(a.IsSameDevice(b));
        Assert.True(b.IsSameDevice(a));
    }

    /// <summary>
    /// Builds a SecurityManager pre-seeded with a distinct device identity, so two
    /// managers in one test process do not collide on the same machine locator.
    /// </summary>
    private static SecurityManager BuildDevice(string name, string mac, out DeviceLocator locator)
    {
        using var rsa = RSA.Create(2048);

        locator = new DeviceLocator { DeviceName = name, MacAddress = mac, IPv4 = "127.0.0.1", IPv6 = "" };

        var config = new SecurityConfig();
        config.DeviceKeys.Add(new DeviceKeyImpl
        {
            Device = locator,
            RsaPublicKeyPem = rsa.ExportRSAPublicKeyPem(),
            RsaPrivateKeyPem = rsa.ExportRSAPrivateKeyPem(),
            AddedAt = DateTime.Now
        });

        var discovery = new FakeDeviceDiscoveryService
        {
            DefaultDeviceInfo = new DeviceInfo { Device = locator }
        };

        return new SecurityManager(new FakeConfigService(securityConfig: config), discovery);
    }

    /// <summary>
    /// Simulates receiver B's user entering the password, retrying until the server
    /// has a pending exchange to accept (handles the accept/decrypt re-prompt race).
    /// </summary>
    private async Task AcceptExchangeWithRetryAsync(string password)
    {
        for (var i = 0; i < 200; i++)
        {
            if (_serverB.AcceptExchangeKey(password))
                return;
            await Task.Delay(25);
        }

        Assert.Fail("Timed out waiting to accept the key exchange.");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _serverB.Stop();
        _deviceA.Dispose();
        _deviceB.Dispose();
    }

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
