using System.Security.Cryptography;
using KitX.Core.Security;
using KitX.Core.Test.Xunit.Fakes;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// SecurityManager.AesEncrypt / AesDecrypt 的测试。
/// 覆盖：往返一致性、每次加密随机性（salt/IV）、错误密钥拒绝、短密文拒绝、空内容往返。
/// </summary>
public class AesSecurityTests : IClassFixture<AesSecurityTests.Fixture>
{
    private readonly SecurityManager _manager;

    public AesSecurityTests(Fixture fixture) => _manager = fixture.Manager;

    [Fact]
    public void EncryptThenDecrypt_RestoresOriginal()
    {
        const string content = "KitX AES round trip 中文内容 #123";

        var encrypted = _manager.AesEncrypt(content, "test-key");

        Assert.NotEmpty(encrypted);
        Assert.Equal(content, _manager.AesDecrypt(encrypted, "test-key"));
    }

    [Fact]
    public void Encrypt_SameInputProducesDifferentCiphertext()
    {
        var first = _manager.AesEncrypt("same input", "same-key");
        var second = _manager.AesEncrypt("same input", "same-key");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        var encrypted = _manager.AesEncrypt("secret content", "correct-key");

        Assert.ThrowsAny<CryptographicException>(() => _manager.AesDecrypt(encrypted, "wrong-key"));
    }

    [Fact]
    public void Decrypt_TooShortCiphertext_ThrowsCryptographicException()
    {
        // 合法 Base64，但解码后仅 16 字节（< 32 字节的 salt + IV 下限）
        var shortCiphertext = Convert.ToBase64String(new byte[16]);

        Assert.ThrowsAny<CryptographicException>(() => _manager.AesDecrypt(shortCiphertext, "any-key"));
    }

    [Fact]
    public void EncryptThenDecrypt_EmptyContent_RoundTrips()
    {
        var encrypted = _manager.AesEncrypt("", "test-key");

        Assert.Equal("", _manager.AesDecrypt(encrypted, "test-key"));
    }

    public sealed class Fixture : IDisposable
    {
        public SecurityManager Manager { get; } = new(new FakeConfigService(), new FakeDeviceDiscoveryService());

        public void Dispose() => Manager.Dispose();
    }
}
