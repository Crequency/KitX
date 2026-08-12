using KitX.ToolKit.Data;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class DataStoreTests
{
    private static DataStore Create() => new(new DataStoreOptions { DefaultWaitTimeout = TimeSpan.FromMilliseconds(500) });

    [Fact]
    public void Set_Get_Reads_Back_Normalized_Value()
    {
        var store = Create();
        store.Set("name", "KitX");
        store.Set("n", 42);
        store.Set("obj", new { a = 1 });

        Assert.Equal("KitX", store.Get("name")?.GetString());
        Assert.Equal(42, store.Get("n")?.GetInt32());
        Assert.Equal(1, store.Get("obj")?.GetProperty("a").GetInt32());
    }

    [Fact]
    public void Get_Missing_Returns_Null()
    {
        var store = Create();
        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void Remove_Contains_Keys()
    {
        var store = Create();
        store.Set("a", 1);
        store.Set("b", 2);

        Assert.True(store.Contains("a"));
        Assert.Equal(2, store.Keys().Count());

        Assert.True(store.Remove("a"));
        Assert.False(store.Contains("a"));
        Assert.Single(store.Keys());
    }

    [Fact]
    public async Task Wait_Blocks_Until_All_Keys_Ready_And_Returns_Object()
    {
        var store = Create();

        var writer = Task.Run(() =>
        {
            Thread.Sleep(120);
            store.Set("x", 10);
            Thread.Sleep(120);
            store.Set("y", 20);
        });

        var result = store.Wait(["x", "y"]);

        await writer;
        Assert.Equal(10, result.GetProperty("x").GetInt32());
        Assert.Equal(20, result.GetProperty("y").GetInt32());
    }

    [Fact]
    public void Wait_Returns_Immediately_When_Already_Ready()
    {
        var store = Create();
        store.Set("k", "v");

        var result = store.Wait(["k"]);

        Assert.Equal("v", result.GetProperty("k").GetString());
    }

    [Fact]
    public async Task WaitAny_Returns_On_First_Key()
    {
        var store = Create();

        var writer = Task.Run(() =>
        {
            Thread.Sleep(100);
            store.Set("a", 1);
        });

        var result = store.WaitAny(["a", "b"], TimeSpan.FromSeconds(5));

        await writer;
        Assert.True(result.TryGetProperty("a", out _));
    }

    [Fact]
    public void Wait_TimesOut_Returns_Empty_Object()
    {
        var store = Create();
        var result = store.Wait(["never"], TimeSpan.FromMilliseconds(50));
        Assert.Empty(result.EnumerateObject());
    }

    [Fact]
    public void Clear_Resets_All_Keys()
    {
        var store = Create();
        store.Set("a", 1);
        store.Clear();
        Assert.Empty(store.Keys());
    }
}
