using KitX.Core;
using KitX.Core.Configuration;
using KitX.Core.Device;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// 设备信任收紧的测试（DevicesOrganizer 主设备让位逻辑）。
/// 覆盖：未授权设备的伪造 IsMainDevice 广播不生效；已授权设备的主设备声明才触发让位。
/// </summary>
public class DevicesOrganizerTrustTests : IDisposable
{
    private readonly bool _originalIsMainMachine;
    private readonly DateTime _originalServerBuildTime;
    private readonly string? _originalMainMachineAddress;
    private readonly int _originalMainMachinePort;

    public DevicesOrganizerTrustTests()
    {
        _originalIsMainMachine = ConstantTable.IsMainMachine;
        _originalServerBuildTime = ConstantTable.ServerBuildTime;
        _originalMainMachineAddress = ConstantTable.MainMachineAddress;
        _originalMainMachinePort = ConstantTable.MainMachinePort;
    }

    public void Dispose()
    {
        ConstantTable.IsMainMachine = _originalIsMainMachine;
        ConstantTable.ServerBuildTime = _originalServerBuildTime;
        ConstantTable.MainMachineAddress = _originalMainMachineAddress;
        ConstantTable.MainMachinePort = _originalMainMachinePort;
    }

    [Fact]
    public void ForgedMainDeviceClaim_FromUnauthorizedDevice_DoesNotYield()
    {
        ConstantTable.ServerBuildTime = DateTime.UtcNow;
        ConstantTable.IsMainMachine = true;
        ConstantTable.MainMachineAddress = null;
        ConstantTable.MainMachinePort = -1;

        var (organizer, discovery, keys) = CreateOrganizer(authorized: false);
        _ = organizer;
        _ = keys;

        discovery.RaiseDeviceDiscovered(ForgedMainDeviceInfo());

        Assert.True(ConstantTable.IsMainMachine);
        Assert.Null(ConstantTable.MainMachineAddress);
        Assert.Equal(-1, ConstantTable.MainMachinePort);
    }

    [Fact]
    public void MainDeviceClaim_FromAuthorizedDevice_Yields()
    {
        ConstantTable.ServerBuildTime = DateTime.UtcNow;
        ConstantTable.IsMainMachine = true;

        var (organizer, discovery, keys) = CreateOrganizer(authorized: true);
        _ = organizer;
        _ = keys;

        discovery.RaiseDeviceDiscovered(ForgedMainDeviceInfo());

        Assert.False(ConstantTable.IsMainMachine);
    }

    private static (DevicesOrganizer Organizer, FakeDeviceDiscoveryService Discovery, FakeDeviceKeyService Keys)
        CreateOrganizer(bool authorized)
    {
        var config = new FakeConfigService();
        ((AppConfig)config.AppConfig).Web.DevicesViewRefreshDelay = 3_600_000;

        var discovery = new FakeDeviceDiscoveryService();
        var keys = new FakeDeviceKeyService { Authorized = authorized };
        var organizer = new DevicesOrganizer(config, new FakeEventService(), discovery, keys);
        return (organizer, discovery, keys);
    }

    private static DeviceInfo ForgedMainDeviceInfo() => new()
    {
        Device = new DeviceLocator
        {
            DeviceName = "forged-device",
            IPv4 = "192.168.1.100",
            MacAddress = "AA-BB-CC-DD-EE-FF"
        },
        IsMainDevice = true,
        DevicesServerBuildTime = DateTime.UtcNow.AddHours(-1),
        DevicesServerPort = 8888,
        SendTime = DateTime.UtcNow
    };
}
