using KitX.ToolKit.Models;
using KitX.ToolKit.Storage;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ToolkitStoreTests
{
    private static Toolkit ValidToolkit(string name = "demo")
        => new()
        {
            Meta = new ToolkitMeta { Name = name },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "wf" }] }],
        };

    private static string TempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "toolkit-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_Assigns_Id_And_RoundTrips()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var saved = store.Save(ValidToolkit());

            Assert.False(string.IsNullOrWhiteSpace(saved.Id));
            Assert.True(store.Exists(saved.Id));

            var loaded = store.Load(saved.Id);
            Assert.NotNull(loaded);
            Assert.Equal(saved.Id, loaded!.Id);
            Assert.Equal("demo", loaded.Meta.Name);
            Assert.Single(loaded.Workflows);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void List_Returns_All_Stored_Toolkits()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            store.Save(ValidToolkit("a"));
            store.Save(ValidToolkit("b"));

            var list = store.List();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, t => t.Meta.Name == "a");
            Assert.Contains(list, t => t.Meta.Name == "b");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_Rejects_Invalid_Config()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var bad = ValidToolkit();
            bad.Triggers = [new Trigger { Id = "t", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "missing" }] }];
            Assert.Throws<InvalidOperationException>(() => store.Save(bad));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_Removes_Directory()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            var saved = store.Save(ValidToolkit());
            Assert.True(store.Delete(saved.Id));
            Assert.False(store.Exists(saved.Id));
            Assert.False(store.Delete(saved.Id)); // idempotent false on absent
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_Returns_Null_For_Absent()
    {
        var root = TempRoot();
        try
        {
            var store = new ToolkitStore(root);
            Assert.Null(store.Load("nope"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
