using KitX.ToolKit.Bench;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ToolkitFileStoreTests
{
    private static string TempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "toolkit-filestore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ResolveWorkflowPath_Rejects_Escape()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitFileStore(root);
            Assert.Throws<InvalidOperationException>(() => store.ResolveWorkflowPath("tk", "../evil.kcs"));
            Assert.Throws<InvalidOperationException>(() => store.ResolveWorkflowPath("tk", "..\\evil.kcs"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWorkflowPath_Appends_Extension_And_Scopes_To_Toolkit()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitFileStore(root);
            var path = store.ResolveWorkflowPath("tk", "workflows/wf");
            Assert.EndsWith(Path.Combine("workflows", "wf.kcs"), path, StringComparison.Ordinal);
            Assert.StartsWith(Path.Combine(root, "tk") + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteMinimalWorkflow_RoundTrips()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitFileStore(root);
            var workflow = new ToolkitWorkflow { Id = "wf1", Name = "New", File = "workflows/wf1.kcs" };
            await store.WriteMinimalWorkflowAsync("tk", workflow, "author");

            var path = store.ResolveWorkflowPath("tk", workflow.File);
            var kcs = await store.LoadAsync(path);
            Assert.NotNull(kcs);
            Assert.Equal("wf1", kcs!.Id);
            Assert.Equal("New", kcs.Name);
            Assert.Equal("author", kcs.Author);
            Assert.Equal("v6", kcs.IrVersion);
            Assert.False(string.IsNullOrWhiteSpace(kcs.IrData));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_Returns_Null_For_Missing_And_Corrupt()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitFileStore(root);
            Assert.Null(await store.LoadAsync(Path.Combine(root, "tk", "nope.kcs")));

            var corrupt = Path.Combine(root, "tk", "bad.kcs");
            Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
            await File.WriteAllTextAsync(corrupt, "{ not valid json");
            Assert.Null(await store.LoadAsync(corrupt));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_Returns_Null_For_Oversized()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitFileStore(root);
            var big = Path.Combine(root, "tk", "big.kcs");
            Directory.CreateDirectory(Path.GetDirectoryName(big)!);
            using (var fs = new FileStream(big, FileMode.CreateNew))
                fs.SetLength(11 * 1024 * 1024); // > 10 MB cap

            Assert.Null(await store.LoadAsync(big));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
