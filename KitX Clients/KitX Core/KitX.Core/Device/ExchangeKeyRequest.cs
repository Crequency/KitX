namespace KitX.Core.Device;

/// <summary>
/// Exchange key request model
/// </summary>
public class ExchangeKeyRequest
{
    /// <summary>
    /// AES encrypted device key
    /// </summary>
    public string? DeviceKey { get; set; }

    /// <summary>
    /// Address of requesting device
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// SHA1 of verification code
    /// </summary>
    public string? VerifyCodeSHA1 { get; set; }
}