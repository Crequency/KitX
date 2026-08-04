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
    /// <remarks>
    /// 已废弃：保留仅为协议兼容（旧客户端会发送该字段），服务端不再校验它。
    /// 密钥交换的安全校验改由用户在接收端输入临时密码完成。
    /// </remarks>
    [Obsolete("Kept for protocol compatibility only. Server no longer validates this field.")]
    public string? VerifyCodeSHA1 { get; set; }
}