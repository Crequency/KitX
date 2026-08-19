using System.Diagnostics;
using System.Text.Json;
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
    public async Task Wait_Cancelled_Returns_Empty_Object_Promptly()
    {
        // C5: a cancelled run must unblock a never-satisfied Wait promptly (same empty-object
        // semantics as a timeout, no exception) — far sooner than the 30s default timeout.
        var store = Create();
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => store.Wait(["never"], TimeSpan.FromSeconds(30), cts.Token));

        await Task.Delay(100);
        cts.Cancel();

        var result = await task;
        sw.Stop();
        Assert.Empty(result.EnumerateObject());
        Assert.True(sw.ElapsedMilliseconds < 5000, $"cancellation took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Clear_Resets_All_Keys()
    {
        var store = Create();
        store.Set("a", 1);
        store.Clear();
        Assert.Empty(store.Keys());
    }

    [Fact]
    public void Changed_Fires_On_Set_Remove_Append()
    {
        var store = Create();
        var events = new List<DataStoreChangedEventArgs>();
        store.Changed += (_, e) => events.Add(e);

        store.Set("k", 1);
        store.Append("k", 2);
        store.Remove("k");

        Assert.Equal(3, events.Count);
        Assert.False(events[0].Removed);
        Assert.Equal(1, events[0].NewValue?.GetInt32());
        Assert.False(events[1].Removed);
        Assert.True(events[2].Removed);
        Assert.Null(events[2].NewValue);
    }

    [Fact]
    public void Append_Builds_Array_And_Respects_Ring_Limit()
    {
        var store = Create();
        store.Append("log", "a");
        store.Append("log", "b");
        store.Append("log", "c", maxEntries: 2);

        var arr = store.Get("log");
        Assert.NotNull(arr);
        Assert.Equal(2, arr.Value.GetArrayLength());
        Assert.Equal("b", arr.Value[0].GetString());
        Assert.Equal("c", arr.Value[1].GetString());
    }

    [Fact]
    public void Append_Overwrites_NonArray_Key()
    {
        var store = Create();
        store.Set("k", "not-an-array");
        store.Append("k", 1);

        var arr = store.Get("k");
        Assert.NotNull(arr);
        Assert.Equal(JsonValueKind.Array, arr.Value.ValueKind);
        Assert.Single(arr.Value.EnumerateArray());
    }

    [Fact]
    public void Append_Ring_Returns_Last_Limit_And_Get_Is_Idempotent()
    {
        var store = Create();
        for (var i = 0; i < 5; i++)
            store.Append("log", i, maxEntries: 3);

        var arr = store.Get("log");
        Assert.NotNull(arr);
        Assert.Equal(3, arr.Value.GetArrayLength());
        Assert.Equal(2, arr.Value[0].GetInt32());
        Assert.Equal(3, arr.Value[1].GetInt32());
        Assert.Equal(4, arr.Value[2].GetInt32());

        // Lazy materialization is idempotent: repeated Get returns the same array.
        var again = store.Get("log");
        Assert.Equal(arr.Value.GetRawText(), again?.GetRawText());
    }

    [Fact]
    public void Append_Changed_Carries_Single_Entry_And_Appended_Flag()
    {
        var store = Create();
        var events = new List<DataStoreChangedEventArgs>();
        store.Changed += (_, e) => events.Add(e);

        store.Append("log", "a");
        store.Append("log", "b");

        Assert.Equal(2, events.Count);
        Assert.True(events[0].Appended);
        Assert.Equal("a", events[0].NewValue?.GetString());
        Assert.True(events[1].Appended);
        Assert.Equal("b", events[1].NewValue?.GetString());
    }

    [Fact]
    public void Append_Continues_After_Set_Array()
    {
        var store = Create();
        store.Set("log", new[] { "a", "b" });
        store.Append("log", "c");

        var arr = store.Get("log");
        Assert.NotNull(arr);
        Assert.Equal(3, arr.Value.GetArrayLength());
        Assert.Equal("a", arr.Value[0].GetString());
        Assert.Equal("b", arr.Value[1].GetString());
        Assert.Equal("c", arr.Value[2].GetString());
    }

    [Fact]
    public void Append_Then_Remove_Removes_From_Index()
    {
        var store = Create();
        store.Append("t1/i1/wf/a", 1);
        Assert.True(store.Contains("t1/i1/wf/a"));
        Assert.Contains("t1/i1/wf/a", store.KeysByPrefix("t1/i1/wf"));

        Assert.True(store.Remove("t1/i1/wf/a"));
        Assert.False(store.Contains("t1/i1/wf/a"));
        Assert.DoesNotContain("t1/i1/wf/a", store.KeysByPrefix("t1/i1/wf"));
    }

    [Fact]
    public void KeysByPrefix_Filters_Namespaces_Precisely()
    {
        var store = Create();
        store.Set("t1/i1/wf/a", 1);
        store.Set("t1/i1/panel/b", 2);
        store.Set("t1/i2/wf/c", 3);
        store.Set("t2/i1/wf/d", 4);
        store.Set("global", 5);

        Assert.Equal(new[] { "t1/i1/wf/a" }, store.KeysByPrefix("t1/i1/wf"));
        Assert.Equal(new[] { "t1/i1/panel/b" }, store.KeysByPrefix("t1/i1/panel"));
        Assert.Equal(new[] { "t1/i1/panel/b", "t1/i1/wf/a" }, store.KeysByPrefix("t1/i1").OrderBy(x => x));
        Assert.Equal(new[] { "t2/i1/wf/d" }, store.KeysByPrefix("t2"));
        Assert.Equal(new[] { "global" }, store.KeysByPrefix("global"));
        Assert.Empty(store.KeysByPrefix("t3"));
    }

    [Fact]
    public void Set_Overwrite_Does_Not_Duplicate_Index_Entry()
    {
        var store = Create();
        store.Set("t1/i1/wf/a", 1);
        store.Set("t1/i1/wf/a", 2);
        Assert.Single(store.KeysByPrefix("t1/i1/wf"));
    }

    [Fact]
    public void RemoveByPrefix_Removes_And_Fires_Changed_Per_Key()
    {
        var store = Create();
        store.Set("t1/i1/wf/a", 1);
        store.Set("t1/i1/wf/b", 2);
        store.Set("t1/i1/panel/c", 3);
        store.Set("t2/i1/wf/d", 4);

        var events = new List<DataStoreChangedEventArgs>();
        store.Changed += (_, e) => events.Add(e);

        var removed = store.RemoveByPrefix("t1/i1/wf");

        Assert.Equal(2, removed);
        Assert.False(store.Contains("t1/i1/wf/a"));
        Assert.False(store.Contains("t1/i1/wf/b"));
        Assert.True(store.Contains("t1/i1/panel/c"));
        Assert.True(store.Contains("t2/i1/wf/d"));
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.Removed));
    }

    [Fact]
    public async Task WaitAll_Two_Buckets_Only_Wakes_When_All_Set()
    {
        var store = Create();
        var task = Task.Run(() => store.Wait(["a", "b"], TimeSpan.FromSeconds(5)));

        await Task.Delay(100);
        store.Set("a", 1);
        await Task.Delay(100);
        Assert.False(task.IsCompleted);

        store.Set("b", 2);
        var result = await task;
        Assert.Equal(1, result.GetProperty("a").GetInt32());
        Assert.Equal(2, result.GetProperty("b").GetInt32());
    }

    [Fact]
    public async Task WaitAny_Wakes_On_Any_Key()
    {
        var store = Create();
        var task = Task.Run(() => store.WaitAny(["a", "b"], TimeSpan.FromSeconds(5)));

        await Task.Delay(100);
        store.Set("b", 2);
        var result = await task;
        Assert.True(result.TryGetProperty("b", out _));
    }

    [Fact]
    public async Task Set_Unrelated_Key_Does_Not_Wake_Waiter_On_Other_Key()
    {
        var store = Create();
        var task = Task.Run(() => store.Wait(["x"], TimeSpan.FromMilliseconds(300)));

        await Task.Delay(100);
        store.Set("y", 1); // unrelated key
        await Task.Delay(100);
        Assert.False(task.IsCompleted);

        var result = await task; // times out
        Assert.Empty(result.EnumerateObject());
    }

    [Fact]
    public async Task Clear_Releases_All_Waiters()
    {
        var store = Create();
        var task = Task.Run(() => store.Wait(["never"], TimeSpan.FromSeconds(30)));

        await Task.Delay(100);
        store.Clear();
        var result = await task;
        Assert.Empty(result.EnumerateObject());
    }

    [Fact]
    public async Task WaitAny_Cancelled_Returns_Empty_Object_Promptly()
    {
        var store = Create();
        using var cts = new CancellationTokenSource();
        var task = Task.Run(() => store.WaitAny(["never"], TimeSpan.FromSeconds(30), cts.Token));

        await Task.Delay(100);
        cts.Cancel();

        var result = await task;
        Assert.Empty(result.EnumerateObject());
    }
}
