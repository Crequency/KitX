using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Device;
using KitX.Core.Event;
using KitX.Shared.CSharp.Device;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Device server for HTTP API
/// Phase 5: Simplified implementation using direct WebHostBuilder
/// </summary>
public class DevicesServer : IDeviceServer
{
    private static DevicesServer? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static DevicesServer Instance => _instance ??= new();

    private readonly Dictionary<DeviceLocator, string> _signedDeviceTokens = new();
    private ServerStatus _status = ServerStatus.Pending;
    private IWebHost? _host;
    private int? _configuredPort;

    /// <summary>
    /// Gets the service status
    /// </summary>
    public ServerStatus Status => _status;

    /// <summary>
    /// Event raised when port changes
    /// </summary>
    public event EventHandler<int>? PortChanged;

    /// <summary>
    /// Gets or sets the port
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Configures the port for the server
    /// </summary>
    /// <param name="port">The port number</param>
    public void ConfigurePort(int port)
    {
        _configuredPort = port is >= 0 and <= 65535 ? port : 8888;
    }

    /// <summary>
    /// Starts the device server
    /// </summary>
    /// <returns>The server instance</returns>
    public IDeviceServer Run()
    {
        if (_status != ServerStatus.Pending)
            return this;

        _status = ServerStatus.Starting;

        var port = _configuredPort ?? 8888;

        try
        {
            // Create and start the ASP.NET Core web host
            _host = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    // Add routing services
                    services.AddRouting();

                    // Add core services
                    services.AddSingleton(_signedDeviceTokens);
                })
                .UseKestrel()
                .UseUrls($"http://0.0.0.0:{port}")
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        // Basic health check endpoint
                        endpoints.MapGet("/", async context =>
                        {
                            await context.Response.WriteAsync("KitX DevicesServer is running");
                        });

                        // Device authentication endpoint (placeholder for future implementation)
                        endpoints.MapPost("/api/device/auth", async context =>
                        {
                            // TODO: Implement device authentication logic
                            await context.Response.WriteAsync("{\"status\":\"pending\"}");
                        });
                    });
                })
                .Build();

            // Start the host in a background thread
            var hostThread = new Thread(async () =>
            {
                try
                {
                    await _host.StartAsync();

                    // Get the actual port
                    var server = _host.Services.GetService<IServer>();
                    var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;

                    if (addresses is not null && addresses.Count > 0)
                    {
                        var uri = new Uri(addresses.First());
                        Port = uri.Port;

                        // Update ConstantTable with the actual port
                        ConstantTable.DevicesServerPort = Port ?? 0;

                        // Publish port changed event via EventService only (removed direct PortChanged event to avoid potential recursion)
                        EventService.Instance.Publish(EventNames.DevicesServerPortChanged, new PortChangedEventArgs { Port = Port ?? 0 });

                        Log.Information($"DevicesServer started on port {Port}");
                    }

                    _status = ServerStatus.Running;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to start DevicesServer: {ex.Message}");
                    _status = ServerStatus.Errored;
                }
            })
            {
                IsBackground = true
            };

            hostThread.Start();

            // Wait for server to start
            var timeout = 0;
            while (_status == ServerStatus.Starting && timeout < 50) // 5 seconds timeout
            {
                Thread.Sleep(100);
                timeout++;
            }

            if (_status != ServerStatus.Running)
            {
                Log.Warning("DevicesServer start timed out or failed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error starting DevicesServer: {ex.Message}");
            _status = ServerStatus.Errored;
        }

        return this;
    }

    /// <summary>
    /// Stops the device server
    /// </summary>
    public void Stop()
    {
        if (_status != ServerStatus.Running)
            return;

        _status = ServerStatus.Stopping;

        try
        {
            if (_host is not null)
            {
                _host.StopAsync().Wait(TimeSpan.FromSeconds(5));
                _host.Dispose();
                _host = null;
            }

            Log.Information("DevicesServer stopped");
            _status = ServerStatus.Pending;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error stopping DevicesServer: {ex.Message}");
            _status = ServerStatus.Errored;
        }
    }

    /// <summary>
    /// Closes the device server asynchronously (for backward compatibility)
    /// </summary>
    /// <returns>Task representing the asynchronous operation</returns>
    public async System.Threading.Tasks.Task CloseAsync()
    {
        Stop();
        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a device token exists
    /// </summary>
    /// <param name="token">The token to check</param>
    /// <returns>True if the token exists</returns>
    public bool IsDeviceTokenExist(string token) => _signedDeviceTokens.ContainsValue(token);

    /// <summary>
    /// Searches for a device by token
    /// </summary>
    /// <param name="token">The token to search for</param>
    /// <returns>The device locator or null if not found</returns>
    public DeviceLocator? SearchDeviceByToken(string token)
    {
        if (!IsDeviceTokenExist(token))
            return null;

        return _signedDeviceTokens.First(x => x.Value.Equals(token)).Key;
    }

    /// <summary>
    /// Checks if a device is signed in
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>True if the device is signed in</returns>
    public bool IsDeviceSignedIn(DeviceLocator locator) => _signedDeviceTokens.ContainsKey(locator);

    /// <summary>
    /// Adds a device token
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <param name="token">The token</param>
    public void AddDeviceToken(DeviceLocator locator, string token) => _signedDeviceTokens.Add(locator, token);

    /// <summary>
    /// Signs in a device
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>The generated token</returns>
    public string SignInDevice(DeviceLocator locator)
    {
        var token = Guid.NewGuid().ToString();

        while (_signedDeviceTokens.ContainsValue(token))
            token = Guid.NewGuid().ToString();

        if (!_signedDeviceTokens.TryAdd(locator, token))
            _signedDeviceTokens[locator] = token;

        Log.Information($"Device {locator} signed in with token {token}");

        return token;
    }
}
