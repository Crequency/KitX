using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Test.Xunit.Fakes;

/// <summary>
/// 最小 IDeviceKeyService 实现：IsDeviceAuthorized 结果可通过 Authorized 字段配置，
/// 其余成员返回默认值。
/// </summary>
public class FakeDeviceKeyService : IDeviceKeyService
{
    public volatile bool Authorized;

    public IReadOnlyList<IDeviceKey> GetDeviceKeys() => Array.Empty<IDeviceKey>();

    public bool AddDeviceKey(string macAddress, string deviceName, string publicKey) => false;

    public bool RemoveDeviceKey(string macAddress) => false;

    public DeviceKey? SearchDeviceKey(DeviceLocator locator) => null;

    public bool IsDeviceKeyCorrect(DeviceLocator locator, DeviceKey key) => false;

    public bool IsDeviceAuthorized(DeviceLocator device) => Authorized;

    public DeviceKey? GetPrivateDeviceKey() => null;
}
