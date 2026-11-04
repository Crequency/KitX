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
    public void Prefix_With_One_Complete_Segment_Spans_Every_Bucket_Of_That_Namespace()
    {
        // Regression: the inverted index buckets keys by their first two segments, so a prefix of
        // "t1/" maps to a bucket literally named "t1/" which never exists. RemoveByPrefix and
        // KeysByPrefix both returned nothing, which is how a whole-toolkit wipe silently no-ops.
        var store = Create();
        store.Set("t1/status", 1);
        store.Set("t1/workdir", 2);
        store.Set("t1/panel/i1/p/log", 3);
        store.Set("t10/status", 4);   // same textual prefix, different toolkit
        store.Set("t2/status", 5);

        Assert.Equal(3, store.KeysByPrefix("t1/").Count);

        var removed = store.RemoveByPrefix("t1/");

        Assert.Equal(3, removed);
        Assert.False(store.Contains("t1/status"));
        Assert.False(store.Contains("t1/workdir"));
        Assert.False(store.Contains("t1/panel/i1/p/log"));

        // "t10/" is a different namespace and must survive a "t1/" sweep.
        Assert.True(store.Contains("t10/status"));
        Assert.True(store.Contains("t2/status"));
    }

    [Fact]
    public void Prefix_With_Two_Complete_Segments_Still_Pins_One_Bucket()
    {
        // The fast path every pre-S4 caller used must keep working.
        var store = Create();
        store.Set("t1/i1/wf/a", 1);
        store.Set("t1/i1/panel/b", 2);
        store.Set("t1/i2/wf/c", 3);

        Assert.Equal(1, store.KeysByPrefix("t1/i1/wf").Count);
        Assert.Equal(2, store.RemoveByPrefix("t1/i1"));

        Assert.True(store.Contains("t1/i2/wf/c"));
    }

}