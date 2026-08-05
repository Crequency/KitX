using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using KitX.Core;
using KitX.Core.Device;
using KitX.Core.Event;
using KitX.Core.Test.Xunit.Fakes;
using ServerStatus = KitX.Core.Contract.Device.ServerStatus;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// PluginsServer 安全修复测试。
/// 覆盖：回环绑定（仅监听 127.0.0.1，局域网 IP 不可达）、连接 ID 必须为 GUID 格式。
/// </summary>
public class PluginsServerSecurityTests
{
    [Fact]
    public async Task Run_BindsLoopbackOnly()
    {
        var eventService = new FakeEventService();
        var server = new PluginsServer(eventService);
        var originalPluginsServerPort = ConstantTable.PluginsServerPort;
        try
        {
            server.ConfigurePort(GetFreePort());
            server.Run();

            Assert.Equal(ServerStatus.Running, server.Status);
            Assert.NotNull(server.Port);
            Assert.True(server.Port > 0);

            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, server.Port.Value, CancellationToken.None);
                Assert.True(tcp.Connected);
            }

            var lanIp = GetNonLoopbackIPv4();
            if (lanIp is not null)
            {
                using var lanTcp = new TcpClient();
                bool connected;
                try
                {
                    var connectTask = lanTcp.ConnectAsync(lanIp, server.Port.Value, CancellationToken.None).AsTask();
                    await Task.WhenAny(connectTask, Task.Delay(1500));
                    connected = connectTask.IsCompletedSuccessfully;
                }
                catch
                {
                    connected = false;
                }

                Assert.False(connected);
            }
        }
        finally
        {
            ConstantTable.PluginsServerPort = originalPluginsServerPort;
            await server.Close();
        }
    }

    [Theory]
    [InlineData("/not-a-guid")]
    [InlineData("/")]
    [InlineData("/123456789")]
    public async Task Connect_InvalidConnectionId_IsRejected(string path)
    {
        var eventService = new FakeEventService();
        var server = new PluginsServer(eventService);
        var originalPluginsServerPort = ConstantTable.PluginsServerPort;
        try
        {
            server.ConfigurePort(GetFreePort());
            server.Run();
            Assert.NotNull(server.Port);

            using var client = new ClientWebSocket();
            bool rejected;
            try
            {
                await client.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}{path}"), CancellationToken.None);
                rejected = false;
            }
            catch (WebSocketException)
            {
                // Fleck 1.2.0 在握手响应发送前执行 Start 回调：拒绝逻辑（Send + Close）会
                // 打断握手响应，客户端表现为握手失败 —— 连接同样被拒绝，属预期结果。
                rejected = true;
            }

            if (!rejected)
            {
                try
                {
                    var buffer = new byte[512];
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                    var rejectedMessage = await client.ReceiveAsync(buffer, cts.Token);
                    if (rejectedMessage.MessageType == WebSocketMessageType.Text)
                        Assert.Equal("Connection rejected.", Encoding.UTF8.GetString(buffer, 0, rejectedMessage.Count));

                    var close = await client.ReceiveAsync(buffer, cts.Token);
                    Assert.Equal(WebSocketMessageType.Close, close.MessageType);
                }
                catch (WebSocketException)
                {
                    rejected = true;
                }
            }

            Assert.True(rejected, "无效连接 ID 应被拒绝（握手中断或收到拒绝消息）");
            Assert.False(eventService.WasPublished(EventNames.PluginConnected));
        }
        finally
        {
            ConstantTable.PluginsServerPort = originalPluginsServerPort;
            await server.Close();
        }
    }

    [Fact]
    public async Task Connect_ValidGuid_IsAccepted()
    {
        var eventService = new FakeEventService();
        var server = new PluginsServer(eventService);
        var originalPluginsServerPort = ConstantTable.PluginsServerPort;
        var connectionId = Guid.NewGuid().ToString();
        try
        {
            server.ConfigurePort(GetFreePort());
            server.Run();
            Assert.NotNull(server.Port);

            using var client = new ClientWebSocket();
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/{connectionId}"), CancellationToken.None);

            var registered = await WaitUntilAsync(
                () => server.FindConnection(connectionId) is not null,
                TimeSpan.FromSeconds(5));

            Assert.True(registered, "合法 GUID 连接未在超时内注册到 PluginsServer");
            Assert.True(eventService.WasPublished(EventNames.PluginConnected));

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        finally
        {
            ConstantTable.PluginsServerPort = originalPluginsServerPort;
            await server.Close();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? GetNonLoopbackIPv4() =>
        Dns.GetHostEntry(Dns.GetHostName()).AddressList
            .FirstOrDefault(ip =>
                ip.AddressFamily == AddressFamily.InterNetwork &&
                !ip.Equals(IPAddress.Loopback))
            ?.ToString();

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }
        return condition();
    }
}
