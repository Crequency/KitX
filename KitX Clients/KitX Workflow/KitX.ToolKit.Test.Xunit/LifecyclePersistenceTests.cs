using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Triggers;
using KitX.WorkflowV6.Backend;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The lifecycle survives a restart (design §4 table + §4.2, Q12/Q66): a disabled toolkit comes
/// back disabled, a mounted one restores the values it had, and uninstalling erases both.
/// </summary>
/// <remarks>
/// <para><b>What "restart" means here.</b> A new <see cref="ToolkitInstanceManager"/> over a new
/// <see cref="DataStore"/> pointing at the same directory — the same thing a process restart
/// produces. The store is deliberately fresh: restoring from a live one would prove nothing,
/// since the values would still be there even if the file were never written.</para>
///
/// <para><b>Why this is worth a test at all.</b> The state field used to be an in-memory property
/// reset by <c>Mount</c>, so "disabled stays disabled" was a statement about a field, not about
/// the product. Nothing about the code made the second part true; only the restart does.</para>
/// </remarks>
[Trait("Category", "Spec")]
public sealed class LifecyclePersistenceTests : IDisposable
{
    private const string TkId = "persisted";

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit ToolkitWith(params KeyDeclaration[] keys) => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "persisted" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [.. keys],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    /// <summary>
    /// Builds a manager over the shared root; <paramref name="store"/> is the fresh store the
    /// "restarted" process would have.
    /// </summary>
    private ToolkitInstanceManager Manager(DataStore store, ToolkitStateStore stateStore)
        => new(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)),
            stateStore: stateStore);

    private ToolkitStateStore StateStore() => new(new ToolkitStore(_root.Root));

    [Fact]
    public void A_disabled_toolkit_comes_back_disabled_after_a_restart()
    {
        var toolkit = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));

        using (var manager = Manager(new DataStore(), StateStore()))
        {
            manager.Mount(toolkit);
            manager.Disable(TkId);

            Assert.Equal(LifecycleState.Disabled, manager.GetLifecycleState(TkId));
        }

        // A new manager, a new store, a new data plane: everything memory-resident is gone.
        using (var restarted = Manager(new DataStore(), StateStore()))
        {
            restarted.Mount(toolkit);

            Assert.Equal(LifecycleState.Disabled, restarted.GetLifecycleState(TkId));
        }
    }

    [Fact]
    public void An_enabled_toolkit_comes_back_enabled_after_a_restart()
    {
        var toolkit = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));

        using (var manager = Manager(new DataStore(), StateStore()))
        {
            manager.Mount(toolkit);
            manager.Disable(TkId);
            manager.Enable(TkId);
        }

        using (var restarted = Manager(new DataStore(), StateStore()))
        {
            restarted.Mount(toolkit);

            Assert.Equal(LifecycleState.Enabled, restarted.GetLifecycleState(TkId));
        }
    }

    [Fact]
    public void Declared_key_values_come_back_after_a_restart()
    {
        var toolkit = ToolkitWith(
            new KeyDeclaration("status", "string", KeyRole.Out),
            new KeyDeclaration("total", "number", KeyRole.Out));

        var firstStore = new DataStore();

        using (var manager = Manager(firstStore, StateStore()))
        {
            manager.Mount(toolkit);
            firstStore.Set($"{TkId}/status", "halfway");
            firstStore.Set($"{TkId}/total", 17);

            // The transition is what writes the file: an ordinary workflow write does not have
            // to know about persistence for the value to survive.
            manager.Disable(TkId);
        }

        var secondStore = new DataStore();

        using (var manager = Manager(secondStore, StateStore()))
        {
            manager.Mount(toolkit);

            Assert.True(secondStore.TryGet($"{TkId}/status", out var status));
            Assert.Equal("halfway", status.GetString());
            Assert.True(secondStore.TryGet($"{TkId}/total", out var total));
            Assert.Equal(17, total.GetInt32());
        }
    }

    [Fact]
    public void A_stored_value_whose_key_is_no_longer_declared_is_dropped()
    {
        // The declaration is the contract. A value whose key was removed from toolkit.json is an
        // orphan: no workflow is allowed to read it, so restoring it would only make the state
        // file disagree with the config about what this toolkit has.
        var before = ToolkitWith(
            new KeyDeclaration("status", "string", KeyRole.Out),
            new KeyDeclaration("removed", "string", KeyRole.Out));

        var firstStore = new DataStore();

        using (var manager = Manager(firstStore, StateStore()))
        {
            manager.Mount(before);
            firstStore.Set($"{TkId}/status", "kept");
            firstStore.Set($"{TkId}/removed", "orphan");
            manager.Disable(TkId);
        }

        var after = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));
        var secondStore = new DataStore();

        using (var manager = Manager(secondStore, StateStore()))
        {
            manager.Mount(after);

            Assert.True(secondStore.TryGet($"{TkId}/status", out _));
            Assert.False(secondStore.TryGet($"{TkId}/removed", out _));
        }
    }

    [Fact]
    public void Uninstall_erases_the_state_so_a_remount_does_not_resurrect_deleted_values()
    {
        // Without deleting the state file, uninstall would restore the values it just removed and
        // the deletion would look like it silently failed on the next mount.
        var toolkit = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));

        var firstStore = new DataStore();

        using (var manager = Manager(firstStore, StateStore()))
        {
            manager.Mount(toolkit);
            firstStore.Set($"{TkId}/status", "will-be-erased");
            manager.Disable(TkId);
            manager.Uninstall(TkId);
        }

        var secondStore = new DataStore();

        using (var manager = Manager(secondStore, StateStore()))
        {
            manager.Mount(toolkit);

            Assert.False(secondStore.TryGet($"{TkId}/status", out _));
        }
    }

    [Fact]
    public void A_corrupt_state_file_is_treated_as_absent_rather_than_fatal()
    {
        // Nothing in the state file is a declaration, so an unreadable one costs the last-written
        // values, not the toolkit. Refusing to mount would let a cache block the product.
        var toolkit = ToolkitWith(new KeyDeclaration("status", "string", KeyRole.Out));

        var statePath = Path.Combine(_root.Root, TkId, ToolkitStateFile.FileName);
        Directory.CreateDirectory(Path.Combine(_root.Root, TkId));
        File.WriteAllText(statePath, "{ this is not json");

        var store = new DataStore();

        using (var manager = Manager(store, StateStore()))
        {
            manager.Mount(toolkit);

            // Mounted, enabled, nothing restored.
            Assert.Equal(LifecycleState.Enabled, manager.GetLifecycleState(TkId));
            Assert.False(store.TryGet($"{TkId}/status", out _));
        }
    }

    /// <summary>An executor that does nothing; these tests exercise the lifecycle, not scheduling.</summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
