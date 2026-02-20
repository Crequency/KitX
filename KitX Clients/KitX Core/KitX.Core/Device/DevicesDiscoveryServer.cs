using KitX.Core.Configuration;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CTask = System.Threading.Tasks.Task;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Device discovery server for UDP broadcast
/// </summary>
public class DevicesDiscoveryServer : IDeviceDiscoveryService
{
    private static DevicesDiscoveryServer? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static DevicesDiscoveryServer Instance => _instance ??= new();

    private readonly IConfigService _configService;
    private UdpClient? _udpSender;
    private UdpClient? _udpReceiver;
    private System.Timers.Timer? _udpSendTimer;
    private readonly List<int> _supportedNetworkInterfacesIndexes = new();
    private bool _disposed;
    private ServerStatus _status = ServerStatus.Pending;
    private int _deviceInfoUpdatedTimes = 0;
    private int _lastTimeToOSVersionUpdated = 0;

    /// <summary>
    /// Gets the service status
    /// </summary>
    public ServerStatus Status => _status;

    /// <summary>
    /// Gets or sets the port
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Configured port for the server
    /// </summary>
    private int? _configuredPort;

    /// <summary>
    /// Configures the port for the server
    /// </summary>
    /// <param name="port">The port number</param>
    public void ConfigurePort(int port)
    {
        _configuredPort = port is >= 0 and <= 65535 ? port : null;
    }

    /// <summary>
    /// Request to close the server
    /// </summary>
    public bool CloseDevicesDiscoveryServerRequest { get; internal set; }

    /// <summary>
    /// Queue of messages to broadcast
    /// </summary>
    public Queue<string> Messages2BroadCast { get; } = new();

    /// <summary>
    /// Default device information
    /// </summary>
    public DeviceInfo DefaultDeviceInfo { get; private set; }

    /// <summary>
    /// Event raised when a device is discovered
    /// </summary>
    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Event raised when a device goes offline
    /// </summary>
    public event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;

    /// <summary>
    /// Private constructor
    /// </summary>
    private DevicesDiscoveryServer()
    {
        _configService = ConfigManager.Instance; // Will be injected via DI in production
        DefaultDeviceInfo = NetworkHelper.GetDeviceInfo();

        // Note: DevicesOrganizer.Run() should be called after services are fully initialized
        // to avoid blocking during DI container setup
    }

    /// <summary>
    /// Starts the device discovery service
    /// </summary>
    /// <returns>The service instance</returns>
    public IDeviceDiscoveryService Run()
    {
        if (_status != ServerStatus.Pending)
            return this;

        _status = ServerStatus.Starting;

        Initialize();

        // Read configuration from IConfigService
        var udpPortSend = _configService.AppConfig.Web.UdpPortSend;
        var udpPortReceive = _configService.AppConfig.Web.UdpPortReceive;
        var udpBroadcastAddress = _configService.AppConfig.Web.UdpBroadcastAddress;

        Port = udpPortSend;

        _udpSender = new UdpClient(udpPortSend, AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
            MulticastLoopback = true,
        };

        _udpReceiver = new UdpClient(new IPEndPoint(IPAddress.Any, udpPortReceive));

        CTask.Run(() =>
        {
            try
            {
                FindSupportNetworkInterfaces(
                    [_udpSender, _udpReceiver],
                    IPAddress.Parse(udpBroadcastAddress)
                );
            }
            catch (Exception ex)
            {
                const string location = $"{nameof(DevicesDiscoveryServer)}.{nameof(Run)}";
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        });

        CTask.Run(MultiDevicesBroadCastSend);
        CTask.Run(MultiDevicesBroadCastReceive);

        _status = ServerStatus.Running;

        return this;
    }

    /// <summary>
    /// Stops the device discovery service
    /// </summary>
    public void Stop()
    {
        if (_status != ServerStatus.Running)
            return;

        _status = ServerStatus.Stopping;
        CloseDevicesDiscoveryServerRequest = true;

        CTask.Run(async () =>
        {
            await CTask.Delay(1000); // Wait for threads to finish
            _status = ServerStatus.Pending;
        });
    }

    private void Initialize()
    {
        _disposed = false;
        CloseDevicesDiscoveryServerRequest = false;
        _supportedNetworkInterfacesIndexes.Clear();
        Messages2BroadCast.Clear();
        DefaultDeviceInfo = NetworkHelper.GetDeviceInfo();
        _deviceInfoUpdatedTimes = 0;
        _lastTimeToOSVersionUpdated = 0;
    }

    private void FindSupportNetworkInterfaces(List<UdpClient?> clients, IPAddress multicastAddress)
    {
        var multicastGroupJoinedInterfacesCount = 0;

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var adapterProperties = adapter.GetIPProperties();

            if (adapterProperties is null)
                continue;

            if (!CheckNetworkInterface(adapter, adapterProperties))
                continue;

            var unicastIPAddresses = adapterProperties.UnicastAddresses;

            if (unicastIPAddresses is null)
                continue;

            var p = adapterProperties.GetIPv4Properties();

            if (p is null)
                continue;

            _supportedNetworkInterfacesIndexes.Add(IPAddress.HostToNetworkOrder(p.Index));

            foreach (var ipAddress in unicastIPAddresses.Select(x => x.Address).Where(x => x.AddressFamily == AddressFamily.InterNetwork))
            {
                try
                {
                    foreach (var udpClient in clients)
                        udpClient?.JoinMulticastGroup(multicastAddress, ipAddress);

                    ++multicastGroupJoinedInterfacesCount;
                }
                catch (Exception ex)
                {
                    const string location = $"{nameof(DevicesDiscoveryServer)}.{nameof(FindSupportNetworkInterfaces)}";

                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }
        }

        Log.Information($"Find {_supportedNetworkInterfacesIndexes.Count} supported network interfaces.");
        Log.Information($"Joined {multicastGroupJoinedInterfacesCount} multicast groups.");
    }

    private bool CheckNetworkInterface(NetworkInterface adapter, IPInterfaceProperties adapterProperties)
    {
        // Filter for operational, supported network interfaces
        return adapter.OperationalStatus == OperationalStatus.Up &&
               adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
               adapter.Supports(NetworkInterfaceComponent.IPv4);
    }

    private void UpdateDefaultDeviceInfo()
    {
        DefaultDeviceInfo.IsMainDevice = ConstantTable.IsMainMachine;
        DefaultDeviceInfo.SendTime = DateTime.UtcNow;
        DefaultDeviceInfo.Device.ResetIPv4(NetworkHelper.GetInterNetworkIPv4())
            .ResetIPv6(NetworkHelper.GetInterNetworkIPv6());
        DefaultDeviceInfo.PluginsServerPort = ConstantTable.PluginsServerPort;
        DefaultDeviceInfo.PluginsCount = 0; // TODO: Will be updated from PluginsManager
        DefaultDeviceInfo.IsMainDevice = ConstantTable.IsMainMachine;
        DefaultDeviceInfo.DevicesServerPort = ConstantTable.DevicesServerPort;
        DefaultDeviceInfo.DevicesServerBuildTime = ConstantTable.ServerBuildTime;

        // Update OS version periodically
        if (_lastTimeToOSVersionUpdated > _configService.AppConfig.IO.OperatingSystemVersionUpdateInterval)
        {
            _lastTimeToOSVersionUpdated = 0;
            DefaultDeviceInfo.DeviceOSVersion = NetworkHelper.TryGetOsVersionString() ?? "";
        }

        ++_deviceInfoUpdatedTimes;
        ++_lastTimeToOSVersionUpdated;

        if (_deviceInfoUpdatedTimes < 0)
            _deviceInfoUpdatedTimes = 0;
    }

    private void MultiDevicesBroadCastSend()
    {
        const string location = $"{nameof(DevicesDiscoveryServer)}.{nameof(MultiDevicesBroadCastSend)}";

        var udpPortReceive = _configService.AppConfig.Web.UdpPortReceive;
        var udpBroadcastAddress = _configService.AppConfig.Web.UdpBroadcastAddress;
        var udpSendFrequency = _configService.AppConfig.Web.UdpSendFrequency;

        var multicast = new IPEndPoint(
            IPAddress.Parse(udpBroadcastAddress),
            udpPortReceive
        );

        _udpSender?.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var erroredInterfacesIndexes = new List<int>();
        var erroredInterfacesIndexesTTL = 60;

        _udpSendTimer = new System.Timers.Timer { Interval = udpSendFrequency, AutoReset = true };

        _udpSendTimer.Elapsed += (_, _) =>
        {
            var closingRequest = CloseDevicesDiscoveryServerRequest;

            --erroredInterfacesIndexesTTL;

            if (erroredInterfacesIndexesTTL <= 0)
            {
                erroredInterfacesIndexesTTL = 60;
                erroredInterfacesIndexes.Clear();
            }

            UpdateDefaultDeviceInfo();

            if (closingRequest)
                DefaultDeviceInfo.SendTime -= TimeSpan.FromSeconds(20);

            var sendText = JsonSerializer.Serialize(DefaultDeviceInfo);
            var sendBytes = System.Text.Encoding.UTF8.GetBytes(sendText);

            foreach (var item in _supportedNetworkInterfacesIndexes)
            {
                if (erroredInterfacesIndexes.Contains(item))
                    continue;

                try
                {
                    _udpSender?.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, item);
                    _udpSender?.Send(sendBytes, sendBytes.Length, multicast);

                    while (Messages2BroadCast.Count > 0)
                    {
                        var messageBytes = System.Text.Encoding.UTF8.GetBytes(Messages2BroadCast.Dequeue());
                        _udpSender?.Send(messageBytes, messageBytes.Length, multicast);
                    }
                }
                catch (Exception ex)
                {
                    if (!erroredInterfacesIndexes.Contains(item))
                        erroredInterfacesIndexes.Add(item);

                    Log.Warning(ex, $"In {location}: Errored interface index: {item}, recorded.");
                }
            }

            if (closingRequest)
            {
                _udpSendTimer?.Stop();
                _udpSendTimer?.Close();
                _udpSender?.Close();
                _udpReceiver?.Close();
                CloseDevicesDiscoveryServerRequest = false;
            }
        };

        _udpSendTimer.Start();
    }

    private void MultiDevicesBroadCastReceive()
    {
        const string location = $"{nameof(DevicesDiscoveryServer)}.{nameof(MultiDevicesBroadCastReceive)}";

        var multicast = new IPEndPoint(IPAddress.Any, 0);

        _udpReceiver?.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var thread = new Thread(async () =>
        {
            try
            {
                while (!CloseDevicesDiscoveryServerRequest)
                {
                    var bytes = _udpReceiver?.Receive(ref multicast);
                    var client = $"{multicast.Address}:{multicast.Port}";

                    if (bytes is null)
                        continue;

                    var result = System.Text.Encoding.UTF8.GetString(bytes);

                    Log.Information($"UDP From: {client, -21}, Receive: {result}");

                    try
                    {
                        var info = JsonSerializer.Deserialize<DeviceInfo>(result);

                        if (info is not null)
                        {
                            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs
                            {
                                DeviceInfo = info
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, $"When trying to deserialize `{result}`");
                    }
                }

                _status = ServerStatus.Pending;
            }
            catch (Exception e)
            {
                Log.Error(e, $"In {location}: {e.Message}");
                _status = ServerStatus.Errored;
            }

            await CTask.Run(() => Stop());
        });

        thread.Start();
    }

    /// <summary>
    /// Disposes the server
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        CloseDevicesDiscoveryServerRequest = false;

        _udpSender?.Dispose();
        _udpReceiver?.Dispose();

        GC.Collect();
    }
}
