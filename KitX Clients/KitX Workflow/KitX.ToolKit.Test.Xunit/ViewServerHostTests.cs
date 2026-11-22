using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Server;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// <see cref="ViewServerHost"/>: the three decisions the desktop makes before a listener exists —
/// whether to start, on which port, and how long stopping may take.
/// </summary>
/// <remarks>
/// <para><b>Why these are not tests of <see cref="ViewServer"/>.</b> The transport has its own
/// coverage. What had none was the layer that translates a configuration value into a listener,
/// which is where <c>0</c> quietly means "pick a free port" and where a stop can hang the process
/// exit. Both of those are one-line mistakes with consequences that only appear on a desktop.</para>
///
/// <para><b>The fixed-port tests race, and that is stated rather than hidden.</b> A free port is
/// discovered by binding and releasing one, and another process could take it in between. The suite
/// runs on a developer machine, the window is milliseconds, and the alternative — a hard-coded
/// number — fails for the opposite reason the moment something else uses it.</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ViewServerHostTests
{
    /// <summary>A channel that is never asked to do anything.</summary>
    private sealed class SilentChannel : IViewChannel, IDisposable
    {
        public event EventHandler<ViewMessage>? Received
        {
            add { }
            remove { }
        }

        public ValueTask<ChannelReply> SendAsync(
            ViewMessage message, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ChannelReply.Accepted(message));

        public void Dispose()
        {
        }
    }

    private static (ViewServerHost Host, List<string> Log) Host()
    {
        var log = new List<string>();

        return (new ViewServerHost(() => new SilentChannel(), line =>
        {
            lock (log)
                log.Add(line);
        }), log);
    }

    /// <summary>Asks the OS for a port nobody is using right now.</summary>
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private static async Task<bool> ConnectsAsync(int port)
    {
        using var socket = new ClientWebSocket();

        // A short deadline: the question is only whether somebody is listening there.
        using var give = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), give.Token);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public void No_configured_port_starts_nothing_and_says_why()
    {
        var (host, log) = Host();

        using (host)
        {
            Assert.False(host.Start(null));

            Assert.False(host.IsListening);
            Assert.Null(host.Port);

            Assert.Contains(log, line => line.Contains("not starting", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_and_negative_mean_the_same_as_absent_rather_than_pick_a_port(int configured)
    {
        // The transport's own default is `port: 0`, which Fleck reads as "bind anything free". To a
        // desktop that is a listener nobody can find, so the wrapper has to translate the value
        // instead of forwarding it.
        var (host, _) = Host();

        using (host)
        {
            Assert.False(host.Start(configured));
            Assert.False(host.IsListening);
        }
    }

    [Fact]
    public async Task A_configured_port_listens_on_that_exact_number()
    {
        var port = FreePort();

        var (host, _) = Host();

        using (host)
        {
            Assert.True(host.Start(port));

            // Exact, not "some port": `Port` is what the desktop would print and what a remote
            // client is told to dial, and "some port" is precisely the behaviour being ruled out.
            Assert.Equal(port, host.Port);
            Assert.True(host.IsListening);

            Assert.True(await ConnectsAsync(port));
        }
    }

    [Fact]
    public async Task Stopping_releases_the_port()
    {
        var port = FreePort();

        var (host, _) = Host();

        Assert.True(host.Start(port));
        Assert.True(await ConnectsAsync(port));

        host.Dispose();

        Assert.False(host.IsListening);

        // The port has to be free for somebody else: that is what "disposed" means to the person
        // who wants to start the app again.
        using var again = new TcpListener(IPAddress.Loopback, port);

        again.Start();
    }

    [Fact]
    public async Task Stopping_returns_within_its_patience_even_with_a_client_attached()
    {
        // The desktop's shutdown waits on an event with no timeout before Environment.Exit(0), so a
        // stop that blocks is a process that never exits. This asserts the bound rather than the
        // mechanism: what matters is that the call comes back.
        var port = FreePort();

        var (host, _) = Host();

        Assert.True(host.Start(port));

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        var started = DateTime.UtcNow;

        host.Dispose();

        var took = DateTime.UtcNow - started;

        Assert.True(
            took < ViewServerHost.StopPatience + TimeSpan.FromSeconds(2),
            $"stopping took {took.TotalSeconds:0.##}s, which is longer than the patience it promises");
    }

    [Fact]
    public void The_network_switch_turns_it_off_even_when_a_port_is_configured()
    {
        // A person who disabled the network system does not expect a listening socket to appear
        // anyway. Same reason as `null`/`0`, reached through a different input.
        var (host, log) = Host();

        using (host)
        {
            Assert.False(host.Start(FreePort(), networkSystemEnabled: false));

            Assert.False(host.IsListening);

            Assert.Contains(log, line => line.Contains("network system is disabled", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Starting_twice_does_not_open_a_second_listener()
    {
        var port = FreePort();

        var (host, log) = Host();

        using (host)
        {
            Assert.True(host.Start(port));
            Assert.True(host.Start(port));

            Assert.Equal(port, host.Port);

            Assert.Contains(log, line => line.Contains("Already listening", StringComparison.Ordinal));
        }
    }
}
