using KitX.Core.Contract.Device;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Test.Xunit.Fakes;

/// <summary>
/// 最小 IDeviceDiscoveryService 实现：可手动触发 DeviceDiscovered 事件，
/// Run/Stop 为空操作。
/// </summary>
public class FakeDeviceDiscoveryService : IDeviceDiscoveryService
{
    public DeviceInfo DefaultDeviceInfo { get; set; } = new()
    {
        Device = new DeviceLocator
        {
            DeviceName = "test-machine",
            MacAddress = "00-11-22-33-44-55"
        }
    };

    public int? Port { get; set; }

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

#pragma warning disable CS0067
    public event EventHandler<DeviceOfflineEventArgs>? DeviceOffline;
#pragma warning restore CS0067

    public IDeviceDiscoveryService Run() => this;

    public void Stop() { }

    public void RaiseDeviceDiscovered(DeviceInfo info) =>
        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs { DeviceInfo = info });
}
