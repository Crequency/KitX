using System.Text;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;
using Serilog;
using Timer = System.Timers.Timer;

namespace KitX.Core.Device;

/// <summary>
/// Devices organizer for managing discovered devices
/// Phase 6.5: Aligned with legacy DevicesOrganizer functionality
/// </summary>
public class DevicesOrganizer : IDevicesOrganizer, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IEventService _eventService;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IDeviceKeyService _deviceKeyService;
    private readonly object _receivedDeviceInfo4WatchLock = new();

    // C-10: concurrent queue — the UDP receive path (DeviceDiscovered handler)
    // enqueues while the timer path dequeues, previously unsynchronized.
    private readonly System.Collections.Concurrent.ConcurrentQueue<DeviceInfo> _deviceInfosQueue = new();
    private bool _keepCheckAndRemoveTaskRunning = false;
    private List<DeviceInfo>? _receivedDeviceInfo4Watch;

    // C-10: guards that at most one main-device observation thread is alive
    // (the old recursive restart leaked a new thread per failure).
    private int _observingMainDevice;

    private System.Timers.Timer? _keepCheckAndRemoveTimer;

    /// <summary>
    /// Max queued device infos before dropping the oldest one
    /// </summary>
    private const int MaxQueuedDeviceInfos = 1024;

    /// <summary>
    /// Event raised when a device is discovered
    /// </summary>
    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Event raised when a device goes offline
    /// </summary>
    public event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;

    /// <summary>
    /// Creates a new devices organizer with dependency injection
    /// </summary>
    public DevicesOrganizer(IConfigService configService, IEventService eventService, IDeviceDiscoveryService deviceDiscoveryService, IDeviceKeyService deviceKeyService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _deviceDiscoveryService = deviceDiscoveryService ?? throw new ArgumentNullException(nameof(deviceDiscoveryService));
        _deviceKeyService = deviceKeyService ?? throw new ArgumentNullException(nameof(deviceKeyService));
        Initialize();
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
        _deviceDiscoveryService.DeviceDiscovered += (_, args) =>
        {
            if (args.DeviceInfo is null) return;

            // Bounded queue: drop the oldest entries if the queue grows too large
            while (_deviceInfosQueue.Count >= MaxQueuedDeviceInfos)
            {
                if (!_deviceInfosQueue.TryDequeue(out DeviceInfo _))
                    break;
            }

            _deviceInfosQueue.Enqueue(args.DeviceInfo);

            lock (_receivedDeviceInfo4WatchLock)
            {
                _receivedDeviceInfo4Watch?.Add(args.DeviceInfo);
            }

            // Check for main device changes
            if (args.DeviceInfo.IsMainDevice && args.DeviceInfo.DevicesServerBuildTime < ConstantTable.ServerBuildTime)
            {
                // Only authorized devices may claim the main device role.
                // A forged IsMainDevice broadcast from an unauthorized device
                // must not make this machine yield its main device identity.
                if (!_deviceKeyService.IsDeviceAuthorized(args.DeviceInfo.Device))
                {
                    Log.Debug(
                        $"In {nameof(DevicesOrganizer)}.{nameof(InitEvents)}: " +
                        $"Ignoring main device claim from unauthorized device {args.DeviceInfo.Device.IPv4}:{args.DeviceInfo.DevicesServerPort}."
                    );
                }
                else
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
            }
        };
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

        _keepCheckAndRemoveTimer = timer;

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

        while (_deviceInfosQueue.TryDequeue(out var info))
        {
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

        // C-10: only one observation thread at a time. The old implementation restarted
        // itself recursively on failure (leaking a thread per failure) and could be invoked
        // again from the DeviceDiscovered handler while an earlier pass was still running,
        // letting two threads fight over _receivedDeviceInfo4Watch.
        if (Interlocked.CompareExchange(ref _observingMainDevice, 1, 0) != 0)
            return;

        new Thread(() =>
        {
            try
            {
                // C-10: retry loop replaces the recursive restart — on failure, wait and
                // retry on this same thread instead of spawning a new one.
                while (!token.IsCancellationRequested)
                {
                    _receivedDeviceInfo4Watch = [];

                    var checkedTime = 0;
                    var hadMainDevice = false;
                    var earliestBuiltServerTime = DateTime.UtcNow;
                    var serverPort = 0;
                    var serverAddress = string.Empty;

                    try
                    {
                        while (checkedTime < 7 && token.IsCancellationRequested == false)
                        {
                            if (_receivedDeviceInfo4Watch is null)
                                continue;

                            lock (_receivedDeviceInfo4WatchLock)
                            {
                                foreach (var item in _receivedDeviceInfo4Watch)
                                {
                                    // Only authorized devices may participate in the main device decision.
                                    // Forged IsMainDevice broadcasts from unauthorized devices must not
                                    // contribute to hadMainDevice / earliestBuiltServerTime, and must not
                                    // redirect MainMachineAddress / MainMachinePort.
                                    if (!_deviceKeyService.IsDeviceAuthorized(item.Device))
                                    {
                                        if (item.IsMainDevice)
                                        {
                                            Log.Debug(
                                                $"In {location}: Ignoring main device claim from unauthorized device " +
                                                $"{item.Device.IPv4}:{item.DevicesServerPort}."
                                            );
                                        }

                                        continue;
                                    }

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

                            // Dedicated observation thread — a blocking sleep is intentional
                            // (C-10: keep, converting to Task.Delay would require async plumbing
                            // for no benefit on this long-lived thread).
                            Thread.Sleep(1 * 1000); // Sleep 1 second
                        }

                        // C-10: observation pass completed — fall through and start the
                        // next 7-second pass (the outer while is the retry/continuous loop;
                        // an early `break` here would exit it and kill the observer).
                    }
                    catch (Exception e)
                    {
                        _receivedDeviceInfo4Watch?.Clear();
                        _receivedDeviceInfo4Watch = null;

                        Log.Error(e, $"In {location}: {e.Message} Rewatch.");

                        // Retry on this thread after a brief pause (was: recursive ObserveMainDevice()).
                        Thread.Sleep(1 * 1000);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _observingMainDevice, 0);
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

    /// <summary>
    /// C-10: releases the resident check-and-remove timer. Registered as a singleton in DI;
    /// the container disposes it on shutdown. (The main-device observation thread is
    /// short-lived and needs no disposal.)
    /// </summary>
    public void Dispose()
    {
        _keepCheckAndRemoveTimer?.Stop();
        _keepCheckAndRemoveTimer?.Dispose();
        _keepCheckAndRemoveTimer = null;
        GC.SuppressFinalize(this);
    }
}
