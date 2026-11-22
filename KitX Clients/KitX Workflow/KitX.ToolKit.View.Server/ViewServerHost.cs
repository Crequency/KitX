using System.Net.WebSockets;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Server;

namespace KitX.ToolKit.View.Server;

/// <summary>
/// Starts and stops a <see cref="ViewServer"/> from a configured port, and answers the two
/// questions a caller actually has: *did it start* and *on which port*.
/// </summary>
/// <remarks>
/// <para><b>Why this wrapper exists at all.</b> Everything interesting about starting the server from
/// the desktop is a decision, not a call: which port, what <c>0</c> means, whether to start at all,
/// and how long stopping may take. Left inline in <c>AppFramework</c>, every one of those decisions
/// would be reachable only by launching the whole application — which is not a test. Here they are
/// three fields and four branches, and a test can hold them.</para>
///
/// <para><b><c>0</c> means "do not start", not "pick one".</b> The transport's own default is
/// <c>port: 0</c>, and in Fleck that means *bind whatever is free* — correct for a test that will
/// read <c>Port</c> back, and useless in a desktop application, where it would open a listener
/// nobody can find. So the configuration is read here and translated: absent or zero starts nothing
/// and says so.</para>
///
/// <para><b>Stopping is bounded.</b> The desktop's shutdown path waits on a
/// <see cref="ManualResetEventSlim"/> with no timeout before calling <c>Environment.Exit(0)</c>, so a
/// stop that blocks forever means a process that never exits. <see cref="Dispose"/> therefore gives
/// the server a deadline and reports when it had to walk away — a leaked port is bad, a hung exit is
/// worse, and a log line is how the difference gets found later.</para>
/// </remarks>
public sealed class ViewServerHost : IDisposable
{
    /// <summary>How long a stop may take before it is abandoned.</summary>
    /// <remarks>
    /// Chosen against the shutdown path rather than against the network: closing a handful of
    /// loopback sockets is milliseconds, and anything that needs longer is already stuck. Two seconds
    /// is long enough not to interrupt a real close and short enough that a stuck one is visible
    /// instead of merely slow.
    /// </remarks>
    public static readonly TimeSpan StopPatience = TimeSpan.FromSeconds(2);

    private readonly Func<IViewChannel> _newChannel;
    private readonly Action<string> _log;

    private ViewServer? _server;

    /// <summary>Creates a host that will start nothing until <see cref="Start"/> is called.</summary>
    /// <param name="newChannel">The per-connection channel factory, forwarded to the transport.</param>
    /// <param name="log">
    /// One line at a time. A delegate rather than a logger type so that this assembly keeps its
    /// reference list to the contract and the transport; the desktop supplies Serilog.
    /// </param>
    public ViewServerHost(Func<IViewChannel> newChannel, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(newChannel);
        ArgumentNullException.ThrowIfNull(log);

        _newChannel = newChannel;
        _log = log;
    }

    /// <summary>The port actually bound, or <see langword="null"/> when nothing is running.</summary>
    public int? Port => _server?.Port;

    /// <summary>Whether a listener is currently open.</summary>
    public bool IsListening => _server is not null;

    /// <summary>Starts on <paramref name="configuredPort"/> if that is a usable number.</summary>
    /// <returns><see langword="true"/> when a listener is now open.</returns>
    public bool Start(int? configuredPort)
    {
        if (_server is not null)
        {
            // Starting twice would open a second listener on a port the first one already holds.
            _log($"[ViewServer] Already listening on port {_server.Port}; ignoring the second request.");

            return true;
        }

        // `null` and `0` and negatives all mean the same thing here: nobody asked for a listener.
        // The transport's 0-means-random behaviour is exactly what must not leak out to a desktop.
        if (configuredPort is not { } port || port <= 0)
        {
            _log("[ViewServer] No port configured (Web.UserSpecifiedViewServerPort is null or 0); not starting.");

            return false;
        }

        try
        {
            var server = new ViewServer(_newChannel, port);

            server.Start();

            _server = server;

            _log($"[ViewServer] Listening on ws://127.0.0.1:{server.Port}/");

            return true;
        }
        catch (Exception ex)
        {
            // A port already taken, a bind refused, a firewall rule: none of these should stop the
            // desktop from starting. The application's job does not depend on this listener.
            _log($"[ViewServer] Could not listen on port {port}: {ex.Message}");

            return false;
        }
    }

    /// <summary>Starts on <paramref name="configuredPort"/>, honouring the "no network" switch.</summary>
    /// <param name="configuredPort">The configured port, or <see langword="null"/>.</param>
    /// <param name="networkSystemEnabled">
    /// <c>false</c> when the user launched with the network system turned off. A person who disabled
    /// networking does not expect a listening socket to appear anyway — same reason as
    /// <c>null</c>/<c>0</c>, reached through a different input.
    /// </param>
    public bool Start(int? configuredPort, bool networkSystemEnabled)
    {
        if (!networkSystemEnabled)
        {
            _log("[ViewServer] The network system is disabled for this run; not starting.");

            return false;
        }

        return Start(configuredPort);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var server = _server;

        _server = null;

        if (server is null)
            return;

        // Bounded, because the caller of Dispose is a shutdown path that waits without a timeout.
        // Task.Run keeps a blocking close off this thread; the wait below is what makes the deadline
        // real, and a timeout is reported rather than swallowed.
        var stopped = Task.Run(() =>
        {
            server.Dispose();

            return true;
        });

        try
        {
            if (!stopped.Wait(StopPatience))
                _log($"[ViewServer] Stopping took longer than {StopPatience.TotalSeconds:0.#}s; " +
                     "leaving it to the process exit rather than blocking it.");
        }
        catch (AggregateException ex)
        {
            _log($"[ViewServer] Stopping failed: {ex.GetBaseException().Message}");
        }
    }
}
