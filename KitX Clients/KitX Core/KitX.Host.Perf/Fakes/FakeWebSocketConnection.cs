using Fleck;

namespace KitX.Host.Perf.Fakes;

/// <summary>
/// In-memory <see cref="IWebSocketConnection"/> used to drive a real
/// <see cref="KitX.Core.Device.PluginConnection"/> without any network socket.
/// The <c>OnMessage</c> delegate is invoked directly by the benchmark to feed message
/// text through the real <c>PluginConnection.OnMessage</c> parsing path.
/// </summary>
public sealed class FakeWebSocketConnection : IWebSocketConnection
{
    public Action? OnOpen { get; set; }
    public Action? OnClose { get; set; }
    public Action<string>? OnMessage { get; set; }
    public Action<byte[]>? OnBinary { get; set; }
    public Action<byte[]>? OnPing { get; set; }
    public Action<byte[]>? OnPong { get; set; }
    public Action<Exception>? OnError { get; set; }

    public IWebSocketConnectionInfo ConnectionInfo { get; } = new FakeConnectionInfo();

    public bool IsAvailable => true;

    public Task Send(string message) => Task.CompletedTask;
    public Task Send(byte[] message) => Task.CompletedTask;
    public Task SendPing(byte[] message) => Task.CompletedTask;
    public Task SendPong(byte[] message) => Task.CompletedTask;
    public void Close() { }
    public void Close(int code) { }

    /// <summary>Feeds one message through the real OnMessage handler.</summary>
    public void Deliver(string message) => OnMessage?.Invoke(message);

    private sealed class FakeConnectionInfo : IWebSocketConnectionInfo
    {
        public string SubProtocol => string.Empty;
        public string Origin => string.Empty;
        public string Host => string.Empty;
        public string Path => string.Empty;
        public string ClientIpAddress => "127.0.0.1";
        public int ClientPort => 0;
        public IDictionary<string, string> Cookies { get; } = new Dictionary<string, string>();
        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        public Guid Id => Guid.NewGuid();
        public string NegotiatedSubProtocol => string.Empty;
    }
}
