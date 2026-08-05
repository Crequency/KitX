using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KitX.Core.Security;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Security;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// SecurityManager.EncryptStringAsync 的测试（C-1 回归）。
/// 覆盖：RSA-only 分支必须使用目标设备公钥（而非本机公钥）、长度判定基于
/// UTF-8 字节数（中文多字节内容不越界）、混用分支仍可解密。
/// </summary>
public class EncryptStringAsyncSecurityTests : IClassFixture<EncryptStringAsyncSecurityTests.Fixture>
{
    private readonly SecurityManager _manager;
    private readonly Fixture _fixture;

    public EncryptStringAsyncSecurityTests(Fixture fixture)
    {
        _manager = fixture.Manager;
        _fixture = fixture;
    }

    [Fact]
    public async Task ShortContent_IsEncryptedWithTargetPublicKey()
    {
        const string content = "hello kitx";

        var encrypted = await _manager.EncryptStringAsync(content, Fixture.TargetMacAddress);

        // RSA-only 分支：flag 0
        Assert.Equal(0, Convert.FromBase64String(encrypted)[0]);

        // 目标私钥必须能解密 —— 这是 C-1 的核心回归（修复前用本机公钥，目标私钥解不开）
        var decrypted = Decrypt(encrypted, _fixture.TargetPrivateKeyPem);
        Assert.Equal(content, decrypted);
    }

    [Fact]
    public async Task ShortAsciiAtBoundary_89Chars_IsRsaOnly()
    {
        // 89 个 ASCII 字节 < 90 → RSA-only
        var content = new string('a', 89);

        var encrypted = await _manager.EncryptStringAsync(content, Fixture.TargetMacAddress);

        Assert.Equal(0, Convert.FromBase64String(encrypted)[0]);
        Assert.Equal(content, Decrypt(encrypted, _fixture.TargetPrivateKeyPem));
    }

    [Fact]
    public async Task ShortAsciiAtBoundary_90Chars_IsHybrid()
    {
        // 90 个 ASCII 字节 >= 90 → 混用分支
        var content = new string('b', 90);

        var encrypted = await _manager.EncryptStringAsync(content, Fixture.TargetMacAddress);

        Assert.Equal(1, Convert.FromBase64String(encrypted)[0]);
        Assert.Equal(content, Decrypt(encrypted, _fixture.TargetPrivateKeyPem));
    }

    [Fact]
    public async Task ChineseShortContent_ByteBasedBranchSelection_IsRsaOnly()
    {
        // 29 个中文字符 = 87 UTF-8 字节 < 90 → RSA-only（按字符数 29 远小于 90，
        // 但按旧判定两种方式都会走 RSA-only；关键是不能因字节数超 OAEP 上限而抛异常）
        var content = new string('中', 29);

        var encrypted = await _manager.EncryptStringAsync(content, Fixture.TargetMacAddress);

        Assert.Equal(0, Convert.FromBase64String(encrypted)[0]);
        Assert.Equal(content, Decrypt(encrypted, _fixture.TargetPrivateKeyPem));
    }

    [Fact]
    public async Task ChineseBoundary_30Chars_90Bytes_IsHybrid()
    {
        // 30 个中文字符 = 90 字节 >= 90 → 混用分支
        var content = new string('国', 30);

        var encrypted = await _manager.EncryptStringAsync(content, Fixture.TargetMacAddress);

        Assert.Equal(1, Convert.FromBase64String(encrypted)[0]);
        Assert.Equal(content, Decrypt(encrypted, _fixture.TargetPrivateKeyPem));
    }

    [Fact]
    public async Task ChineseLongContent_DoesNotOverflowRsaKey()
    {
        // 89 个中文字符 ≈ 267 字节，远超 2048-bit RSA-OAEP-SHA256 的 190 字节上限。
        // 旧实现按字符数判定会误入 RSA-only 分支并抛 CryptographicException。
        var content = new string('测', 89);

        var encrypted = await _manager.EncryptStringAsync(content, Fixture.TargetMacAddress);

        Assert.Equal(1, Convert.FromBase64String(encrypted)[0]);
        Assert.Equal(content, Decrypt(encrypted, _fixture.TargetPrivateKeyPem));
    }

    [Fact]
    public async Task UnknownTargetMac_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _manager.EncryptStringAsync("hello", "00:00:00:00:00:99"));
    }

    /// <summary>
    /// 按输出 flag 分派解密：0=RSA-only（目标私钥直接解密），1=Hybrid（RSA+AES）。
    /// </summary>
    private string Decrypt(string encrypted, string targetPrivateKeyPem)
    {
        var bytes = Convert.FromBase64String(encrypted);
        Assert.True(bytes.Length > 1);
        var flag = bytes[0];
        var payload = bytes[1..];

        if (flag == 0)
        {
            using var rsa = RSA.Create(2048);
            rsa.ImportFromPem(targetPrivateKeyPem);
            return Encoding.UTF8.GetString(rsa.Decrypt(payload, RSAEncryptionPadding.OaepSHA256));
        }

        var json = Encoding.UTF8.GetString(payload);
        var encryptedContent = JsonSerializer.Deserialize<EncryptedContent>(json)
            ?? throw new InvalidOperationException("Failed to deserialize encrypted content");
        var key = new DeviceKey { RsaPrivateKeyPem = targetPrivateKeyPem };
        return _manager.RsaDecryptContent(key, encryptedContent);
    }

    public sealed class Fixture : IDisposable
    {
        public const string TargetMacAddress = "AA-BB-CC-DD-EE-01";

        public SecurityManager Manager { get; }

        public string TargetPrivateKeyPem { get; }

        public Fixture()
        {
            Manager = new SecurityManager(new FakeConfigService(), new FakeDeviceDiscoveryService());

            // 生成"目标设备"的 RSA 密钥对，把公钥注册进 SecurityConfig
            using var rsa = RSA.Create(2048);
            TargetPrivateKeyPem = rsa.ExportRSAPrivateKeyPem();
            var publicKeyPem = rsa.ExportRSAPublicKeyPem();

            var added = Manager.AddDeviceKey(TargetMacAddress, "TestTarget", publicKeyPem);
            Assert.True(added);
        }

        public void Dispose() => Manager.Dispose();
    }
}
