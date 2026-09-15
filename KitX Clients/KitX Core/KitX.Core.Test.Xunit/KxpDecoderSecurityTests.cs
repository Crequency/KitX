using System.Security.Cryptography;
using System.Text;
using ExtensionsPackageDecoder = KitX.FileFormats.CSharp.ExtensionsPackage.Decoder;
using ExtensionsPackageEncoder = KitX.FileFormats.CSharp.ExtensionsPackage.Encoder;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// KXP 解包路径穿越安全修复的测试（Decoder.Decode 路径校验）。
/// 覆盖：相对路径穿越（../）、绝对路径文件名（Windows / POSIX 风格）被拒绝且不产出文件，
/// 合法包正常解包。
/// </summary>
public class KxpDecoderSecurityTests
{
    private const string KxpHeader = "It is a KXP file";

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("sub/../../evil.txt")]
    public void Decode_TraversalFileName_ThrowsAndWritesNothing(string fileName)
    {
        var root = CreateTempDir();
        try
        {
            var packagePath = Path.Combine(root, "malicious.kxp");
            File.WriteAllBytes(packagePath, BuildPackage([(fileName, Encoding.UTF8.GetBytes("pwned"))]));

            var releaseFolder = Path.Combine(root, "release");
            Directory.CreateDirectory(releaseFolder);

            var ex = Assert.Throws<InvalidDataException>(() => new ExtensionsPackageDecoder(packagePath).Decode(releaseFolder));

            Assert.Contains("Invalid file path", ex.Message);
            Assert.Empty(Directory.GetFiles(releaseFolder, "*", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "evil.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("C:\\Windows\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("/etc/evil")]
    public void Decode_AbsoluteFileName_ThrowsAndWritesNothing(string fileName)
    {
        var root = CreateTempDir();
        try
        {
            var packagePath = Path.Combine(root, "malicious.kxp");
            File.WriteAllBytes(packagePath, BuildPackage([(fileName, Encoding.UTF8.GetBytes("pwned"))]));

            var releaseFolder = Path.Combine(root, "release");
            Directory.CreateDirectory(releaseFolder);

            var ex = Assert.Throws<InvalidDataException>(() => new ExtensionsPackageDecoder(packagePath).Decode(releaseFolder));

            Assert.Contains("Invalid file path", ex.Message);
            Assert.Empty(Directory.GetFiles(releaseFolder, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Decode_ValidPackage_ExtractsFiles()
    {
        var root = CreateTempDir();
        try
        {
            var sourceFile = Path.Combine(root, "data.txt");
            File.WriteAllText(sourceFile, "hello kxp");

            var encoder = new ExtensionsPackageEncoder([sourceFile], "loader-struct", "plugin-struct");
            encoder.Encode(root + Path.DirectorySeparatorChar, root, "good");

            var packagePath = Path.Combine(root, "good.kxp");
            var releaseFolder = Path.Combine(root, "release");

            var (loader, plugin) = new ExtensionsPackageDecoder(packagePath).Decode(releaseFolder);

            Assert.Equal("loader-struct", loader);
            Assert.Equal("plugin-struct", plugin);
            Assert.Equal("hello kxp", File.ReadAllText(Path.Combine(releaseFolder, "data.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kitx-kxp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 手写构造 kxp 包：16B 头 + 16B MD5（32 字节之后全部内容）+ 结构段 + 文件表 + 文件体。
    /// 文件名可以任意指定（包括恶意路径），MD5 按 Decoder 的校验方式正确计算。
    /// </summary>
    private static byte[] BuildPackage(
        IEnumerable<(string FileName, byte[] Body)> files,
        string loader = "loader",
        string plugin = "plugin")
    {
        var loaderBytes = Encoding.UTF8.GetBytes(loader);
        var pluginBytes = Encoding.UTF8.GetBytes(plugin);
        var fileMap = files.Select(f => (NameBytes: Encoding.UTF8.GetBytes(f.FileName), f.Body)).ToList();

        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes((long)loaderBytes.Length));
        body.AddRange(loaderBytes);
        body.AddRange(BitConverter.GetBytes((long)pluginBytes.Length));
        body.AddRange(pluginBytes);
        body.AddRange(BitConverter.GetBytes((long)fileMap.Count));
        foreach (var item in fileMap)
        {
            body.AddRange(BitConverter.GetBytes((long)item.NameBytes.Length));
            body.AddRange(BitConverter.GetBytes((long)item.Body.Length));
        }
        foreach (var item in fileMap)
        {
            body.AddRange(item.NameBytes);
            body.AddRange(item.Body);
        }

        var bodyArray = body.ToArray();
        var hash = MD5.HashData(bodyArray);

        var result = new byte[32 + bodyArray.Length];
        Encoding.ASCII.GetBytes(KxpHeader).CopyTo(result, 0);
        hash.CopyTo(result, 16);
        bodyArray.CopyTo(result, 32);
        return result;
    }
}
