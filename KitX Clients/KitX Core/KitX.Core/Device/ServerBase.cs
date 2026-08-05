using KitX.Core.Contract.Device;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Base class for server implementations, providing common status management
/// and lifecycle patterns. All three servers (DevicesServer, DevicesDiscoveryServer,
/// PluginsServer) share the same status transition pattern.
///
/// <para>Usage pattern:</para>
/// <code>
/// public class MyServer : ServerBase
/// {
///     public MyServer Run()
///     {
///         if (!TryStart()) return this;
///
///         try
///         {
///             // startup logic
///             SetRunning();
///         }
///         catch (Exception ex)
///         {
///             SetErrored(ex);
///         }
///         return this;
///     }
///
///     public void Stop()
///     {
///         if (!TryStop()) return;
///
///         try
///         {
///             // shutdown logic
///             SetPending();
///         }
///         catch (Exception ex)
///         {
///             SetErrored(ex);
///         }
///     }
/// }
/// </code>
/// </summary>
public abstract class ServerBase
{
    private ServerStatus _status = ServerStatus.Pending;

    /// <summary>
    /// Gets the current server status
    /// </summary>
    public ServerStatus Status => _status;

    /// <summary>
    /// Gets whether the server can start (status is Pending)
    /// </summary>
    protected bool IsPending => _status == ServerStatus.Pending;

    /// <summary>
    /// Gets whether the server is starting
    /// </summary>
    protected bool IsStarting => _status == ServerStatus.Starting;

    /// <summary>
    /// Gets whether the server is running
    /// </summary>
    protected bool IsRunning => _status == ServerStatus.Running;

    /// <summary>
    /// Gets whether the server is stopping
    /// </summary>
    protected bool IsStopping => _status == ServerStatus.Stopping;

    /// <summary>
    /// Gets whether the server is in an errored state
    /// </summary>
    protected bool IsErrored => _status == ServerStatus.Errored;

    /// <summary>
    /// Tries to transition from Pending to Starting. Returns false if already started.
    /// </summary>
    /// <returns>True if transition succeeded</returns>
    protected bool TryStart()
    {
        if (_status != ServerStatus.Pending)
            return false;
        _status = ServerStatus.Starting;
        return true;
    }

    /// <summary>
    /// Transitions to Running state
    /// </summary>
    protected void SetRunning() => _status = ServerStatus.Running;

    /// <summary>
    /// Tries to transition from Running to Stopping. Returns false if not running.
    /// </summary>
    /// <returns>True if transition succeeded</returns>
    protected bool TryStop()
    {
        if (_status != ServerStatus.Running)
            return false;
        _status = ServerStatus.Stopping;
        return true;
    }

    /// <summary>
    /// Transitions to Pending state (stopped cleanly)
    /// </summary>
    protected void SetPending() => _status = ServerStatus.Pending;

    /// <summary>
    /// Transitions to Errored state and logs the exception
    /// </summary>
    /// <param name="ex">The exception that caused the error</param>
    /// <param name="context">Context string for logging</param>
    protected void SetErrored(Exception? ex, string context)
    {
        _status = ServerStatus.Errored;
        if (ex != null)
            Log.Error(ex, $"[{context}] {ex.Message}");
    }
}
