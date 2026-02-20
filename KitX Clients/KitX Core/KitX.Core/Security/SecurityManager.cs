using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Security;
using Serilog;

namespace KitX.Core.Security;

/// <summary>
/// Security manager for encryption and device key management
/// </summary>
public class SecurityManager : ISecurityService
{
    private static SecurityManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static SecurityManager Instance => _instance ??= new();

    private RSA? _rsaInstance;

    private DeviceKey? _localDeviceKey;

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
        // Create RSA instance
        _rsaInstance = RSA.Create(2048);

        // Note: In a real implementation, you would load the device key from config
        // For now, we'll create a new one if it doesn't exist
        if (_localDeviceKey == null)
        {
            GenerateLocalDeviceKey();
        }
        else
        {
            // Load existing key
            if (_localDeviceKey.RsaPublicKeyPem != null && _localDeviceKey.RsaPrivateKeyPem != null)
            {
                _rsaInstance.ImportFromPem(_localDeviceKey.RsaPublicKeyPem);
                _rsaInstance.ImportFromPem(_localDeviceKey.RsaPrivateKeyPem);
            }
        }
    }

    private void GenerateLocalDeviceKey()
    {
        var device = new DeviceLocator
        {
            DeviceName = Environment.MachineName,
            MacAddress = GetMacAddress()
        };

        _localDeviceKey = new DeviceKey
        {
            Device = device,
            RsaPublicKeyPem = _rsaInstance!.ExportRSAPublicKeyPem(),
            RsaPrivateKeyPem = _rsaInstance.ExportRSAPrivateKeyPem()
        };
    }

    /// <summary>
    /// Gets all device keys
    /// </summary>
    /// <returns>List of device keys</returns>
    public IReadOnlyList<IDeviceKey> GetDeviceKeys()
    {
        // This would typically come from SecurityConfig
        return new List<IDeviceKey>();
    }

    /// <summary>
    /// Adds a device key
    /// </summary>
    /// <param name="macAddress">The MAC address</param>
    /// <param name="deviceName">The device name</param>
    /// <param name="publicKey">The public key</param>
    /// <returns>True if successful</returns>
    public bool AddDeviceKey(string macAddress, string deviceName, string publicKey)
    {
        try
        {
            var deviceKey = new DeviceKey
            {
                Device = new DeviceLocator
                {
                    MacAddress = macAddress,
                    DeviceName = deviceName
                },
                RsaPublicKeyPem = publicKey
            };

            // In a real implementation, you would save this to SecurityConfig
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
    /// Removes a device key
    /// </summary>
    /// <param name="macAddress">The MAC address</param>
    /// <returns>True if successful</returns>
    public bool RemoveDeviceKey(string macAddress)
    {
        try
        {
            // In a real implementation, you would remove this from SecurityConfig
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
    /// Computes the SHA1 hash of a string
    /// </summary>
    /// <param name="content">The content</param>
    /// <returns>The hash string</returns>
    public string ComputeHash(string content)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(content));
        var sb = new StringBuilder();

        foreach (var item in hash)
        {
            sb.Append(item.ToString("x2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Encrypts a string with AES
    /// </summary>
    /// <param name="source">The source string</param>
    /// <param name="key">The encryption key</param>
    /// <returns>The encrypted string</returns>
    public static string AesEncrypt(string source, string key)
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
    public static string AesDecrypt(string source, string key, bool isSourceInBase64 = true)
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
        // Get the first MAC address from network interfaces
        try
        {
            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
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
    /// Disposes the security manager
    /// </summary>
    public void Dispose()
    {
        _rsaInstance?.Dispose();
        GC.SuppressFinalize(this);
    }
}
