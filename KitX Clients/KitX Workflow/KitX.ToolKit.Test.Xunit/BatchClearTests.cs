using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KitX.ToolKit;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// A batch delete must arrive as <b>one</b> event, and must not be mistaken for a wipe.
/// </summary>
/// <remarks>
/// <para><b>The defect these guard against.</b> Clearing a table used to be a loop of single
/// removes, and every remove raised <c>Changed</c>. A thousand-key table meant a thousand messages,
/// and a client watching them saw the table half gone in the middle. <c>DataStore.Clear()</c> does
/// emit one <c>Cleared</c>, but it empties the <i>process-wide</i> store — using it here would take
/// every other mounted ToolKit's keys with it.</para>
///
/// <para><b>The trap in the obvious fix.</b> Reusing <c>Cleared</c> would have been correct-looking
/// and wrong: <see cref="KeyWaitRegistry"/> subscribes to it and releases <i>every</i> blocked flow
/// with an empty object, because after a real clear nothing a waiter wants can still arrive. A
/// subset deletion is not that, and releasing on it hands a flow "your key is gone" for a key that
/// is still in the store.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class BatchClearTests : IDisposable
{
    private const string TkId = "batch";

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit ToolkitWith(params KeyDeclaration[] keys) => ToolkitWith(TkId, keys);

    private static Toolkit ToolkitWith(string toolkitId, params KeyDeclaration[] keys) => new()
    {
        Id = toolkitId,
        Meta = new ToolkitMeta { Name = toolkitId },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [.. keys],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    private (DataStore Store, ToolkitInstanceManager Manager) Create(
        string toolkitId, params KeyDeclaration[] keys)
    {
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));
        manager.Mount(ToolkitWith(toolkitId, keys));
        return (store, manager);
    }

    [Fact]
    public void Clearing_an_entire_table_raises_one_event_naming_every_key()
    {
        var (store, manager) = Create(
            TkId,
            new KeyDeclaration("a", "string", KeyRole.Out),
            new KeyDeclaration("b", "string", KeyRole.Out),
            new KeyDeclaration("c", "string", KeyRole.Out));
        using (manager)
        {
            store.Set($"{TkId}/a", 1);
            store.Set($"{TkId}/b", 2);
            store.Set($"{TkId}/c", 3);

            var singles = 0;
            var batches = new List<IReadOnlyCollection<string>>();
            store.Changed += (_, _) => singles++;
            store.RemovedMany += (_, e) => batches.Add(e.Keys);

            var removed = manager.ClearAllOf(TkId);

            Assert.Equal(3, removed);
            Assert.Empty(store.Keys().Where(k => k.StartsWith(TkId + "/", StringComparison.Ordinal)));

            // The whole point: one notification, not one per key.
            Assert.Equal(0, singles);
            Assert.Single(batches);
            Assert.Equal(3, batches[0].Count);
        }
    }

    [Fact]
    public void Clearing_a_region_raises_one_event_covering_only_that_region()
    {
        var (store, manager) = Create(
            TkId,
            new KeyDeclaration("txn", "string", KeyRole.Out, Region: "current"),
            new KeyDeclaration("lines", "array", KeyRole.Stream, Region: "current"),
            new KeyDeclaration("total", "number", KeyRole.Out, Region: "cumulative"));
        using (manager)
        {
            store.Set($"{TkId}/txn", "b1");
            store.Set($"{TkId}/lines", new[] { 1, 2 });
            store.Set($"{TkId}/total", 42);

            var singles = 0;
            var batches = new List<IReadOnlyCollection<string>>();
            store.Changed += (_, _) => singles++;
            store.RemovedMany += (_, e) => batches.Add(e.Keys);

            var removed = manager.ClearRegionOf(TkId, "current");

            Assert.Equal(2, removed);
            Assert.Equal(0, singles);
            Assert.Single(batches);
            // The untouched region is not named — a client that clears by this message must not be
            // told to drop data that is still there.
            Assert.DoesNotContain($"{TkId}/total", batches[0]);
            Assert.True(store.Contains($"{TkId}/total"));
        }
    }

    [Fact]
    public async Task Clearing_one_toolkit_does_not_release_another_toolkits_waiters()
    {
        // The risk the obvious fix introduced. `Cleared` releases every waiter because after a real
        // clear nothing can arrive; a subset delete must not, or B's flow is woken with an empty
        // object for a key that is still sitting in the store.
        var store = new DataStore(new DataStoreOptions { DefaultWaitTimeout = TimeSpan.FromSeconds(5) });
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));

        const string A = "toolkit-a";
        const string B = "toolkit-b";

        manager.Mount(ToolkitWith(A, new KeyDeclaration("x", "string", KeyRole.Out)));
        manager.Mount(ToolkitWith(B, new KeyDeclaration("y", "string", KeyRole.Out)));

        using (manager)
        {
            store.Set($"{A}/x", "a-value");

            var waits = new KeyWaitRegistry(store);
            using (waits)
            {
                var waiting = waits.WaitAsync(
                    [$"{B}/y"], TimeSpan.FromSeconds(5), CancellationToken.None);

                // Clear A. B's waiter must still be waiting.
                manager.ClearAllOf(A);

                var early = await Task.WhenAny(waiting, Task.Delay(300)).ConfigureAwait(true);
                Assert.NotSame(waiting, early);

                // Now satisfy it. A released waiter would have returned an empty object already and
                // this write would arrive too late to be observed.
                store.Set($"{B}/y", "b-value");

                var completed = await waiting.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                // `BuildResult` keys the object by the full key, not the local name — the waiter
                // was given a full key, so that is what comes back.
                Assert.Equal("b-value", completed.GetProperty($"{B}/y").GetString());
            }
        }
    }

    [Fact]
    public void Clear_and_RemoveMany_are_different_events_because_they_mean_different_things()
    {
        // `Cleared` says the whole store is empty. `RemovedMany` says a subset went away. Collapsing
        // them would make every subscriber of the first re-derive which one it got.
        var store = new DataStore();
        store.Set("one/a", 1);
        store.Set("two/b", 2);

        var cleared = 0;
        var batched = 0;
        store.Cleared += (_, _) => cleared++;
        store.RemovedMany += (_, _) => batched++;

        store.RemoveMany(["one/a"]);

        Assert.Equal(0, cleared);
        Assert.Equal(1, batched);
        Assert.True(store.Contains("two/b"));
    }

    /// <summary>
    /// A workflow executor that does nothing. These tests exercise the data plane and the event
    /// shape, not the scheduler, so the executor only has to satisfy the constructor.
    /// </summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
