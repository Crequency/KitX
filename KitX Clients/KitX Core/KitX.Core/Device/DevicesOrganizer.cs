using KitX.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Shared.CSharp.Device;
using Serilog;
using Timer = System.Timers.Timer;

namespace KitX.Core.Device;

/// <summary>
/// Devices organizer for managing discovered devices
/// Phase 6.5: Aligned with legacy DevicesOrganizer functionality
/// </summary>
public class DevicesOrganizer : IDevicesOrganizer
{
    private static DevicesOrganizer? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static DevicesOrganizer Instance => _instance ??= new();

    private readonly IConfigService _configService;
    private readonly IEventService _eventService;
    private readonly object _receivedDeviceInfo4WatchLock = new();
    private readonly Queue<DeviceInfo> _deviceInfosQueue = new();
    private readonly object _addDeviceCardLock = new();
    private bool _keepCheckAndRemoveTaskRunning = false;
    private List<DeviceInfo>? _receivedDeviceInfo4Watch;

    /// <summary>
    /// Event raised when a device is discovered
    /// </summary>
    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Event raised when a device goes offline
    /// </summary>
    public event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;

    private DevicesOrganizer()
    {
        _configService = ConfigManager.Instance;
        _eventService = EventService.Instance;
        Initialize();
    }

    /// <summary>
    /// Runs the devices organizer
    /// </summary>
    public static void Run()
    {
        _instance = Instance;
    }

    /// <summary>
    /// Initializes the devices organizer
    /// </summary>
    private void Initialize()
    {
        InitEvents();

        KeepCheckAndRemove();

        ObserveMainDevice();
    }

    /// <summary>
    /// Initializes event subscriptions
    /// </summary>
    private void InitEvents()
    {
        // Subscribe to device discovery events from DevicesDiscoveryServer
        DevicesDiscoveryServer.Instance.DeviceDiscovered += (_, args) =>
        {
            if (args.DeviceInfo is null) return;

            _deviceInfosQueue.Enqueue(args.DeviceInfo);

            lock (_receivedDeviceInfo4WatchLock)
            {
                _receivedDeviceInfo4Watch?.Add(args.DeviceInfo);
            }

            // Check for main device changes
            if (args.DeviceInfo.IsMainDevice && args.DeviceInfo.DevicesServerBuildTime < ConstantTable.ServerBuildTime)
            {
                ConstantTable.IsMainMachine = false;

                ObserveMainDevice();

                Log.Information(
                    new StringBuilder()
                        .AppendLine("Watched earlier built server.")
                        .AppendLine($"DevicesServerAddress: {args.DeviceInfo.Device.IPv4}:{args.DeviceInfo.DevicesServerPort}")
                        .AppendLine($"DevicesServerBuildTime: {args.DeviceInfo.DevicesServerBuildTime}")
                        .ToString()
                );
            }
        };
    }

    /// <summary>
    /// Updates the source and adds device cards
    /// </summary>
    /// <param name="deviceInfo">The device information</param>
    public void UpdateSourceAndAddCards(DeviceInfo deviceInfo)
    {
        lock (_addDeviceCardLock)
        {
            // Trigger event to notify UI layer
            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs
            {
                DeviceInfo = deviceInfo
            });
        }
    }

    /// <summary>
    /// Keeps checking and removing offline devices
    /// </summary>
    private void KeepCheckAndRemove()
    {
        const string location = $"{nameof(DevicesOrganizer)}.{nameof(KeepCheckAndRemove)}";

        var timer = new Timer
        {
            Interval = _configService.AppConfig.Web.DevicesViewRefreshDelay,
            AutoReset = true
        };

        timer.Elapsed += (_, _) =>
        {
            try
            {
                if (_keepCheckAndRemoveTaskRunning)
                {
                    Log.Information($"In {location}: Timer elapsed and skip task.");
                }
                else
                {
                    _keepCheckAndRemoveTaskRunning = true;

                    UpdateSourceAndAddCards();

                    if (_configService.AppConfig.Web.DisableRemovingOfflineDeviceCard == false)
                        RemoveOfflineCards();

                    // TODO: Implement MoveSelfCardToFirst if needed

                    _keepCheckAndRemoveTaskRunning = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"In {location}: {ex.Message}");
            }
        };

        timer.Start();

        // Subscribe to config changes via IEventService
        _eventService.Subscribe(EventNames.AppConfigChanged, (s, e) =>
        {
            timer.Interval = _configService.AppConfig.Web.DevicesViewRefreshDelay;
        });
    }

    /// <summary>
    /// Updates source and adds cards from queue
    /// </summary>
    private void UpdateSourceAndAddCards()
    {
        var thisTurnAdded = new List<int>();

        while (_deviceInfosQueue.Count > 0)
        {
            var info = _deviceInfosQueue.Dequeue();
            var hashCode = info.GetHashCode();

            if (thisTurnAdded.Contains(hashCode))
                continue;

            // Trigger event for UI layer to handle deduplication
            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs
            {
                DeviceInfo = info
            });

            thisTurnAdded.Add(hashCode);
        }
    }

    /// <summary>
    /// Removes offline device cards
    /// </summary>
    private void RemoveOfflineCards()
    {
        // Trigger offline events for devices that haven't been seen recently
        if (_receivedDeviceInfo4Watch is null) return;

        var ttl = TimeSpan.FromSeconds(_configService.AppConfig.Web.DeviceInfoTTLSeconds);
        var now = DateTime.UtcNow;

        foreach (var device in _receivedDeviceInfo4Watch)
        {
            if (now - device.SendTime.ToUniversalTime() > ttl)
            {
                DeviceOffline?.Invoke(this, new DeviceOfflineEventArgs
                {
                    DeviceId = device.GetHashCode().ToString()
                });
            }
        }
    }

    /// <summary>
    /// Observes main device in the network
    /// </summary>
    /// <param name="token">Cancellation token</param>
    internal void ObserveMainDevice(CancellationToken token = default)
    {
        const string location = $"{nameof(DevicesOrganizer)}.{nameof(ObserveMainDevice)}";

        new Thread(() =>
        {
            _receivedDeviceInfo4Watch = [];

            var checkedTime = 0;
            var hadMainDevice = false;
            var earliestBuiltServerTime = DateTime.UtcNow;
            var serverPort = 0;
            var serverAddress = string.Empty;

            while (checkedTime < 7 && token.IsCancellationRequested == false)
            {
                try
                {
                    if (_receivedDeviceInfo4Watch is null)
                        continue;

                    lock (_receivedDeviceInfo4WatchLock)
                    {
                        foreach (var item in _receivedDeviceInfo4Watch)
                        {
                            if (item.IsMainDevice)
                            {
                                if (item.DevicesServerBuildTime.ToUniversalTime() < earliestBuiltServerTime)
                                {
                                    serverPort = item.DevicesServerPort;
                                    serverAddress = item.Device.IPv4;
                                }
                                hadMainDevice = true;
                            }
                        }
                    }

                    ++checkedTime;

                    Log.Information($"In {location}: Watched for {checkedTime} times.");

                    if (checkedTime == 7)
                    {
                        _receivedDeviceInfo4Watch?.Clear();
                        _receivedDeviceInfo4Watch = null;

                        if (token.IsCancellationRequested == false)
                            WatchingOver(hadMainDevice, serverAddress, serverPort);
                    }

                    Thread.Sleep(1 * 1000); // Sleep 1 second
                }
                catch (Exception e)
                {
                    _receivedDeviceInfo4Watch?.Clear();
                    _receivedDeviceInfo4Watch = null;

                    Log.Error(e, $"In {location}: {e.Message} Rewatch.");

                    if (token.IsCancellationRequested == false)
                        ObserveMainDevice();

                    break;
                }
            }
        }).Start();
    }

    /// <summary>
    /// Called when main device observation is complete
    /// </summary>
    private void WatchingOver(bool foundMainDevice, string serverAddress, int serverPort)
    {
        const string location = $"{nameof(DevicesOrganizer)}.{nameof(WatchingOver)}";

        Log.Information(
            new StringBuilder()
                .Append($"In {location}: ")
                .Append($"{nameof(foundMainDevice)} -> {foundMainDevice}")
                .Append(", ")
                .Append($"{nameof(serverAddress)} -> {serverAddress}")
                .Append(", ")
                .Append($"{nameof(serverPort)} -> {serverPort}")
                .ToString()
        );

        if (foundMainDevice)
        {
            ConstantTable.MainMachineAddress = serverAddress;
            ConstantTable.MainMachinePort = serverPort;
        }
        else
        {
            ConstantTable.IsMainMachine = true;
        }
    }
}

/// <summary>
/// Devices organizer interface
/// </summary>
public interface IDevicesOrganizer
{
    /// <summary>
    /// Updates the source and adds device cards
    /// </summary>
    void UpdateSourceAndAddCards(DeviceInfo deviceInfo);

    /// <summary>
    /// Event raised when a device is discovered
    /// </summary>
    event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Event raised when a device goes offline
    /// </summary>
    event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;
}
