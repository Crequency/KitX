using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Network helper for device discovery and network operations
/// Phase 5: Simplified version without Dashboard dependencies
/// </summary>
internal static class NetworkHelper
{
    /// <summary>
    /// Gets the local IPv4 address (excluding Docker and virtual interfaces)
    /// </summary>
    /// <returns>IPv4 address or empty string if not found</returns>
    internal static string GetInterNetworkIPv4()
    {
        const string location = $"{nameof(NetworkHelper)}.{nameof(GetInterNetworkIPv4)}";

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var search = host.AddressList
                .Where(ip =>
                    ip.AddressFamily == AddressFamily.InterNetwork &&
                    !ip.ToString().Equals("127.0.0.1") &&
                    IsInterNetworkAddressV4(ip) &&
                    !IsExcludedNetworkInterface(ip))
                .FirstOrDefault();

            var result = search?.ToString();

            return result ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Checks if the IP belongs to an excluded network interface (e.g., Docker)
    /// </summary>
    private static bool IsExcludedNetworkInterface(IPAddress ip)
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up);

            foreach (var nic in nics)
            {
                var description = nic.Description.ToLowerInvariant();
                // Exclude Docker, veth (virtual ethernet), Hyper-V, etc.
                if (description.Contains("docker") ||
                    description.Contains("veth") ||
                    description.Contains("hyper-v") ||
                    description.Contains("virtual"))
                {
                    var addresses = nic.GetIPProperties().UnicastAddresses;
                    if (addresses.Any(a => a.Address.ToString() == ip.ToString()))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // If we can't determine, don't exclude
        }
        return false;
    }

    /// <summary>
    /// Gets the local IPv6 address
    /// </summary>
    /// <returns>IPv6 address or empty string if not found</returns>
    internal static string GetInterNetworkIPv6()
    {
        const string location = $"{nameof(NetworkHelper)}.{nameof(GetInterNetworkIPv6)}";

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var search = host.AddressList
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.ToString().Equals("::1"))
                .FirstOrDefault();

            var result = search?.ToString();

            return result ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Tries to get the device MAC address
    /// </summary>
    /// <returns>MAC address or null if not found</returns>
    internal static string? TryGetDeviceMacAddress()
    {
        const string location = $"{nameof(NetworkHelper)}.{nameof(TryGetDeviceMacAddress)}";

        try
        {
            var ipv4 = GetInterNetworkIPv4();

            if (string.IsNullOrEmpty(ipv4))
                return null;

            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                    n.GetIPProperties().UnicastAddresses.Any(x => x.Address.ToString() == ipv4));

            var result = nic?.GetPhysicalAddress().ToString();

            // Format MAC address with colons (e.g., "AA:BB:CC:DD:EE:FF")
            if (!string.IsNullOrEmpty(result) && result.Length == 12)
            {
                return string.Join(":", result.Chunk(2).Select(c => new string(c)));
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"In {location}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Tries to get the OS version string
    /// </summary>
    /// <returns>OS version string or default if not found</returns>
    internal static string? TryGetOsVersionString()
    {
        const string location = $"{nameof(NetworkHelper)}.{nameof(TryGetOsVersionString)}";

        var result = Environment.OSVersion.VersionString;

        try
        {
            var osType = OperatingSystemHelper.GetOSType();

            // For now, return the basic OS version string
            // TODO: Implement Linux/MacOS specific version detection if needed
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Gets device information for discovery
    /// </summary>
    /// <returns>Device information</returns>
    internal static DeviceInfo GetDeviceInfo()
    {
        var osType = OperatingSystemHelper.GetOSType();

        return new DeviceInfo
        {
            Device = new DeviceLocator
            {
                DeviceName = Environment.MachineName,
                MacAddress = TryGetDeviceMacAddress() ?? "",
                IPv4 = GetInterNetworkIPv4(),
                IPv6 = GetInterNetworkIPv6(),
            },
            IsMainDevice = false, // Will be set by DevicesOrganizer
            SendTime = DateTime.UtcNow,
            DeviceOSType = osType,
            DeviceOSVersion = TryGetOsVersionString() ?? "",
            PluginsServerPort = 7777, // Default port
            DevicesServerPort = 8888,  // Default port
            DevicesServerBuildTime = DateTime.Now,
            PluginsCount = 0, // Will be updated from PluginsManager
        };
    }

    /// <summary>
    /// Checks if an IP address is an internal/private network address
    /// </summary>
    /// <param name="address">IP address to check</param>
    /// <returns>True if it's an internal network address</returns>
    private static bool IsInterNetworkAddressV4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            10 => true,                           // 10.0.0.0/8
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true, // 172.16.0.0/12
            192 when bytes[1] == 168 => true,     // 192.168.0.0/16
            _ => false,
        };
    }
}
