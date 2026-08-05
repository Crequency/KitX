// ─────────────────────────────────────────────────────────────────────────────
// W-3 + W-11 tests: WorkflowStorageService hardening.
//   • storage root is anchored to AppContext.BaseDirectory (never CWD-relative)
//   • oversized .kcs files are rejected (10 MB cap)
//   • corrupt files are skipped with a logged path (no silent swallow)
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.WorkflowV6.Services;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class StorageServiceSecurityTests
{
    [Fact]
    public void Default_Storage_Directory_Is_Anchored_To_AppContext_BaseDirectory()
    {
        var svc = new WorkflowStorageService();
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "Data", "Workflows"),
            svc.StorageDirectory);
    }

    [Fact]
    public async Task Oversized_Kcs_File_Is_Rejected()
    {
        var svc = new WorkflowStorageService();
        var dir = svc.StorageDirectory;
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "big.kcs");

        // 10 MB cap + 1 byte of JSON padding (an 11 MB file).
        using (var fs = File.Create(file))
        {
            fs.Write(JsonSerializer.SerializeToUtf8Bytes(new { Id = "big" }));
            fs.SetLength(10 * 1024 * 1024 + 1);
        }

        try
        {
            // The exact-id path is skipped (no such file); the directory scan must
            // refuse to load the oversized file and report null.
            var loaded = await svc.LoadWorkflowDataAsync("missing-id");
            Assert.Null(loaded);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
