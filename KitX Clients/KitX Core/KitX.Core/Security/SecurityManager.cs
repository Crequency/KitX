using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KitX.Core.Contract.Security;
using KitX.Core.Configuration;
using KitX.Core.Device;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Security;
using Serilog;

namespace KitX.Core.Security;

/// <summary>
/// Security manager for encryption and device key management
/// </summary>
public class SecurityManager : IDeviceKeyService, IEncryptionService
{
    private static SecurityManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static SecurityManager Instance => _instance ??= new();

    private RSA? _rsaInstance;

    private DeviceKey? _localDeviceKey;

    /// <summary>
    /// Reference to ConfigManager instance
    /// </summary>
    private ConfigManager? _configManager;

    /// <summary>
    /// Gets typed SecurityConfig for direct property access
    /// </summary>
    private SecurityConfig? TypedSecurityConfig => _configManager?.TypedSecurityConfig;

    /// <summary>
    /// Gets the local device key
    /// </summary>
    public DeviceKey? LocalDeviceKey
    {
        get => _localDeviceKey;
        set => _localDeviceKey = value;
    }

    /// <summary>
    /// Private constructor
    /// </summary>
    private SecurityManager()
    {
        Initialize();
    }

    /// <summary>
    /// Initializes the security manager
    /// </summary>
    private void Initialize()
    {
        // Get ConfigManager instance
        _configManager = ConfigManager.Instance;

        // Create RSA instance
        _rsaInstance = RSA.Create(2048);

        // Get current device info from DevicesDiscoveryServer (same as legacy architecture)
        var defaultDeviceInfo = DevicesDiscoveryServer.Instance?.DefaultDeviceInfo;
        var currentDevice = defaultDeviceInfo?.Device ?? new DeviceLocator
        {
            DeviceName = Environment.MachineName,
            MacAddress = GetMacAddress()
        };

        Log.Information("Initializing SecurityManager with device: {DeviceName}, {MacAddress}",
            currentDevice.DeviceName, currentDevice.MacAddress);

        // Get typed SecurityConfig
        var typedSecurityConfig = _configManager.SecurityConfig as SecurityConfig;

        // Try to find existing device key in SecurityConfig
        var existingKey = typedSecurityConfig?.DeviceKeys
            .FirstOrDefault(x => x.Device.IsSameDevice(currentDevice));

        if (existingKey != null)
        {
            // Found existing key
            _localDeviceKey = new DeviceKey
            {
                Device = existingKey.Device,
                RsaPublicKeyPem = existingKey.RsaPublicKeyPem,
                RsaPrivateKeyPem = existingKey.RsaPrivateKeyPem
            };

            // Load RSA instance with existing keys
            if (!string.IsNullOrEmpty(_localDeviceKey.RsaPublicKeyPem) &&
                !string.IsNullOrEmpty(_localDeviceKey.RsaPrivateKeyPem))
            {
                _rsaInstance.ImportFromPem(_localDeviceKey.RsaPublicKeyPem);
                _rsaInstance.ImportFromPem(_localDeviceKey.RsaPrivateKeyPem);
                Log.Information("Loaded existing local device key from config");
            }
            else
            {
                // Keys incomplete, regenerate
                Log.Warning("Existing device key incomplete, regenerating...");
                GenerateLocalDeviceKey();
            }
        }
        else
        {
            // No existing key, generate new one
            Log.Information("No existing device key found, generating new one...");
            GenerateLocalDeviceKey();
        }
    }

    private void GenerateLocalDeviceKey()
    {
        // Get current network addresses
        var ipv4 = NetworkHelper.GetInterNetworkIPv4();
        var ipv6 = NetworkHelper.GetInterNetworkIPv6();

        Log.Information("Generating local device key. IPv4: {IPv4}, IPv6: {IPv6}", ipv4, ipv6);

        var device = new DeviceLocator
        {
            DeviceName = Environment.MachineName,
            MacAddress = GetMacAddress(),
            IPv4 = ipv4 ?? "",
            IPv6 = ipv6 ?? ""
        };

        Log.Information("Device created. DeviceName: {DeviceName}, MacAddress: {MacAddress}, IPv4: {IPv4}, IPv6: {IPv6}",
            device.DeviceName, device.MacAddress, device.IPv4, device.IPv6);

        _localDeviceKey = new DeviceKey
        {
            Device = device,
            RsaPublicKeyPem = _rsaInstance!.ExportRSAPublicKeyPem(),
            RsaPrivateKeyPem = _rsaInstance.ExportRSAPrivateKeyPem()
        };

        // Add to SecurityConfig and save
        var deviceKeyImpl = new DeviceKeyImpl
        {
            Device = device,
            RsaPublicKeyPem = _localDeviceKey.RsaPublicKeyPem,
            RsaPrivateKeyPem = _localDeviceKey.RsaPrivateKeyPem,
            AddedAt = DateTime.Now
        };

        _configManager?.TypedSecurityConfig?.DeviceKeys.Add(deviceKeyImpl);
        _configManager?.SaveAll();

        Log.Information($"Generated and saved new local device key. Keys count: {_configManager?.TypedSecurityConfig?.DeviceKeys.Count}");
    }

    /// <summary>
    /// Gets all device keys from SecurityConfig
    /// </summary>
    /// <returns>List of device keys</returns>
    public IReadOnlyList<Contract.Configuration.IDeviceKey> GetDeviceKeys()
    {
        var keys = _configManager?.TypedSecurityConfig?.DeviceKeys;
        if (keys == null)
            return new List<Contract.Configuration.IDeviceKey>();

        // Return the device keys directly
        return keys.ToList();
    }

    /// <summary>
    /// Adds a device key to SecurityConfig
    /// </summary>
    /// <param name="macAddress">The MAC address</param>
    /// <param name="deviceName">The device name</param>
    /// <param name="publicKey">The public key</param>
    /// <returns>True if successful</returns>
    public bool AddDeviceKey(string macAddress, string deviceName, string publicKey)
    {
        try
        {
            var deviceKey = new DeviceKeyImpl
            {
                Device = new DeviceLocator
                {
                    MacAddress = macAddress,
                    DeviceName = deviceName
                },
                RsaPublicKeyPem = publicKey,
                AddedAt = DateTime.Now
            };

            _configManager?.TypedSecurityConfig?.DeviceKeys.Add(deviceKey);
            _configManager?.SaveAll();

            Log.Information($"Added device key for {deviceName} ({macAddress})");

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error adding device key: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a device key from SecurityConfig
    /// </summary>
    /// <param name="macAddress">The MAC address</param>
    /// <returns>True if successful</returns>
    public bool RemoveDeviceKey(string macAddress)
    {
        try
        {
            var typedSecurityConfig = _configManager?.SecurityConfig as SecurityConfig;
            var keysToRemove = typedSecurityConfig?.DeviceKeys
                .Where(x => IsSameDevice(x.Device.MacAddress, macAddress))
                .ToList();

            if (keysToRemove != null)
            {
                foreach (var key in keysToRemove)
                {
                    _configManager?.TypedSecurityConfig?.DeviceKeys.Remove(key);
                }
                _configManager?.SaveAll();
            }

            Log.Information($"Removed device key for {macAddress}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error removing device key: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the private device key for local device
    /// </summary>
    /// <returns>The private device key, or null if not available</returns>
    public DeviceKey? GetPrivateDeviceKey()
    {
        return _localDeviceKey is null
            ? null
            : new DeviceKey
            {
                Device = _localDeviceKey.Device,
                RsaPrivateKeyPem = _localDeviceKey.RsaPrivateKeyPem
            };
    }

    /// <summary>
    /// Checks if a device is authorized
    /// </summary>
    /// <param name="device">The device locator</param>
    /// <returns>True if the device is authorized</returns>
    public bool IsDeviceAuthorized(DeviceLocator device)
    {
        try
        {
            var deviceKeys = GetDeviceKeys();
            return deviceKeys.Any(x => IsSameDevice(x.MacAddress, device.MacAddress));
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error checking device authorization: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if two MAC addresses represent the same device
    /// </summary>
    private static bool IsSameDevice(string mac1, string mac2)
    {
        // Normalize MAC addresses for comparison
        var normalized1 = mac1.Replace(":", "").Replace("-", "").ToUpperInvariant();
        var normalized2 = mac2.Replace(":", "").Replace("-", "").ToUpperInvariant();
        return normalized1 == normalized2;
    }

    /// <summary>
    /// Encrypts a string
    /// </summary>
    /// <param name="content">The content to encrypt</param>
    /// <param name="targetDeviceMacAddress">The target device MAC address</param>
    /// <returns>The encrypted string</returns>
    public async Task<string> EncryptStringAsync(string content, string targetDeviceMacAddress)
    {
        if (_rsaInstance == null)
        {
            throw new InvalidOperationException("RSA instance not initialized");
        }

        if (content.Length >= 90)
        {
            // TODO: Implement data splitting for longer content
            Log.Warning("Data length is too long for RSA encryption");
        }

        try
        {
            var dataBytes = Encoding.UTF8.GetBytes(content);
            var encrypted = _rsaInstance.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error encrypting string: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Decrypts a string
    /// </summary>
    /// <param name="encryptedContent">The encrypted content</param>
    /// <param name="sourceDeviceMacAddress">The source device MAC address</param>
    /// <returns>The decrypted string</returns>
    public async Task<string> DecryptStringAsync(string encryptedContent, string sourceDeviceMacAddress)
    {
        if (_rsaInstance == null)
        {
            throw new InvalidOperationException("RSA instance not initialized");
        }

        if (encryptedContent.Length >= 90)
        {
            // TODO: Implement data splitting for longer content
            Log.Warning("Data length is too long for RSA decryption");
        }

        try
        {
            var encryptedDataBytes = Convert.FromBase64String(encryptedContent);
            var decrypted = _rsaInstance.Decrypt(encryptedDataBytes, RSAEncryptionPadding.OaepSHA256);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error decrypting string: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Computes SHA1 hash of a string
    /// </summary>
    /// <param name="data">The data to hash</param>
    /// <returns>The SHA1 hash string</returns>
    public string GetSHA1(string data)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(data));
        var sb = new StringBuilder();

        foreach (var item in hash)
        {
            sb.Append(item.ToString("x2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Searches for a device key by device locator (instance method for interface)
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>The device key if found, otherwise null</returns>
    public DeviceKey? SearchDeviceKey(DeviceLocator locator)
    {
        var existing = _configManager?.TypedSecurityConfig?.DeviceKeys
            .FirstOrDefault(x => x.Device.IsSameDevice(locator));
        if (existing == null) return null;
        return new DeviceKey
        {
            Device = existing.Device,
            RsaPublicKeyPem = existing.RsaPublicKeyPem,
            RsaPrivateKeyPem = existing.RsaPrivateKeyPem
        };
    }

    /// <summary>
    /// Encrypts a string with AES
    /// </summary>
    /// <param name="source">The source string</param>
    /// <param name="key">The encryption key</param>
    /// <returns>The encrypted string</returns>
    public string AesEncrypt(string source, string key)
    {
        var data = Encoding.UTF8.GetBytes(source);
        var expandedKey = ExpandKey(key, 16);
        var keyData = expandedKey;
        var iv = expandedKey;

        using var aes = Aes.Create();
        aes.Key = keyData;
        aes.IV = iv;

        var result = aes.EncryptCbc(data, iv, PaddingMode.ISO10126);
        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a string with AES
    /// </summary>
    /// <param name="source">The source string</param>
    /// <param name="key">The decryption key</param>
    /// <param name="isSourceInBase64">Whether the source is in Base64</param>
    /// <returns>The decrypted string</returns>
    public string AesDecrypt(string source, string key, bool isSourceInBase64 = true)
    {
        var data = isSourceInBase64 ? Convert.FromBase64String(source) : Encoding.UTF8.GetBytes(source);
        var expandedKey = ExpandKey(key, 16);
        var keyData = expandedKey;
        var iv = expandedKey;

        using var aes = Aes.Create();
        aes.Key = keyData;
        aes.IV = iv;

        var result = aes.DecryptCbc(data, iv, PaddingMode.ISO10126);
        return Encoding.UTF8.GetString(result);
    }

    private static byte[] ExpandKey(string key, int length)
    {
        var expandedKey = key.Length <= length ? key : key[..length];
        var expandIndex = 0;

        while (expandedKey.Length < length)
        {
            if (expandIndex == key.Length)
                expandIndex = 0;

            expandedKey += key[expandIndex];
            expandIndex++;
        }

        return Encoding.ASCII.GetBytes(expandedKey);
    }

    private string GetMacAddress()
    {
        // Get the MAC address of the network interface matching current IPv4 address
        try
        {
            var ipv4 = NetworkHelper.GetInterNetworkIPv4();
            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

            // First, try to find the interface matching the current IPv4 address
            foreach (var nic in nics)
            {
                if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                        || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211))
                {
                    var addr = nic.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(x => x.Address.ToString() == ipv4);
                    if (addr != null)
                    {
                        return nic.GetPhysicalAddress().ToString();
                    }
                }
            }

            // Fallback: return the first Up interface's MAC address
            foreach (var nic in nics)
            {
                if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    return nic.GetPhysicalAddress().ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting MAC address");
        }

        return "Unknown";
    }

    /// <summary>
    /// Checks if a device key is correct (instance method for interface)
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <param name="key">The device key to verify</param>
    /// <returns>True if the key is correct</returns>
    public bool IsDeviceKeyCorrect(DeviceLocator locator, DeviceKey key)
    {
        var existing = SearchDeviceKey(locator);
        if (existing is null) return false;
        return existing.IsSameKey(key);
    }

    /// <summary>
    /// Encrypts a string using RSA with a specific device's public key
    /// </summary>
    /// <param name="key">The device key containing the public key</param>
    /// <param name="data">The data to encrypt</param>
    /// <returns>The encrypted data as Base64 string</returns>
    public string? RsaEncryptString(DeviceKey key, string data)
    {
        if (data.Length >= 90)
            throw new ArgumentOutOfRangeException(nameof(data), "Data length is too long.");

        using var rsa = RSA.Create(2048);
        rsa.ImportFromPem(key.RsaPublicKeyPem);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var encrypted = rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts a string using RSA with a specific device's private key
    /// </summary>
    /// <param name="key">The device key containing the private key</param>
    /// <param name="encryptedData">The encrypted data as Base64 string</param>
    /// <returns>The decrypted data</returns>
    public string? RsaDecryptString(DeviceKey key, string encryptedData)
    {
        using var rsa = RSA.Create(2048);
        rsa.ImportFromPem(key.RsaPrivateKeyPem);
        var dataBytes = Convert.FromBase64String(encryptedData);
        var decrypted = rsa.Decrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);
        return Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>
    /// Encrypts content using RSA+AES hybrid encryption
    /// </summary>
    /// <param name="key">The device key</param>
    /// <param name="content">The content to encrypt</param>
    /// <returns>The encrypted content</returns>
    public EncryptedContent RsaEncryptContent(DeviceKey key, string content)
    {
        var aesKey = GenerateRandomKey(16);
        var encryptedAesKey = RsaEncryptString(key, aesKey);
        var encryptedContent = AesEncrypt(content, aesKey);
        return new EncryptedContent
        {
            Device = key.Device,
            RsaEncryptedAesKeyBase64 = encryptedAesKey,
            AesEncryptedContentBase64 = encryptedContent,
        };
    }

    /// <summary>
    /// Decrypts content using RSA+AES hybrid decryption
    /// </summary>
    /// <param name="key">The device key</param>
    /// <param name="content">The encrypted content</param>
    /// <returns>The decrypted content</returns>
    public string RsaDecryptContent(DeviceKey key, EncryptedContent content)
    {
        ArgumentNullException.ThrowIfNull(content.RsaEncryptedAesKeyBase64);
        ArgumentNullException.ThrowIfNull(content.AesEncryptedContentBase64);
        var aesKey = RsaDecryptString(key, content.RsaEncryptedAesKeyBase64);
        return AesDecrypt(content.AesEncryptedContentBase64, aesKey!);
    }

    /// <summary>
    /// Generates a random key for AES encryption
    /// </summary>
    /// <param name="length">The key length</param>
    /// <returns>The random key as string</returns>
    private static string GenerateRandomKey(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)[..length];
    }

    /// <summary>
    /// Disposes the security manager
    /// </summary>
    public void Dispose()
    {
        _rsaInstance?.Dispose();
        GC.SuppressFinalize(this);
    }
}
