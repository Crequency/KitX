using System.Diagnostics;
using System.Text.Json;
using KitX.ToolKit.Data;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Tests for the flow-scheduling half of the data plane (<see cref="KeyWaitRegistry"/>).
///
/// <para><b>Why these are not <see cref="DataStore"/> tests any more.</b> ADR-0004 D1 removed
/// waiting from the storage API: a store is a map and has no flows to block. The behaviour these
/// pin — AND vs OR satisfaction, timeouts, cancellation, wake-on-write, release-on-clear — is
/// unchanged; only the owner moved. Keeping them as store tests would have meant keeping a
/// <c>Wait</c> method on the store, which is what the ADR removes.</para>
/// </summary>
[Trait("Category", "Unit")]
public class KeyWaitRegistryTests
{
    private static (DataStore Store, KeyWaitRegistry Waits) Create()
    {
        var store = new DataStore(new DataStoreOptions { DefaultWaitTimeout = TimeSpan.FromMilliseconds(500) });
        return (store, new KeyWaitRegistry(store));
    }

    [Fact]
    public async Task Wait_Blocks_Until_All_Keys_Ready_And_Returns_Object()
    {
        var (store, waits) = Create();

        var writer = Task.Run(() =>
        {
            Thread.Sleep(120);
            store.Set("x", 10);
            Thread.Sleep(120);
            store.Set("y", 20);
        });

        var result = waits.Wait(["x", "y"], TimeSpan.FromSeconds(5), CancellationToken.None);

        await writer;
        Assert.Equal(10, result.GetProperty("x").GetInt32());
        Assert.Equal(20, result.GetProperty("y").GetInt32());
    }

    [Fact]
    public void Wait_Returns_Immediately_When_Already_Ready()
    {
        var (store, waits) = Create();
        store.Set("k", "v");

        var result = waits.Wait(["k"], TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("v", result.GetProperty("k").GetString());
    }

    [Fact]
    public async Task WaitAny_Returns_On_First_Key()
    {
        var (store, waits) = Create();

        var writer = Task.Run(() =>
        {
            Thread.Sleep(100);
            store.Set("a", 1);
        });

        var result = waits.WaitAny(["a", "b"], TimeSpan.FromSeconds(5), CancellationToken.None);

        await writer;
        Assert.True(result.TryGetProperty("a", out _));
    }

    [Fact]
    public void Wait_TimesOut_Returns_Empty_Object()
    {
        var (_, waits) = Create();
        var result = waits.Wait(["never"], TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Empty(result.EnumerateObject());
    }

    [Fact]
    public async Task Wait_Cancelled_Returns_Empty_Object_Promptly()
    {
        // A cancelled run must unblock a never-satisfied wait promptly — same empty-object
        // semantics as a timeout, no exception — far sooner than the 30s default.
        var (_, waits) = Create();
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => waits.Wait(["never"], TimeSpan.FromSeconds(30), cts.Token));

        await Task.Delay(100);
        cts.Cancel();

        var result = await task;
        sw.Stop();
        Assert.Empty(result.EnumerateObject());
        Assert.True(sw.ElapsedMilliseconds < 5000, $"cancellation took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitAll_Two_Buckets_Only_Wakes_When_All_Set()
    {
        var (store, waits) = Create();
        var task = Task.Run(() => waits.Wait(["a", "b"], TimeSpan.FromSeconds(5), CancellationToken.None));

        await Task.Delay(100);
        store.Set("a", 1);
        await Task.Delay(100);
        Assert.False(task.IsCompleted, "AND semantics must not release on the first of two keys");

        store.Set("b", 2);
        var result = await task;
        Assert.Equal(1, result.GetProperty("a").GetInt32());
        Assert.Equal(2, result.GetProperty("b").GetInt32());
    }

    [Fact]
    public async Task WaitAny_Wakes_On_Any_Key()
    {
        var (store, waits) = Create();
        var task = Task.Run(() => waits.WaitAny(["a", "b"], TimeSpan.FromSeconds(5), CancellationToken.None));

        await Task.Delay(100);
        store.Set("b", 2);

        var result = await task;
        Assert.True(result.TryGetProperty("b", out _));
    }

    [Fact]
    public async Task Set_Unrelated_Key_Does_Not_Wake_Waiter_On_Other_Key()
    {
        var (store, waits) = Create();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => waits.Wait(["x"], TimeSpan.FromMilliseconds(300), CancellationToken.None));

        await Task.Delay(50);
        store.Set("y", 1);

        var result = await task;
        sw.Stop();
        Assert.Empty(result.EnumerateObject());
        Assert.True(sw.ElapsedMilliseconds >= 200, $"released early by an unrelated key in {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Clear_Releases_All_Waiters()
    {
        var (store, waits) = Create();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => waits.Wait(["never"], TimeSpan.FromSeconds(30), CancellationToken.None));

        await Task.Delay(100);
        store.Clear();

        var result = await task;
        sw.Stop();
        Assert.Empty(result.EnumerateObject());
        Assert.True(sw.ElapsedMilliseconds < 5000, $"clear did not release the waiter ({sw.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public async Task WaitAny_Cancelled_Returns_Empty_Object_Promptly()
    {
        var (_, waits) = Create();
        using var cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => waits.WaitAny(["never"], TimeSpan.FromSeconds(30), cts.Token));

        await Task.Delay(100);
        cts.Cancel();

        var result = await task;
        sw.Stop();
        Assert.Empty(result.EnumerateObject());
        Assert.True(sw.ElapsedMilliseconds < 5000, $"cancellation took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Dispose_Releases_Pending_Waiters()
    {
        // A registry going away must not leave a flow blocked for its whole timeout.
        var (_, waits) = Create();
        var sw = Stopwatch.StartNew();
        var task = Task.Run(() => waits.Wait(["never"], TimeSpan.FromSeconds(30), CancellationToken.None));

        Thread.Sleep(100);
        waits.Dispose();

        var result = task.GetAwaiter().GetResult();
        sw.Stop();
        Assert.Empty(result.EnumerateObject());
        Assert.True(sw.ElapsedMilliseconds < 5000, $"dispose did not release the waiter ({sw.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public void Write_Landing_Between_Registration_And_First_Check_Is_Not_Missed()
    {
        // The register-then-check ordering exists so this race cannot lose a write.
        var (store, waits) = Create();
        for (var i = 0; i < 200; i++)
        {
            var key = $"k{i}";
            var writer = Task.Run(() => store.Set(key, i));
            var result = waits.Wait([key], TimeSpan.FromMilliseconds(500), CancellationToken.None);
            writer.Wait();
            Assert.True(result.TryGetProperty(key, out _), $"iteration {i} lost the write");
        }
    }
}
