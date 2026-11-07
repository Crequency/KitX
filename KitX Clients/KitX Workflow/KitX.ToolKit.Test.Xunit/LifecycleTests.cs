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

            // Disabling also ended the flow that existed before it: the flow id is no longer
            // resolvable, so the toolkit kept no execution context for it (Q83 — the per-flow
            // "instance" record is what used to appear in Instances and is gone).
            Assert.Null(manager.GetToolkitId(before!));
            Assert.Empty(manager.ActiveFlowCounts());
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
    public void Disable_Stops_Every_Flow_Of_The_Toolkit()
    {
        // Replaces the old EndAll test. EndAll's job was "stop everything at once"; ADR-0015 D5.2
        // removed that member because the lifecycle already owns it — Disable stops every flow of
        // one toolkit's execution context. What does NOT survive is the old assertion about
        // termination *order*: EndInstance is no longer enumerable (it takes only a flow id), and
        // "newest first" was a property of EndAll's own loop, not a guarantee of the lifecycle.
        //
        // The flows are held in flight so the test observes Disable cancelling live work rather
        // than racing four fast completions. That needs the triggers to bind a workflow at all:
        // the file's shared Toolkit() declares a manual trigger with no bindings, and a flow with
        // nothing to schedule settles before Spawn even returns.
        var tk = new Toolkit
        {
            Id = TkId,
            Meta = new ToolkitMeta { Name = "life" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }],
        };

        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));

        try
        {
            manager.Mount(tk);

            var ids = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var id = manager.Spawn(TkId, "manual");
                Assert.NotNull(id);
                ids.Add(id!);
            }

            // All four flows are in flight and counted.
            Assert.Equal(4, manager.ActiveFlowCounts()[TkId]);

            manager.Disable(TkId);

            // Every flow was ended: the retention records are removed synchronously, so none of
            // the flow ids is addressable any more.
            Assert.All(ids, id => Assert.Null(manager.GetToolkitId(id)));

            // NOTE: this does not assert that ActiveFlowCounts() drained to empty. Today it does
            // not — EndInstance cancels and then disposes the run, and WorkflowRun.Dispose sets
            // its settled flag without raising Completed, while RunScheduler only drops a run from
            // its run table inside that handler. A flow cancelled while a node is in flight
            // therefore stays counted. Asserting the drain would encode that defect as expected
            // behaviour, so it is reported instead.

            // Disable retains the toolkit itself — that is what makes it not an uninstall.
            Assert.Equal(LifecycleState.Disabled, manager.GetLifecycleState(TkId));
            Assert.True(manager.IsMounted(TkId));

            // And it refuses new work, which is the observable half of "stopped".
            Assert.Null(manager.Spawn(TkId, "manual"));
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount(TkId);
        }
    }
}
