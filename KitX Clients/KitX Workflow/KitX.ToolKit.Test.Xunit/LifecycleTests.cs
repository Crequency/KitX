using System.Text.Json;
using KitX.ToolKit.Run;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Time;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The lifecycle axis (ADR-0005 D2): enable / disable / uninstall and what each does to the data
/// a ToolKit declared, plus the two things that are easy to conflate with them — unmount (keeps
/// data, forgets the scheduler) and a plain instance end (keeps the toolkit).
/// </summary>
/// <remarks>
/// These are the acceptance tests behind S4 completion criteria ① (disable retains / uninstall
/// clears) and ② (ordered termination with a reason), written against the public surface so they
/// would still hold if the manager were rewritten underneath.
/// </remarks>
[Trait("Category", "Unit")]
public class LifecycleTests : IDisposable
{
    private const string TkId = "tk-life";

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit Toolkit() => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "life" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Triggers = [new Trigger { Id = "manual", Type = TriggerType.Manual }],
        Keys =
        [
            new KeyDeclaration("status", "string", KeyRole.Out),
            new KeyDeclaration("workdir", "string", KeyRole.Out),
        ],
    };

    private (ToolkitInstanceManager Manager, DataStore Store) Build(IClock? clock = null)
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)),
            null,
            null,
            clock);
        return (manager, store);
    }

    /// <summary>Writes one declared key the way a workflow would: full key, as KeyTable builds it.</summary>
    private static void Seed(DataStore store, string local, string value)
        => store.Set($"{TkId}/{local}", JsonSerializer.SerializeToElement(value));

    [Fact]
    public void Disable_Keeps_Declared_Data_And_Enable_Brings_Triggers_Back()
    {
        var (manager, store) = Build();
        try
        {
            manager.Mount(Toolkit());
            Seed(store, "status", "seeded");

            Assert.Equal(LifecycleState.Enabled, manager.GetLifecycleState(TkId));

            manager.Disable(TkId);

            // Data survives: this is the whole difference between disable and uninstall.
            Assert.Equal(LifecycleState.Disabled, manager.GetLifecycleState(TkId));
            Assert.True(store.TryGet($"{TkId}/status", out var kept));
            Assert.Equal("seeded", kept.GetString());

            // Still mounted, still enumerable — a disabled toolkit is addressable, just not
            // triggerable. Collapsing those two would make disable == uninstall.
            Assert.True(manager.IsMounted(TkId));
            Assert.Contains(TkId, manager.MountedToolkitIds);

            manager.Enable(TkId);
            Assert.Equal(LifecycleState.Enabled, manager.GetLifecycleState(TkId));
            Assert.True(store.TryGet($"{TkId}/status", out _));
        }
        finally
        {
            manager.Unmount(TkId);
        }
    }

    [Fact]
    public void Disabled_Toolkit_Refuses_To_Spawn_With_A_Stable_Code()
    {
        var (manager, _) = Build();
        try
        {
            manager.Mount(Toolkit());

            var before = manager.Spawn(TkId, "manual");
            Assert.False(string.IsNullOrEmpty(before));

            var rejected = new List<TriggerRejectedEvent>();
            manager.ToolkitEvent += (_, e) =>
            {
                if (e is TriggerRejectedEvent r)
                    lock (rejected) rejected.Add(r);
            };

            manager.Disable(TkId);
            var after = manager.Spawn(TkId, "manual");

            Assert.Null(after);
            TriggerRejectedEvent reason;
            lock (rejected) reason = Assert.Single(rejected);

            // The code is the contract, not the sentence around it: a host switches on this.
            Assert.Equal("toolkit.not-enabled", reason.Code);

            // Disabling also ended the instance that was running.
            Assert.Empty(manager.Instances);
        }
        finally
        {
            manager.Unmount(TkId);
        }
    }

    [Fact]
    public void Uninstall_Deletes_Every_Declared_Key_And_Unmounts()
    {
        var (manager, store) = Build();
        try
        {
            manager.Mount(Toolkit());
            Seed(store, "status", "a");
            Seed(store, "workdir", "/tmp/x");
            store.Set($"{TkId}/panel/inst/p/log", JsonSerializer.SerializeToElement("line"));

            var removed = manager.Uninstall(TkId);

            Assert.True(removed >= 2, $"expected at least the two declared keys, got {removed}");
            Assert.False(store.TryGet($"{TkId}/status", out _));
            Assert.False(store.TryGet($"{TkId}/workdir", out _));

            // The panel namespace is this toolkit's data too, even though it is not declared.
            Assert.False(store.TryGet($"{TkId}/panel/inst/p/log", out _));

            Assert.False(manager.IsMounted(TkId));
            Assert.Null(manager.GetLifecycleState(TkId));
        }
        finally
        {
            manager.Unmount(TkId);
        }
    }

    [Fact]
    public void Unmount_Keeps_Data_Because_It_Is_Not_Uninstall()
    {
        var (manager, store) = Build();
        manager.Mount(Toolkit());
        Seed(store, "status", "kept");

        manager.Unmount(TkId);

        Assert.False(manager.IsMounted(TkId));
        Assert.Null(manager.GetLifecycleState(TkId));
        Assert.True(store.TryGet($"{TkId}/status", out var value),
            "unmount forgets the scheduler, not the data — only uninstall erases");
        Assert.Equal("kept", value.GetString());
    }

    [Fact]
    public void Uninstall_Does_Not_Take_A_Prefix_Neighbours_Data_With_It()
    {
        // A toolkit id that is a prefix of another's is the realistic way this goes wrong:
        // a naive `RemoveByPrefix(toolkitId)` erases both.
        var (manager, store) = Build();
        try
        {
            manager.Mount(Toolkit());
            store.Set("tk-life-2/status", JsonSerializer.SerializeToElement("neighbour"));

            manager.Uninstall(TkId);

            Assert.True(store.TryGet("tk-life-2/status", out var neighbour),
                "uninstalling tk-life must not touch tk-life-2");
            Assert.Equal("neighbour", neighbour.GetString());
        }
        finally
        {
            manager.Unmount(TkId);
        }
    }

    [Fact]
    public void EndAll_Terminates_Newest_First()
    {
        // A stepping clock makes StartedAt strictly increasing, so "newest first" is a real
        // ordering rather than a tie broken by id. Without it the four spawns can share a tick and
        // this test would pass no matter what order EndAll used.
        var clock = new SteppingClock();
        var (manager, _) = Build(clock);
        try
        {
            manager.Mount(Toolkit());

            var ids = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var id = manager.Spawn(TkId, "manual");
                Assert.NotNull(id);
                ids.Add(id!);
            }

            // Observe the actual termination order off the event channel.
            var ended = new List<string>();
            manager.ToolkitEvent += (_, e) =>
            {
                if (e is InstanceCancelledEvent c)
                    lock (ended) ended.Add(c.InstanceId);
            };

            manager.EndAll();

            List<string> order;
            lock (ended) order = [.. ended];

            Assert.Equal(ids.Count, order.Count);
            Assert.Equal(Enumerable.Reverse(ids), order);
            Assert.Empty(manager.Instances);
        }
        finally
        {
            manager.Unmount(TkId);
        }
    }

    /// <summary>A clock that advances one second per read, so successive spawns are strictly ordered.</summary>
    private sealed class SteppingClock : IClock
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
